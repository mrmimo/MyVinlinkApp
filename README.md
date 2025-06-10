# MyVinlinkApp

A simple .NET console application to fetch and display vehicle reports from the Vinlink service using a VIN number.

## Features
- Fetches vehicle reports from `service-ba.vinlink.com` using Basic Authentication
- Supports both basic and enhanced report types
- Credentials are securely stored in `appsettings.json` after first successful login
- Command line argument parsing using System.CommandLine
- Pretty-prints VIN section as a readable table
- Optionally displays raw JSON with `--raw` switch

## Usage

```
MyVinlinkApp.exe <VIN> [--type <reportType>] [--raw]
```

- `<VIN>` – Vehicle Identification Number (required)
- `--type`, `-t`, `/type` – Report type (default: `basic`, e.g. `enhanced`)
- `--raw` – Show raw JSON output instead of formatted table

### Example

```
MyVinlinkApp.exe 1J4PR5GK3AC144323 --type enhanced
```

## Authentication
- On first run, you will be prompted for your Vinlink username and password.
- Credentials are saved to `appsettings.json` in the application directory for future use.

## Output
- By default, the VIN section is displayed as a readable table.
- For enhanced reports, additional vehicle details are shown.
- Use `--raw` to display the full JSON response.

## Requirements
- .NET 6.0 or newer

## Development
- Project uses [System.CommandLine](https://github.com/dotnet/command-line-api) for argument parsing
- Configuration is managed via [Microsoft.Extensions.Configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)

## License
MIT License
