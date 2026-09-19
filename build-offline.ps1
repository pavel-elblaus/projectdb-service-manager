$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $root 'payload'
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$project = Join-Path $payload 'projectdb-v3.4.0-win-x64.zip'
$winsw = Join-Path $payload 'WinSW-x64.exe'
$out = Join-Path $root ('ProjectDB-Setup-' + $version + '.exe')
$icon = Join-Path $payload 'projectdb.ico'
$resource = Join-Path $root 'setup_windows_amd64.syso'
$versionInfo = Join-Path $root 'versioninfo.setup.json'

$projectUrl = 'https://github.com/pavel-elblaus/projectdb/releases/download/17.8.0/projectdb-v3.4.0-win-x64.zip'
$winswUrl = 'https://github.com/winsw/winsw/releases/download/v2.12.0/WinSW-x64.exe'

$expectedProject = '3878c4eba1337e040aea30b9428b07ba6f6a062d7c518db489bcc47f85213758'
$expectedWinSw = '05b82d46ad331cc16bdc00de5c6332c1ef818df8ceefcd49c726553209b3a0da'

function Assert-FileHash([string]$Path, [string]$Expected) {
	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing payload: $Path" }
	$actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
	if ($actual -ne $Expected) { throw "SHA-256 mismatch for $Path`nExpected: $Expected`nActual:   $actual" }
}

function Get-VerifiedPayload([string]$Path, [string]$Url, [string]$Expected) {
	if (Test-Path -LiteralPath $Path -PathType Leaf) {
		$actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
		if ($actual -eq $Expected) {
			Write-Host "Using cached payload: $([IO.Path]::GetFileName($Path))"
			return
		}
		Write-Host "Cached payload has an unexpected hash and will be replaced: $([IO.Path]::GetFileName($Path))"
	}

	New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
	$temp = "$Path.download"
	Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue

	try {
		Write-Host "Downloading: $Url"
		Invoke-WebRequest -Uri $Url -OutFile $temp -UseBasicParsing
		Assert-FileHash $temp $Expected
		Move-Item -LiteralPath $temp -Destination $Path -Force
	} finally {
		Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
	}
}

Get-VerifiedPayload $project $projectUrl $expectedProject
Get-VerifiedPayload $winsw $winswUrl $expectedWinSw

$go = Get-Command go.exe -ErrorAction Stop
Push-Location $root
try {
	$env:GOOS = 'windows'
	$env:GOARCH = 'amd64'

	# Embed Windows file properties and the ProjectDB icon into the Setup executable.
	$numericVersion = $version -replace '-.*

Write-Host "Built: $out"
Get-FileHash -LiteralPath $out -Algorithm SHA256
, ''
	$versionParts = @($numericVersion.Split('.') | ForEach-Object { [int]$_ })
	while ($versionParts.Count -lt 4) { $versionParts += 0 }

	$versionObject = [ordered]@{
		FixedFileInfo = [ordered]@{
			FileVersion = [ordered]@{
				Major = $versionParts[0]
				Minor = $versionParts[1]
				Patch = $versionParts[2]
				Build = $versionParts[3]
			}
			ProductVersion = [ordered]@{
				Major = $versionParts[0]
				Minor = $versionParts[1]
				Patch = $versionParts[2]
				Build = $versionParts[3]
			}
			FileFlagsMask = '3f'
			FileFlags = '00'
			FileOS = '040004'
			FileType = '01'
			FileSubType = '00'
		}
		StringFileInfo = [ordered]@{
			Comments = 'ProjectDB Service Manager setup package'
			CompanyName = 'Pavel Elblaus'
			FileDescription = 'ProjectDB Service Manager Setup'
			FileVersion = ($versionParts -join '.')
			InternalName = 'ProjectDB Setup'
			LegalCopyright = 'Copyright (c) 2022-2026 Pavel Elblaus'
			LegalTrademarks = ''
			OriginalFilename = 'ProjectDB-Setup.exe'
			PrivateBuild = $version
			ProductName = 'ProjectDB Service Manager'
			ProductVersion = $version
			SpecialBuild = ''
		}
		VarFileInfo = [ordered]@{
			Translation = [ordered]@{
				LangID = '0409'
				CharsetID = '04B0'
			}
		}
		IconPath = $icon
	}

	$json = $versionObject | ConvertTo-Json -Depth 8
	$utf8 = New-Object Text.UTF8Encoding($false)
	[IO.File]::WriteAllText($versionInfo, $json, $utf8)

	& $go.Source run 'github.com/josephspurrier/goversioninfo/cmd/goversioninfo@v1.7.0' -64 -o $resource $versionInfo
	if ($LASTEXITCODE -ne 0) { throw "Windows version resource generation failed with exit code $LASTEXITCODE" }

	& $go.Source build -trimpath -ldflags '-H=windowsgui -s -w' -o $out .
	if ($LASTEXITCODE -ne 0) { throw "Go build failed with exit code $LASTEXITCODE" }
} finally {
	Remove-Item -LiteralPath $resource -Force -ErrorAction SilentlyContinue
	Remove-Item -LiteralPath $versionInfo -Force -ErrorAction SilentlyContinue
	Pop-Location
}

Write-Host "Built: $out"
Get-FileHash -LiteralPath $out -Algorithm SHA256
