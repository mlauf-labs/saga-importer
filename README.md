# SAGA Importer

> Folder-watching importer for **SAGA** (*Self-organizing Archive for Generative Agents*).
>
> A cross-platform tool that watches a folder and automatically imports every file
> dropped into it to a [SAGA](https://github.com/mlauf-labs/saga-core) instance via its REST API.
> Successfully ingested files are deleted from the watched folder; failures are moved
> aside.

[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

## What it does

- **Watches** a folder for new files using filesystem events (inotify on Linux,
  `FileSystemWatcher` on Windows), plus a periodic safety rescan (default every 10
  minutes) to catch anything missed.
- **Imports** each new, fully-copied file into Saga via `POST /documents`
  (`multipart/form-data`, Bearer token).
- **Confirms** ingestion by polling `GET /documents/{id}/status` until `ready`.
- **Cleans up**: deletes the source file once ingestion succeeds; moves files that fail
  into a `failed/` subfolder so they are not retried endlessly.
- **Two deployment targets:**
  - **Windows** — system-tray application with a settings UI and on-demand SMB share
    creation.
  - **Linux** — headless daemon on a Debian VM, watching a **Samba (SMB) inbox share**,
    managed by systemd.

## Project structure

```
SagaImporter.sln
├── src/SagaImporter.Core/       # Shared library: client, pipeline, watcher, queue
├── src/SagaImporter.Windows/    # Windows WPF tray application
├── src/SagaImporter.Daemon/     # Linux headless daemon (systemd worker)
└── tests/SagaImporter.Tests/    # xUnit test suite
```

---

## Windows — Tray Application

### Requirements

- Windows 10 or 11 (x64).
- A reachable Saga instance and an API token.
- To **build** from source: [.NET 10 SDK](https://dotnet.microsoft.com/download).

### Quick start

1. Build or download `SagaImporter.exe` (see below).
2. Double-click it. A tray icon appears.
3. Right-click the tray icon → **Open** to show the window.
4. On the **Settings** tab set:
   - **Watched folder** (e.g. `C:\Inbox`)
   - **Saga base URL** (default `http://localhost:8000`)
   - **API token**
5. Click **Test connection**, then **Save**. Drop a file into the folder and watch the
   **Status/Log** tab.

### Build from source

```powershell
# Restore, build and test
dotnet build -c Release
dotnet test  -c Release

# Produce a single, self-contained exe (no install, no runtime needed)
dotnet publish src/SagaImporter.Windows/SagaImporter.Windows.csproj -c Release `
  -r win-x64 --self-contained true -p:PublishSingleFile=true

# Result:
# src/SagaImporter.Windows/bin/Release/net10.0-windows/win-x64/publish/SagaImporter.exe
```

### Windows settings reference

| Setting | Description | Default |
|---------|-------------|---------|
| Watched folder | Folder monitored for new files. | (none) |
| Saga base URL | Root URL of the Saga REST API. | `http://localhost:8000` |
| API token | Bearer token; stored encrypted with Windows DPAPI. | (none) |
| Delete trigger | Delete after `ready` (safe) or right after upload (`accepted`). | `ready` |
| Status poll timeout | Max time to wait for `ready` before treating as failed. | 10 min |
| Rescan interval | Periodic full folder rescan. | 10 min |
| Failed subfolder | Where failed files are moved. | `failed` |
| Include extensions | Comma-separated allowlist (empty = all). | (empty) |
| Exclude extensions | Comma-separated blocklist. | `.tmp,.crdownload,.part` |
| Stability delay | Quiet time a file size must hold before importing. | 2 s |
| Start with Windows | Launch on user logon (`HKCU\...\Run`). | off |

Settings are stored at `%APPDATA%\SagaImporter\settings.json`. The API token is
encrypted with the Windows Data Protection API (current-user scope).

---

## Linux — Debian VM Daemon

### Overview

The Linux variant is a headless daemon intended to run on a small Debian VM (or any
Debian-based server). The `scripts/install-debian.sh` init script automates the entire
setup: it installs the .NET runtime-free self-contained binary, configures a Samba
share so any Windows or Linux machine on the LAN can drop files into the inbox, and
registers a hardened systemd service that starts on boot.

### VM quick start

1. Clone this repository on your Debian 13 server:

   ```bash
   git clone https://github.com/mlauf-labs/saga-importer.git
   cd saga-importer
   ```

2. Run the init script as root:

   ```bash
   sudo bash scripts/install-debian.sh
   ```

   The script prompts for the Saga URL, API token, and Samba credentials. All
   values can also be supplied as environment variables for unattended installation:

   ```bash
   sudo SAGA_BASE_URL=http://192.168.1.10:8000 \
        SAGA_API_TOKEN=my-token \
        SMB_USER=smbinbox \
        SMB_PASSWORD=s3cret \
        bash scripts/install-debian.sh
   ```

3. Drop a file from any machine into `\\<vm-hostname>\Inbox` and it will be imported
   automatically.

### What the init script does

| Step | Action |
|------|--------|
| 1 | Detect Debian; `apt-get update` with retry/backoff |
| 2 | Install packages: `samba`, `acl`, `ca-certificates`, `curl` |
| 3 | Install .NET 10 SDK via `dotnet-install.sh` into `/opt/dotnet` |
| 4 | `dotnet publish` the daemon as a self-contained single binary |
| 5 | Create system user `saga-importer` (no login shell) |
| 6 | Create inbox (`/srv/saga/inbox`) and `failed/` subfolder |
| 7 | Write `/etc/saga-importer/settings.json` (mode 640) |
| 8 | Append a Samba share block to `/etc/samba/smb.conf`; create Samba user |
| 9 | Install and enable the systemd unit; `systemctl enable --now` |
| 10 | Verify the service is active; probe `/health` |

Re-running the script is safe and idempotent.

### Linux configuration file

`/etc/saga-importer/settings.json` (mode `640`, owner `root:saga-importer`):

```json
{
  "watchedFolder": "/srv/saga/inbox",
  "baseUrl": "http://localhost:8000",
  "encryptedApiToken": "your-api-token",
  "deleteTrigger": 0,
  "statusPollTimeoutMinutes": 10,
  "statusPollIntervalSeconds": 3,
  "rescanIntervalMinutes": 10,
  "maxConcurrentImports": 4,
  "failedSubfolderName": "failed",
  "includeExtensions": "",
  "excludeExtensions": ".tmp,.crdownload,.part",
  "stabilityDelaySeconds": 2
}
```

After editing the config, restart the service:

```bash
sudo systemctl restart saga-importer
```

### Linux settings reference

| Setting | Description | Default |
|---------|-------------|---------|
| watchedFolder | Absolute path of the folder to watch. | (none) |
| baseUrl | Root URL of the Saga REST API. | `http://localhost:8000` |
| encryptedApiToken | API token stored as plaintext; protected by file permissions (chmod 640). | (none) |
| deleteTrigger | `0` = delete after `ready`; `1` = delete after accepted. | `0` |
| statusPollTimeoutMinutes | Max time to wait for `ready` before treating as failed. | 10 |
| statusPollIntervalSeconds | Interval between status polls. | 3 |
| rescanIntervalMinutes | Periodic full folder rescan. | 10 |
| maxConcurrentImports | How many files to import in parallel (clamped 1–16). Raise to drain a backlog faster. | 4 |
| failedSubfolderName | Subfolder name for failed files. | `failed` |
| includeExtensions | Comma-separated allowlist (empty = all). | (empty) |
| excludeExtensions | Comma-separated blocklist. | `.tmp,.crdownload,.part` |
| stabilityDelaySeconds | Quiet period a file size must hold before importing. | 2 |

### Useful systemd commands

```bash
sudo journalctl -u saga-importer -f          # live log
sudo systemctl status saga-importer          # service status
sudo systemctl restart saga-importer         # restart after config change
sudo systemctl stop saga-importer            # stop the daemon
```

### Samba troubleshooting

```bash
testparm                                          # validate smb.conf
smbstatus                                         # show active connections
sudo smbpasswd -a <user>                          # reset Samba password
```

### Security notes — Linux

- The API token is stored as **plaintext** in `/etc/saga-importer/settings.json`.
  The file is owned `root:saga-importer` with mode `640`, so only root and the
  daemon's own service user can read it. This is the Linux equivalent of Windows DPAPI
  (filesystem access control instead of OS-level encryption).
- The systemd unit runs with `NoNewPrivileges`, `ProtectSystem=strict`,
  `PrivateTmp`, and write access restricted to the inbox folder and log directory only.
- Samba is configured with `force user/group = saga-importer` so all dropped files
  are owned by the service user and can always be deleted after a successful import.

---

## Import flow

```
new/changed file -> stability gate -> queue -> POST /documents (202, id)
                                              -> poll GET /documents/{id}/status
                                              -> ready  : delete source file
                                              -> failed : move to failed/ subfolder
```

---

## Build from source (full solution)

```powershell
# Build everything (Windows dev machine)
dotnet build -c Release

# Run tests
dotnet test -c Release

# Publish Windows tray exe
dotnet publish src/SagaImporter.Windows/SagaImporter.Windows.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Publish Linux daemon (cross-compile from Windows or build on Linux)
dotnet publish src/SagaImporter.Daemon/SagaImporter.Daemon.csproj `
  -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

---

## Contributing

See [AGENTS.md](AGENTS.md) for project conventions (also used to guide AI-assisted
development). Issues and pull requests are welcome.

## License

[Apache-2.0](LICENSE).
