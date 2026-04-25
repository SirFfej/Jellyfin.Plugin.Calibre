# Jellyfin Plugin for Calibre

<p align="center">
  <img alt="Alpha" src="https://img.shields.io/badge/status-alpha-red?labelColor=black" />
  <img alt="Jellyfin" src="https://img.shields.io/badge/Jellyfin-10.11%2B-00A4DC?logo=jellyfin&logoColor=white&labelColor=black" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white&labelColor=black" />
  <img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-blue?labelColor=black" />
</p>

> [!WARNING]
> **This plugin is in active alpha development. Expect breaking changes between releases. Use at your own risk and back up your Jellyfin data before installing.**

Bridges [Calibre](https://calibre-ebook.com/) and Jellyfin so your existing **eBooks** library becomes the unified front-end for your ebook collection — no separate sidebar, no duplicate entries.

---

## How it works

Calibre manages your ebook files, metadata, and covers. Jellyfin displays everything. This plugin connects the two:

- Jellyfin scans your local ebook files as it normally would
- The plugin enriches each scanned item with richer metadata pulled from Calibre (cover art, series, publishers, tags, descriptions)
- Supports both direct database access (local mode) and Calibre Content Server (host mode)

---

## Features

### Metadata
- **Cover art** — fetches cover images from your Calibre library and uses them as primary images in Jellyfin
- **Rich book metadata** — title, authors, series name and sequence number, publisher, genres, tags, description, publication date
- **Book ID provider IDs** — stored on the Jellyfin item so future lookups are fast
- **Local mode** — direct SQLite access to Calibre's `metadata.db` (no server required)
- **Host mode** — connects to Calibre Content Server via HTTP API

### Configuration
- **Test Connection** — verify your settings before saving
- **Library Selection** — select which Jellyfin book libraries to sync with Calibre
- **Access Mode Toggle** — switch between local folder and Content Server

### API Endpoints (for advanced use)
- `GET /Calibre/TestConnection` — test server/database connection
- `GET /Calibre/Libraries` — get Jellyfin libraries for config UI
- `GET /Calibre/LibraryIds` — get configured library IDs
- `GET /Calibre/Config` — get current configuration

---

## Requirements

| Component | Version |
|-----------|---------|
| Jellyfin Server | 10.11.8 or newer |
| Calibre | Any version with `metadata.db` |
| .NET Runtime | 9.0 (included in Jellyfin's Docker image) |

Both services must be reachable from the machine running Jellyfin — either on the same host or on the same Docker network.

---

## Installation

### From GitHub Release

1. Download the latest release from [GitHub Releases](https://github.com/SirFfej/Jellyfin.Plugin.Calibre/releases)
2. Go to **Dashboard → Plugins → Install from ZIP**
3. Upload the `.zip` file
4. Restart Jellyfin when prompted
5. Go to **Dashboard → Plugins → Calibre → Settings**

### From source

1. Clone the repository:
   ```bash
   git clone https://github.com/SirFfej/Jellyfin.Plugin.Calibre.git
   cd Jellyfin.Plugin.Calibre
   ```

2. Build the plugin:
   ```bash
   dotnet build Jellyfin.Plugin.Calibre/Jellyfin.Plugin.Calibre.csproj -c Release
   ```

3. Copy the output DLL to your Jellyfin plugins directory:
   ```bash
   # Linux / Docker volume
   cp Jellyfin.Plugin.Calibre/bin/Release/net9.0/Jellyfin.Plugin.Calibre.dll \
      /path/to/jellyfin/plugins/Calibre/
   ```

4. Restart Jellyfin.

---

## Configuration

### Host Access Mode

Use this mode to connect to a running [Calibre Content Server](https://calibre-ebook.com/docs/latest/http_api.html):

1. Enable **Use Calibre Content Server**
2. Enter your Calibre server URL (e.g., `http://192.168.1.10:8080`)
3. Enter username and password (if server requires auth)
4. Click **Test Connection** to verify
5. Click **Save**

### Local Mode

Use this mode to access a Calibre library directly on the server (no server required):

1. Disable **Use Calibre Content Server**
2. Enter the full path to your Calibre library folder (where `metadata.db` is located)
   - Example: `/path/to/calibre/library` or `C:\Calibre Library`
3. Click **Test Connection** to verify
4. Click **Save**

### Library Selection

Select which Jellyfin book libraries to link with Calibre. Leave all unchecked to link all available libraries.

---

## Scheduled Tasks

Tasks appear under **Dashboard → Scheduled Tasks → Calibre**:

| Task | Default trigger | What it does |
|------|----------------|--------------|
| **Enrich existing data with Calibre** | Daily 3am | Checks existing items against Calibre for items not yet connected |
| **Validate Calibre matches** | Weekly Sun 4am | Re-validates Calibre matches for previously linked items |

---

## Configuration

### Access Mode

Choose how to connect to Calibre:

- **Unchecked** = **Local Mode**: Access Calibre directly via local folder path (where `metadata.db` is located)
- **Checked** = **Host Mode**: Connect to a running Calibre Content Server via HTTP

---

## Plugin log

All plugin log entries are written to Jellyfin's main log with prefix `[Jellyfin.Plugin.Calibre]`. Enable debug logging in **Dashboard → Advanced → Logging** for detailed diagnostics.

---

## Known limitations

- **Author matching** — `BookInfo` in Jellyfin does not expose author name during metadata lookup, so fuzzy fallback is title-only
- **Series handling** — series are linked but primary series display in Jellyfin may vary by client
- **Progress sync** — not yet implemented (future feature)

---

## Building from source

```bash
# Requires .NET 9 SDK
dotnet build Jellyfin.Plugin.Calibre/Jellyfin.Plugin.Calibre.csproj -c Release
# Expected: 0 errors
```

---

## Contributing

Bug reports and pull requests are welcome. Please open an issue before starting any significant work.

---

## License

[GPL-3.0](LICENSE)](LICENSE)