@echo off
chcp 65001 >nul
if not exist "%~dp0Builds\Windows\BlockArena.exe" (
  echo 请先在Unity菜单中选择 方块斗兽场 / 构建 Windows 试玩版
  pause
  exit /b 1
)
start "" "%~dp0Builds\Windows\BlockArena.exe"
