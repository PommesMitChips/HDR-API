param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_-]{1,48}$')][string]$Asset
)
# Offline authoring only. PNG/JPEG/BMP/GIF (first frame) -> packaged RGBA DDS.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$image = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $InputPath).Path)
try {
    if ($image.Width -gt 2048 -or $image.Height -gt 2048) { throw 'Images must be at most 2048 by 2048; resize before packing.' }
    $textureDir = Join-Path $taskRoot 'Mod\Textures\HoloMap'
    [void][System.IO.Directory]::CreateDirectory($textureDir)
    $output = Join-Path $textureDir "$Asset.dds"
    $stream = [System.IO.File]::Create($output)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint32]0x20534444) # DDS magic
        $header = [uint32[]]::new(31)
        $header[0]=124; $header[1]=0x100F; $header[2]=$image.Height; $header[3]=$image.Width; $header[4]=$image.Width*4
        $header[18]=32; $header[19]=0x41; $header[21]=32
        $header[22]=0xFF; $header[23]=0xFF00; $header[24]=0xFF0000; $header[25]=[uint32]4278190080; $header[26]=0x1000
        foreach ($word in $header) { $writer.Write($word) }
        for ($y=0; $y -lt $image.Height; $y++) {
            for ($x=0; $x -lt $image.Width; $x++) {
                $pixel=$image.GetPixel($x,$y)
                $writer.Write([byte]$pixel.R); $writer.Write([byte]$pixel.G); $writer.Write([byte]$pixel.B); $writer.Write([byte]$pixel.A)
            }
        }
    } finally { $writer.Dispose(); $stream.Dispose() }
    $material = @"
<?xml version="1.0" encoding="utf-8"?>
<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <TransparentMaterials><TransparentMaterial>
    <Id><TypeId>TransparentMaterialDefinition</TypeId><SubtypeId>HoloMap_Image_$Asset</SubtypeId></Id>
    <Texture>Textures\HoloMap\$Asset.dds</Texture>
    <CanBeAffectedByOtherLights>false</CanBeAffectedByOtherLights>
    <SoftParticleDistanceScale>0</SoftParticleDistanceScale>
    <AlphaMistingEnable>false</AlphaMistingEnable><Reflectivity>0</Reflectivity>
  </TransparentMaterial></TransparentMaterials>
  <LCDTextures><LCDTextureDefinition><Id><TypeId>LCDTextureDefinition</TypeId><SubtypeId>HoloMap_Image_$Asset</SubtypeId></Id><TexturePath>Textures\HoloMap\$Asset.dds</TexturePath><SpritePath>Textures\HoloMap\$Asset.dds</SpritePath></LCDTextureDefinition></LCDTextures>
</Definitions>
"@
    [System.IO.File]::WriteAllText((Join-Path $taskRoot "Mod\Data\Image_$Asset.sbc"),$material,[System.Text.UTF8Encoding]::new($false))
    Write-Output "Packed $Asset ($($image.Width) x $($image.Height)): $output"
} finally { $image.Dispose() }
