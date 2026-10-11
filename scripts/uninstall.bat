@echo off
setlocal
set "APPDIR=%LOCALAPPDATA%\MouseBatteryTray"
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseBatteryTray /f >nul 2>&1
taskkill /IM MouseBatteryTray.exe /F >nul 2>&1
if exist "%APPDIR%" rmdir /S /Q "%APPDIR%"
reg delete "HKCU\Software\MouseBatteryTray" /f >nul 2>&1
echo MouseBatteryTray removed.
