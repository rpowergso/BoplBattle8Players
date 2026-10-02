param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Bopl Battle',
    [string]$DotNet = "$env:USERPROFILE\.dotnet-sdk8\dotnet.exe",
    [string]$BepInExCore = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\BoplBattle\profiles\Bopl8Dev\BepInEx\core"
)

$ErrorActionPreference = 'Stop'
if (Get-Process BoplBattle -ErrorAction SilentlyContinue) {
    throw 'Close Bopl Battle before running the UI test. This script will not stop an existing game session.'
}
$repository = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $repository ('artifacts\ui-test-' + [Guid]::NewGuid().ToString('N'))
$testBepInEx = Join-Path $testDirectory 'BepInEx'
$pluginDirectory = Join-Path $testBepInEx 'plugins'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
Copy-Item -LiteralPath $BepInExCore -Destination $testBepInEx -Recurse
$buildDirectory = Join-Path $testDirectory 'build'
& $DotNet build (Join-Path $repository 'MorePlayers.csproj') -c Release --no-restore '-p:DefineConstants=BOPL8_UI_SMOKE' "-p:OutputPath=$buildDirectory\"
if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
Copy-Item -LiteralPath (Join-Path $buildDirectory 'Bopl8Players.dll') -Destination $pluginDirectory
$targetAssembly = Join-Path $testBepInEx 'core\BepInEx.Preloader.dll'
$arguments = '-screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --doorstop-enabled true --doorstop-target-assembly "' + $targetAssembly + '"'
$testProcess = Start-Process -FilePath (Join-Path $GameDirectory 'BoplBattle.exe') -WorkingDirectory $GameDirectory -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $testProcess.WaitForExit(45000)) {
    Stop-Process -Id $testProcess.Id
    throw "UI test timed out. Its log is in $testBepInEx"
}
$logFile = Join-Path $testBepInEx 'LogOutput.log'
$logText = Get-Content -LiteralPath $logFile -Raw
if ($logText.Contains('[Error') -or -not $logText.Contains('[UI smoke] PASS: online scene re-entry')) {
    throw "UI test failed. Inspect $logFile"
}
Select-String -LiteralPath $logFile -Pattern '\[UI smoke\]' | ForEach-Object { $_.Line }
Write-Host "UI test passed. Log and screenshot: $testDirectory"
