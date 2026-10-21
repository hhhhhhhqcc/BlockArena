param([switch]$SkipBuild, [switch]$ShowWindows, [string[]]$Only)
$ErrorActionPreference = 'Stop'
if (-not $ShowWindows) { throw 'Native screenshot checks require visible game windows. Run with -ShowWindows.' }
$arenaRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checkRoot = Join-Path $arenaRoot '.checks'
$unity = 'E:\unity\2022.3.62f3c1\Editor\Unity.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$player = Join-Path $arenaRoot 'Builds\Windows-0.9.3\BlockArena.exe'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
function Get-FreeArenaPort {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, 0)
    try { $probe.Start(); return $probe.LocalEndpoint.Port }
    finally { $probe.Stop() }
}
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
    $buildLog = Join-Path $checkRoot 'unity-build-0.9.3.log'
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$arenaRoot+'"'),'-executeMethod','BlockArena.Editor.ArenaBuild.BuildWindows','-logFile',('"'+$buildLog+'"'))
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Wait-ArenaCheck $process 600 'Unity Windows build'
    if (-not (Select-String -LiteralPath $buildLog -SimpleMatch 'ARENA_BUILD_OK' -Quiet)) { throw 'Unity build marker missing.' }
    Write-Output 'UNITY_BUILD_VERIFIED 0.9.3'
}
$runs = @(
    @{ name='worlds-1440x900'; width=1440; height=900; flag='-battlefield-test'; marker='ARENA_BATTLEFIELD_OK'; timeout=160 },
    @{ name='worlds-1280x720'; width=1280; height=720; flag='-battlefield-test'; marker='ARENA_BATTLEFIELD_OK'; timeout=160 },
    @{ name='worlds-1920x1080'; width=1920; height=1080; flag='-battlefield-test'; marker='ARENA_BATTLEFIELD_OK'; timeout=160 },
    @{ name='interface-1440x900'; width=1440; height=900; flag='-interface-test'; marker='ARENA_INTERFACE_OK'; timeout=100 },
    @{ name='interface-1280x720'; width=1280; height=720; flag='-interface-test'; marker='ARENA_INTERFACE_OK'; timeout=100 },
    @{ name='interface-1920x1080'; width=1920; height=1080; flag='-interface-test'; marker='ARENA_INTERFACE_OK'; timeout=100 },
    @{ name='vfx'; width=1440; height=900; flag='-vfx-test'; marker='ARENA_VFX_OK'; timeout=160 },
    @{ name='economy'; width=1440; height=900; flag='-economy-test'; marker='ARENA_ECONOMY_OK'; timeout=160 },
    @{ name='full-game'; width=1440; height=900; flag='-smoke-test'; marker='ARENA_SMOKE_OK'; timeout=220 },
    @{ name='crystal'; width=1440; height=900; flag='-presentation-test'; marker='ARENA_PRESENTATION_OK'; timeout=100 }
)
foreach ($run in $runs) {
    if ($Only -and $run.name -notin $Only) { continue }
    $captures = Join-Path $arenaRoot ('Captures\0.9.3\'+$run.name)
    New-Item -ItemType Directory -Path $captures -Force | Out-Null
    $log = Join-Path $checkRoot ('native-'+$run.name+'-0.9.3.log')
    $nativePort = Get-FreeArenaPort
    $arguments = @($run.flag,'-smoke-exit','-screen-fullscreen','0','-screen-width',[string]$run.width,'-screen-height',[string]$run.height,'-port',[string]$nativePort,'-capture-dir',('"'+$captures+'"'),'-logFile',('"'+$log+'"'))
    Write-Output ('VERIFYING '+$run.name)
    $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Normal -PassThru
    Wait-ArenaCheck $process $run.timeout ('Native '+$run.name)
    if (-not (Select-String -LiteralPath $log -SimpleMatch $run.marker -Quiet)) { throw "Missing success marker: $log" }
    if (Select-String -LiteralPath $log -Pattern 'ARENA_.*FAILED|Exception:|NullReferenceException|Shader error|Invalid texture used for cursor' -Quiet) { throw "Runtime error: $log" }
    Write-Output ('NATIVE_VERIFIED '+$run.name)
}
$serverPort = Get-FreeArenaPort
$serverLog = Join-Path $checkRoot 'native-server-0.9.3.log'
$networkLog = Join-Path $checkRoot 'network-unity-0.9.3.log'
$networkProject = Join-Path $arenaRoot 'Tools\NetworkChecks\NetworkChecks.csproj'
& $dotnet restore $networkProject --configfile (Join-Path $arenaRoot 'Tools\NetworkChecks\NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Network checker restore failed.' }
& $dotnet build $networkProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Network checker build failed.' }
$server = $null
try {
    Set-Content -LiteralPath $serverLog -Value '' -Encoding utf8
    $server = Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics','-arena-server','-port',[string]$serverPort,'-logFile',('"'+$serverLog+'"')) -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        if ($server.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Native server did not start.' }
    } until ((Test-Path -LiteralPath $serverLog) -and (Select-String -LiteralPath $serverLog -SimpleMatch ('ARENA_SERVER_READY TCP '+$serverPort) -Quiet))
    & $dotnet (Join-Path $arenaRoot 'Tools\NetworkChecks\bin\Release\net8.0\NetworkChecks.dll') --external 127.0.0.1 $serverPort | Tee-Object -FilePath $networkLog
    if ($LASTEXITCODE -ne 0) { throw 'External Unity server checks failed.' }
    Write-Output 'UNITY_SERVER_VERIFIED 0.9.3'
} finally {
    if ($null -ne $server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
Write-Output ('ASSEMBLY_SHA256 '+(Get-FileHash -LiteralPath (Join-Path $arenaRoot 'Builds\Windows-0.9.3\BlockArena_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash)
