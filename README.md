# Jellyfin.Books.Calibre

A Jellyfin plugin for integrating with [Calibre](https://calibre-ebook.com/) ebook library.

## Features

- **Dual Access Modes**: Connect to Calibre Content Server (host mode) or directly to a local Calibre library folder
- **Metadata Enrichment**: Enriches locally-scanned ebooks with metadata and cover images from Calibre
- **Library Filtering**: Select which Jellyfin book libraries to sync with Calibre
- **Reading Progress Sync**: Bidirectional sync of reading progress (future feature)

## Installation

1. Download the latest release from GitHub
2. Place the `.zip` file in Jellyfin's plugin folder
3. Restart Jellyfin
4. Go to Dashboard → Plugins → Calibre → Configure

## Configuration

### Host Access Mode

Use this mode to connect to a running [Calibre Content Server](https://calibre-ebook.com/docs/latest/http_api.html):

1. Enable "Use Calibre Content Server"
2. Enter your Calibre server URL (e.g., `http://192.168.1.10:8080`)
3. Enter username and password (if server requires auth)
4. Click "Test Connection" to verify

### Local Mode

Use this mode to access a Calibre library directly on the server:

1. Disable "Use Calibre Content Server"
2. Enter the full path to your Calibre library folder (where `metadata.db` is located)
3. Click "Save"

### Jellyfin Libraries

Select which Jellyfin book libraries to link with Calibre. Leave all unchecked to link all book libraries.

## Requirements

- Jellyfin 10.11.8+
- .NET 9.0

## Development

```bash
# Build
dotnet build Jellyfin.Books.Calibre/Jellyfin.Books.Calibre.csproj -c Debug

# Release build
dotnet build Jellyfin.Books.Calibre/Jellyfin.Books.Calibre.csproj -c Release
```

## Changelog

See [GitHub Releases](https://github.com/SirFfej/Jellyfin.Books.Calibre/releases) for version history.

## License

GPL-3.0