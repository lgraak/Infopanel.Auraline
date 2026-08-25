[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repo 'assets\branding\auraline-mark.png'
$outputDirectory = Join-Path $repo 'assets\branding\generated'
$frameDirectory = Join-Path $outputDirectory '.frames'
$applicationSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$traySizes = @(16, 20, 24, 32, 40, 48, 64)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $outputDirectory -File |
    Where-Object Name -Match '^auraline-(?:app|tray)-\d+\.png$' |
    Remove-Item -Force
if (Test-Path -LiteralPath $frameDirectory) {
    Get-ChildItem -LiteralPath $frameDirectory -File | Remove-Item -Force
    Remove-Item -LiteralPath $frameDirectory -Force
}
New-Item -ItemType Directory -Path $frameDirectory | Out-Null

function New-TransparentBitmap([int]$width, [int]$height) {
    return [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Set-HighQuality([Drawing.Graphics]$graphics) {
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
}

function Save-ResizedPng([Drawing.Image]$source, [int]$size, [string]$path) {
    $bitmap = New-TransparentBitmap $size $size
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            Set-HighQuality $graphics
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
        }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

function New-TrayMaster([Drawing.Image]$source, [string]$path) {
    $clipped = New-TransparentBitmap $source.Width $source.Height
    try {
        $graphics = [Drawing.Graphics]::FromImage($clipped)
        try {
            Set-HighQuality $graphics
            $graphics.Clear([Drawing.Color]::Transparent)
            $shape = [Drawing.Drawing2D.GraphicsPath]::new()
            $region = [Drawing.Region]::new()
            try {
                $region.MakeEmpty()
                $shape.AddPolygon([Drawing.Point[]]@(
                    [Drawing.Point]::new(627, 0),
                    [Drawing.Point]::new(1215, 1040),
                    [Drawing.Point]::new(40, 1040)
                ))
                $region.Union($shape)
                $region.Union([Drawing.Rectangle]::new(0, 440, 1254, 340))
                $graphics.SetClip($region, [Drawing.Drawing2D.CombineMode]::Replace)
                $graphics.DrawImageUnscaled($source, 0, 0)
                $graphics.ResetClip()
            }
            finally {
                $region.Dispose()
                $shape.Dispose()
            }
        }
        finally { $graphics.Dispose() }
        Save-ResizedPng $clipped 512 $path
    }
    finally { $clipped.Dispose() }
}

function Write-PngIcon([string[]]$framePaths, [string]$path) {
    $frames = [Collections.Generic.List[byte[]]]::new()
    foreach ($framePath in $framePaths) { $frames.Add([IO.File]::ReadAllBytes($framePath)) }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$frames.Count)
            $offset = 6 + (16 * $frames.Count)
            for ($index = 0; $index -lt $frames.Count; $index++) {
                $bitmap = [Drawing.Bitmap]::FromFile($framePaths[$index])
                try {
                    $writer.Write([byte]($(if ($bitmap.Width -ge 256) { 0 } else { $bitmap.Width })))
                    $writer.Write([byte]($(if ($bitmap.Height -ge 256) { 0 } else { $bitmap.Height })))
                }
                finally { $bitmap.Dispose() }
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frame in $frames) { $writer.Write($frame) }
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-TransparentPng([string]$path, [int]$expectedSize) {
    $bitmap = [Drawing.Bitmap]::FromFile($path)
    try {
        if ($bitmap.Width -ne $expectedSize -or $bitmap.Height -ne $expectedSize) {
            throw "Unexpected dimensions for ${path}: $($bitmap.Width)x$($bitmap.Height)."
        }
        $corner = $bitmap.GetPixel(0, 0)
        if ($corner.A -ne 0) { throw "Expected transparent corner alpha in $path; found $($corner.A)." }
    }
    finally { $bitmap.Dispose() }
}

if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Canonical artwork is missing: $sourcePath" }
$source = [Drawing.Bitmap]::FromFile($sourcePath)
try {
    if ($source.Width -ne 1254 -or $source.Height -ne 1254) {
        throw "Canonical artwork dimensions changed: $($source.Width)x$($source.Height)."
    }
    if ($source.GetPixel(0, 0).A -ne 0) { throw 'Canonical artwork no longer has transparent corners.' }

    $trayMasterPath = Join-Path $outputDirectory 'auraline-tray-master.png'
    New-TrayMaster $source $trayMasterPath
    $trayMaster = [Drawing.Bitmap]::FromFile($trayMasterPath)
    try {
        $trayFrames = foreach ($size in $traySizes) {
            $framePath = Join-Path $frameDirectory "auraline-tray-$size.png"
            Save-ResizedPng $trayMaster $size $framePath
            Assert-TransparentPng $framePath $size
            $framePath
        }
        $applicationFrames = foreach ($size in $applicationSizes) {
            $framePath = Join-Path $frameDirectory "auraline-app-$size.png"
            $frameSource = if ($size -le 48) { $trayMaster } else { $source }
            Save-ResizedPng $frameSource $size $framePath
            Assert-TransparentPng $framePath $size
            $framePath
        }
        Save-ResizedPng $source 96 (Join-Path $outputDirectory 'auraline-mark-96.png')
        Save-ResizedPng $source 256 (Join-Path $outputDirectory 'auraline-mark-256.png')
        Write-PngIcon $applicationFrames (Join-Path $outputDirectory 'auraline.ico')
        Write-PngIcon $trayFrames (Join-Path $outputDirectory 'auraline-tray.ico')
    }
    finally { $trayMaster.Dispose() }
}
finally { $source.Dispose() }

Get-ChildItem -LiteralPath $frameDirectory -File | Remove-Item -Force
Remove-Item -LiteralPath $frameDirectory -Force

Get-ChildItem -LiteralPath $outputDirectory -File |
    Sort-Object Name |
    Select-Object Name, Length, @{ Name = 'SHA256'; Expression = { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash } }
