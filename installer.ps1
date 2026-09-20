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

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace ProjectDB.Setup
{
	public static class Taskbar
	{
		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		public static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
	}
}
'@
try { [void][ProjectDB.Setup.Taskbar]::SetCurrentProcessExplicitAppUserModelID('ProjectDB.ServiceManager.Setup') } catch {}


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
$SetupCaption = "ProjectDB Setup $ServiceManagerVersion"
$ProjectDbVersion = '3.4.0'
$ProjectDbArchitecture = 'x64'
$ProjectDbLicense = 'MIT'
$ServiceManagerArchitecture = 'x64'
$ServiceManagerLicense = 'MIT'
$WinSwVersion = '2.12.0'
$WinSwArchitecture = 'x64'
$WinSwLicense = 'MIT'

# Palette matching ProjectDB Service Manager.
$UiSurface = [Drawing.Color]::White
$UiBorder = [Drawing.Color]::FromArgb(214,225,228)
$UiText = [Drawing.Color]::FromArgb(24,41,48)
$UiMuted = [Drawing.Color]::FromArgb(101,119,126)
$UiAccent = [Drawing.Color]::FromArgb(34,160,171)
$UiAccentHover = [Drawing.Color]::FromArgb(26,142,153)
$UiDisabled = [Drawing.Color]::FromArgb(240,244,245)
$UiDisabledText = [Drawing.Color]::FromArgb(155,167,171)
$UiDivider = [Drawing.Color]::FromArgb(235,240,242)

