param(
	[Parameter(Mandatory=$true)]
	[string]$WorkerScript,

	[Parameter(Mandatory=$true)]
	[string]$TraySource,

	[Parameter(Mandatory=$true)]
	[string]$ServiceControlSource,

	[Parameter(Mandatory=$true)]
	[string]$LogWrapperSource,

	[Parameter(Mandatory=$true)]
	[string]$ProjectDbArchive,

	[Parameter(Mandatory=$true)]
	[string]$WinSwBinary,

	[Parameter(Mandatory=$true)]
	[string]$ProjectDbIcon,

	[Parameter(Mandatory=$true)]
	[string]$ThirdPartyNotices
)

$ErrorActionPreference = 'Stop'
try {
	$utf8 = New-Object Text.UTF8Encoding($false)
	[Console]::OutputEncoding = $utf8
	$OutputEncoding = $utf8
} catch {}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$ProgramFiles64 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$DefaultAppDir = Join-Path $ProgramFiles64 'ProjectDB'
$UninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectDB'
$runtimeDir = Split-Path -Parent $WorkerScript
$configPath = Join-Path $runtimeDir 'worker-config.json'
$statusPath = Join-Path $runtimeDir 'worker-status.json'
$workerProcess = $null
$lastStatusJson = $null
$installationFinished = $false
$ServiceManagerVersion = '0.1.0-dev'

# Palette matching ProjectDB Service Manager.
$UiSurface = [Drawing.Color]::White
$UiBorder = [Drawing.Color]::FromArgb(214,225,228)
$UiText = [Drawing.Color]::FromArgb(24,41,48)
$UiMuted = [Drawing.Color]::FromArgb(101,119,126)
$UiAccent = [Drawing.Color]::FromArgb(34,160,171)
$UiAccentHover = [Drawing.Color]::FromArgb(26,142,153)
$UiDisabled = [Drawing.Color]::FromArgb(240,244,245)
$UiDisabledText = [Drawing.Color]::FromArgb(155,167,171)

