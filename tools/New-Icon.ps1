# Rebuild the native Windows icon from vector geometry, with no external packages.
# Run: powershell.exe -NoProfile -STA -File .\tools\New-Icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/ProcessGuard.App/Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$outerPath = 'M128,10 C161,32 196,37 222,42 L222,112 C222,176 179,222 128,247 C77,222 34,176 34,112 L34,42 C60,37 95,32 128,10 Z'
$innerPath = 'M128,32 C155,49 182,54 202,58 L202,112 C202,163 166,203 128,224 C90,203 54,163 54,112 L54,58 C74,54 101,49 128,32 Z'
$pulsePath = 'M70,125 L93,125 L107,94 L127,158 L145,117 L160,125 L187,125'
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <title>ProcessGuard - Process Sentinel</title>
  <defs><linearGradient id="teal" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#32D3D5"/><stop offset="1" stop-color="#087F99"/></linearGradient></defs>
  <path d="$outerPath" fill="#132D43"/>
  <path d="$innerPath" fill="url(#teal)"/>
  <path d="$pulsePath" fill="none" stroke="#FFFFFF" stroke-width="13" stroke-linejoin="round" stroke-linecap="round"/>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $assetDirectory 'ProcessGuard.svg'), $svg, [Text.UTF8Encoding]::new($false))
$outer = [Windows.Media.Geometry]::Parse($outerPath)
$inner = [Windows.Media.Geometry]::Parse($innerPath)
$pulse = [Windows.Media.Geometry]::Parse($pulsePath)
$navy = [Windows.Media.BrushConverter]::new().ConvertFromString('#132D43')
$gradient = [Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#32D3D5'), [Windows.Media.ColorConverter]::ConvertFromString('#087F99'), 45)
$pen = [Windows.Media.Pen]::new([Windows.Media.Brushes]::White, 13)
$pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
$pen.LineJoin = [Windows.Media.PenLineJoin]::Round
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
    $drawing.DrawGeometry($navy, $null, $outer)
    $drawing.DrawGeometry($gradient, $null, $inner)
    $drawing.DrawGeometry($null, $pen, $pulse)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $frames.Add($bytes)
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'ProcessGuard.png'), $bytes) }
    $stream.Dispose()
}
# ICO directory with PNG payloads supported by modern Windows (16 through 256px).
$iconStream = [IO.File]::Create((Join-Path $assetDirectory 'ProcessGuard.ico'))
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
Write-Output "Created SVG, PNG and nine-frame ICO in $assetDirectory"
