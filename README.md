# ProjectDB Service Manager

[![GitHub Release](https://img.shields.io/github/v/release/pavel-elblaus/projectdb-service-manager?sort=semver&style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest)
[![GitHub Downloads](https://img.shields.io/github/downloads/pavel-elblaus/projectdb-service-manager/total?style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases)
[![Build](https://github.com/pavel-elblaus/projectdb-service-manager/actions/workflows/build.yml/badge.svg)](https://github.com/pavel-elblaus/projectdb-service-manager/actions/workflows/build.yml)
[![License](https://img.shields.io/github/license/pavel-elblaus/projectdb-service-manager?style=flat-square)](LICENSE)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases)

Описание и руководство также доступны [на русском языке](README.ru.md).

**ProjectDB Service Manager** is a Windows desktop application for installing ProjectDB and running one or more ProjectDB applications as native Windows services.

It provides a single graphical interface for registering applications, starting and stopping them, viewing their status and recent activity, opening logs, updating the local ProjectDB runtime and managing a local `app.so` override. No manual service creation, WinSW configuration or `sc.exe` commands are required.

ProjectDB itself is available at [github.com/pavel-elblaus/projectdb](https://github.com/pavel-elblaus/projectdb) and [projectdb.pro](https://projectdb.pro).

## Why use ProjectDB Service Manager?

A regular ProjectDB Windows archive can run an application directly. ProjectDB Service Manager adds the Windows service lifecycle and a convenient management layer around it.

- **One installation, multiple applications.** ProjectDB binaries are shared, while every registered application gets its own configuration and Windows service.
- **Automatic startup.** Registered applications run as Windows services and can continue working without an open terminal or signed-in user session.
- **Simple service control.** Start, stop and restart applications from the GUI. Restart all currently running applications with one action.
- **Status at a glance.** Each application card shows service state, service identifier, process ID and ProjectDB source/version information when available.
- **Recent activity preview.** The latest structured log entry is visible directly in the application card, with warnings and errors highlighted.
- **Fast access to logs.** Open the log directory for a specific application without searching through the installation tree.
- **Safe application removal.** Remove one registered application or all registrations without deleting the shared ProjectDB installation.
- **ProjectDB library management.** Select or remove a local `app.so` override directly from the manager. A locally selected library is preserved during ProjectDB updates.
- **Tray integration.** The manager stays available from the Windows notification area and provides quick access to common actions.
- **Install and update with the same Setup.** Setup detects an existing installation automatically, preserves registered applications and local `app.so`, updates the shared runtime and restarts services that were running before the update.
- **Offline installation.** The published Setup executable contains the required ProjectDB Windows package and WinSW runtime; no downloads are required while installing.
- **Integrity checks.** Embedded ProjectDB and WinSW payloads are verified against pinned SHA-256 hashes before installation.

## Download

Download the latest **Windows x64** installer from [GitHub Releases](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest):

`ProjectDB-Setup-<version>.exe`

The Setup executable requests administrator privileges because it installs files under Program Files, creates Windows services, configures service permissions and registers the application for Windows startup.

The default installation directory is:

`C:\Program Files\ProjectDB`

## Installation

1. Download the latest Setup executable from [GitHub Releases](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest).
2. Run `ProjectDB-Setup-<version>.exe`.
3. Approve the Windows administrator prompt.
4. Keep the default installation directory or choose another location.
5. Click **Install**.
6. After installation, ProjectDB Service Manager starts automatically.

Setup installs the ProjectDB Windows runtime, ProjectDB Service Manager and the required Windows service wrapper.

No application credentials are requested during installation. Applications are registered afterward from the Service Manager.

## Add an application

Open **ProjectDB Service Manager** and click **Add application**.

Enter:

| Field | Description |
| --- | --- |
| **ProjectDB server address** | Configuration server used by the application. The default is `node.projectdb.pro`. |
| **Application name** | ProjectDB application identifier, for example `SHELL`. |
| **Access password** | Password used to obtain the application's ProjectDB configuration. |

Click **Register**.

The manager creates the application-specific configuration and Windows service, then adds it to the main window. ProjectDB binaries are reused; a separate ProjectDB copy is not created for every application.

## Manage applications

Every registered application is displayed as a separate card.

Available actions:

| Action | Description |
| --- | --- |
| **Start** | Starts the application's Windows service. |
| **Restart** | Restarts a running application. |
| **Stop** | Stops the application's Windows service. |
| **Logs** | Opens the application's log directory. |
| **Remove** | Removes the application's Windows service, local service configuration and service logs. Shared ProjectDB binaries remain installed. |

The top toolbar also provides:

- **Add application** — register another ProjectDB application;
- **Restart all** — restart all currently running registered applications;
- **Remove all** — remove all registered applications while keeping the shared ProjectDB runtime and Service Manager installed.

Service IDs use the short `PDB` prefix and include the application name plus a stable suffix to avoid collisions.

## Status and recent activity

The application card shows the current Windows service state using a status indicator:

- green — running and initialized;
- yellow — starting, stopping or waiting for initialization;
- red — stopped;
- gray — unavailable or unknown.

For running applications, the card can also show the process ID and ProjectDB source/version information reported by the application.

The **Last activity** area shows the latest structured log entry. Messages containing errors or failures are highlighted to make problems easier to notice.

## Manage `app.so`

The **ProjectDB library** section lets you select a local `app.so` file.

A selected local library is stored under the ProjectDB installation and takes priority over the normal ProjectDB release-library selection. The manager records metadata for the selected file and preserves the local override when Setup updates ProjectDB.

Use **Remove** in the library section to delete the local override and return to the normal ProjectDB library selection behavior.

## Updating

Download a newer Setup executable and run it normally.

When an existing installation is detected, Setup switches to **Update** mode automatically. During an update it:

1. verifies the embedded ProjectDB and WinSW payloads;
2. remembers which ProjectDB services are running;
3. stops Service Manager and active ProjectDB services;
4. updates the shared ProjectDB runtime and Service Manager components;
5. preserves registered applications and a local `app.so` override;
6. updates service wrapper/runtime files and service commands when required;
7. restarts the ProjectDB services that were running before the update;
8. starts ProjectDB Service Manager again.

You do not need to remove or re-register applications for a normal update.

## Removing an application vs uninstalling ProjectDB

These are different operations.

**Remove** on an application card deletes only that application's Windows service, service configuration and service logs. The shared ProjectDB installation and other applications remain available.

**Remove all** deletes all registered applications but keeps ProjectDB Service Manager and the shared ProjectDB runtime installed.

To remove the complete installation, use **Uninstall ProjectDB** from the Service Manager tray menu or the Windows installed-apps interface.

## Installed layout

ProjectDB remains in the installation root exactly as supplied by the ProjectDB Windows release. Service Manager components are kept separately under `bin`.

Typical layout:

```text
C:\Program Files\ProjectDB\
├─ projectdb.exe
├─ ... ProjectDB release files
├─ projectdb.ico
├─ THIRD-PARTY-NOTICES.txt
├─ bin\
│  ├─ projectdb-service-manager.exe
│  ├─ projectdb-service-control.exe
│  ├─ projectdb-log-wrapper.exe
│  ├─ projectdb-uninstall.exe
│  └─ winsw.exe
├─ service\
├─ log\
├─ lib\
└─ tmp\
```

ProjectDB release files are not renamed or relocated by Service Manager.

## Security and service control

Installation requires administrator privileges. After setup, ProjectDB services receive the Windows service permissions needed for the interactive user to query and control them from Service Manager without requiring an elevation prompt for every ordinary Start/Stop/Restart action.

Locally built Service Manager helper executables are Authenticode-signed during installation with a local ProjectDB publisher certificate created on the machine and placed in the local trusted publisher stores.

The offline installer verifies the SHA-256 hashes of its embedded ProjectDB and WinSW payloads before copying them into the installation.

## Bundled components

The current build includes:

| Component | Version | Architecture | License |
| --- | --- | --- | --- |
| ProjectDB | 3.4.0 | x64 | MIT |
| ProjectDB Service Manager | Current release | x64 | MIT |
| Windows Service Wrapper (WinSW) | 2.12.0 | x64 | MIT |

ProjectDB is maintained in the separate [ProjectDB repository](https://github.com/pavel-elblaus/projectdb).

Third-party license notices are included in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and installed with the application.

## Build from source

Requirements:

- Windows x64;
- Windows PowerShell;
- Go.

Clone the repository and run:

```powershell
.\build-offline.ps1
```

The build script downloads the pinned ProjectDB Windows archive and WinSW binary when they are not already present in `payload`, verifies their SHA-256 hashes and builds a self-contained offline Setup executable.

Large ProjectDB and WinSW payload binaries are intentionally not stored in Git.

Manual CI builds are available through **Actions → Build test installer**. Test artifacts include the GitHub Actions build number in the file name and are not published as releases.

Published releases are created from version tags by the release workflow.

## Versioning

Published versions follow semantic versioning.

- stable releases: `1.0.0`, `1.1.0`, `2.0.0`, ...
- optional release candidates: `1.0.0-rc.1`, ...
- development builds may use a `-dev` suffix.

CI build numbers identify test artifacts only and are not product versions.

See [CHANGELOG.md](CHANGELOG.md) for project changes.

## License

ProjectDB Service Manager is distributed under the [MIT License](LICENSE).

ProjectDB is also distributed under the MIT License in its own repository. WinSW retains its own MIT copyright notices; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Related links

- [ProjectDB](https://github.com/pavel-elblaus/projectdb)
- [ProjectDB website](https://projectdb.pro)
- [ProjectDB Service Manager releases](https://github.com/pavel-elblaus/projectdb-service-manager/releases)
- [Windows Service Wrapper (WinSW)](https://github.com/winsw/winsw)
