@echo off
chcp 65001 >nul
set "ARENA_PORT=%~1"
if "%ARENA_PORT%"=="" set "ARENA_PORT=7777"
"%~dp0Builds\Windows-0.8.0\BlockArena.exe" -batchmode -nographics -arena-server -port %ARENA_PORT% -logFile "%~dp0server.log"
