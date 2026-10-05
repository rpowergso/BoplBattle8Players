param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Bopl Battle',
    [string]$ProfileDirectory = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\BoplBattle\profiles\Bopl8Dev"
)

$ErrorActionPreference = 'Stop'
if (Get-Process BoplBattle -ErrorAction SilentlyContinue) {
    throw 'Bopl Battle is already running. Close it before launching this profile.'
}
$gameExecutable = Join-Path $GameDirectory 'BoplBattle.exe'
$preloader = Join-Path $ProfileDirectory 'BepInEx\core\BepInEx.Preloader.dll'
$plugin = Join-Path $ProfileDirectory 'BepInEx\plugins\Bopl8Players.dll'
foreach ($required in @($gameExecutable, $preloader, $plugin)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file missing: $required" }
}
$arguments = '--doorstop-enabled true --doorstop-target-assembly "' + $preloader + '"'
Start-Process -FilePath $gameExecutable -WorkingDirectory $GameDirectory -ArgumentList $arguments
