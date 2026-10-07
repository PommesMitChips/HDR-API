$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$glyphs=@{}
foreach($line in Get-Content "$PSScriptRoot/../Mod/Data/Scripts/HoloMap/VectorFontData.cs") {
    if($line -match '\{(\d+),"(\d+);([^;]*);([^"]*)"\}') {
        $glyphs[[int]$Matches[1]]=@(([double]$Matches[2]/1000),$Matches[3],$Matches[4])
    }
}
$canvas=[System.Drawing.Bitmap]::new(2400,780)
$graphics=[System.Drawing.Graphics]::FromImage($canvas)
$graphics.Clear([System.Drawing.Color]::FromArgb(14,20,30))
$brush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(97,225,234))
$samples=@('HDR API — Mining arm','Énergie 42°C · Ωαβ · Привет','Lowercase g j p q y','Menus / layers / controls')
for($row=0;$row -lt $samples.Length;$row++) {
    $x=60
    foreach($char in $samples[$row].ToCharArray()) {
        $glyph=$glyphs[[int]$char];if(!$glyph){$glyph=$glyphs[63]}
        if($glyph[1].Length) {
            $coordinates=@($glyph[1].Split(',')|ForEach-Object {[double]$_/1000000})
            $triangles=@($glyph[2].Split(',')|ForEach-Object {[int]$_})
            for($triangle=0;$triangle -lt $triangles.Length;$triangle+=3) {
                $polygon=[System.Drawing.PointF[]]::new(3)
                for($corner=0;$corner -lt 3;$corner++) {
                    $index=$triangles[$triangle+$corner]*2
                    $polygon[$corner]=[System.Drawing.PointF]::new(($x+$coordinates[$index]*120),(170+$row*170-$coordinates[$index+1]*120))
                }
                $graphics.FillPolygon($brush,$polygon)
            }
        }
        $x+=$glyph[0]*120
    }
}
$preview=[System.Drawing.Bitmap]::new(800,260)
$previewGraphics=[System.Drawing.Graphics]::FromImage($preview)
$previewGraphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$previewGraphics.DrawImage($canvas,0,0,800,260)
$preview.Save("$PSScriptRoot/../artifacts/VectorFontPreview.png",[System.Drawing.Imaging.ImageFormat]::Png)
$previewGraphics.Dispose();$preview.Dispose();$graphics.Dispose();$canvas.Dispose();$brush.Dispose()
