# ProjectDB Service Manager

Windows x64 service manager and offline installer for ProjectDB.

## Development status

Current development version: **0.1.0-dev**.

The installer UI is currently being redesigned. Development builds are not published as GitHub Releases. Stable releases will start with **1.0.0**.

## Included components

- ProjectDB 3.4.0 for Windows x64
- ProjectDB Service Manager
- Windows Service Wrapper (WinSW) 2.12.0 x64
- ProjectDB service-control and log-wrapper helpers

Default installation directory: `C:\Program Files\ProjectDB`.

Setup detects an existing installation and switches between install and update mode. Application connection settings are configured after installation from ProjectDB Service Manager.

## Versioning

The project follows semantic versioning for published versions:

- `0.x.y-dev` — active development
- `1.0.0-rc.N` — optional release candidates
- `1.0.0` and later — stable releases

Routine test builds may use CI build numbers in artifact names, but those numbers do not change the product version and are not published as Releases.

## Offline payload

The large offline payload binaries are intentionally not stored in Git. `build-offline.ps1` downloads the pinned dependencies when they are missing and verifies their SHA-256 hashes before compilation:

- ProjectDB 3.4.0 x64 is downloaded from the pinned ProjectDB GitHub release asset.
- WinSW 2.12.0 x64 is downloaded from the official WinSW GitHub release.

The exact source URLs and expected hashes are documented in `payload/README.txt`. The generated Setup EXE embeds both files, so installation itself remains fully offline.

`payload/projectdb.ico` is part of the repository and is embedded into the generated components.

## Build

Run from Windows PowerShell with Go installed:

```powershell
.\build-offline.ps1
```

The current development build is written as `ProjectDB-Setup-0.1.0-dev.exe`.

GitHub Actions manual test builds add a CI build number to the downloadable artifact name without changing the product version. Release publishing is tag-driven, so ordinary test builds never create GitHub Releases.

## Third-party licenses

Third-party notices are kept in `THIRD-PARTY-NOTICES.txt` and copied into the installation directory. WinSW 2.12.0 is distributed under the MIT License; the required notice is included there.
