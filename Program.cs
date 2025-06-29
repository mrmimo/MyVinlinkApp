﻿using System;
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


        // Define command line arguments and options
        // We use a simple command line interface with one required argument (VIN) and two options (type and raw)
        var vinArgument = new Argument<string>("vin", "VIN to fetch report for");
        var typeOption = new Option<string>(
            aliases: new[] { "--type", "-t", "/type" },
            getDefaultValue: () => "basic",
            description: "Type of report (basic, basic_plus, enhanced, etc.)"
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

        // Set the handler for the command
        // This is where we will perform the HTTP request to fetch the VIN report
        rootCommand.SetHandler(async (string vin, string reportType, bool raw) =>
        {
            // prepare the URL, we send the vin and report type, we ask to output JSON using xsl=json transformation
            // by default the service returns XML, but we can request JSON format which is easier to work with
            string url = $"https://service-ba.vinlink.com/report?vin={vin}&type={reportType}&xsl=json";
            using HttpClient client = new HttpClient();

            string username;
            string password;

            // If saved credentials are available, use them; otherwise prompt for input
            // This allows the user to save their credentials in appsettings.json for future use
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


            // Set the Authorization header with Basic authentication
            var byteArray = System.Text.Encoding.ASCII.GetBytes($"{username}:{password}");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            try
            {
                Console.WriteLine($"Fetching report for VIN: {vin}...");
                // Make the request
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
                        // VIN section, here we process the part that belongs to basic and basic_plus reports
                        // For enhanced reports, we will also print VEHICLES section further down
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
                            // VEHICLE can be an array or an object
                            if (vehicle.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var veh in vehicle.EnumerateArray())
                                {
                                    PrintVehicleDetails(veh);
                                }
                            }
                            else
                            {
                                PrintVehicleDetails(vehicle);
                            }
                        }

                        // RECALLS section
                        if (root.GetProperty("REPORTS").GetProperty("REPORT").TryGetProperty("RECALLS", out var recalls))
                        {
                            Console.WriteLine("\n[RECALLS]");
                            if (recalls.TryGetProperty("RECALL", out var recallArray))
                            {
                                if (recallArray.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var recall in recallArray.EnumerateArray())
                                    {
                                        PrintRecall(recall);
                                    }
                                }
                                else if (recallArray.ValueKind == JsonValueKind.Object)
                                {
                                    PrintRecall(recallArray);
                                }
                            }
                        }
                    }

                    catch (Exception ex)
                    {
                        Console.WriteLine("(Could not pretty-print VIN/VEHICLE section, showing raw output)");
                        #if DEBUG
                        Console.WriteLine(ex);
                        #endif

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

    // Helper to print VEHICLE details
    private static void PrintVehicleDetails(JsonElement vehicle)
    {
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
                        if (string.IsNullOrWhiteSpace(desc))
                            desc = spec.TryGetProperty("item_name", out var n) ? n.GetString() ?? "" : "";
                        string val = spec.TryGetProperty("item_value", out var v) ? v.GetString() ?? "" : spec.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(desc) || !string.IsNullOrWhiteSpace(val))
                            Console.WriteLine($"  {desc,-40} : {val}");
                    }
                }
            }
            else if (prop.NameEquals("EQUIPMENT"))
            {
                Console.WriteLine("\nEquipment:");
                if (prop.Value.TryGetProperty("PACKAGE", out var packages) && packages.ValueKind == JsonValueKind.Array)
                {
                    int pkgIdx = 1;
                    foreach (var pkg in packages.EnumerateArray())
                    {
                        // Print package name if present
                        string pkgName = pkg.TryGetProperty("package_name", out var pn) ? pn.GetString() ?? string.Empty : string.Empty;
                        string pkgStatus = pkg.TryGetProperty("status", out var ps) ? ps.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(pkgName))
                            Console.WriteLine($"  Package: {pkgName} ({pkgStatus})");
                        else if (!string.IsNullOrWhiteSpace(pkgStatus))
                            Console.WriteLine($"  Package {pkgIdx++} ({pkgStatus})");
                        // Print items in package
                        if (pkg.TryGetProperty("ITEM", out var items) && items.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                string eqName = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                                string eqStatus = item.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                                string eqType = eqStatus == "O" ? "Optional" : "Standard";
                                if (eqStatus != "S" && eqStatus != "O") eqType = "Standard"; // treat unknown as Standard
                                if (!string.IsNullOrWhiteSpace(eqName))
                                    Console.WriteLine($"    - {eqName} [{eqType}]");
                            }
                        }
                    }
                }
            }
            else
            {
                // Print all other VEHICLE fields (Make, Model, Model_Year, etc.)
                Console.WriteLine($"{prop.Name,-30} : {prop.Value.GetString()}");
            }
        }
    }

    // Helper to print RECALL details
    private static void PrintRecall(JsonElement recall)
    {
        Console.WriteLine("--------------------");
        foreach (var prop in recall.EnumerateObject())
        {
            Console.WriteLine($"{prop.Name,-20}: {prop.Value.GetString()}");
        }
    }
}