function Get-ExistingInstallDir() {
	try {
		$item = Get-ItemProperty -Path $UninstallKey -ErrorAction Stop
		$candidate = [string]$item.InstallLocation
		if (-not [string]::IsNullOrWhiteSpace($candidate)) {
			$projectDb = Join-Path $candidate 'projectdb.exe'
			if ([IO.File]::Exists($projectDb)) {
				return $candidate.TrimEnd('\')
			}
		}
	} catch {}

	$defaultProjectDb = Join-Path $DefaultAppDir 'projectdb.exe'
	if ([IO.File]::Exists($defaultProjectDb)) {
		return $DefaultAppDir
	}
	return $null
}

function New-RoundedPath([Drawing.Rectangle]$Rect, [int]$Radius) {
	$path = New-Object Drawing.Drawing2D.GraphicsPath
	$diameter = [Math]::Max(2, [Math]::Min($Radius * 2, [Math]::Min($Rect.Width, $Rect.Height)))
	$arc = New-Object Drawing.Rectangle($Rect.X, $Rect.Y, $diameter, $diameter)
	$path.AddArc($arc, 180, 90)
	$arc.X = $Rect.Right - $diameter
	$path.AddArc($arc, 270, 90)
	$arc.Y = $Rect.Bottom - $diameter
	$path.AddArc($arc, 0, 90)
	$arc.X = $Rect.X
	$path.AddArc($arc, 90, 90)
	$path.CloseFigure()
	return $path
}

function New-RightRoundedPath([Drawing.Rectangle]$Rect, [int]$Radius) {
	$path = New-Object Drawing.Drawing2D.GraphicsPath
	$diameter = [Math]::Max(2, [Math]::Min($Radius * 2, [Math]::Min($Rect.Width, $Rect.Height)))
	$left = [int]$Rect.Left
	$top = [int]$Rect.Top
	$right = [int]$Rect.Right
	$bottom = [int]$Rect.Bottom

	$arc = New-Object Drawing.Rectangle
	$arc.X = $right - $diameter
	$arc.Y = $top
	$arc.Width = $diameter
	$arc.Height = $diameter

	$path.StartFigure()
	$path.AddLine($left, $top, $right - $Radius, $top)
	$path.AddArc($arc, 270, 90)
	$arc.Y = $bottom - $diameter
	$path.AddArc($arc, 0, 90)
	$path.AddLine($right - $Radius, $bottom, $left, $bottom)
	$path.CloseFigure()
	return $path
}

function New-ModernButton([string]$Text, [bool]$Primary, [int]$Radius = 6, [bool]$RightOnly = $false) {
	$button = New-Object System.Windows.Forms.Button
	$button.Text = $Text
	$button.Size = New-Object Drawing.Size(112, 40)
	$button.FlatStyle = [Windows.Forms.FlatStyle]::Flat
	$button.FlatAppearance.BorderSize = 0
	$button.UseVisualStyleBackColor = $false
	$button.Cursor = [Windows.Forms.Cursors]::Hand
	$button.Font = New-Object Drawing.Font('Segoe UI', 9)
	$button.TextAlign = [Drawing.ContentAlignment]::MiddleCenter
	$button.UseCompatibleTextRendering = $false
	$button.Padding = New-Object Windows.Forms.Padding(0)
	$button.Tag = [pscustomobject]@{ Primary = $Primary; Hover = $false; Radius = $Radius; RightOnly = $RightOnly }

	$button.Add_MouseEnter({
		if ($this.Enabled) {
			$this.Tag.Hover = $true
			$this.Invalidate()
		}
	})
	$button.Add_MouseLeave({
		$this.Tag.Hover = $false
		$this.Invalidate()
	})
	$button.Add_EnabledChanged({ $this.Invalidate() })
	$button.Add_Paint({
		param($sender, $e)

		$e.Graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
		$parentColor = if ($null -ne $this.Parent) { $this.Parent.BackColor } else { $UiSurface }
		$e.Graphics.Clear($parentColor)

		$rect = New-Object Drawing.Rectangle(0, 0, [Math]::Max(1, $this.Width - 1), [Math]::Max(1, $this.Height - 1))
		if (-not $this.Enabled) {
			$back = $UiDisabled
			$border = [Drawing.Color]::FromArgb(224,231,233)
			$textColor = $UiDisabledText
		} elseif ($this.Tag.Primary) {
			$back = if ($this.Tag.Hover) { $UiAccentHover } else { $UiAccent }
			$border = $back
			$textColor = [Drawing.Color]::White
		} else {
			$back = if ($this.Tag.Hover) { [Drawing.Color]::FromArgb(247,251,252) } else { $UiSurface }
			$border = $UiBorder
			$textColor = $UiText
		}

		$path = if ($this.Tag.RightOnly) {
			New-RightRoundedPath $rect ([int]$this.Tag.Radius)
		} else {
			New-RoundedPath $rect ([int]$this.Tag.Radius)
		}
		$brush = New-Object Drawing.SolidBrush($back)
		$pen = New-Object Drawing.Pen($border)
		try {
			$e.Graphics.FillPath($brush, $path)
			$e.Graphics.DrawPath($pen, $path)
			$flags = [Windows.Forms.TextFormatFlags]::HorizontalCenter -bor
				[Windows.Forms.TextFormatFlags]::VerticalCenter -bor
				[Windows.Forms.TextFormatFlags]::SingleLine -bor
				[Windows.Forms.TextFormatFlags]::EndEllipsis
			[Windows.Forms.TextRenderer]::DrawText($e.Graphics, $this.Text, $this.Font, $this.ClientRectangle, $textColor, $flags)
		} finally {
			$pen.Dispose()
			$brush.Dispose()
			$path.Dispose()
		}
	})
	return $button
}

function Set-ButtonEnabled($Button, [bool]$Enabled, [bool]$Primary) {
	$Button.Enabled = $Enabled
	$Button.Cursor = if ($Enabled) { [Windows.Forms.Cursors]::Hand } else { [Windows.Forms.Cursors]::Default }
	$Button.Invalidate()
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
$form.Text = $SetupCaption
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ClientSize = New-Object Drawing.Size(600, 516)
$form.Font = New-Object Drawing.Font('Segoe UI', 9)
$form.BackColor = $UiSurface
$form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::Dpi
$form.ShowInTaskbar = $true
$form.TopMost = $true
try { $form.Icon = New-Object Drawing.Icon($ProjectDbIcon) } catch {}
$form.Add_Shown({
	$form.Activate()
	$form.BringToFront()
	$form.TopMost = $false

	# Do not auto-focus/select the installation path on first launch.
	if ($null -ne $installButton) {
		$form.ActiveControl = $installButton
		$installButton.Focus() | Out-Null
	}
	if (-not $isUpdate -and $installDirBox -is [Windows.Forms.TextBox]) {
		$installDirBox.SelectionLength = 0
	}
})

$title = Add-Label $actionTitle 28 22 540 34 $UiText (New-Object Drawing.Font('Segoe UI Semibold', 15))

$subtitleSeparator = [char]0x00B7
$subtitleText = "Service Manager $ServiceManagerVersion  $subtitleSeparator  ProjectDB $ProjectDbVersion  $subtitleSeparator  Windows $ProjectDbArchitecture"
$subtitle = Add-Label $subtitleText 30 58 538 24 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.8))

