<#
Builds the Thunderstore package for Organized Crime.

Output: dist\MadJagStudios-OrganizedCrime-<version>.zip containing
  manifest.json, README.md, CHANGELOG.md, icon.png at the root and Mods\OrganizedCrime.dll,
the layout Thunderstore expects for a MelonLoader mod. The same zip is what Nexus takes.

Usage, from anywhere:
  powershell -ExecutionPolicy Bypass -File packaging\build-package.ps1            (builds Release first)
  powershell -ExecutionPolicy Bypass -File packaging\build-package.ps1 -SkipBuild  (packages the DLL already in bin\Release)

The version comes from packaging\thunderstore\manifest.json and must match the MelonInfo version in
tools\OrganizedCrime\Mod.cs; the script refuses to package a mismatch.
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$packaging = Join-Path $root "packaging\thunderstore"
$project = Join-Path $root "tools\OrganizedCrime\OrganizedCrime.csproj"
$dll = Join-Path $root "tools\OrganizedCrime\bin\Release\net6.0\OrganizedCrime.dll"
$modSource = Join-Path $root "tools\OrganizedCrime\Mod.cs"
$dist = Join-Path $root "dist"

$manifest = Get-Content (Join-Path $packaging "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
if ($manifest.description.Length -gt 250) { throw "manifest description is $($manifest.description.Length) characters; Thunderstore allows 250." }

$modText = Get-Content $modSource -Raw
if ($modText -notmatch [regex]::Escape("`"$version`"")) {
    throw "Mod.cs does not carry MelonInfo version `"$version`"; bump Mod.cs and its reachability test first."
}

if (-not $SkipBuild) {
    & dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
}
if (-not (Test-Path $dll)) { throw "Release DLL not found at $dll" }

Add-Type -AssemblyName System.Drawing
$icon = Join-Path $packaging "icon.png"
$image = [System.Drawing.Image]::FromFile($icon)
try {
    if ($image.Width -ne 256 -or $image.Height -ne 256) { throw "icon.png must be 256 by 256; it is $($image.Width) by $($image.Height)." }
} finally { $image.Dispose() }

$staging = Join-Path $dist "staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $staging "Mods") | Out-Null
foreach ($name in @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")) {
    Copy-Item (Join-Path $packaging $name) (Join-Path $staging $name)
}
Copy-Item $dll (Join-Path $staging "Mods\OrganizedCrime.dll")

$zipPath = Join-Path $dist "MadJagStudios-OrganizedCrime-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $staging -Recurse -File | ForEach-Object {
        # Forward slashes in entry names, so the zip opens correctly on every platform and in r2modman.
        $entryName = $_.FullName.Substring($staging.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName) | Out-Null
    }
} finally { $zip.Dispose() }
Remove-Item $staging -Recurse -Force

$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLower()
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
Write-Output "package  $zipPath"
Write-Output "zip      sha256 $zipHash"
Write-Output "dll      sha256 $dllHash"
Write-Output "entries:"
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try { $zip.Entries | ForEach-Object { Write-Output "  $($_.FullName)  $($_.Length) bytes" } } finally { $zip.Dispose() }