function Get-ExistingInstallDir() {
	try {
		$item = Get-ItemProperty -Path $UninstallKey -ErrorAction Stop
		$candidate = [string]$item.InstallLocation
		if (-not [string]::IsNullOrWhiteSpace($candidate)) {
			$manager = Join-Path $candidate 'ProjectDB-Service-Manager.exe'
			$projectDb = Join-Path $candidate 'projectdb.exe'
			if ((Test-Path -LiteralPath $manager -PathType Leaf) -or (Test-Path -LiteralPath $projectDb -PathType Leaf)) {
				return $candidate.TrimEnd('\')
			}
		}
	} catch {}

	$defaultManager = Join-Path $DefaultAppDir 'ProjectDB-Service-Manager.exe'
	$defaultProjectDb = Join-Path $DefaultAppDir 'projectdb.exe'
	if ((Test-Path -LiteralPath $defaultManager -PathType Leaf) -or (Test-Path -LiteralPath $defaultProjectDb -PathType Leaf)) {
		return $DefaultAppDir
	}
	return $null
}

function New-ModernButton([string]$Text, [bool]$Primary) {
	$button = New-Object System.Windows.Forms.Button
	$button.Text = $Text
	$button.Size = New-Object Drawing.Size(112, 36)
	$button.FlatStyle = [Windows.Forms.FlatStyle]::Flat
	$button.FlatAppearance.BorderSize = 1
	$button.Cursor = [Windows.Forms.Cursors]::Hand
	$button.Font = New-Object Drawing.Font('Segoe UI', 9)
	$button.UseVisualStyleBackColor = $false
	if ($Primary) {
		$button.BackColor = $UiAccent
		$button.ForeColor = [Drawing.Color]::White
		$button.FlatAppearance.BorderColor = $UiAccent
		$button.Add_MouseEnter({ if ($this.Enabled) { $this.BackColor = $UiAccentHover; $this.FlatAppearance.BorderColor = $UiAccentHover } })
		$button.Add_MouseLeave({ if ($this.Enabled) { $this.BackColor = $UiAccent; $this.FlatAppearance.BorderColor = $UiAccent } })
	} else {
		$button.BackColor = $UiSurface
		$button.ForeColor = $UiText
		$button.FlatAppearance.BorderColor = $UiBorder
		$button.Add_MouseEnter({ if ($this.Enabled) { $this.BackColor = [Drawing.Color]::FromArgb(247,251,252) } })
		$button.Add_MouseLeave({ if ($this.Enabled) { $this.BackColor = $UiSurface } })
	}
	return $button
}

function Set-ButtonEnabled($Button, [bool]$Enabled, [bool]$Primary) {
	$Button.Enabled = $Enabled
	if ($Enabled) {
		$Button.Cursor = [Windows.Forms.Cursors]::Hand
		if ($Primary) {
			$Button.BackColor = $UiAccent
			$Button.ForeColor = [Drawing.Color]::White
			$Button.FlatAppearance.BorderColor = $UiAccent
		} else {
			$Button.BackColor = $UiSurface
			$Button.ForeColor = $UiText
			$Button.FlatAppearance.BorderColor = $UiBorder
		}
	} else {
		$Button.Cursor = [Windows.Forms.Cursors]::Default
		$Button.BackColor = $UiDisabled
		$Button.ForeColor = $UiDisabledText
		$Button.FlatAppearance.BorderColor = $UiBorder
	}
}

function Add-Label([string]$Text, [int]$X, [int]$Y, [int]$Width, [int]$Height, $Color, $Font = $null) {
	$label = New-Object System.Windows.Forms.Label
	$label.Text = $Text
	$label.Location = New-Object Drawing.Point($X, $Y)
	$label.Size = New-Object Drawing.Size($Width, $Height)
	$label.ForeColor = $Color
	if ($null -ne $Font) { $label.Font = $Font }
	$form.Controls.Add($label)
	return $label
}

$existingInstallDir = Get-ExistingInstallDir
$isUpdate = -not [string]::IsNullOrWhiteSpace([string]$existingInstallDir)
$AppDir = if ($isUpdate) { $existingInstallDir } else { $DefaultAppDir }
$mode = if ($isUpdate) { 'update' } else { 'install' }
$actionTitle = if ($isUpdate) { 'Update ProjectDB Service Manager' } else { 'Install ProjectDB Service Manager' }
$readyText = if ($isUpdate) { 'Ready to update.' } else { 'Ready to install.' }

$form = New-Object System.Windows.Forms.Form
$form.Text = "ProjectDB Setup v$ServiceManagerVersion"
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ClientSize = New-Object Drawing.Size(600, 487)
$form.Font = New-Object Drawing.Font('Segoe UI', 9)
$form.BackColor = $UiSurface
$form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::Dpi
$form.ShowInTaskbar = $true
$form.TopMost = $true
try { $form.Icon = New-Object Drawing.Icon($ProjectDbIcon) } catch {}
$form.Add_Shown({ $form.Activate(); $form.BringToFront(); $form.TopMost = $false })

$title = Add-Label $actionTitle 28 22 540 34 $UiText (New-Object Drawing.Font('Segoe UI Semibold', 15))

$subtitleText = "Service Manager v$ServiceManagerVersion  |  ProjectDB 3.4.0  |  Windows x64"
$subtitle = Add-Label $subtitleText 32 58 536 24 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.8))

$noteText = if ($isUpdate) {
	'Existing installation detected. Registered applications and local app.so are preserved.'
} else {
	'Applications are configured after setup in ProjectDB Service Manager.'
}
$note = Add-Label $noteText 32 82 536 24 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.8))

$dirLabel = Add-Label 'Installation directory' 32 121 536 22 $UiText

$dirBorder = New-Object System.Windows.Forms.Panel
$dirBorder.Location = New-Object Drawing.Point(32, 146)
$dirBorder.Size = New-Object Drawing.Size(414, 40)
$dirBorder.BackColor = $UiBorder
$form.Controls.Add($dirBorder)

