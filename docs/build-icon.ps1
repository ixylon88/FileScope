param (
    [string]$InputPng = ".\icon.png",
    [string]$OutputIco = ".\icon.ico"
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $InputPng)) {
    Write-Error "Eingabedatei nicht gefunden: $InputPng"
    exit 1
}

$sizes = @(16, 32, 48, 256)
$sourceBitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $InputPng))

$fs = [System.IO.File]::OpenWrite($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputIco))
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR Header (6 Bytes)
$bw.Write([uint16]0)             # Reserved (immer 0)
$bw.Write([uint16]1)             # Type (1 = Icon)
$bw.Write([uint16]$sizes.Count)  # Anzahl der Bilder

$imageDataList = New-Object System.Collections.Generic.List[byte[]]

# Alle Grössen als hochqualitative PNG-Streams vorbereiten
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $g.DrawImage($sourceBitmap, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageDataList.Add($ms.ToArray())
    
    $ms.Dispose()
    $bmp.Dispose()
}

# Offset berechnen (Header + alle Directory-Einträge à 16 Bytes)
$offset = 6 + (16 * $sizes.Count)

# ICONDIRENTRY für jedes Bild schreiben
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $imageDataList[$i]

    # Breite & Höhe (0 steht im ICO-Format für 256px)
    $dim = if ($size -ge 256) { [byte]0 } else { [byte]$size }

    $bw.Write($dim)                 # Width
    $bw.Write($dim)                 # Height
    $bw.Write([byte]0)              # ColorCount (0 = TrueColor)
    $bw.Write([byte]0)              # Reserved
    $bw.Write([uint16]1)            # ColorPlanes
    $bw.Write([uint16]32)           # BitsPerPixel (32-Bit RGBA)
    $bw.Write([uint32]$data.Length) # Grösse der Bilddaten in Bytes
    $bw.Write([uint32]$offset)      # Offset zu den Bilddaten

    $offset += $data.Length
}

# Bilddaten anhängen
foreach ($data in $imageDataList) {
    $bw.Write($data)
}

$bw.Close()
$fs.Close()
$sourceBitmap.Dispose()

Write-Host "Sauberes Icon erstellt unter: $OutputIco" -ForegroundColor Green
