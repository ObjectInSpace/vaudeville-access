# Builds the player package: MelonLoader 0.7.3 (x64) + Vaudeville Access + a Loader.cfg that hides the console,
# zipped so it unpacks straight into the game folder.
# Usage: pwsh -File tools\Make-Release.ps1
# Output: artifacts\VaudevilleAccess-<version>.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$melonVersion = '0.7.3'
$melonUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v$melonVersion/MelonLoader.x64.zip"
# SHA-256 of the official MelonLoader.x64.zip v0.7.3 contents' version.dll, checked after extraction.
$melonVersionDllSha256 = '0CE7A4E530C7F83F172A2C44AED45EEDB3BDCF06B83760F143A0FBA6885FA929'

# The mod's version comes from its MelonInfo attribute.
$modSource = Get-Content (Join-Path $root 'src\VaudevilleAccessMod.cs') -Raw
if ($modSource -notmatch 'MelonInfo\(typeof\([^)]*\),\s*"Vaudeville Access",\s*"([0-9.]+)"') { throw 'Could not read the mod version.' }
$version = $Matches[1]

Write-Output "Building Vaudeville Access $version..."
dotnet build (Join-Path $root 'VaudevilleAccess.csproj') -c Release -nologo -p:SkipDeploy=true | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$dll = Join-Path $root 'bin\Release\VaudevilleAccess.dll'

$artifacts = Join-Path $root 'artifacts'
$cache = Join-Path $artifacts 'cache'
$stage = Join-Path $artifacts "stage\VaudevilleAccess-$version"
New-Item -ItemType Directory -Force $cache | Out-Null
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

$melonZip = Join-Path $cache "MelonLoader.x64-$melonVersion.zip"
if (-not (Test-Path $melonZip)) {
    Write-Output "Downloading MelonLoader $melonVersion..."
    Invoke-WebRequest $melonUrl -OutFile $melonZip
}
Expand-Archive $melonZip -DestinationPath $stage
$actual = (Get-FileHash (Join-Path $stage 'version.dll') -Algorithm SHA256).Hash
if ($actual -ne $melonVersionDllSha256) { throw "MelonLoader version.dll hash mismatch: $actual" }

New-Item -ItemType Directory -Force (Join-Path $stage 'Mods'), (Join-Path $stage 'UserData') | Out-Null
Copy-Item $dll (Join-Path $stage 'Mods')
# MelonLoader fills in every other setting with its defaults on first launch.
Set-Content (Join-Path $stage 'UserData\Loader.cfg') "[console]`nhide_console = true`n" -NoNewline
Copy-Item (Join-Path $root 'docs\PLAYER-README.md') (Join-Path $stage 'Vaudeville Access README.md')
if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'Vaudeville Access LICENSE.txt') }

$zip = Join-Path $artifacts "VaudevilleAccess-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$modHash = (Get-FileHash $dll -Algorithm SHA256).Hash.Substring(0, 16)
Write-Output "Package: $zip"
Write-Output "SHA-256: $hash"
Write-Output "Mod build: $modHash"
Get-ChildItem $stage -Recurse -File | Measure-Object | ForEach-Object { Write-Output "$($_.Count) files." }
