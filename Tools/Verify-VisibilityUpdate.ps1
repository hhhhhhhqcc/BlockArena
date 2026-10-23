param([switch]$SkipBuild, [switch]$ShowWindows, [string[]]$Only)
$ErrorActionPreference = 'Stop'
if (-not $ShowWindows) { throw 'Native screenshot checks require visible game windows. Run with -ShowWindows.' }
$arenaRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checkRoot = Join-Path $arenaRoot '.checks\0.13.0'
$unity = 'E:\unity\2022.3.62f3c1\Editor\Unity.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$player = Join-Path $arenaRoot 'Builds\Windows-0.13.0\BlockArena.exe'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
function Get-FreeArenaPort {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $probe.Start(); return $probe.LocalEndpoint.Port }
    finally { $probe.Stop() }
}
function Stop-ArenaCheck($process) {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
function Wait-ArenaCheck($process, [int]$seconds, [string]$name) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            Stop-ArenaCheck $process
            throw "$name timed out; stopped only this verification process."
        }
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "$name failed: exit $($process.ExitCode)." }
}
function Invoke-ArenaDotnet([string[]]$arguments, [string]$name, [int]$seconds = 180) {
    $stdout = Join-Path $checkRoot ($name + '.log')
    $stderr = Join-Path $checkRoot ($name + '.stderr.log')
    $process = $null
    try {
        $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $arenaRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        Wait-ArenaCheck $process $seconds $name
    } catch {
        if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Tail 35 | Write-Output }
        if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 20 | Write-Output }
        throw
    } finally { Stop-ArenaCheck $process }
}
if (-not $SkipBuild) {
    $buildLog = Join-Path $checkRoot 'unity-build.log'
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$arenaRoot+'"'),'-executeMethod','BlockArena.Editor.ArenaBuild.BuildWindows','-logFile',('"'+$buildLog+'"'))
    $process = $null
    try {
        $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
        Wait-ArenaCheck $process 600 'Unity Windows build'
    } finally { Stop-ArenaCheck $process }
    if (-not (Select-String -LiteralPath $buildLog -SimpleMatch 'ARENA_BUILD_OK' -Quiet)) { throw 'Unity build marker missing.' }
    Write-Output 'UNITY_BUILD_VERIFIED 0.13.0'
}
if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw ('Missing native player: ' + $player) }
$runs = @(
    @{ name='performance-1280x720'; width=1280; height=720; flag='-performance-test'; marker='ARENA_PERFORMANCE_OK'; timeout=180 },
    @{ name='performance-1600x900'; width=1600; height=900; flag='-performance-test'; marker='ARENA_PERFORMANCE_OK'; timeout=180 },
    @{ name='vision-1280x720'; width=1280; height=720; flag='-vision-test'; marker='ARENA_VISION_OK'; timeout=150 },
    @{ name='vision-1600x900'; width=1600; height=900; flag='-vision-test'; marker='ARENA_VISION_OK'; timeout=150 },
    @{ name='environment-ui-1280x720'; width=1280; height=720; flag='-environment-ui-test'; marker='ARENA_ENVIRONMENT_UI_OK'; timeout=180 },
    @{ name='environment-ui-1600x900'; width=1600; height=900; flag='-environment-ui-test'; marker='ARENA_ENVIRONMENT_UI_OK'; timeout=180 },
    @{ name='refinement-1280x720'; width=1280; height=720; flag='-refinement-test'; marker='ARENA_REFINEMENT_OK'; timeout=180 },
    @{ name='refinement-1600x900'; width=1600; height=900; flag='-refinement-test'; marker='ARENA_REFINEMENT_OK'; timeout=180 },
    @{ name='controls-1280x720'; width=1280; height=720; flag='-controls-test'; marker='ARENA_CONTROLS_OK'; timeout=180 },
    @{ name='controls-1600x900'; width=1600; height=900; flag='-controls-test'; marker='ARENA_CONTROLS_OK'; timeout=180 },
    @{ name='ui-1280x720'; width=1280; height=720; flag='-expansion-ui-test'; marker='ARENA_EXPANSION_UI_OK'; timeout=170 },
    @{ name='ui-1600x900'; width=1600; height=900; flag='-expansion-ui-test'; marker='ARENA_EXPANSION_UI_OK'; timeout=170 },
    @{ name='expansion-1600x900'; width=1600; height=900; flag='-expansion-test'; marker='ARENA_EXPANSION_OK'; timeout=220 },
    @{ name='communication-1600x900'; width=1600; height=900; flag='-communication-test'; marker='ARENA_COMMUNICATION_OK'; timeout=150 },
    @{ name='settings'; width=1440; height=900; flag='-settings-test'; marker='ARENA_SETTINGS_OK'; timeout=170 },
    @{ name='menus'; width=1280; height=720; flag='-menu-test'; marker='ARENA_MENUS_OK'; timeout=150 },
    @{ name='economy'; width=1600; height=900; flag='-economy-test'; marker='ARENA_ECONOMY_OK'; timeout=180 },
    @{ name='full-game'; width=1600; height=900; flag='-smoke-test'; marker='ARENA_SMOKE_OK'; timeout=260 },
    @{ name='crystal'; width=1600; height=900; flag='-presentation-test'; marker='ARENA_PRESENTATION_OK'; timeout=120 }
)
$validNames = @($runs | ForEach-Object { $_.name }) + @('comms', 'network')
foreach ($name in $Only) { if ($name -notin $validNames) { throw ('Unknown -Only value: '+$name+'; choose '+($validNames -join ', ')) } }
foreach ($run in $runs) {
    if ($Only -and $run.name -notin $Only) { continue }
    $captures = Join-Path $arenaRoot ('Captures\0.13.0\'+$run.name)
    New-Item -ItemType Directory -Path $captures -Force | Out-Null
    $log = Join-Path $checkRoot ('native-'+$run.name+'.log')
    $nativePort = Get-FreeArenaPort
    $arguments = @($run.flag,'-smoke-exit','-screen-fullscreen','0','-screen-width',[string]$run.width,'-screen-height',[string]$run.height,'-port',[string]$nativePort,'-capture-dir',('"'+$captures+'"'),'-logFile',('"'+$log+'"'))
    Write-Output ('VERIFYING '+$run.name)
    $process = $null
    try {
        # Visual verification must render in a real, visible window.
        $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Normal -PassThru
        Wait-ArenaCheck $process $run.timeout ('Native '+$run.name)
    } finally { Stop-ArenaCheck $process }
    if (-not (Select-String -LiteralPath $log -SimpleMatch $run.marker -Quiet)) { throw "Missing success marker: $log" }
    if (Select-String -LiteralPath $log -Pattern 'ARENA_.*FAILED|Exception:|NullReferenceException|Shader error|Invalid texture used for cursor' -Quiet) { throw "Runtime error: $log" }
    Write-Output ('NATIVE_VERIFIED '+$run.name)
}
foreach ($suite in @(@{ name='comms'; project='CommsChecks'; marker='COMMS_CHECKS_OK' }, @{ name='network'; project='NetworkChecks'; marker='NETWORK_CHECKS_OK' })) {
    if ($Only -and $suite.name -notin $Only) { continue }
    $projectRoot = Join-Path $arenaRoot ('Tools\'+$suite.project)
    $project = Join-Path $projectRoot ($suite.project+'.csproj')
    Invoke-ArenaDotnet @('restore',('"'+$project+'"'),'--configfile',('"'+(Join-Path $projectRoot 'NuGet.Config')+'"')) ($suite.name+'-restore')
    Invoke-ArenaDotnet @('build',('"'+$project+'"'),'-c','Release','--no-restore') ($suite.name+'-build')
    # Each suite needs four free seats, so never share its server with another suite.
    $serverPort = Get-FreeArenaPort
    $serverLog = Join-Path $checkRoot ('native-server-'+$suite.name+'.log')
    $server = $null
    try {
        Set-Content -LiteralPath $serverLog -Value '' -Encoding utf8
        $server = Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics','-arena-server','-port',[string]$serverPort,'-logFile',('"'+$serverLog+'"')) -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 250
            $server.Refresh()
            if ($server.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw ('Native server did not start: '+$suite.name) }
        } until ((Test-Path -LiteralPath $serverLog) -and (Select-String -LiteralPath $serverLog -SimpleMatch ('ARENA_SERVER_READY TCP '+$serverPort) -Quiet))
        $dll = Join-Path $projectRoot ('bin\Release\net8.0\'+$suite.project+'.dll')
        Invoke-ArenaDotnet @(('"'+$dll+'"'),'--external','127.0.0.1',[string]$serverPort) ($suite.name+'-unity') 240
        # Both executables fail with nonzero exit code on an unmet assertion.
        Write-Output ('UNITY_SERVER_VERIFIED '+$suite.name+' 0.13.0')
    } finally { Stop-ArenaCheck $server }
}
Write-Output ('ASSEMBLY_SHA256 '+(Get-FileHash -LiteralPath (Join-Path $arenaRoot 'Builds\Windows-0.13.0\BlockArena_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash)

