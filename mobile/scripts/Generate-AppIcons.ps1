$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$mobileRoot = Split-Path -Parent $scriptRoot
$repoRoot = Split-Path -Parent $mobileRoot
$sourceIcon = Join-Path $repoRoot "img\icon.png"

if (-not (Test-Path -LiteralPath $sourceIcon)) {
    throw "Source app icon was not found at $sourceIcon"
}

Add-Type -AssemblyName System.Drawing

function New-Canvas {
    param(
        [int]$Width,
        [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    return $bitmap
}

function Save-ResizedPng {
    param(
        [System.Drawing.Image]$Source,
        [string]$Destination,
        [int]$Width,
        [int]$Height,
        [switch]$RoundMask,
        [switch]$Contain,
        [double]$ArtworkScale = 1.0
    )

    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $bitmap = New-Canvas -Width $Width -Height $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        if ($RoundMask) {
            $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $path.AddEllipse(0, 0, $Width, $Height)
                $graphics.SetClip($path)
            }
            finally {
                $path.Dispose()
            }
        }

        if ($Contain) {
            $scale = [Math]::Min($Width / $Source.Width, $Height / $Source.Height)
            $drawWidth = [int][Math]::Round($Source.Width * $scale)
            $drawHeight = [int][Math]::Round($Source.Height * $scale)
            $drawX = [int][Math]::Round(($Width - $drawWidth) / 2)
            $drawY = [int][Math]::Round(($Height - $drawHeight) / 2)
            $graphics.DrawImage($Source, $drawX, $drawY, $drawWidth, $drawHeight)
        }
        else {
            $sourceSize = [Math]::Min($Source.Width, $Source.Height)
            $sourceX = [int][Math]::Floor(($Source.Width - $sourceSize) / 2)
            $sourceY = [int][Math]::Floor(($Source.Height - $sourceSize) / 2)
            $sourceRectangle = [System.Drawing.Rectangle]::new($sourceX, $sourceY, $sourceSize, $sourceSize)
            $drawWidth = [int][Math]::Round($Width * $ArtworkScale)
            $drawHeight = [int][Math]::Round($Height * $ArtworkScale)
            $drawX = [int][Math]::Round(($Width - $drawWidth) / 2)
            $drawY = [int][Math]::Round(($Height - $drawHeight) / 2)
            $destinationRectangle = [System.Drawing.Rectangle]::new($drawX, $drawY, $drawWidth, $drawHeight)
            $graphics.DrawImage($Source, $destinationRectangle, $sourceRectangle, [System.Drawing.GraphicsUnit]::Pixel)
        }
    }
    finally {
        $graphics.Dispose()
    }

    try {
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Save-SplashPng {
    param(
        [System.Drawing.Image]$Source,
        [string]$Destination,
        [int]$Width,
        [int]$Height
    )

    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $corner = [System.Drawing.Bitmap]$Source
    $background = $corner.GetPixel(0, 0)
    $bitmap = New-Canvas -Width $Width -Height $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear($background)

        $targetSize = [int][Math]::Round([Math]::Min($Width, $Height) * 0.72)
        $drawX = [int][Math]::Round(($Width - $targetSize) / 2)
        $drawY = [int][Math]::Round(($Height - $targetSize) / 2)
        $destinationRectangle = [System.Drawing.Rectangle]::new($drawX, $drawY, $targetSize, $targetSize)
        $sourceRectangle = [System.Drawing.Rectangle]::new(0, 0, $Source.Width, $Source.Height)
        $graphics.DrawImage($Source, $destinationRectangle, $sourceRectangle, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    try {
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$source = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $sourceIcon).Path)
try {
    $adaptiveForegroundArtworkScale = 0.60

    Save-ResizedPng -Source $source -Destination (Join-Path $mobileRoot "src\ElectronicLogbook.Mobile\wwwroot\icon-192.png") -Width 192 -Height 192
    Save-ResizedPng -Source $source -Destination (Join-Path $mobileRoot "src\ElectronicLogbook.Mobile\wwwroot\icon-512.png") -Width 512 -Height 512

    $launcherSizes = @{
        "mdpi" = 48
        "hdpi" = 72
        "xhdpi" = 96
        "xxhdpi" = 144
        "xxxhdpi" = 192
    }

    foreach ($density in $launcherSizes.Keys) {
        $size = $launcherSizes[$density]
        $directory = Join-Path $mobileRoot "android\app\src\main\res\mipmap-$density"
        Save-ResizedPng -Source $source -Destination (Join-Path $directory "ic_launcher.png") -Width $size -Height $size
        Save-ResizedPng -Source $source -Destination (Join-Path $directory "ic_launcher_round.png") -Width $size -Height $size -RoundMask
        Save-ResizedPng -Source $source -Destination (Join-Path $directory "ic_launcher_foreground.png") -Width ([int]($size * 2.25)) -Height ([int]($size * 2.25)) -ArtworkScale $adaptiveForegroundArtworkScale
    }

    $splashSizes = @(
        @{ Path = "drawable\splash.png"; Width = 480; Height = 320 },
        @{ Path = "drawable-land-mdpi\splash.png"; Width = 480; Height = 320 },
        @{ Path = "drawable-land-hdpi\splash.png"; Width = 800; Height = 480 },
        @{ Path = "drawable-land-xhdpi\splash.png"; Width = 1280; Height = 720 },
        @{ Path = "drawable-land-xxhdpi\splash.png"; Width = 1600; Height = 960 },
        @{ Path = "drawable-land-xxxhdpi\splash.png"; Width = 1920; Height = 1280 },
        @{ Path = "drawable-port-mdpi\splash.png"; Width = 320; Height = 480 },
        @{ Path = "drawable-port-hdpi\splash.png"; Width = 480; Height = 800 },
        @{ Path = "drawable-port-xhdpi\splash.png"; Width = 720; Height = 1280 },
        @{ Path = "drawable-port-xxhdpi\splash.png"; Width = 960; Height = 1600 },
        @{ Path = "drawable-port-xxxhdpi\splash.png"; Width = 1280; Height = 1920 }
    )

    foreach ($splash in $splashSizes) {
        Save-SplashPng -Source $source -Destination (Join-Path (Join-Path $mobileRoot "android\app\src\main\res") $splash.Path) -Width $splash.Width -Height $splash.Height
    }
}
finally {
    $source.Dispose()
}

Write-Host "Generated app icons from $sourceIcon"
