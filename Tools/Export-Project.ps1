param([string]$Destination = 'E:\project\BlockArena')
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetRoot = [IO.Path]::GetFullPath($Destination)
if ($sourceRoot.TrimEnd('\') -eq $targetRoot.TrimEnd('\')) { throw '源目录与目标目录不能相同。' }
if (Test-Path -LiteralPath $targetRoot) {
    $existing = @(Get-ChildItem -LiteralPath $targetRoot -Force)
    if ($existing.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $targetRoot 'Assets\Scripts\Runtime\ArenaApp.cs'))) {
        throw '目标文件夹非空且不是本游戏工程，请使用一个新的子目录。'
    }
}
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
$entries = @('Assets','Packages','ProjectSettings','Builds','Captures','README.md','Windows-试玩说明.md','验证记录.md','生物图鉴.md','CHANGELOG.md','开始游戏.cmd','打开Unity工程.cmd','启动专用服务器.cmd','.gitignore','.gitattributes')
foreach ($entry in $entries) {
    $sourceEntry = Join-Path $sourceRoot $entry
    if (Test-Path -LiteralPath $sourceEntry) {
        Copy-Item -LiteralPath $sourceEntry -Destination $targetRoot -Recurse -Force
    }
}
$targetTools = Join-Path $targetRoot 'Tools'
New-Item -ItemType Directory -Path $targetTools -Force | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination $targetTools -Force
foreach ($audioTool in @('Generate-ArenaAudio.py','ArenaAudio-Notes.md','Verify-CombatVfx.ps1','Verify-Battlefields.ps1','Verify-CommandUpdate.ps1','Generate-BattlefieldAudio.py','Generate-InterfaceAudio.py','Generate-EnvironmentAudio.py','Verify-PolishUpdate.ps1','Verify-InterfaceUpdate.ps1','Verify-MenuUpdate.ps1','Verify-SettingsUpdate.ps1','Verify-ExpansionUpdate.ps1','Package-Release.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $audioTool) -Destination $targetTools -Force
}
foreach ($suite in @('CoreChecks','NetworkChecks','CommsChecks')) {
    $targetSuite = Join-Path $targetTools $suite
    New-Item -ItemType Directory -Path $targetSuite -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot ('Tools\' + $suite)) -File |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $targetSuite -Force }
}
Write-Output ('EXPORTED_PROJECT ' + $targetRoot)
Get-Item -LiteralPath (Join-Path $targetRoot 'Builds\Windows-0.10.0\BlockArena.exe') | Select-Object FullName,Length
