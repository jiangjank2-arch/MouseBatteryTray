@echo off
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
if not exist "%ROOT%release" mkdir "%ROOT%release"
"%CSC%" /nologo /utf8output /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%ROOT%release\MouseBatteryTray.exe" "%ROOT%src\MouseBatteryTray.cs"
if errorlevel 1 exit /b 1
echo Build complete: "%ROOT%release\MouseBatteryTray.exe"
