# Compatible with Windows PowerShell 5.1 and PowerShell 7. No existing output is replaced.
[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.9.5',
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-NewReleaseText {
    param([string]$Path, [string]$Text, [bool]$WithBom = $false)
    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n")
    if (-not $normalized.EndsWith("`r`n")) { $normalized += "`r`n" }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = New-Object IO.StreamWriter($stream, (New-Object Text.UTF8Encoding($WithBom)))
        try { $writer.Write($normalized) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-EntrySha256 {
    param([IO.Compression.ZipArchiveEntry]$Entry)
    $stream = $Entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '')
    } finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$buildRoot = Join-Path $projectRoot ('Builds\Windows-' + $Version)
if (-not (Test-Path -LiteralPath $buildRoot -PathType Container)) {
    throw ('Build directory does not exist: ' + $buildRoot)
}
foreach ($required in @('BlockArena.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'BlockArena_Data\Managed\Assembly-CSharp.dll', 'MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot $required) -PathType Leaf)) {
        throw ('Required Unity runtime file is missing: ' + $required)
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $outputRoot = Join-Path $projectRoot ('Releases/v' + $Version)
} else {
    $outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
}
$outputRoot = [IO.Path]::GetFullPath($outputRoot).TrimEnd('\')
if ($outputRoot.Equals($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($buildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside the source build directory.'
}
if (Test-Path -LiteralPath $outputRoot) {
    throw ('OutputDirectory already exists; choose a new directory. No files were replaced: ' + $outputRoot)
}

$archiveName = 'BlockArena-v' + $Version + '-Windows-x64.zip'
$archivePath = Join-Path $outputRoot $archiveName
if (Test-Path -LiteralPath $archivePath) { throw ('ZIP already exists: ' + $archivePath) }
$buildEntries = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -Force | Sort-Object FullName)
foreach ($entry in $buildEntries) {
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('A source build entry is a link; refusing an ambiguous runtime tree: ' + $entry.FullName)
    }
}
$buildFiles = @($buildEntries | Where-Object { -not $_.PSIsContainer })
if ($buildFiles.Count -eq 0) { throw 'The source build contains no files.' }

# A failed run keeps its partial output for inspection. It never deletes or retries over it.
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$playerRoot = Join-Path $outputRoot 'Player'
$runtimeRoot = Join-Path $playerRoot '游戏文件'
$serverRoot = Join-Path $playerRoot '联机工具'
[IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
[IO.Directory]::CreateDirectory($serverRoot) | Out-Null
foreach ($directory in @($buildEntries | Where-Object { $_.PSIsContainer })) {
    $relative = $directory.FullName.Substring($buildRoot.Length).TrimStart('\')
    [IO.Directory]::CreateDirectory((Join-Path $runtimeRoot $relative)) | Out-Null
}

$sourceManifest = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in $buildFiles) {
    $relative = $file.FullName.Substring($buildRoot.Length).TrimStart('\')
    $destination = Join-Path $runtimeRoot $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    [IO.File]::Copy($file.FullName, $destination, $false)
    $copy = Get-Item -LiteralPath $destination
    $copyHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($copy.Length -ne $file.Length -or $copyHash -ne $sourceHash) {
        throw ('Runtime copy verification failed: ' + $relative)
    }
    $sourceManifest.Add([ordered]@{
        path = $relative.Replace('\', '/')
        bytes = [long]$copy.Length
        sha256 = $copyHash
    })
}

$gameLauncher = @'
@echo off
chcp 65001 >nul
if not exist "%~dp0游戏文件\BlockArena.exe" (
  echo 请先完整解压游戏压缩包，再双击“开始游戏”。
  pause
  exit /b 1
)
start "" /D "%~dp0游戏文件" "%~dp0游戏文件\BlockArena.exe" %*
exit /b %errorlevel%
'@
Write-NewReleaseText -Path (Join-Path $playerRoot '开始游戏.cmd') -Text $gameLauncher

$serverLauncher = @'
@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0专用服务器.ps1" %*
exit /b %errorlevel%
'@
Write-NewReleaseText -Path (Join-Path $serverRoot '启动专用服务器.cmd') -Text $serverLauncher

$serverScript = @'
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateRange(1, 65535)]
    [int]$Port = 7777
)

$ErrorActionPreference = 'Stop'
$gameDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\游戏文件'))
$gameExe = Join-Path $gameDirectory 'BlockArena.exe'
$logPath = Join-Path $PSScriptRoot 'server.log'
if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw '找不到游戏程序。请先完整解压压缩包，并保留“游戏文件”和“联机工具”的相对位置。'
}
$serverArguments = '-batchmode -nographics -arena-server -port {0} -logFile "{1}"' -f $Port.ToString([Globalization.CultureInfo]::InvariantCulture), $logPath
Write-Host ('服务器端口：TCP {0}；日志：{1}' -f $Port, $logPath)
$serverProcess = Start-Process -FilePath $gameExe -ArgumentList $serverArguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru
try {
    $serverProcess.WaitForExit()
    $serverExitCode = $serverProcess.ExitCode
} finally {
    if (-not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id }
}
exit $serverExitCode
'@
# Windows PowerShell 5.1 requires a BOM to read Chinese string literals reliably.
Write-NewReleaseText -Path (Join-Path $serverRoot '专用服务器.ps1') -Text $serverScript -WithBom $true

$readme = @"
方块斗兽场 v$Version · Windows x64

先完整解压压缩包，再双击“开始游戏.cmd”。不要直接在压缩包中运行。
无需安装 Unity 编辑器或 .NET SDK，运行文件已随包提供。

单人：点击“单人试玩”，直接与三名电脑对战。
联机：房主选择“创建房间”；朋友选择“加入房间”，填写房主地址和端口。
局域网填写房主的局域网 IP；内网穿透须支持 TCP，默认端口 7777。
所有人使用同版 v$Version，联机协议 v7。防火墙提示时允许游戏联网。

WASD 移动 Steve；Shift + 左键拖动框选生物，Shift + 单击多选。
准备与战术阶段左键拖动生物摆位；战术阶段右键设置路线。
Q 使用指挥技能；Esc 打开菜单。窗口、全屏和分辨率在“设置”中切换。

通常直接在游戏里开房即可；需要无画面服务器时，运行“联机工具”里的
“启动专用服务器.cmd”。可在命令行末尾填写端口，如：启动专用服务器.cmd 8888
服务器日志写入“联机工具\server.log”，服务器窗口须保持运行。

请保留“游戏文件”完整内容，以及它与启动脚本、联机工具的相对位置。
"@
Write-NewReleaseText -Path (Join-Path $playerRoot '试玩说明.txt') -Text $readme

$payloadManifest = New-Object 'System.Collections.Generic.List[object]'
$payloadByPath = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
foreach ($file in @(Get-ChildItem -LiteralPath $playerRoot -Recurse -File -Force | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($playerRoot.Length).TrimStart('\').Replace('\', '/')
    $item = [ordered]@{
        path = $relative
        bytes = [long]$file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    $payloadManifest.Add($item)
    $payloadByPath.Add($relative, $item)
}

# .NET Framework recognizes Encoding.UTF8 for the ZIP EFS flag. A custom
# UTF8Encoding(false) writes UTF-8 name bytes without that flag on PowerShell 5.1.
[IO.Compression.ZipFile]::CreateFromDirectory(
    $playerRoot, $archivePath, [IO.Compression.CompressionLevel]::Optimal,
    $false, [Text.Encoding]::UTF8
)

# Verify the actual ZIP, including Unicode names and the absence of a wrapper folder.
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
$verifiedZipFiles = 0
$verifiedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName.Replace('\', '/')
        if (-not $payloadByPath.ContainsKey($relative)) {
            throw ('Unexpected archive path: ' + $entry.FullName)
        }
        if (-not $verifiedPaths.Add($relative)) { throw ('Duplicate archive path: ' + $relative) }
        $expected = $payloadByPath[$relative]
        if ($entry.Length -ne $expected.bytes -or (Get-EntrySha256 -Entry $entry) -ne $expected.sha256) {
            throw ('Archive verification failed: ' + $relative)
        }
        $verifiedZipFiles++
    }
} finally { $archive.Dispose() }
if ($verifiedZipFiles -ne $payloadManifest.Count) { throw 'Archive file count does not match the player payload.' }

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
Write-NewReleaseText -Path (Join-Path $outputRoot 'SHA256SUMS.txt') -Text ($archiveHash + ' *' + $archiveName)
$manifest = [ordered]@{
    version = $Version
    protocol = 7
    createdUtc = [DateTime]::UtcNow.ToString('o')
    sourceBuild = $buildRoot
    playerDirectory = $playerRoot
    runtimeFileCount = $sourceManifest.Count
    runtimeBytes = [long](($buildFiles | Measure-Object -Property Length -Sum).Sum)
    runtimeFiles = @($sourceManifest.ToArray())
    playerFileCount = $payloadManifest.Count
    playerFiles = @($payloadManifest.ToArray())
    archive = [ordered]@{
        name = $archiveName
        bytes = [long](Get-Item -LiteralPath $archivePath).Length
        sha256 = $archiveHash
        verifiedFileCount = $verifiedZipFiles
        encoding = 'UTF-8'
        compression = 'Optimal'
        includesWrapperDirectory = $false
    }
}
Write-NewReleaseText -Path (Join-Path $outputRoot 'package-manifest.json') -Text ($manifest | ConvertTo-Json -Depth 8)
Write-Output ('RELEASE_PACKAGE_OK ' + $archivePath)
Write-Output ('Runtime files: {0}; packaged files: {1}; ZIP SHA256: {2}' -f $sourceManifest.Count, $payloadManifest.Count, $archiveHash)
