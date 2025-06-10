using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Load config
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
        string? savedUsername = config["Username"];
        string? savedPassword = config["Password"];

        var vinArgument = new Argument<string>("vin", "VIN to fetch report for");
        var typeOption = new Option<string>(
            aliases: new[] { "--type", "-t", "/type" },
            getDefaultValue: () => "basic",
            description: "Type of report (basic, enhanced, etc.)"
        );
        var rawOption = new Option<bool>(
            aliases: new[] { "--raw" },
            description: "Show raw JSON output instead of formatted report"
        );

        var rootCommand = new RootCommand("VIN report fetcher")
        {
            vinArgument,
            typeOption,
            rawOption
        };

        rootCommand.SetHandler(async (string vin, string reportType, bool raw) =>
        {
            string url = $"https://service-ba.vinlink.com/report?vin={vin}&type={reportType}&xsl=xml2json";
            using HttpClient client = new HttpClient();

            string username;
            string password;
            if (!string.IsNullOrEmpty(savedUsername) && !string.IsNullOrEmpty(savedPassword))
            {
                username = savedUsername;
                password = savedPassword;
            }
            else
            {
                Console.Write("Username: ");
                string? usernameInput = Console.ReadLine();
                username = usernameInput ?? string.Empty;
                Console.Write("Password: ");
                password = ReadPassword();
            }

            var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{password}");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            try
            {
                Console.WriteLine($"Fetching report for VIN: {vin}...");
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string report = await response.Content.ReadAsStringAsync();
                Console.WriteLine("VIN Report:");
                if (raw)
                {
                    Console.WriteLine(report);
                }
                else
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(report);
                        var root = doc.RootElement;
                        // VIN section
                        var vinSection = root
                            .GetProperty("REPORTS")
                            .GetProperty("REPORT")
                            .GetProperty("VINPOWER")
                            .GetProperty("VIN");
                        string vinNumber = vinSection.GetProperty("number").GetString() ?? "";
                        var decoded = vinSection.GetProperty("DECODED");
                        Console.WriteLine($"VIN: {vinNumber}");
                        Console.WriteLine($"Make: {decoded.GetProperty("Make").GetString()}");
                        Console.WriteLine($"Model: {decoded.GetProperty("Model").GetString()}");
                        Console.WriteLine($"Model Year: {decoded.GetProperty("Model_Year").GetString()}");
                        Console.WriteLine();
                        Console.WriteLine($"{"Field Name",-30} | Value");
                        Console.WriteLine(new string('-', 60));
                        foreach (var item in decoded.GetProperty("ITEM").EnumerateArray())
                        {
                            string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string value = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : item.GetProperty("value").GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(name))
                                Console.WriteLine($"{name,-30} | {value}");
                        }
                        Console.WriteLine();
                        // VEHICLES section (ENHANCED only)
                        if (root.GetProperty("REPORTS").GetProperty("REPORT").TryGetProperty("VEHICLES", out var vehicles))
                        {
                            var vehicle = vehicles.GetProperty("VEHICLE");
                            Console.WriteLine("[VEHICLE DETAILS]");
                            foreach (var prop in vehicle.EnumerateObject())
                            {
                                if (prop.NameEquals("SPECYFICATION"))
                                {
                                    Console.WriteLine("\nSpecification:");
                                    if (prop.Value.TryGetProperty("ITEM", out var specItems) && specItems.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var spec in specItems.EnumerateArray())
                                        {
                                            string desc = spec.TryGetProperty("item_description", out var d) ? d.GetString() ?? "" : "";
                                            string val = spec.TryGetProperty("item_value", out var v) ? v.GetString() ?? "" : "";
                                            if (!string.IsNullOrWhiteSpace(desc))
                                                Console.WriteLine($"  {desc,-40} : {val}");
                                        }
                                    }
                                }
                                else if (prop.NameEquals("EQUIPMENT"))
                                {
                                    // skip or implement if needed
                                }
                                else
                                {
                                    Console.WriteLine($"{prop.Name,-30} : {prop.Value.GetString()}");
                                }
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine("(Could not pretty-print VIN/VEHICLE section, showing raw output)");
                        Console.WriteLine(report);
                    }
                }

                // Save credentials if successful
                var newConfig = new { Username = username, Password = password };
                var json = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
                var configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                await System.IO.File.WriteAllTextAsync(configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while fetching the report: {ex.Message}");
            }
        }, vinArgument, typeOption, rawOption);

        return await rootCommand.InvokeAsync(args);
    }

    // Helper method to read password without echoing
    private static string ReadPassword()
    {
        var pwd = string.Empty;
        ConsoleKey key;
        do
        {
            var keyInfo = Console.ReadKey(intercept: true);
            key = keyInfo.Key;
            if (key == ConsoleKey.Backspace && pwd.Length > 0)
            {
                pwd = pwd[0..^1];
                Console.Write("\b \b");
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                pwd += keyInfo.KeyChar;
                Console.Write("*");
            }
        } while (key != ConsoleKey.Enter);
        Console.WriteLine();
        return pwd;
    }
}