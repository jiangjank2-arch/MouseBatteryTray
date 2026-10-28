param(
    [string]$OutputPath = (Join-Path $env:TEMP 'MouseBatteryTray-green-preview.png')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $projectRoot 'release\MouseBatteryTray.exe'
if (!(Test-Path -LiteralPath $applicationPath)) { throw 'Run build.bat first.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$temporaryBase = Join-Path $env:TEMP ('MouseBatteryTray-Preview-' + [guid]::NewGuid().ToString('N'))
$sourcePath = $temporaryBase + '.cs'
$previewExe = $temporaryBase + '.exe'
$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

internal static class IconPreview
{
    private static readonly Color Green = Color.FromArgb(48, 209, 88);
    private static void Label(Graphics g, string text, float size, Color color, float x, float y, bool bold)
    {
        using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y);
    }
    private static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args[0]);
        var render = assembly.GetType("BatteryMonitor").GetMethod("RenderBatteryBitmap", BindingFlags.Static | BindingFlags.NonPublic);
        using (var canvas = new Bitmap(1080, 680))
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.FromArgb(14, 23, 20));
            Label(g, "MouseBatteryTray", 32, Color.White, 42, 30, true);
            Label(g, "绿色电量图标 · 与托盘程序使用同一渲染逻辑", 17, Color.FromArgb(159, 185, 170), 44, 78, false);
            using (var card = new SolidBrush(Color.FromArgb(25, 39, 32))) g.FillRectangle(card, 40, 124, 1000, 250);
            string[] values = { "100", "73", "8", "?" };
            for (int i = 0; i < values.Length; i++)
            {
                using (var icon = (Bitmap)render.Invoke(null, new object[] { 156, values[i], Green }))
                    g.DrawImageUnscaled(icon, 87 + 244 * i, 144);
                Label(g, values[i] == "?" ? "尚未读取" : values[i] + "%", 18,
                    Color.FromArgb(192, 212, 201), 94 + 244 * i, 320, true);
            }
            Label(g, "任务栏实际尺寸 / 16、20、24、32、48 px", 18, Color.White, 44, 400, true);
            int[] sizes = { 16, 20, 24, 32, 48 };
            Color[] backgrounds = { Color.FromArgb(28, 28, 28), Color.FromArgb(242, 244, 242) };
            for (int row = 0; row < 2; row++)
            {
                int y = 441 + row * 80;
                using (var fill = new SolidBrush(backgrounds[row])) g.FillRectangle(fill, 40, y, 1000, 68);
                Color foreground = row == 0 ? Color.FromArgb(200, 210, 204) : Color.FromArgb(45, 60, 51);
                Label(g, row == 0 ? "深色" : "浅色", 16, foreground, 58, y + 23, false);
                for (int i = 0; i < sizes.Length; i++)
                {
                    using (var icon = (Bitmap)render.Invoke(null, new object[] { sizes[i], "100", Green }))
                        g.DrawImageUnscaled(icon, 180 + i * 126, y + (68 - sizes[i]) / 2);
                    Label(g, sizes[i].ToString(), 12, foreground, 188 + sizes[i] + i * 126, y + 24, false);
                }
                Label(g, "100%", 18, foreground, 896, y + 23, true);
            }
            Label(g, "数值为外观示例；运行时显示鼠标返回的真实百分比。未知电量保持灰色。", 14,
                Color.FromArgb(159, 185, 170), 44, 623, false);
            canvas.Save(args[1], ImageFormat.Png);
        }
        Console.WriteLine(args[1]);
    }
}
'@
try {
    [System.IO.File]::WriteAllText($sourcePath, $source, [System.Text.Encoding]::UTF8)
    & $compiler /nologo /utf8output /target:exe /r:System.Drawing.dll /out:$previewExe $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }
    & $previewExe $applicationPath $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed.' }
}
finally {
    foreach ($temporaryFile in @($sourcePath, $previewExe)) {
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
    }
}