$installDirBox = New-Object System.Windows.Forms.TextBox
$installDirBox.Text = $AppDir
$installDirBox.BorderStyle = [Windows.Forms.BorderStyle]::None
$installDirBox.Location = New-Object Drawing.Point(10, 10)
$installDirBox.Size = New-Object Drawing.Size(394, 19)
$installDirBox.Font = New-Object Drawing.Font('Segoe UI', 9.5)
$installDirBox.BackColor = $UiSurface
$installDirBox.ForeColor = $UiText
$dirBorder.Controls.Add($installDirBox)
$installDirBox.Add_GotFocus({ $dirBorder.BackColor = $UiAccent })
$installDirBox.Add_LostFocus({ $dirBorder.BackColor = $UiBorder })

$browseButton = New-ModernButton 'Browse...' $false
$browseButton.Location = New-Object Drawing.Point(456, 148)
$browseButton.Size = New-Object Drawing.Size(112, 36)
$form.Controls.Add($browseButton)

$componentsLabel = Add-Label 'Installed components' 32 207 536 22 $UiText

$componentsBorder = New-Object System.Windows.Forms.Panel
$componentsBorder.Location = New-Object Drawing.Point(32, 232)
$componentsBorder.Size = New-Object Drawing.Size(536, 101)
$componentsBorder.BackColor = $UiBorder
$form.Controls.Add($componentsBorder)

$componentsPanel = New-Object System.Windows.Forms.Panel
$componentsPanel.Location = New-Object Drawing.Point(1, 1)
$componentsPanel.Size = New-Object Drawing.Size(534, 99)
$componentsPanel.BackColor = $UiSurface
$componentsBorder.Controls.Add($componentsPanel)

function Add-ComponentLine([string]$Name, [string]$Details, [int]$Y) {
	$nameLabel = New-Object System.Windows.Forms.Label
	$nameLabel.Text = $Name
	$nameLabel.Location = New-Object Drawing.Point(12, $Y)
	$nameLabel.Size = New-Object Drawing.Size(330, 23)
	$nameLabel.ForeColor = $UiText
	$componentsPanel.Controls.Add($nameLabel)

	$detailsLabel = New-Object System.Windows.Forms.Label
	$detailsLabel.Text = $Details
	$detailsLabel.Location = New-Object Drawing.Point(330, $Y)
	$detailsLabel.Size = New-Object Drawing.Size(190, 23)
	$detailsLabel.TextAlign = [Drawing.ContentAlignment]::MiddleRight
	$detailsLabel.ForeColor = $UiMuted
	$componentsPanel.Controls.Add($detailsLabel)
}

Add-ComponentLine 'ProjectDB' '3.4.0  |  x64' 8
Add-ComponentLine 'ProjectDB Service Manager' ('v' + $ServiceManagerVersion) 37
Add-ComponentLine 'Windows Service Wrapper (WinSW)' '2.12.0  |  x64  |  MIT' 66

$licenseNote = Add-Label 'Third-party license notices are installed with the application.' 32 339 536 22 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.5))

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Location = New-Object Drawing.Point(32, 372)
$progress.Size = New-Object Drawing.Size(536, 14)
$progress.Minimum = 0
$progress.Maximum = 100
$form.Controls.Add($progress)

$status = Add-Label $readyText 32 394 536 30 $UiText

$installButtonText = if ($isUpdate) { 'Update' } else { 'Install' }
$installButton = New-ModernButton $installButtonText $true
$installButton.Location = New-Object Drawing.Point(346, 429)
$form.Controls.Add($installButton)

$closeButton = New-ModernButton 'Close' $false
$closeButton.Location = New-Object Drawing.Point(466, 429)
$closeButton.Size = New-Object Drawing.Size(102, 36)
$closeButton.Add_Click({ $form.Close() })
$form.Controls.Add($closeButton)
$form.AcceptButton = $installButton
$form.CancelButton = $closeButton

if ($isUpdate) {
	$installDirBox.ReadOnly = $true
	$installDirBox.BackColor = [Drawing.Color]::FromArgb(248,250,251)
	Set-ButtonEnabled $browseButton $false $false
}

