# Reproducible native LCD triangle templates; no third-party image assets.
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$dimension=256
$textureDir=Join-Path $taskRoot 'Mod\Textures\HDRAPI'
[void][IO.Directory]::CreateDirectory($textureDir)
foreach($taskMirror in @($false,$true)) {
 $asset=if($taskMirror){'TriangleLeft'}else{'TriangleRight'}
 $stream=[IO.File]::Create((Join-Path $textureDir "$asset.dds"));$writer=[IO.BinaryWriter]::new($stream)
 try {
  $writer.Write([uint32]0x20534444)
  $header=[uint32[]]::new(31);$header[0]=124;$header[1]=0x100F;$header[2]=$dimension;$header[3]=$dimension;$header[4]=$dimension*4
  $header[18]=32;$header[19]=0x41;$header[21]=32;$header[22]=0xFF;$header[23]=0xFF00;$header[24]=0xFF0000;$header[25]=[uint32]4278190080;$header[26]=0x1000
  foreach($word in $header){$writer.Write($word)}
  for($y=0;$y -lt $dimension;$y++){for($x=0;$x -lt $dimension;$x++){
   $coverage=0
   for($sy=0;$sy -lt 4;$sy++){for($sx=0;$sx -lt 4;$sx++){
    $px=($x+($sx+0.5)/4)/$dimension;$py=($y+($sy+0.5)/4)/$dimension
    if(($taskMirror -and $px -ge $py) -or (!$taskMirror -and $px+$py -le 1)){$coverage++}
   }}
   $writer.Write([byte]255);$writer.Write([byte]255);$writer.Write([byte]255);$writer.Write([byte][Math]::Round(255*$coverage/16))
  }}
 } finally {$writer.Dispose();$stream.Dispose()}
}
$definition=@'
<?xml version="1.0" encoding="utf-8"?>
<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
 <LCDTextures>
  <LCDTextureDefinition><Id><TypeId>LCDTextureDefinition</TypeId><SubtypeId>HDRAPI_TriangleLeft</SubtypeId></Id><TexturePath>Textures\HDRAPI\TriangleLeft.dds</TexturePath><SpritePath>Textures\HDRAPI\TriangleLeft.dds</SpritePath></LCDTextureDefinition>
  <LCDTextureDefinition><Id><TypeId>LCDTextureDefinition</TypeId><SubtypeId>HDRAPI_TriangleRight</SubtypeId></Id><TexturePath>Textures\HDRAPI\TriangleRight.dds</TexturePath><SpritePath>Textures\HDRAPI\TriangleRight.dds</SpritePath></LCDTextureDefinition>
 </LCDTextures>
</Definitions>
'@
[IO.File]::WriteAllText((Join-Path $taskRoot 'Mod\Data\LcdTriangles.sbc'),$definition,[Text.UTF8Encoding]::new($false))
