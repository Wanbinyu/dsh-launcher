[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assetDirectory = $PSScriptRoot
$sourcePath = Join-Path $assetDirectory 'dsh-launcher.svg'
$smallSourcePath = Join-Path $assetDirectory 'dsh-launcher-small.svg'
$previewPath = Join-Path $assetDirectory 'dsh-launcher.png'
$iconPath = Join-Path $assetDirectory 'dsh-launcher.ico'
$renderPath = Join-Path $assetDirectory '.dsh-launcher-render.png'
$smallRenderPath = Join-Path $assetDirectory '.dsh-launcher-small-render.png'

$browser = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($browser)) {
    throw 'Microsoft Edge or Google Chrome is required to render the SVG icon.'
}

try {
    $sourceUri = ([Uri]$sourcePath).AbsoluteUri
    & $browser --headless=new --disable-gpu --hide-scrollbars --log-level=3 `
        --window-size=1024,1024 --force-device-scale-factor=1 `
        --default-background-color=00000000 --screenshot=$renderPath $sourceUri | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $renderPath -PathType Leaf)) {
        throw 'The browser could not render the SVG icon.'
    }

    $smallSourceUri = ([Uri]$smallSourcePath).AbsoluteUri
    & $browser --headless=new --disable-gpu --hide-scrollbars --log-level=3 `
        --window-size=1024,1024 --force-device-scale-factor=1 `
        --default-background-color=00000000 --screenshot=$smallRenderPath $smallSourceUri | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $smallRenderPath -PathType Leaf)) {
        throw 'The browser could not render the small SVG icon.'
    }

    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($renderPath)
    $smallSource = [System.Drawing.Image]::FromFile($smallRenderPath)
    try {
        $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
        $images = [System.Collections.Generic.List[byte[]]]::new()
        foreach ($size in $sizes) {
            $bitmap = [System.Drawing.Bitmap]::new(
                $size,
                $size,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $frameSource = if ($size -le 24) { $smallSource } else { $source }
                    $graphics.DrawImage($frameSource, 0, 0, $size, $size)
                } finally {
                    $graphics.Dispose()
                }

                $stream = [System.IO.MemoryStream]::new()
                try {
                    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                    $images.Add($stream.ToArray())
                } finally {
                    $stream.Dispose()
                }

                if ($size -eq 256) {
                    $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
                }
            } finally {
                $bitmap.Dispose()
            }
        }

        $output = [System.IO.MemoryStream]::new()
        $writer = [System.IO.BinaryWriter]::new($output)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$images.Count)
            $offset = 6 + (16 * $images.Count)
            for ($index = 0; $index -lt $images.Count; $index++) {
                $size = $sizes[$index]
                $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$images[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $images[$index].Length
            }

            foreach ($image in $images) {
                $writer.Write($image)
            }

            [System.IO.File]::WriteAllBytes($iconPath, $output.ToArray())
        } finally {
            $writer.Dispose()
            $output.Dispose()
        }
    } finally {
        $smallSource.Dispose()
        $source.Dispose()
    }
} finally {
    Remove-Item -LiteralPath $renderPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $smallRenderPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Created $previewPath"
Write-Host "Created $iconPath"
