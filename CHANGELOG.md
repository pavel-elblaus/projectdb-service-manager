# Changelog

## Unreleased — 0.1.0-dev

- Switched application service identifiers from the `ProjectDB` prefix to the shorter `PDB` prefix; legacy identifiers are shown with the shortened prefix in the UI.
- Fixed the custom Browse button paint path on Windows PowerShell 5.1 and corrected the centered-dot subtitle separator.
- Moved Service Manager executables into `bin` with lowercase names while preserving ProjectDB release files in place.
- Added update migration for obsolete root-level Service Manager executables.
- Switched the Setup progress indicator to a square-corner style for a cleaner, more consistent layout.
- Kept the standard 6 px button radius while making the Browse button square on the field-facing left side.
- Smoothed the custom Setup progress indicator and aligned subtitle separators with Service Manager.
- Matched Setup button sizes, directory display, and progress indicator to the Service Manager UI.
- Added ProjectDB icon resources for the Setup executable and taskbar identity.
- Aligned the Setup UI styling and spacing with the Add ProjectDB application dialog.
- Added a consistent component table with version, architecture and MIT license information.
- Added the ProjectDB Service Manager MIT license.
- Imported the current ProjectDB Service Manager and offline installer source as the initial GitHub development baseline.
- Switched the Service Manager development version from the former internal installer build number `v31` to `0.1.0-dev`.
- Kept ProjectDB Core 3.4.0 and WinSW 2.12.0 as independently versioned bundled components.
- Installer UI redesign is in progress.
