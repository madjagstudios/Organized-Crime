param(
    [Parameter(Mandatory = $true)]
    [string]$GamePath,
    [string]$AssetRipperPath,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

function Get-CommandStatus {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return [ordered]@{
            available = $false
            path = $null
        }
    }

    return [ordered]@{
        available = $true
        path = $command.Source
    }
}

function Test-Directory {
    param([string]$Path)

    return -not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path -LiteralPath $Path -PathType Container)
}

function Test-File {
    param([string]$Path)

    return -not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path -LiteralPath $Path -PathType Leaf)
}

function Get-FileStatus {
    param([string]$Path)

    $exists = Test-File $Path
    $hash = $null
    if ($exists) {
        $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }

    return [ordered]@{
        path = $Path
        exists = $exists
        sha256 = $hash
    }
}

function Get-GameStatus {
    param([string]$Path)

    $il2CppPath = Join-Path $Path 'MelonLoader\Il2CppAssemblies'
    $modsPath = Join-Path $Path 'Mods'
    $latestLogPath = Join-Path $Path 'MelonLoader\Latest.log'
    $playerLogPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\TVGS\Schedule I\Player.log'

    $modFiles = @()
    if (Test-Directory $modsPath) {
        $modFiles = @(Get-ChildItem -LiteralPath $modsPath -File -Filter '*.dll' |
            Where-Object { $_.Name -like 'OrganizedCrime*.dll' -or $_.Name -like 'PropertyProbe*.dll' } |
            Sort-Object Name |
            ForEach-Object { Get-FileStatus $_.FullName })
    }

    return [ordered]@{
        gamePath = $Path
        gamePathExists = Test-Directory $Path
        modsPathExists = Test-Directory $modsPath
        latestLogExists = Test-File $latestLogPath
        playerLogExists = Test-File $playerLogPath
        il2cpp = [ordered]@{
            generatedPathExists = Test-Directory $il2CppPath
            assemblyCSharp = Get-FileStatus (Join-Path $il2CppPath 'Assembly-CSharp.dll')
            fishNet = Get-FileStatus (Join-Path $il2CppPath 'Il2CppFishNet.Runtime.dll')
            unityCoreModule = Get-FileStatus (Join-Path $il2CppPath 'UnityEngine.CoreModule.dll')
        }
        mods = $modFiles
    }
}

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    readOnly = $true
    tools = [ordered]@{
        dotnet = Get-CommandStatus 'dotnet'
        ilspycmd = Get-CommandStatus 'ilspycmd'
        assetRipper = [ordered]@{
            providedPath = $AssetRipperPath
            providedPathExists = Test-File $AssetRipperPath
        }
    }
    game = Get-GameStatus $GamePath
    reminders = @(
        'This probe only reads tool, game, log, assembly, and DLL metadata.',
        'Do not commit game assemblies, generated IL2CPP assemblies, logs, or AssetRipper exports.',
        'If generated IL2CPP assemblies are missing, launch the IL2CPP game with MelonLoader once and inspect Latest.log.'
    )
}

if ($Json) {
    $result | ConvertTo-Json -Depth 10
    return
}

Write-Host 'Schedule I local environment probe (read-only)'
Write-Host "Game path:       $($result.game.gamePath)"
Write-Host "Game exists:     $($result.game.gamePathExists)"
Write-Host "Mods exists:     $($result.game.modsPathExists)"
Write-Host "Latest.log:      $($result.game.latestLogExists)"
Write-Host "Player.log:      $($result.game.playerLogExists)"
Write-Host "IL2CPP asm:      $($result.game.il2cpp.generatedPathExists)"
Write-Host "Assembly-CSharp: $($result.game.il2cpp.assemblyCSharp.exists)"
Write-Host "FishNet:         $($result.game.il2cpp.fishNet.exists)"
Write-Host "dotnet:          $($result.tools.dotnet.available) $($result.tools.dotnet.path)"
Write-Host "ilspycmd:        $($result.tools.ilspycmd.available) $($result.tools.ilspycmd.path)"

foreach ($mod in $result.game.mods) {
    Write-Host "Mod DLL:         $($mod.path)"
    Write-Host "  SHA256:        $($mod.sha256)"
}