$noteText = if ($isUpdate) {
	'Existing installation detected. Registered applications and local app.so are preserved.'
} else {
	'Applications are configured after setup in ProjectDB Service Manager.'
}
$note = Add-Label $noteText 30 82 538 24 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.8))

$dirLabel = Add-Label 'Installation directory' 30 121 538 22 $UiText

$dirBorder = New-Object System.Windows.Forms.Panel
$dirBorder.Location = New-Object Drawing.Point(32, 146)
$dirBorder.Size = New-Object Drawing.Size(414, 40)
$dirBorder.BackColor = $UiBorder
$form.Controls.Add($dirBorder)

$dirInner = New-Object System.Windows.Forms.Panel
$dirInner.Location = New-Object Drawing.Point(1, 1)
$dirInner.Size = New-Object Drawing.Size(412, 38)
$dirInner.BackColor = $UiSurface
$dirBorder.Controls.Add($dirInner)

if ($isUpdate) {
	$installDirBox = New-Object System.Windows.Forms.Label
	$installDirBox.Text = $AppDir
	$installDirBox.Location = New-Object Drawing.Point(10, 1)
	$installDirBox.Size = New-Object Drawing.Size(392, 36)
	$installDirBox.Font = New-Object Drawing.Font('Segoe UI', 9.5)
	$installDirBox.BackColor = $UiSurface
	$installDirBox.ForeColor = $UiText
	$installDirBox.TextAlign = [Drawing.ContentAlignment]::MiddleLeft
	$installDirBox.Cursor = [Windows.Forms.Cursors]::Default
	$dirInner.Cursor = [Windows.Forms.Cursors]::Default
	$dirInner.Controls.Add($installDirBox)
} else {
	$installDirBox = New-Object System.Windows.Forms.TextBox
	$installDirBox.Text = $AppDir
	$installDirBox.BorderStyle = [Windows.Forms.BorderStyle]::None
	$installDirBox.Location = New-Object Drawing.Point(10, 9)
	$installDirBox.Size = New-Object Drawing.Size(392, 20)
	$installDirBox.Font = New-Object Drawing.Font('Segoe UI', 9.5)
	$installDirBox.BackColor = $UiSurface
	$installDirBox.ForeColor = $UiText
	$dirInner.Controls.Add($installDirBox)

	$installDirBox.Add_GotFocus({ $dirBorder.BackColor = $UiAccent })
	$installDirBox.Add_LostFocus({ $dirBorder.BackColor = $UiBorder })
	$dirInner.Add_Click({ $installDirBox.Focus() })
	$dirInner.Cursor = [Windows.Forms.Cursors]::IBeam
}

$browseButton = New-ModernButton 'Browse...' $false 6 $true
$browseButton.Location = New-Object Drawing.Point(456, 146)
$browseButton.Size = New-Object Drawing.Size(112, 40)
$form.Controls.Add($browseButton)

$componentsLabel = Add-Label 'Installed components' 30 207 538 22 $UiText

$componentsBorder = New-Object System.Windows.Forms.Panel
$componentsBorder.Location = New-Object Drawing.Point(32, 232)
$componentsBorder.Size = New-Object Drawing.Size(536, 126)
$componentsBorder.BackColor = $UiBorder
$form.Controls.Add($componentsBorder)

$componentsPanel = New-Object System.Windows.Forms.Panel
$componentsPanel.Location = New-Object Drawing.Point(1, 1)
$componentsPanel.Size = New-Object Drawing.Size(534, 124)
$componentsPanel.BackColor = $UiSurface
$componentsBorder.Controls.Add($componentsPanel)