$browseButton.Add_Click({
	$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
	$dialog.Description = 'Select the ProjectDB installation directory.'
	$dialog.ShowNewFolderButton = $true
	try {
		if (-not [string]::IsNullOrWhiteSpace($installDirBox.Text) -and (Test-Path -LiteralPath $installDirBox.Text -PathType Container)) {
			$dialog.SelectedPath = $installDirBox.Text
		}
		if ($dialog.ShowDialog($form) -eq [Windows.Forms.DialogResult]::OK) {
			$installDirBox.Text = $dialog.SelectedPath
		}
	} finally {
		$dialog.Dispose()
	}
})

function Set-ControlsEnabled([bool]$Enabled) {
	if (-not $isUpdate) {
		$installDirBox.Enabled = $Enabled
		Set-ButtonEnabled $browseButton $Enabled $false
	}
	Set-ButtonEnabled $installButton $Enabled $true
	Set-ButtonEnabled $closeButton $Enabled $false
}

function Get-InstallerLog() {
	$dir = $installDirBox.Text.Trim()
	if ([string]::IsNullOrWhiteSpace($dir)) { $dir = $AppDir }
	return Join-Path $dir 'log\installer\setup.log'
}

function Finish-Error([string]$Message) {
	$installationFinished = $true
	$timer.Stop()
	$errorPrefix = if ($isUpdate) { 'Update error: ' } else { 'Installation error: ' }
	$status.Text = $errorPrefix + $Message
	$status.ForeColor = [Drawing.Color]::DarkRed
	Set-ControlsEnabled $true
	[Windows.Forms.MessageBox]::Show($form, ($Message + "`r`n`r`nLog: " + (Get-InstallerLog)), 'ProjectDB Setup - Error', 'OK', 'Error') | Out-Null
}

