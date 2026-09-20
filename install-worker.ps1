param(
	[Parameter(Mandatory=$true)]
	[string]$Config,

	[Parameter(Mandatory=$true)]
	[string]$Status,

	[Parameter(Mandatory=$true)]
	[string]$ServiceManagerVersion,

	[Parameter(Mandatory=$true)]
	[string]$ManagerSource,

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
$ProgressPreference = 'SilentlyContinue'

try {
	$utf8 = New-Object Text.UTF8Encoding($false)
	[Console]::OutputEncoding = $utf8
	$OutputEncoding = $utf8
} catch {}

Add-Type -AssemblyName System.ServiceProcess

$ProjectDbArchiveSha256 = '3878c4eba1337e040aea30b9428b07ba6f6a062d7c518db489bcc47f85213758'
$WinSwSha256 = '05b82d46ad331cc16bdc00de5c6332c1ef818df8ceefcd49c726553209b3a0da'
$ProgramFiles64 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$AppDir = $null
$ServiceRoot = $null
$ManagerExe = $null
$ServiceControlExe = $null
$LogWrapperExe = $null
$UninstallExe = $null
$BinDir = $null
$CommonWinSw = $null
$InstalledIcon = $null
$InstalledNotices = $null
$ServiceStateDir = $null
$StartupTaskName = 'ProjectDB Service Startup'
$UninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectDB'
$InstallerLogDir = $null
$InstallerLog = $null
$PublisherSubject = 'CN=ProjectDB Local Publisher'
$PublisherFriendlyName = 'ProjectDB Local Publisher'
$ProjectDbRegistry = 'HKLM:\SOFTWARE\ProjectDB'
$PublisherThumbprintValue = 'SigningCertificateThumbprint'

function Add-InstallerLog([string]$Text) {
	try {
		New-Item -ItemType Directory -Force -Path $InstallerLogDir | Out-Null
		$line = ('{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $Text)
		[IO.File]::AppendAllText($InstallerLog, $line + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
	} catch {}
}

function Write-Status([int]$Percent, [string]$Text, [string]$State = 'running', $Result = $null, [string]$ErrorText = $null) {
	$obj = [ordered]@{
		state = $State
		percent = [Math]::Max(0, [Math]::Min(100, $Percent))
		text = $Text
	}
	if ($null -ne $Result) { $obj.result = $Result }
	if (-not [string]::IsNullOrWhiteSpace($ErrorText)) { $obj.error = $ErrorText }
	$json = $obj | ConvertTo-Json -Depth 5 -Compress

	# The Setup UI may read this file at the same time. Write it in place;
	# the UI retries on the next timer tick if it sees an incomplete JSON value.
	[IO.File]::WriteAllText($Status, $json, (New-Object Text.UTF8Encoding($false)))
	if ($State -eq 'running') { Add-InstallerLog $Text }
}

function Set-InstallPaths([string]$Directory) {
	$script:AppDir = $Directory
	$script:ServiceRoot = Join-Path $AppDir 'service'
	$script:BinDir = Join-Path $AppDir 'bin'

	$script:ManagerExe = Join-Path $BinDir 'projectdb-service-manager.exe'
	$script:ServiceControlExe = Join-Path $BinDir 'projectdb-service-control.exe'
	$script:LogWrapperExe = Join-Path $BinDir 'projectdb-log-wrapper.exe'
	$script:UninstallExe = Join-Path $BinDir 'projectdb-uninstall.exe'
	$script:CommonWinSw = Join-Path $BinDir 'winsw.exe'


	$script:InstalledIcon = Join-Path $AppDir 'projectdb.ico'
	$script:InstalledNotices = Join-Path $AppDir 'THIRD-PARTY-NOTICES.txt'
	$script:ServiceStateDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'ProjectDB\service-state'
	$script:InstallerLogDir = Join-Path $AppDir 'log\installer'
	$script:InstallerLog = Join-Path $InstallerLogDir 'setup.log'
}

function Assert-EmbeddedFile([string]$Path, [string]$ExpectedSha256, [string]$Label) {
	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw ("Embedded {0} is missing from the setup package." -f $Label) }
	$actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
	if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
		throw ("Embedded {0} failed SHA-256 verification. Expected {1}, got {2}. The setup package may be damaged." -f $Label,$ExpectedSha256,$actual)
	}
}

function Ensure-TrustedPublisherCertificate($Certificate) {
	$tempCer = Join-Path ([IO.Path]::GetTempPath()) ('ProjectDB-Publisher-' + [Guid]::NewGuid().ToString('N') + '.cer')
	try {
		Export-Certificate -Cert $Certificate -FilePath $tempCer -Force | Out-Null
		$thumb = $Certificate.Thumbprint
		if (-not (Get-ChildItem 'Cert:\LocalMachine\Root' | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1)) {
			Import-Certificate -FilePath $tempCer -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
		}
		if (-not (Get-ChildItem 'Cert:\LocalMachine\TrustedPublisher' | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1)) {
			Import-Certificate -FilePath $tempCer -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
		}
	} finally {
		Remove-Item -LiteralPath $tempCer -Force -ErrorAction SilentlyContinue
	}
}

function Get-ProjectDbPublisherCertificate() {
	$cert = $null
	$storedThumb = $null
	try {
		$storedThumb = (Get-ItemProperty -Path $ProjectDbRegistry -Name $PublisherThumbprintValue -ErrorAction Stop).$PublisherThumbprintValue
	} catch {}

	if (-not [string]::IsNullOrWhiteSpace([string]$storedThumb)) {
		try {
			$candidate = Get-Item ('Cert:\LocalMachine\My\' + $storedThumb) -ErrorAction Stop
			if ($candidate.HasPrivateKey -and $candidate.NotAfter -gt (Get-Date).AddDays(30)) { $cert = $candidate }
		} catch {}
	}

	if ($null -eq $cert) {
		$cert = Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object {
			$_.Subject -eq $PublisherSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30)
		} | Sort-Object NotAfter -Descending | Select-Object -First 1
	}

	if ($null -eq $cert) {
		Write-Status 73 'Creating local ProjectDB publisher certificate...'
		$cert = New-SelfSignedCertificate -Type CodeSigningCert `
			-Subject $PublisherSubject `
			-FriendlyName $PublisherFriendlyName `
			-CertStoreLocation 'Cert:\LocalMachine\My' `
			-KeyAlgorithm RSA `
			-KeyLength 3072 `
			-HashAlgorithm SHA256 `
			-KeyExportPolicy NonExportable `
			-NotAfter (Get-Date).AddYears(10)
	}

	Ensure-TrustedPublisherCertificate $cert
	New-Item -Path $ProjectDbRegistry -Force | Out-Null
	New-ItemProperty -Path $ProjectDbRegistry -Name $PublisherThumbprintValue -Value $cert.Thumbprint -PropertyType String -Force | Out-Null
	return $cert
}

function Sign-ProjectDbBinary([string]$Path, $Certificate) {
	if (-not (Test-Path -LiteralPath $Path)) { throw "Cannot sign missing file: $Path" }
	$signature = Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -HashAlgorithm SHA256
	if ($signature.Status.ToString() -ne 'Valid') {
		throw ("Authenticode signing failed for {0}. Status: {1}. {2}" -f $Path, $signature.Status, $signature.StatusMessage)
	}
	$verify = Get-AuthenticodeSignature -LiteralPath $Path
	$subject = if ($null -ne $verify.SignerCertificate) { $verify.SignerCertificate.Subject } else { '' }
	if ($verify.Status.ToString() -ne 'Valid' -or $subject -ne $PublisherSubject) {
		throw ("Authenticode verification failed for {0}. Status: {1}. Signer: {2}" -f $Path, $verify.Status, $subject)
	}
	Add-InstallerLog ('Signed and verified local component: {0}; signer={1}; thumbprint={2}' -f $Path, $subject, $Certificate.Thumbprint)
}

function Get-ServiceState([string]$ServiceId) {
	try { return (Get-Service -Name $ServiceId -ErrorAction Stop).Status.ToString() }
	catch { return $null }
}

function Stop-ServiceSafe([string]$ServiceId) {
	try {
		$svc = Get-Service -Name $ServiceId -ErrorAction Stop
		if ($svc.Status -eq 'Stopped') { return }
		if ($svc.Status -eq 'StartPending') { $svc.WaitForStatus('Running', (New-TimeSpan -Seconds 30)) }
		$svc.Refresh()
		if ($svc.Status -eq 'Running' -or $svc.Status -eq 'Paused') { $svc.Stop() }
		$svc.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
	} catch {
		Add-InstallerLog ('Could not stop service {0}: {1}' -f $ServiceId, $_.Exception.Message)
	}
}

function Start-ServiceSafe([string]$ServiceId) {
	try {
		$svc = Get-Service -Name $ServiceId -ErrorAction Stop
		if ($svc.Status -eq 'Running') { return }
		if ($svc.Status -eq 'StopPending') { $svc.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30)) }
		$svc.Refresh()
		if ($svc.Status -eq 'Stopped') { $svc.Start() }
		$svc.WaitForStatus('Running', (New-TimeSpan -Seconds 30))
	} catch {
		Add-InstallerLog ('Could not start service {0}: {1}' -f $ServiceId, $_.Exception.Message)
	}
}

