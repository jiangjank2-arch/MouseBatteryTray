@echo off
setlocal
set "EXE=%~dp0..\release\MouseBatteryTray.exe"
set "APPDIR=%LOCALAPPDATA%\MouseBatteryTray"
if not exist "%EXE%" (
  echo Missing release\MouseBatteryTray.exe. Run build.bat first.
  exit /b 1
)
if not exist "%APPDIR%" mkdir "%APPDIR%"
if errorlevel 1 exit /b 1
taskkill /IM MouseBatteryTray.exe /F >nul 2>&1
copy /Y "%EXE%" "%APPDIR%\MouseBatteryTray.exe" >nul
if errorlevel 1 exit /b 1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseBatteryTray /t REG_SZ /d "\"%APPDIR%\MouseBatteryTray.exe\"" /f >nul
if errorlevel 1 exit /b 1
start "" "%APPDIR%\MouseBatteryTray.exe"
echo Installed and started. MouseBatteryTray will run at Windows sign-in.