function Finish-Success($Result) {
	$installationFinished = $true
	$timer.Stop()
	$progress.Value = 100
	$status.ForeColor = [Drawing.Color]::FromArgb(0,110,0)
	if ([string]$Result.mode -eq 'update') {
		$status.Text = 'Update completed. Existing applications were preserved.'
		$installButton.Text = 'Updated'
		$message = "ProjectDB Service Manager was updated successfully.`r`n`r`nInstallation directory: $($Result.appDir)`r`nProjectDB: 3.4.0 (x64)`r`nService Manager: v$ServiceManagerVersion`r`nWinSW: 2.12.0 (x64, MIT)`r`n`r`nRegistered applications were preserved and previously running services were restarted."
	} else {
		$status.Text = 'Installation completed. Add applications from ProjectDB Service Manager.'
		$installButton.Text = 'Installed'
		$message = "ProjectDB Service Manager was installed successfully.`r`n`r`nInstallation directory: $($Result.appDir)`r`nProjectDB: 3.4.0 (x64)`r`nService Manager: v$ServiceManagerVersion`r`nWinSW: 2.12.0 (x64, MIT)`r`n`r`nOpen ProjectDB Service Manager and add the required application connections."
	}
	Set-ButtonEnabled $installButton $false $true
	Set-ButtonEnabled $closeButton $true $false
	[Windows.Forms.MessageBox]::Show($form, $message, 'ProjectDB Setup', 'OK', 'Information') | Out-Null
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 250
$timer.Add_Tick({
	try {
		if (Test-Path -LiteralPath $statusPath) {
			$json = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8
			if (-not [string]::IsNullOrWhiteSpace($json) -and $json -ne $lastStatusJson) {
				$lastStatusJson = $json
				$data = $json | ConvertFrom-Json
				$progress.Value = [Math]::Max(0, [Math]::Min(100, [int]$data.percent))
				$status.Text = [string]$data.text
				if ([string]$data.state -eq 'success') {
					Finish-Success $data.result
					return
				}
				if ([string]$data.state -eq 'error') {
					Finish-Error ([string]$data.error)
					return
				}
			}
		}
		if ($null -ne $workerProcess -and $workerProcess.HasExited -and -not $installationFinished) {
			Start-Sleep -Milliseconds 100
			if (Test-Path -LiteralPath $statusPath) {
				try {
					$data = (Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8) | ConvertFrom-Json
					if ([string]$data.state -eq 'success') { Finish-Success $data.result; return }
					if ([string]$data.state -eq 'error') { Finish-Error ([string]$data.error); return }
				} catch {}
			}
			Finish-Error ('Installation worker exited unexpectedly with code ' + $workerProcess.ExitCode + '.')
		}
	} catch {
		# A partially replaced status file may be unreadable for one timer tick. Retry on the next tick.
	}
})

$form.Add_FormClosing({
	param($sender, $e)
	if ($null -ne $workerProcess -and -not $workerProcess.HasExited -and -not $installationFinished) {
		$e.Cancel = $true
		[Windows.Forms.MessageBox]::Show($form, 'Setup is in progress. Wait until it finishes before closing.', 'ProjectDB Setup', 'OK', 'Information') | Out-Null
	}
})

$installButton.Add_Click({
	if ($null -ne $workerProcess -and -not $workerProcess.HasExited) { return }

	$selectedDir = $installDirBox.Text.Trim()
	if ([string]::IsNullOrWhiteSpace($selectedDir)) {
		[Windows.Forms.MessageBox]::Show($form, 'Select an installation directory.', 'ProjectDB Setup', 'OK', 'Warning') | Out-Null
		return
	}
	try { $selectedDir = [IO.Path]::GetFullPath($selectedDir).TrimEnd('\') }
	catch {
		[Windows.Forms.MessageBox]::Show($form, 'The installation directory is invalid.', 'ProjectDB Setup', 'OK', 'Warning') | Out-Null
		return
	}
	if (-not [IO.Path]::IsPathRooted($selectedDir)) {
		[Windows.Forms.MessageBox]::Show($form, 'Select an absolute installation directory.', 'ProjectDB Setup', 'OK', 'Warning') | Out-Null
		return
	}
	$installDirBox.Text = $selectedDir

	$configObject = [ordered]@{ appDir=$selectedDir; mode=$mode }
	$configJson = $configObject | ConvertTo-Json -Compress
	[IO.File]::WriteAllText($configPath, $configJson, (New-Object Text.UTF8Encoding($false)))
	try { & icacls.exe $configPath /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null } catch {}
	Remove-Item -LiteralPath $statusPath -Force -ErrorAction SilentlyContinue
	$lastStatusJson = $null
	$installationFinished = $false

	Set-ControlsEnabled $false
	$status.ForeColor = $UiText
	$status.Text = if ($isUpdate) { 'Starting update...' } else { 'Starting installation...' }
	$progress.Value = 0

	$psExe = Join-Path $PSHOME 'powershell.exe'
	$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $WorkerScript + '"' +
		' -Config "' + $configPath + '"' +
		' -Status "' + $statusPath + '"' +
		' -TraySource "' + $TraySource + '"' +
		' -ServiceControlSource "' + $ServiceControlSource + '"' +
		' -LogWrapperSource "' + $LogWrapperSource + '"' +
		' -ProjectDbArchive "' + $ProjectDbArchive + '"' +
		' -WinSwBinary "' + $WinSwBinary + '"' +
		' -ProjectDbIcon "' + $ProjectDbIcon + '"' +
		' -ThirdPartyNotices "' + $ThirdPartyNotices + '"'
	$psi = New-Object Diagnostics.ProcessStartInfo
	$psi.FileName = $psExe
	$psi.Arguments = $arguments
	$psi.UseShellExecute = $false
	$psi.CreateNoWindow = $true
	$psi.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
	$workerProcess = New-Object Diagnostics.Process
	$workerProcess.StartInfo = $psi
	try {
		[void]$workerProcess.Start()
		$timer.Start()
	} catch {
		Set-ControlsEnabled $true
		Finish-Error $_.Exception.Message
	}
})

[void]$form.ShowDialog()

try {
	if ($null -ne $workerProcess) { $workerProcess.Dispose() }
} catch {}
