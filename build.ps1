# Builds AffinityManager.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src  = Join-Path $root 'src'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$ico  = Join-Path $src 'app.ico'

# Generate a simple "chip" icon (PNG-compressed .ico with several sizes).
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 32, 48, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        $u = $s / 16.0
        $pin = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 160, 175))
        foreach ($i in 0..2) {
            $o = (4 + $i * 3) * $u
            $g.FillRectangle($pin, $o, 0.5 * $u, 1.5 * $u, 3 * $u)
            $g.FillRectangle($pin, $o, 12.5 * $u, 1.5 * $u, 3 * $u)
            $g.FillRectangle($pin, 0.5 * $u, $o, 3 * $u, 1.5 * $u)
            $g.FillRectangle($pin, 12.5 * $u, $o, 3 * $u, 1.5 * $u)
        }
        $body = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 103, 192))
        $g.FillRectangle($body, 2.5 * $u, 2.5 * $u, 11 * $u, 11 * $u)
        $core = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255))
        $g.FillRectangle($core, 5.5 * $u, 5.5 * $u, 5 * $u, 5 * $u)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($ico, $out.ToArray())
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$root\AffinityManager.exe" `
    "/win32manifest:$src\app.manifest" `
    "/win32icon:$ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    /r:Microsoft.CSharp.dll `
    "$src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $root\AffinityManager.exe"