function Add-ComponentCell([string]$Text, [int]$X, [int]$Y, [int]$Width, [int]$Height, $Color, $Alignment, $Font = $null) {
	$label = New-Object System.Windows.Forms.Label
	$label.Text = $Text
	$label.Location = New-Object Drawing.Point($X, $Y)
	$label.Size = New-Object Drawing.Size($Width, $Height)
	$label.ForeColor = $Color
	$label.TextAlign = $Alignment
	if ($null -ne $Font) { $label.Font = $Font }
	$componentsPanel.Controls.Add($label)
	return $label
}

function Add-ComponentSeparator([int]$Y) {
	$line = New-Object System.Windows.Forms.Panel
	$line.Location = New-Object Drawing.Point(12, $Y)
	$line.Size = New-Object Drawing.Size(510, 1)
	$line.BackColor = $UiDivider
	$componentsPanel.Controls.Add($line)
}

$headerFont = New-Object Drawing.Font('Segoe UI Semibold', 8.5)
$rowFont = New-Object Drawing.Font('Segoe UI', 9)

Add-ComponentCell 'Component' 12 7 252 24 $UiMuted ([Drawing.ContentAlignment]::MiddleLeft) $headerFont | Out-Null
Add-ComponentCell 'Version' 264 7 100 24 $UiMuted ([Drawing.ContentAlignment]::MiddleCenter) $headerFont | Out-Null
Add-ComponentCell 'Architecture' 364 7 88 24 $UiMuted ([Drawing.ContentAlignment]::MiddleCenter) $headerFont | Out-Null
Add-ComponentCell 'License' 452 7 70 24 $UiMuted ([Drawing.ContentAlignment]::MiddleCenter) $headerFont | Out-Null

Add-ComponentSeparator 32

Add-ComponentCell 'ProjectDB' 12 35 252 27 $UiText ([Drawing.ContentAlignment]::MiddleLeft) $rowFont | Out-Null
Add-ComponentCell $ProjectDbVersion 264 35 100 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $ProjectDbArchitecture 364 35 88 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $ProjectDbLicense 452 35 70 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null

Add-ComponentSeparator 63

Add-ComponentCell 'ProjectDB Service Manager' 12 66 252 27 $UiText ([Drawing.ContentAlignment]::MiddleLeft) $rowFont | Out-Null
Add-ComponentCell $ServiceManagerVersion 264 66 100 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $ServiceManagerArchitecture 364 66 88 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $ServiceManagerLicense 452 66 70 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null

Add-ComponentSeparator 94

Add-ComponentCell 'Windows Service Wrapper (WinSW)' 12 97 252 27 $UiText ([Drawing.ContentAlignment]::MiddleLeft) $rowFont | Out-Null
Add-ComponentCell $WinSwVersion 264 97 100 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $WinSwArchitecture 364 97 88 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null
Add-ComponentCell $WinSwLicense 452 97 70 27 $UiText ([Drawing.ContentAlignment]::MiddleCenter) $rowFont | Out-Null

$licenseNote = Add-Label 'License information for bundled components is installed with the application.' 30 364 538 22 $UiMuted (New-Object Drawing.Font('Segoe UI', 8.5))

