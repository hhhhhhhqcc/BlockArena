param([switch]$SkipBuild, [switch]$ShowWindows)
$ErrorActionPreference = 'Stop'
$arenaRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checkRoot = Join-Path $arenaRoot '.checks'
$unity = 'E:\unity\2022.3.62f3c1\Editor\Unity.exe'
$player = Join-Path $arenaRoot 'Builds\Windows-0.8.1\BlockArena.exe'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null

function Wait-ArenaCheck($process, [int]$seconds, [string]$name) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            Stop-Process -Id $process.Id -Force
            throw "$name timed out; stopped only this verification process."
        }
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "$name failed: exit $($process.ExitCode)." }
}

if (-not $SkipBuild) {
    $buildLog = Join-Path $checkRoot 'unity-build-0.8.1.log'
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$arenaRoot+'"'),'-executeMethod','BlockArena.Editor.ArenaBuild.BuildWindows','-logFile',('"'+$buildLog+'"'))
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Wait-ArenaCheck $process 600 'Unity Windows build'
    if (-not (Select-String -LiteralPath $buildLog -SimpleMatch 'ARENA_BUILD_OK' -Quiet)) { throw 'Unity build marker missing.' }
    Write-Output 'UNITY_BUILD_VERIFIED 0.8.1'
}

$runs = @(
    @{ name='vfx-1440x900'; width=1440; height=900; flag='-vfx-test'; marker='ARENA_VFX_OK'; timeout=100 },
    @{ name='vfx-1280x720'; width=1280; height=720; flag='-vfx-test'; marker='ARENA_VFX_OK'; timeout=100 },
    @{ name='full-game'; width=1440; height=900; flag='-smoke-test'; marker='ARENA_SMOKE_OK'; timeout=180 },
    @{ name='crystal'; width=1440; height=900; flag='-presentation-test'; marker='ARENA_PRESENTATION_OK'; timeout=100 }
)
foreach ($run in $runs) {
    $captures = Join-Path $arenaRoot ('Captures\0.8.1\'+$run.name)
    New-Item -ItemType Directory -Path $captures -Force | Out-Null
    $log = Join-Path $checkRoot ('native-'+$run.name+'-0.8.1.log')
    $arguments = @($run.flag,'-smoke-exit','-screen-fullscreen','0','-screen-width',[string]$run.width,'-screen-height',[string]$run.height,'-port','17790','-capture-dir',('"'+$captures+'"'),'-logFile',('"'+$log+'"'))
    Write-Output ('VERIFYING '+$run.name)
    $style = if ($ShowWindows) { 'Normal' } else { 'Hidden' }
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle $style -PassThru
    Wait-ArenaCheck $process $run.timeout ('Native '+$run.name)
    if (-not (Select-String -LiteralPath $log -SimpleMatch $run.marker -Quiet)) { throw "Missing success marker: $log" }
    if (Select-String -LiteralPath $log -Pattern 'ARENA_.*FAILED|Exception:|NullReferenceException|Shader error|Invalid texture used for cursor' -Quiet) { throw "Runtime error: $log" }
    Write-Output ('NATIVE_VERIFIED '+$run.name)
}
Write-Output ('ASSEMBLY_SHA256 '+(Get-FileHash -LiteralPath (Join-Path $arenaRoot 'Builds\Windows-0.8.1\BlockArena_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash)
