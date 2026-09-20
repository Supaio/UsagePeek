param(
    [string]$SourcePath = (Join-Path $PSScriptRoot "..\assets\usagepeek-logo.png"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\assets\usagepeek.ico")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-ResizedPngBytes {
    param(
        [System.Drawing.Image]$SourceImage,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $destination = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
        $graphics.DrawImage($SourceImage, $destination)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Source image was not found: $SourcePath"
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$sourceImage = [System.Drawing.Image]::FromFile($SourcePath)
$images = New-Object System.Collections.Generic.List[object]

try {
    foreach ($size in $sizes) {
        $images.Add([PSCustomObject]@{
            Size = $size
            Bytes = New-ResizedPngBytes -SourceImage $sourceImage -Size $size
        })
    }
}
finally {
    $sourceImage.Dispose()
}

$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)

try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $imageOffset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt32]$imageOffset)
        $imageOffset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($OutputPath, $iconStream.ToArray())
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}

Write-Output "Created $OutputPath with sizes: $($sizes -join ', ')"
