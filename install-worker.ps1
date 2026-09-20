param(
	[Parameter(Mandatory=$true)]
	[string]$Config,

	[Parameter(Mandatory=$true)]
	[string]$Status,

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
$ProgressPreference = 'SilentlyContinue'

try {
	$utf8 = New-Object Text.UTF8Encoding($false)
	[Console]::OutputEncoding = $utf8
	$OutputEncoding = $utf8
} catch {}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.ServiceProcess

$ProjectDbArchiveSha256 = '3878c4eba1337e040aea30b9428b07ba6f6a062d7c518db489bcc47f85213758'
$WinSwSha256 = '05b82d46ad331cc16bdc00de5c6332c1ef818df8ceefcd49c726553209b3a0da'
$ServiceManagerVersion = '0.1.0-dev'
$ProgramFiles64 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$AppDir = $null
$ServiceRoot = $null
$ManagerExe = $null
$LegacyTrayExe = $null
$LegacyManagerExe = $null
$LegacyServiceControlExe = $null
$LegacyLogWrapperExe = $null
$LegacyUninstallExe = $null
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
	$tmp = $Status + '.tmp.' + [Guid]::NewGuid().ToString('N')
	[IO.File]::WriteAllText($tmp, $json, (New-Object Text.UTF8Encoding($false)))
	Move-Item -LiteralPath $tmp -Destination $Status -Force
	if ($State -eq 'running') { Add-InstallerLog $Text }
}

function Escape-Xml([string]$Text) {
	return [Security.SecurityElement]::Escape($Text)
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

	$script:LegacyTrayExe = Join-Path $AppDir 'projectdb-tray.exe'
	$script:LegacyManagerExe = Join-Path $AppDir 'ProjectDB-Service-Manager.exe'
	$script:LegacyServiceControlExe = Join-Path $AppDir 'projectdb-service-control.exe'
	$script:LegacyLogWrapperExe = Join-Path $AppDir 'projectdb-log-wrapper.exe'
	$script:LegacyUninstallExe = Join-Path $AppDir 'ProjectDB-Uninstall.exe'

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

function Get-ServiceId([string]$AppName) {
	$sanitized = ($AppName -replace '[^A-Za-z0-9]', '')
	if ($sanitized.Length -gt 32) { $sanitized = $sanitized.Substring(0, 32) }
	$sha = [Security.Cryptography.SHA256]::Create()
	try {
		$bytes = [Text.Encoding]::UTF8.GetBytes($AppName)
		$hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').Substring(0, 8)
	} finally {
		$sha.Dispose()
	}
	return 'PDB' + $sanitized + $hash
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

function Invoke-HiddenProcess([string]$FileName, [string[]]$Arguments) {
	$psi = New-Object Diagnostics.ProcessStartInfo
	$psi.FileName = $FileName
	$psi.UseShellExecute = $false
	$psi.CreateNoWindow = $true
	$psi.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
	$psi.RedirectStandardOutput = $true
	$psi.RedirectStandardError = $true
	if ($null -ne $Arguments -and $Arguments.Count -gt 0) {
		$quoted = @()
		foreach ($arg in $Arguments) {
			if ($arg -match '[\s"]') { $quoted += ('"' + ($arg -replace '"','\"') + '"') }
			else { $quoted += $arg }
		}
		$psi.Arguments = ($quoted -join ' ')
	}
	$p = New-Object Diagnostics.Process
	$p.StartInfo = $psi
	[void]$p.Start()
	$stdout = $p.StandardOutput.ReadToEnd()
	$stderr = $p.StandardError.ReadToEnd()
	$p.WaitForExit()
	$code = $p.ExitCode
	$p.Dispose()
	return [pscustomobject]@{ ExitCode=$code; StdOut=$stdout; StdErr=$stderr }
}

function Get-ServiceAutoStartFlag([string]$ServiceId) {
	return Join-Path $ServiceStateDir ($ServiceId + '.autostart')
}

function Ensure-ServiceStateDirectory {
	New-Item -ItemType Directory -Force -Path $ServiceStateDir | Out-Null
	$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
	& $icacls $ServiceStateDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(M)' | Out-Null
	if ($LASTEXITCODE -ne 0) { throw 'Could not configure ProjectDB service startup state permissions.' }
}

function Register-ProjectDbStartupTask {
	$schtasks = Join-Path $env:SystemRoot 'System32\schtasks.exe'
	$taskCommand = '"' + $ServiceControlExe + '" startup'
	& $schtasks /Create /TN $StartupTaskName /TR $taskCommand /SC ONSTART /RU SYSTEM /RL HIGHEST /DELAY 0000:20 /F | Out-Null
	if ($LASTEXITCODE -ne 0) { throw 'Could not register the ProjectDB service startup task.' }
	Add-InstallerLog ('Registered Windows startup task: ' + $StartupTaskName)
}

function Grant-ServiceInteractiveControl([string]$ServiceId) {
	if ([string]::IsNullOrWhiteSpace($ServiceId)) { return }
	$sc = Join-Path $env:SystemRoot 'System32\sc.exe'
	$show = Invoke-HiddenProcess $sc @('sdshow', $ServiceId)
	if ($show.ExitCode -ne 0) { throw ('Could not read service security descriptor for {0}: {1}' -f $ServiceId, $show.StdErr.Trim()) }
	$sddl = $null
	foreach ($line in ($show.StdOut -split "`r?`n")) {
		$value = $line.Trim()
		if ($value.StartsWith('D:')) { $sddl = $value }
	}
	if ([string]::IsNullOrWhiteSpace($sddl)) { throw ('Windows returned an invalid service security descriptor for {0}.' -f $ServiceId) }
	# Allow the currently interactive user session to query/start/stop/interrogate this ProjectDB service.
	$ace = '(A;;LCRPWPLO;;;IU)'
	if ($sddl.IndexOf($ace, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return }
	$sacl = $sddl.IndexOf('S:', [StringComparison]::Ordinal)
	if ($sacl -ge 0) { $updated = $sddl.Insert($sacl, $ace) } else { $updated = $sddl + $ace }
	$set = Invoke-HiddenProcess $sc @('sdset', $ServiceId, $updated)
	if ($set.ExitCode -ne 0) { throw ('Could not grant interactive service control permission for {0}: {1}' -f $ServiceId, $set.StdErr.Trim()) }
	Add-InstallerLog ('Granted non-elevated Start/Stop control for service {0}.' -f $ServiceId)
}

function Grant-AllProjectDbServiceControls {
	if (-not (Test-Path -LiteralPath $ServiceRoot)) { return }
	foreach ($dir in Get-ChildItem -LiteralPath $ServiceRoot -Directory -ErrorAction SilentlyContinue) {
		$xmlPath = Join-Path $dir.FullName 'projectdb-service.xml'
		if (-not (Test-Path -LiteralPath $xmlPath)) { continue }
		try {
			[xml]$doc = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
			$id = [string]$doc.service.id
			if (-not [string]::IsNullOrWhiteSpace($id) -and (Get-Service -Name $id -ErrorAction SilentlyContinue)) {
				Grant-ServiceInteractiveControl $id
			}
		} catch { Add-InstallerLog ('Could not update service control permission in {0}: {1}' -f $dir.FullName, $_.Exception.Message) }
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
				$running.Add([pscustomobject]@{ Id=$id; Directory=$dir.FullName })
				Stop-ServiceSafe $id
			}
		} catch {
			Add-InstallerLog ('Could not inspect service in {0}: {1}' -f $dir.FullName, $_.Exception.Message)
		}
	}
	return $running
}

function Restart-PreviousServices($Services, [string]$TargetId) {
	foreach ($item in $Services) {
		if ($item.Id -eq $TargetId) { continue }
		Start-ServiceSafe $item.Id
	}
}

function Compile-Manager([string]$SourcePath, [string]$OutputPath, [string]$IconPath) {
	$source = [IO.File]::ReadAllText($SourcePath, [Text.Encoding]::UTF8)
	$tempOutput = $OutputPath + '.new.' + [Guid]::NewGuid().ToString('N') + '.exe'
	$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
	try {
		$params = New-Object System.CodeDom.Compiler.CompilerParameters
		$params.GenerateExecutable = $true
		$params.GenerateInMemory = $false
		$params.OutputAssembly = $tempOutput
		$params.CompilerOptions = ('/target:winexe /optimize+ /win32icon:"' + $IconPath + '"')
		@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.ServiceProcess.dll','System.Xml.dll','System.Web.Extensions.dll') | ForEach-Object { [void]$params.ReferencedAssemblies.Add($_) }
		$result = $provider.CompileAssemblyFromSource($params, $source)
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

function Update-ExistingServiceCommands([string]$ExecutablePath) {
	if (-not (Test-Path -LiteralPath $ServiceRoot)) { return }
	foreach ($dir in Get-ChildItem -LiteralPath $ServiceRoot -Directory -ErrorAction SilentlyContinue) {
		$xmlPath = Join-Path $dir.FullName 'projectdb-service.xml'
		if (-not (Test-Path -LiteralPath $xmlPath)) { continue }
		try {
			[xml]$doc = Get-Content -LiteralPath $xmlPath -Raw -Encoding UTF8
			$exeNode = $doc.SelectSingleNode('/service/executable')
			$argsNode = $doc.SelectSingleNode('/service/arguments')
			$workNode = $doc.SelectSingleNode('/service/workingdirectory')
			if ($null -ne $exeNode) { $exeNode.InnerText = $ExecutablePath }
			if ($null -ne $argsNode) { $argsNode.InnerText = $dir.Name }
			if ($null -ne $workNode) { $workNode.InnerText = $AppDir }
			$settings = New-Object System.Xml.XmlWriterSettings
			$settings.Encoding = New-Object Text.UTF8Encoding($false)
			$settings.Indent = $true
			$writer = [System.Xml.XmlWriter]::Create($xmlPath, $settings)
			try { $doc.Save($writer) } finally { $writer.Dispose() }
			Add-InstallerLog ('Updated service command to cleaned-log wrapper: ' + $dir.Name)
		} catch {
			Add-InstallerLog ('Could not update service command for {0}: {1}' -f $dir.Name, $_.Exception.Message)
		}
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
	if (Test-Path -LiteralPath $libraryFile) {
		Copy-Item -LiteralPath $libraryFile -Destination $pinnedLibraryBackup -Force
		if (Test-Path -LiteralPath $libraryMeta) { Copy-Item -LiteralPath $libraryMeta -Destination $pinnedLibraryMetaBackup -Force }
		Add-InstallerLog 'Local lib\app.so override detected and will be preserved.'
	}

	Write-Status 38 'Stopping ProjectDB Service Manager and active ProjectDB services...'
	$managerProcesses = @(Get-Process -Name 'projectdb-tray','projectdb-service-manager' -ErrorAction SilentlyContinue)
	$managerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
	foreach ($managerProcess in $managerProcesses) {
		try { $managerProcess.WaitForExit(5000) } catch {}
	}

	$previousRunning = Stop-ProjectDbServices

	# Remove the previous root-level layout only after service processes have exited.
	@(
		$LegacyTrayExe,
		$LegacyManagerExe,
		$LegacyServiceControlExe,
		$LegacyLogWrapperExe,
		$LegacyUninstallExe
	) | ForEach-Object {
		if (-not [string]::IsNullOrWhiteSpace([string]$_)) {
			Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue
		}
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
	Update-ExistingWinSwWrappers

	Write-Status 70 'Preparing local ProjectDB publisher...'
	$publisherCertificate = Get-ProjectDbPublisherCertificate

	Write-Status 76 'Installing ProjectDB Service Manager...'
	Compile-ServiceControl $ServiceControlSource $ServiceControlExe $InstalledIcon
	Compile-LogWrapper $LogWrapperSource $LogWrapperExe $InstalledIcon
	Copy-Item -LiteralPath $ServiceControlExe -Destination $UninstallExe -Force
	Compile-Manager $TraySource $ManagerExe $InstalledIcon
	Sign-ProjectDbBinary $ServiceControlExe $publisherCertificate
	Sign-ProjectDbBinary $LogWrapperExe $publisherCertificate
	Sign-ProjectDbBinary $UninstallExe $publisherCertificate
	Sign-ProjectDbBinary $ManagerExe $publisherCertificate
	Update-ExistingServiceCommands $LogWrapperExe
	Ensure-ServiceStateDirectory
	Register-ProjectDbStartupTask
	$publisherCertificate = $null

	$runPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
	New-Item -Path $runPath -Force | Out-Null
	Remove-ItemProperty -Path $runPath -Name 'ProjectDB Tray' -ErrorAction SilentlyContinue
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
	New-ItemProperty -Path $UninstallKey -Name 'URLInfoAbout' -Value 'https://github.com/pavel-elblaus/projectdb' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'ProjectDBVersion' -Value '3.4.0' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'Architecture' -Value 'x64' -PropertyType String -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
	New-ItemProperty -Path $UninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null

	Write-Status 87 'Updating ProjectDB service permissions...'
	Grant-AllProjectDbServiceControls

	if ($null -ne $previousRunning -and $previousRunning.Count -gt 0) {
		Write-Status 92 'Restarting previously running ProjectDB services...'
		Restart-PreviousServices $previousRunning ''
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
		try { Restart-PreviousServices $previousRunning '' } catch {}
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
