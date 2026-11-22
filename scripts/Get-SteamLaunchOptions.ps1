# Generates Steam launch options for a Bopl8Players profile on Windows.
# Example: .\Get-SteamLaunchOptions.ps1 -ProfileDirectory 'C:\path\to\profiles\Bopl8' -Copy
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProfileDirectory,

    [switch]$Copy
)

$ErrorActionPreference = 'Stop'
$profileFolder = Get-Item -LiteralPath $ProfileDirectory
if ($profileFolder -isnot [System.IO.DirectoryInfo]) {
    throw 'Select the folder for your Bopl Battle mod manager profile.'
}

$preloader = Join-Path $profileFolder.FullName 'BepInEx\core\BepInEx.Preloader.dll'
if (-not (Test-Path -LiteralPath $preloader -PathType Leaf)) {
    throw 'BepInExPack is missing. Install Bopl8Players and its dependency in this profile, then choose Start modded once.'
}

$plugins = Join-Path $profileFolder.FullName 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath $plugins -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $plugins -Filter Bopl8Players.dll -File -Recurse | Select-Object -First 1)) {
    throw 'Bopl8Players.dll is missing. Select the profile where Bopl8Players is installed.'
}

$launchOptions = '--doorstop-enabled true --doorstop-target-assembly "{0}"' -f $preloader
if ($Copy) {
    Set-Clipboard -Value $launchOptions
}
$launchOptions
