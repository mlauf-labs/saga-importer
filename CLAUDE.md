# CLAUDE.md

Guidance for Claude / AI coding agents working in **SAGA Importer** — the folder-watching
importer for **SAGA** (*Self-organizing Archive for Generative Agents*). Read this before
making any change. Keep changes small, focused, tested, and consistent with the conventions
below.

## Project overview

A cross-platform .NET 10 monorepo that watches a folder and imports new files into a SAGA
instance over its REST API (`POST /documents`, Bearer auth), confirms ingestion by polling
the status endpoint, deletes successfully imported files, and moves failed ones to a
`failed/` subfolder. Two host targets:

- **Windows (`SagaImporter.Windows`)** — single self-contained `win-x64` exe with a WPF
  system-tray UI; secrets protected by Windows DPAPI; autostart via `HKCU\…\Run`; on-demand
  SMB share creation.
- **Linux (`SagaImporter.Daemon`)** — headless `linux-x64` daemon registered as a systemd
  service; secrets in a `chmod 640` settings file; autostart via systemd.

All platform-neutral logic lives in **`SagaImporter.Core`** (shared library).

## Tech stack & requirements

- **.NET 10 SDK**; **C# 14** (`LangVersion=latest`), nullable enabled, file-scoped namespaces.
- Frameworks: `net10.0` (Core, Daemon), `net10.0-windows` (Windows, Tests). WPF on Windows.
- Tests: **xUnit**.

## Solution layout

```
SagaImporter.sln
├── src/SagaImporter.Core/       # net10.0 — platform-neutral logic
│   ├── Abstractions/            # ITokenProtector, IAutostartService, …
│   ├── Models/                  # AppSettings, ImportItem, ImportResult
│   └── Services/                # SagaClient, ImportPipeline, FolderWatcher, FileStability,
│                                # FileFilter, ImportQueue, ImporterEngine, LogService, SettingsService
├── src/SagaImporter.Windows/    # net10.0-windows — WPF tray app
│   ├── Platform/                # DpapiTokenProtector, RegistryAutostartService, SmbShareService
│   ├── Services/                # TrayIconService
│   └── ViewModels/              # MainViewModel, SettingsViewModel, StatusViewModel
├── src/SagaImporter.Daemon/     # net10.0 — Linux headless daemon
│   └── Platform/                # FilePermissionTokenProtector, NoopAutostartService
└── tests/SagaImporter.Tests/    # xUnit (net10.0-windows, references Core + Windows)
```

## Commands

```powershell
dotnet restore
dotnet build -c Release          # build whole solution (no new warnings)
dotnet test  -c Release          # run all tests
dotnet format                    # keep style consistent before committing

# Publish Windows exe
dotnet publish src/SagaImporter.Windows/SagaImporter.Windows.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Publish Linux daemon
dotnet publish src/SagaImporter.Daemon/SagaImporter.Daemon.csproj `
  -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

Build (clean, no new warnings) and tests must pass before a change is done.

## Key constraints (watch out for these)

- **Core stays platform-neutral.** Never add a Windows-only API (DPAPI, Registry, WPF) or a
  Linux-only syscall to `SagaImporter.Core`. Platform code belongs in the Windows/Daemon host
  project only. To add a platform capability: add an interface to `Core/Abstractions/`,
  implement it in the host, inject it into `ImporterEngine` — don't touch Core.
- **Never lose user files.** Only delete a source file after a confirmed successful import.
  On any uncertainty, move it to `failed/` instead of deleting.
- **Low resource use.** Prefer event-driven work (`FileSystemWatcher` / inotify) over polling;
  the only timer is the periodic safety rescan (default 10 min). No per-second loops.
- **Secrets are never logged or written in visible plaintext.** Windows: DPAPI-encrypted in
  `%APPDATA%\…\settings.json`. Linux: `/etc/saga-importer/settings.json`, mode `640`,
  owner `root:saga-importer`.
- **Run unprivileged on Windows** (`asInvoker` manifest). Only the SMB-share feature elevates
  (UAC, on-demand).
- **No install:** ship a single self-contained exe/binary per platform.

## SAGA API contract (do not break)

- Upload: `POST {baseUrl}/documents`, `multipart/form-data`, field `file`, header
  `Authorization: Bearer <token>` → `202` with `{ document_id, status, title }`.
- Status: `GET {baseUrl}/documents/{id}/status` →
  `pending | converting | analyzing | indexing | ready | failed`.
- Health: `GET {baseUrl}/health` (no auth).

## Coding conventions

- English only — code, comments, identifiers, UI strings, docs, commits.
- `async`/`await` end-to-end for I/O; pass `CancellationToken` through services; no blocking
  `.Result`/`.Wait()` on the UI thread.
- Wrap external calls (HTTP, filesystem) in try/catch and surface errors via `LogService`;
  never crash the host on a single file's failure. Comments explain *why*, not *what*.
- Tests (xUnit) cover settings round-trip, DPAPI round-trip (guarded by
  `OperatingSystem.IsWindows()`), file-stability, `SagaClient` against a stubbed
  `HttpMessageHandler`, `FileFilter`. No tests requiring a real SAGA, network, or elevation.

## Git workflow — branching, commits, PRs

**`main` and `develop` are protected**: pull requests are required, and only the repository
**admin/owner** may push directly (force-push and deletion are blocked). **Do not push
directly to `main`/`develop`** — always use a feature branch + PR.

- `main` — stable/release branch. `develop` — integration branch; feature work branches here.

```bash
git switch develop && git pull
git switch -c feature/<short-description>   # or fix/… , docs/… , chore/… , refactor/…
# …focused commits…
git push -u origin feature/<short-description>
gh pr create --base develop --fill          # PR targets develop
```

After review + green CI, **squash-merge** into `develop`. Promote to `main` via a
`develop → main` PR; tagging `vX.Y.Z` on `main` triggers a Release that publishes the exe.

**Commits**
- Only commit or push **when the human explicitly asks.** If you're on `main`/`develop`,
  branch first.
- **Conventional Commits** — `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`,
  `ci:`, `build:`, `perf:`. Imperative mood, English.
- For Claude-authored commits, end the message with:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
- Never `--no-verify`, never force-push shared branches, never commit secrets or build
  artifacts (`bin/`, `obj/`, `publish/`).

## CI / Releases

- `.github/workflows/ci.yml` runs on push/PR to `main` and `develop` (`windows-latest`):
  restore → build (Release) → test → single-file publish → upload artifact.
- `.github/workflows/release.yml` runs on a `v*` tag: publishes the Windows exe to a GitHub
  Release. Keep CI green; update the workflow in the same PR as the code it covers.

## Definition of done

Builds clean (no new warnings across all projects), tests pass, Core still compiles for
`net10.0`. No secrets logged; no new admin requirements in Core; no busy-polling loops.
User-visible behavior documented in `README.md`.