$progress = New-Object System.Windows.Forms.Panel
$progress.Location = New-Object Drawing.Point(32, 397)
$progress.Size = New-Object Drawing.Size(536, 10)
$progress.BackColor = $UiSurface
$progress.Tag = 0
try {
	$doubleBufferedProperty = [Windows.Forms.Control].GetProperty(
		'DoubleBuffered',
		[Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic
	)
	if ($null -ne $doubleBufferedProperty) {
		$doubleBufferedProperty.SetValue($progress, $true, $null)
	}
} catch {}
$progress.Add_Paint({
	param($sender, $e)

	$trackBrush = New-Object Drawing.SolidBrush($UiDivider)
	try {
		$e.Graphics.FillRectangle($trackBrush, 0, 0, $this.Width, $this.Height)
	} finally {
		$trackBrush.Dispose()
	}

	$percent = [Math]::Max(0, [Math]::Min(100, [int]$this.Tag))
	if ($percent -gt 0) {
		$fillWidth = [Math]::Max(1, [int][Math]::Round($this.Width * $percent / 100.0))
		$fillWidth = [Math]::Min($this.Width, $fillWidth)
		$fillBrush = New-Object Drawing.SolidBrush($UiAccent)
		try {
			$e.Graphics.FillRectangle($fillBrush, 0, 0, $fillWidth, $this.Height)
		} finally {
			$fillBrush.Dispose()
		}
	}
})
$form.Controls.Add($progress)

function Set-ProgressValue([int]$Value) {
	$nextValue = [Math]::Max(0, [Math]::Min(100, $Value))
	if ([int]$progress.Tag -eq $nextValue) { return }
	$progress.Tag = $nextValue
	$progress.Invalidate()
}

$status = Add-Label $readyText 30 419 538 30 $UiText

$installButtonText = if ($isUpdate) { 'Update' } else { 'Install' }
$installButton = New-ModernButton $installButtonText $true
$installButton.Location = New-Object Drawing.Point(336, 454)
$form.Controls.Add($installButton)

$closeButton = New-ModernButton 'Close' $false
$closeButton.Location = New-Object Drawing.Point(456, 454)
$closeButton.Size = New-Object Drawing.Size(112, 40)
$closeButton.Add_Click({ $form.Close() })
$form.Controls.Add($closeButton)
$form.AcceptButton = $installButton
$form.CancelButton = $closeButton

if ($isUpdate) {
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
	[Windows.Forms.MessageBox]::Show($form, ($Message + "`r`n`r`nLog: " + (Get-InstallerLog)), "$SetupCaption - Error", 'OK', 'Error') | Out-Null
}

function Finish-Success($Result) {
	$installationFinished = $true
	$timer.Stop()
	Set-ProgressValue 100
	$status.ForeColor = [Drawing.Color]::FromArgb(0,110,0)
	if ([string]$Result.mode -eq 'update') {
		$status.Text = 'Update completed. Existing applications were preserved.'
		$installButton.Text = 'Updated'
		$message = "ProjectDB Service Manager was updated successfully.`r`n`r`nInstallation directory: $($Result.appDir)`r`nProjectDB: $ProjectDbVersion ($ProjectDbArchitecture, $ProjectDbLicense)`r`nService Manager: $ServiceManagerVersion ($ServiceManagerArchitecture, $ServiceManagerLicense)`r`nWinSW: $WinSwVersion ($WinSwArchitecture, $WinSwLicense)`r`n`r`nRegistered applications were preserved and previously running services were restarted."
	} else {
		$status.Text = 'Installation completed. Add applications from ProjectDB Service Manager.'
		$installButton.Text = 'Installed'
		$message = "ProjectDB Service Manager was installed successfully.`r`n`r`nInstallation directory: $($Result.appDir)`r`nProjectDB: $ProjectDbVersion ($ProjectDbArchitecture, $ProjectDbLicense)`r`nService Manager: $ServiceManagerVersion ($ServiceManagerArchitecture, $ServiceManagerLicense)`r`nWinSW: $WinSwVersion ($WinSwArchitecture, $WinSwLicense)`r`n`r`nOpen ProjectDB Service Manager and add the required application connections."
	}
	Set-ButtonEnabled $installButton $false $true
	Set-ButtonEnabled $closeButton $true $false
	[Windows.Forms.MessageBox]::Show($form, $message, $SetupCaption, 'OK', 'Information') | Out-Null
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
				Set-ProgressValue ([int]$data.percent)
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
		[Windows.Forms.MessageBox]::Show($form, 'Setup is in progress. Wait until it finishes before closing.', $SetupCaption, 'OK', 'Information') | Out-Null
	}
})

$installButton.Add_Click({
	if ($null -ne $workerProcess -and -not $workerProcess.HasExited) { return }

	$selectedDir = $installDirBox.Text.Trim()
	if ([string]::IsNullOrWhiteSpace($selectedDir)) {
		[Windows.Forms.MessageBox]::Show($form, 'Select an installation directory.', $SetupCaption, 'OK', 'Warning') | Out-Null
		return
	}
	try { $selectedDir = [IO.Path]::GetFullPath($selectedDir).TrimEnd('\') }
	catch {
		[Windows.Forms.MessageBox]::Show($form, 'The installation directory is invalid.', $SetupCaption, 'OK', 'Warning') | Out-Null
		return
	}
	if (-not [IO.Path]::IsPathRooted($selectedDir)) {
		[Windows.Forms.MessageBox]::Show($form, 'Select an absolute installation directory.', $SetupCaption, 'OK', 'Warning') | Out-Null
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
	Set-ProgressValue 0

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