function Ensure-ServiceStateDirectory {
	New-Item -ItemType Directory -Force -Path $ServiceStateDir | Out-Null
	$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
	& $icacls $ServiceStateDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(M)' | Out-Null
	if ($LASTEXITCODE -ne 0) { throw 'Could not configure ProjectDB service startup state permissions.' }
}

function Register-ProjectDbStartupTask {
	try {
		$service = New-Object -ComObject 'Schedule.Service'
		$service.Connect()

		$folder = $service.GetFolder('\')
		$task = $service.NewTask(0)

		$task.RegistrationInfo.Description = 'Starts ProjectDB applications enabled for automatic startup.'
		$task.Principal.UserId = 'SYSTEM'
		$task.Principal.LogonType = 5
		$task.Principal.RunLevel = 1

		$trigger = $task.Triggers.Create(8)
		$trigger.Enabled = $true
		$trigger.Delay = 'PT20S'

		$action = $task.Actions.Create(0)
		$action.Path = $ServiceControlExe
		$action.Arguments = 'startup'
		$action.WorkingDirectory = $AppDir

		$task.Settings.Enabled = $true
		$task.Settings.StartWhenAvailable = $true
		$task.Settings.DisallowStartIfOnBatteries = $false
		$task.Settings.StopIfGoingOnBatteries = $false
		$task.Settings.ExecutionTimeLimit = 'PT5M'

		# TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_SERVICE_ACCOUNT = 5
		[void]$folder.RegisterTaskDefinition($StartupTaskName, $task, 6, 'SYSTEM', $null, 5, $null)
		Add-InstallerLog ('Registered Windows startup task: ' + $StartupTaskName)
	} catch {
		Add-InstallerLog ('Could not register Windows startup task {0}: {1}' -f $StartupTaskName, $_.Exception.ToString())
		throw ('Could not register the ProjectDB service startup task: ' + $_.Exception.Message)
	}
}

function Stop-ProjectDbServices {
	$running = New-Object System.Collections.Generic.List[object]
	if (-not (Test-Path -LiteralPath $ServiceRoot)) { return $running }

	foreach ($dir in Get-ChildItem -LiteralPath $ServiceRoot -Directory -ErrorAction SilentlyContinue) {
		$xmlPath = Join-Path $dir.FullName 'projectdb-service.xml'
		if (-not (Test-Path -LiteralPath $xmlPath)) { continue }
		try {
			[xml]$doc = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
			$id = [string]$doc.service.id
			if ([string]::IsNullOrWhiteSpace($id)) { continue }
			$statusValue = Get-ServiceState $id
			if ($statusValue -eq 'Running' -or $statusValue -eq 'StartPending' -or $statusValue -eq 'Paused') {
				$running.Add([pscustomobject]@{ Id=$id })
				Stop-ServiceSafe $id
			}
		} catch {
			Add-InstallerLog ('Could not inspect service in {0}: {1}' -f $dir.FullName, $_.Exception.Message)
		}
	}
	return $running
}

function Restart-PreviousServices($Services) {
	foreach ($item in $Services) {
		Start-ServiceSafe $item.Id
	}
}

function Compile-Manager([string]$SourcePath, [string]$OutputPath, [string]$IconPath) {
	$source = [IO.File]::ReadAllText($SourcePath, [Text.Encoding]::UTF8)
	$numericVersion = $ServiceManagerVersion -replace '-.*$', ''
	$versionParts = @($numericVersion.Split('.') | ForEach-Object { [int]$_ })
	while ($versionParts.Count -lt 4) { $versionParts += 0 }
	if ($versionParts.Count -gt 4) { $versionParts = $versionParts[0..3] }
	$fileVersion = ($versionParts -join '.')
	$versionSource = @"
using System.Reflection;
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$ServiceManagerVersion")]
"@
	[string[]]$sources = @($source, $versionSource)

	$tempOutput = $OutputPath + '.new.' + [Guid]::NewGuid().ToString('N') + '.exe'
	$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
	try {
		$params = New-Object System.CodeDom.Compiler.CompilerParameters
		$params.GenerateExecutable = $true
		$params.GenerateInMemory = $false
		$params.OutputAssembly = $tempOutput
		$params.CompilerOptions = ('/target:winexe /optimize+ /win32icon:"' + $IconPath + '"')
		@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.ServiceProcess.dll','System.Xml.dll','System.Web.Extensions.dll') | ForEach-Object { [void]$params.ReferencedAssemblies.Add($_) }
		$result = $provider.CompileAssemblyFromSource($params, $sources)
		if ($result.Errors.HasErrors) {
			$errors = ($result.Errors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
			throw "Service Manager compilation failed:`r`n$errors"
		}
		Copy-Item -LiteralPath $tempOutput -Destination $OutputPath -Force
	} finally {
		$provider.Dispose()
		Remove-Item -LiteralPath $tempOutput -Force -ErrorAction SilentlyContinue
	}
}

function Compile-LogWrapper([string]$SourcePath, [string]$OutputPath, [string]$IconPath) {
	$source = [IO.File]::ReadAllText($SourcePath, [Text.Encoding]::UTF8)
	$tempOutput = $OutputPath + '.new.' + [Guid]::NewGuid().ToString('N') + '.exe'
	$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
	try {
		$params = New-Object System.CodeDom.Compiler.CompilerParameters
		$params.GenerateExecutable = $true
		$params.GenerateInMemory = $false
		$params.OutputAssembly = $tempOutput
		$params.CompilerOptions = ('/target:exe /optimize+ /win32icon:"' + $IconPath + '"')
		@('System.dll','System.Core.dll') | ForEach-Object { [void]$params.ReferencedAssemblies.Add($_) }
		$result = $provider.CompileAssemblyFromSource($params, $source)
		if ($result.Errors.HasErrors) {
			$errors = ($result.Errors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
			throw "ProjectDB log wrapper compilation failed:`r`n$errors"
		}
		Copy-Item -LiteralPath $tempOutput -Destination $OutputPath -Force
	} finally {
		$provider.Dispose()
		Remove-Item -LiteralPath $tempOutput -Force -ErrorAction SilentlyContinue
	}
}

function Update-ExistingWinSwWrappers {
	if (-not (Test-Path -LiteralPath $ServiceRoot)) { return }
	foreach ($dir in Get-ChildItem -LiteralPath $ServiceRoot -Directory -ErrorAction SilentlyContinue) {
		$wrapper = Join-Path $dir.FullName 'projectdb-service.exe'
		$xml = Join-Path $dir.FullName 'projectdb-service.xml'
		if (-not (Test-Path -LiteralPath $xml -PathType Leaf)) { continue }
		Copy-Item -LiteralPath $CommonWinSw -Destination $wrapper -Force
	}
}

function Compile-ServiceControl([string]$SourcePath, [string]$OutputPath, [string]$IconPath) {
	$source = [IO.File]::ReadAllText($SourcePath, [Text.Encoding]::UTF8)
	$tempOutput = $OutputPath + '.new.' + [Guid]::NewGuid().ToString('N') + '.exe'
	$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
	try {
		$params = New-Object System.CodeDom.Compiler.CompilerParameters
		$params.GenerateExecutable = $true
		$params.GenerateInMemory = $false
		$params.OutputAssembly = $tempOutput
		$params.CompilerOptions = ('/target:winexe /optimize+ /win32icon:"' + $IconPath + '"')
		@('System.dll','System.Core.dll','System.Windows.Forms.dll','System.ServiceProcess.dll','System.Xml.dll','System.Web.Extensions.dll') | ForEach-Object { [void]$params.ReferencedAssemblies.Add($_) }
		$result = $provider.CompileAssemblyFromSource($params, $source)
		if ($result.Errors.HasErrors) {
			$errors = ($result.Errors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
			throw "Service control helper compilation failed:`r`n$errors"
		}
		Copy-Item -LiteralPath $tempOutput -Destination $OutputPath -Force
	} finally {
		$provider.Dispose()
		Remove-Item -LiteralPath $tempOutput -Force -ErrorAction SilentlyContinue
	}
}

$previousRunning = $null
$tempRoot = $null
$configObject = $null

try {
	$configObject = Get-Content -LiteralPath $Config -Raw -Encoding UTF8 | ConvertFrom-Json
	$requestedDir = [string]$configObject.appDir
	$mode = [string]$configObject.mode
	Remove-Item -LiteralPath $Config -Force -ErrorAction SilentlyContinue
	$configObject = $null

	if ([string]::IsNullOrWhiteSpace($requestedDir)) { throw 'Installation directory is empty.' }
	try { $requestedDir = [IO.Path]::GetFullPath($requestedDir).TrimEnd('\') }
	catch { throw 'Installation directory is invalid.' }
	if ($requestedDir -notmatch '^[A-Za-z]:\\') { throw 'Installation directory must be on a local Windows drive.' }
	if ($mode -ne 'install' -and $mode -ne 'update') { throw 'Installation mode is invalid.' }

	Set-InstallPaths $requestedDir
	$prepareText = if ($mode -eq 'update') { 'Preparing update...' } else { 'Preparing installation...' }
	Write-Status 1 $prepareText

	$appExe = Join-Path $AppDir 'projectdb.exe'

	$libraryDir = Join-Path $AppDir 'lib'
	$libraryFile = Join-Path $libraryDir 'app.so'
	$libraryMeta = Join-Path $libraryDir 'app.so.meta.json'
	$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('ProjectDB-Worker-' + [Guid]::NewGuid().ToString('N'))
	$projectZip = $ProjectDbArchive
	$extractDir = Join-Path $tempRoot 'projectdb'
	$winSwTemp = $WinSwBinary
	$pinnedLibraryBackup = Join-Path $tempRoot 'pinned-app.so'
	$pinnedLibraryMetaBackup = Join-Path $tempRoot 'pinned-app.so.meta.json'
	New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

	Write-Status 7 'Verifying offline package...'
	Write-Status 12 'Verifying embedded ProjectDB 3.4.0...'
	Assert-EmbeddedFile $projectZip $ProjectDbArchiveSha256 'ProjectDB archive'
	Write-Status 20 'Verifying embedded WinSW 2.12.0...'
	Assert-EmbeddedFile $winSwTemp $WinSwSha256 'WinSW binary'
	if (-not (Test-Path -LiteralPath $ThirdPartyNotices -PathType Leaf)) { throw 'Third-party license notices are missing from the setup package.' }

	Write-Status 28 'Extracting embedded ProjectDB...'
	Expand-Archive -LiteralPath $projectZip -DestinationPath $extractDir -Force
	$releaseExe = Get-ChildItem -LiteralPath $extractDir -Filter 'projectdb.exe' -File -Recurse | Select-Object -First 1
	if ($null -eq $releaseExe) { throw 'projectdb.exe was not found in the release archive.' }

	# Preserve a locally pinned app.so across ProjectDB binary updates.
	if ($mode -eq 'update' -and (Test-Path -LiteralPath $libraryFile)) {
		Copy-Item -LiteralPath $libraryFile -Destination $pinnedLibraryBackup -Force
		if (Test-Path -LiteralPath $libraryMeta) { Copy-Item -LiteralPath $libraryMeta -Destination $pinnedLibraryMetaBackup -Force }
		Add-InstallerLog 'Local lib\app.so override detected and will be preserved.'
	}

	if ($mode -eq 'update') {
		Write-Status 38 'Stopping ProjectDB Service Manager and active ProjectDB services...'
		$managerProcesses = @(Get-Process -Name 'projectdb-service-manager' -ErrorAction SilentlyContinue)
		$managerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
		foreach ($managerProcess in $managerProcesses) {
			try { $managerProcess.WaitForExit(5000) } catch {}
		}

		$previousRunning = @(Stop-ProjectDbServices)
		Add-InstallerLog ('Active ProjectDB services preserved for update: {0}' -f $previousRunning.Count)
	}

	Write-Status 48 'Installing ProjectDB files...'
	New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
	Get-ChildItem -LiteralPath $releaseExe.Directory.FullName -Force | Copy-Item -Destination $AppDir -Recurse -Force
	Copy-Item -LiteralPath $ProjectDbIcon -Destination $InstalledIcon -Force
	Copy-Item -LiteralPath $ThirdPartyNotices -Destination $InstalledNotices -Force
	if (-not (Test-Path -LiteralPath $appExe)) { throw "File was not installed: $appExe" }
	if (Test-Path -LiteralPath $pinnedLibraryBackup) {
		New-Item -ItemType Directory -Force -Path $libraryDir | Out-Null
		Copy-Item -LiteralPath $pinnedLibraryBackup -Destination $libraryFile -Force
		if (Test-Path -LiteralPath $pinnedLibraryMetaBackup) { Copy-Item -LiteralPath $pinnedLibraryMetaBackup -Destination $libraryMeta -Force }
		Add-InstallerLog 'Pinned lib\app.so restored after ProjectDB file update.'
	}

	Write-Status 60 'Installing Windows service runtime...'
	New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
	Copy-Item -LiteralPath $winSwTemp -Destination $CommonWinSw -Force
	if ($mode -eq 'update') { Update-ExistingWinSwWrappers }

	Write-Status 70 'Preparing local ProjectDB publisher...'
	$publisherCertificate = Get-ProjectDbPublisherCertificate

	Write-Status 76 'Installing ProjectDB Service Manager...'
	Compile-ServiceControl $ServiceControlSource $ServiceControlExe $InstalledIcon
	Compile-LogWrapper $LogWrapperSource $LogWrapperExe $InstalledIcon
	Copy-Item -LiteralPath $ServiceControlExe -Destination $UninstallExe -Force
	Compile-Manager $ManagerSource $ManagerExe $InstalledIcon
	Sign-ProjectDbBinary $ServiceControlExe $publisherCertificate
	Sign-ProjectDbBinary $LogWrapperExe $publisherCertificate
	Sign-ProjectDbBinary $UninstallExe $publisherCertificate
	Sign-ProjectDbBinary $ManagerExe $publisherCertificate
	Ensure-ServiceStateDirectory
	Register-ProjectDbStartupTask
	$publisherCertificate = $null

	$runPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
	New-Item -Path $runPath -Force | Out-Null
	New-ItemProperty -Path $runPath -Name 'ProjectDB Service Manager' -Value ('"' + $ManagerExe + '"') -PropertyType String -Force | Out-Null

	# Create standard launch shortcuts so the Manager can be reopened after it is exited.
	try {
		$shell = New-Object -ComObject WScript.Shell
		$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
		$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
		$startFolder = Join-Path $programs 'ProjectDB'
		New-Item -ItemType Directory -Force -Path $startFolder | Out-Null
		$shortcutPaths = @(
			(Join-Path $desktop 'ProjectDB Service Manager.lnk'),
			(Join-Path $startFolder 'ProjectDB Service Manager.lnk')
		)
		foreach ($shortcutPath in $shortcutPaths) {
			$shortcut = $shell.CreateShortcut($shortcutPath)
			$shortcut.TargetPath = $ManagerExe
			$shortcut.WorkingDirectory = $AppDir
			$shortcut.IconLocation = $InstalledIcon
			$shortcut.Description = 'ProjectDB Service Manager'
			$shortcut.Save()
		}
	} catch { Add-InstallerLog ('Could not create Service Manager shortcuts: ' + $_.Exception.Message) }

	# Register standard Windows uninstall entry.
	New-Item -Path $UninstallKey -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'DisplayName' -Value 'ProjectDB Service Manager' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'DisplayVersion' -Value $ServiceManagerVersion -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'Publisher' -Value 'ProjectDB' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'InstallLocation' -Value $AppDir -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'DisplayIcon' -Value $InstalledIcon -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'UninstallString' -Value ('"' + $UninstallExe + '" uninstall-all') -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'URLInfoAbout' -Value 'https://github.com/pavel-elblaus/projectdb-service-manager' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'ProjectDBVersion' -Value '3.4.0' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'Architecture' -Value 'x64' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null

	Write-Status 87 'Finalizing installation...'

	if ($null -ne $previousRunning -and $previousRunning.Count -gt 0) {
		Write-Status 92 'Restarting previously running ProjectDB services...'
		Restart-PreviousServices $previousRunning
		Start-Sleep -Milliseconds 800
		foreach ($item in $previousRunning) {
			$state = Get-ServiceState $item.Id
			Add-InstallerLog ('Restarted service {0}; state={1}' -f $item.Id, $state)
		}
	}

	Write-Status 97 'Starting ProjectDB Service Manager...'
	try { Start-Process -FilePath $ManagerExe -WorkingDirectory $AppDir } catch {}

	$preservedCount = if ($null -eq $previousRunning) { 0 } else { $previousRunning.Count }
	$result = [ordered]@{ mode=$mode; appDir=$AppDir; restartedServices=$preservedCount; publisher=$PublisherFriendlyName }
	if ($mode -eq 'update') {
		Add-InstallerLog ('Installer v{0} update completed. directory={1}; restartedServices={2}' -f $ServiceManagerVersion, $AppDir, $preservedCount)
		Write-Status 100 'Update completed. Existing applications were preserved.' 'success' $result
	} else {
		Add-InstallerLog ('Installer v{0} installation completed. directory={1}' -f $ServiceManagerVersion, $AppDir)
		Write-Status 100 'Installation completed. Add applications from ProjectDB Service Manager.' 'success' $result
	}
	exit 0
}
catch {
	if ($null -ne $previousRunning) {
		try { Restart-PreviousServices $previousRunning } catch {}
	}
	$errorText = $_.Exception.Message
	Add-InstallerLog ('ERROR: ' + $_.Exception.ToString())
	try { Write-Status 0 ('Setup error: ' + $errorText) 'error' $null $errorText } catch {}
	exit 1
}
finally {
	if (-not [string]::IsNullOrWhiteSpace($tempRoot)) {
		try { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}
	}
}
