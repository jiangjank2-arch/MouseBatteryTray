$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$testExe = Join-Path $env:TEMP ('MouseBatteryTray-Tests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    $sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
    $testSource = Join-Path $projectRoot 'tests\ReceiverBatteryTests.cs'
    & $compiler /nologo /utf8output /target:exe /main:ReceiverBatteryTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:$testExe $sources $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
}
