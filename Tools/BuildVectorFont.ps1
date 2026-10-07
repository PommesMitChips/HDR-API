param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FontPath,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FontLicensePath
)
$ErrorActionPreference='Stop'
# Offline conversion only. The mod ships outlines, never loads operating-system fonts.
if (-not (Test-Path -LiteralPath $FontPath -PathType Leaf)) { throw ('Inter font file not found: ' + $FontPath) }
if (-not (Test-Path -LiteralPath $FontLicensePath -PathType Leaf)) { throw ('Inter license file not found: ' + $FontLicensePath) }
$FontPath = (Resolve-Path -LiteralPath $FontPath).ProviderPath
$FontLicensePath = (Resolve-Path -LiteralPath $FontLicensePath).ProviderPath
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
public static class HoloFontBake {
    sealed class Edge { public double X0,Y0,X1,Y1; public int Winding { get {return Y1>Y0?1:-1;} } public double X(double y){return X0+(X1-X0)*(y-Y0)/(Y1-Y0);} }
    static double Area(double[][] xy,int a,int b,int c){return (xy[b][0]-xy[a][0])*(xy[c][1]-xy[a][1])-(xy[b][1]-xy[a][1])*(xy[c][0]-xy[a][0]);}
    static bool Covers(double[][] xy,IList<int> triangles,double x,double y){
        for(int i=0;i<triangles.Count;i+=3){bool inside=true;for(int k=0;k<3;k++){var a=xy[triangles[i+k]];var b=xy[triangles[i+(k+1)%3]];double dx=b[0]-a[0],dy=b[1]-a[1];if(dx*(y-a[1])-dy*(x-a[0]) < -2*Math.Sqrt(dx*dx+dy*dy)){inside=false;break;}}if(inside&&Area(xy,triangles[i],triangles[i+1],triangles[i+2])>4)return true;}return false;
    }
    static void AssertCoverage(double[][] xy,IList<int> before,IList<int> after){
        Action<IList<int>,IList<int>> samples=(source,target)=>{for(int i=0;i<source.Count;i+=3){var a=xy[source[i]];var b=xy[source[i+1]];var c=xy[source[i+2]];if(Area(xy,source[i],source[i+1],source[i+2])<=4)continue;foreach(var weights in new[]{new[]{1d/3,1d/3,1d/3},new[]{.6,.2,.2},new[]{.2,.6,.2},new[]{.2,.2,.6}})if(!Covers(xy,target,a[0]*weights[0]+b[0]*weights[1]+c[0]*weights[2],a[1]*weights[0]+b[1]*weights[1]+c[1]*weights[2]))throw new InvalidOperationException("Font compaction changed triangle coverage.");}};
        samples(before,after);samples(after,before);
        if(xy.Length==0)return;double minX=xy.Min(p=>p[0]),maxX=xy.Max(p=>p[0]),minY=xy.Min(p=>p[1]),maxY=xy.Max(p=>p[1]);
        for(int y=0;y<24;y++)for(int x=0;x<24;x++){double px=minX+(maxX-minX)*(x+.371)/24,py=minY+(maxY-minY)*(y+.619)/24;if(Covers(xy,before,px,py)!=Covers(xy,after,px,py))throw new InvalidOperationException("Font compaction changed sampled coverage.");}
    }
    public static string Bake(string loops) {
        var edges=new List<Edge>();var levels=new SortedSet<double>();
        foreach(string loop in loops.Split('|')) {
            if(loop.Length==0)continue;var n=loop.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            for(int i=0;i<n.Length;i+=2){int j=(i+2)%n.Length;levels.Add(n[i+1]);if(n[i+1]!=n[j+1])edges.Add(new Edge{X0=n[i],Y0=n[i+1],X1=n[j],Y1=n[j+1]});}
        }
        // Overlapping contours can cross between their original vertex levels.
        // Split there so edge ordering stays constant within every scan strip.
        for(int i=0;i<edges.Count;i++)for(int j=i+1;j<edges.Count;j++){
            var a=edges[i];var b=edges[j];double ax=a.X1-a.X0,ay=a.Y1-a.Y0,bx=b.X1-b.X0,by=b.Y1-b.Y0,det=ax*by-ay*bx;
            if(Math.Abs(det)<1e-12)continue;double dx=b.X0-a.X0,dy=b.Y0-a.Y0,t=(dx*by-dy*bx)/det,u=(dx*ay-dy*ax)/det;
            if(t>1e-9&&t<1-1e-9&&u>1e-9&&u<1-1e-9)levels.Add(a.Y0+t*ay);
        }
        var positions=new List<string>();var indices=new Dictionary<string,int>();var triangles=new List<int>();
        Func<double,double,int> point=(x,y)=>{string key=Math.Round(x*1000).ToString(CultureInfo.InvariantCulture)+","+Math.Round(y*1000).ToString(CultureInfo.InvariantCulture);int id;if(!indices.TryGetValue(key,out id)){id=positions.Count;indices.Add(key,id);positions.Add(key);}return id;};
        Action<int,int,int> triangle=(a,b,c)=>{if(a!=b&&b!=c&&c!=a){triangles.Add(a);triangles.Add(b);triangles.Add(c);}};
        var ys=levels.ToArray();
        for(int row=0;row+1<ys.Length;row++) {
            double low=ys[row],high=ys[row+1],mid=(low+high)/2;
            var active=edges.Where(e=>mid>Math.Min(e.Y0,e.Y1)&&mid<Math.Max(e.Y0,e.Y1)).OrderBy(e=>e.X(mid)).ToArray();
            int winding=0;Edge left=null;
            foreach(var edge in active){int previous=winding;winding+=edge.Winding;if(previous==0&&winding!=0)left=edge;else if(previous!=0&&winding==0){
                int a=point(left.X(low),low),b=point(edge.X(low),low),c=point(edge.X(high),high),d=point(left.X(high),high);
                triangle(a,b,c);triangle(a,c,d);
            }}
            if(winding!=0)throw new InvalidOperationException("Unclosed font contour winding.");
        }
        // Compact collinear boundaries without changing the nonzero-fill reference.
        // A candidate must preserve the incident signed area and positive winding.
        var xy=positions.Select(s=>s.Split(',').Select(n=>double.Parse(n,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        for(int i=triangles.Count-3;i>=0;i-=3)if(Area(xy,triangles[i],triangles[i+1],triangles[i+2])<=4)triangles.RemoveRange(i,3);
        var original=triangles.ToArray();
        for(int pass=0;pass<4;pass++){
            bool changed=false;
            for(int vertex=0;vertex<xy.Length;vertex++){
                var counts=new Dictionary<long,int>();Action<int,int> edge=(a,b)=>{long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);int count;counts.TryGetValue(key,out count);counts[key]=count+1;};
                for(int i=0;i<triangles.Count;i+=3){edge(triangles[i],triangles[i+1]);edge(triangles[i+1],triangles[i+2]);edge(triangles[i+2],triangles[i]);}
                var neighbors=new List<int>();foreach(var entry in counts)if(entry.Value==1){int a=(int)(entry.Key>>32),b=(int)entry.Key;if(a==vertex)neighbors.Add(b);else if(b==vertex)neighbors.Add(a);}
                if(neighbors.Count!=2)continue;var p=xy[vertex];var l=xy[neighbors[0]];var r=xy[neighbors[1]];double dx=r[0]-l[0],dy=r[1]-l[1],length=Math.Sqrt(dx*dx+dy*dy);
                if(length==0||Math.Abs(dx*(p[1]-l[1])-dy*(p[0]-l[0]))>length*2||((p[0]-l[0])*(p[0]-r[0])+(p[1]-l[1])*(p[1]-r[1]))>1)continue;
                int replacement=neighbors[0];bool valid=true;double before=0,after=0;
                for(int i=0;i<triangles.Count;i+=3){int a=triangles[i],b=triangles[i+1],c=triangles[i+2];if(a!=vertex&&b!=vertex&&c!=vertex)continue;before+=Area(xy,a,b,c);if(a==vertex)a=replacement;if(b==vertex)b=replacement;if(c==vertex)c=replacement;if(a==b||b==c||c==a)continue;double area=Area(xy,a,b,c);if(area < -4){valid=false;break;}after+=area;}
                if(!valid||Math.Abs(before-after)>Math.Max(4,Math.Abs(before)*1e-4))continue;
                var compact=new List<int>();for(int i=0;i<triangles.Count;i+=3){int a=triangles[i]==vertex?replacement:triangles[i],b=triangles[i+1]==vertex?replacement:triangles[i+1],c=triangles[i+2]==vertex?replacement:triangles[i+2];if(a!=b&&b!=c&&c!=a&&Area(xy,a,b,c)>4){compact.Add(a);compact.Add(b);compact.Add(c);}}
                triangles=compact;changed=true;
            }
            if(!changed)break;
        }
        // Quantized near-collinear corners occasionally reject compaction. Keep
        // the reference mesh for that glyph rather than publishing changed fill.
        try{AssertCoverage(xy,original,triangles);}catch(InvalidOperationException){triangles=original.ToList();AssertCoverage(xy,original,triangles);}
        var used=new Dictionary<int,int>();var final=new List<string>();
        for(int i=0;i<triangles.Count;i++){int id;if(!used.TryGetValue(triangles[i],out id)){id=final.Count;used.Add(triangles[i],id);final.Add(positions[triangles[i]]);}triangles[i]=id;}
        return string.Join(",",final)+";"+string.Join(",",triangles);
    }
    static bool BakedCovers(string baked,double x,double y){var parts=baked.Split(';');var values=parts[0].Split(',').Select(n=>double.Parse(n,CultureInfo.InvariantCulture)).ToArray();var xy=new double[values.Length/2][];for(int i=0;i<xy.Length;i++)xy[i]=new[]{values[i*2],values[i*2+1]};return Covers(xy,parts[1].Split(',').Select(int.Parse).ToArray(),x*1000,y*1000);}
    public static void SelfTest(){
        string overlap=Bake("0,0,1000,0,1000,1000,0,1000|500,0,1500,0,1500,1000,500,1000");
        if(!BakedCovers(overlap,750,500)||!BakedCovers(overlap,250,500)||!BakedCovers(overlap,1250,500))throw new InvalidOperationException("Overlapping glyph contours must use nonzero winding, not XOR.");
        string counter=Bake("0,0,1000,0,1000,1000,0,1000|250,250,250,750,750,750,750,250");
        if(BakedCovers(counter,500,500)||!BakedCovers(counter,100,500))throw new InvalidOperationException("Opposite winding must retain glyph counters.");
        string nested=Bake("0,0,1000,0,1000,1000,0,1000|250,250,750,250,750,750,250,750");
        if(!BakedCovers(nested,500,500))throw new InvalidOperationException("Same-winding nested contours remain filled.");
    }
}
'@
[HoloFontBake]::SelfTest()
$collection = New-Object System.Drawing.Text.PrivateFontCollection
$collection.AddFontFile($FontPath)
$family = $collection.Families | Where-Object Name -eq 'Inter' | Select-Object -First 1
if (-not $family) { $collection.Dispose(); throw 'The supplied font must provide the Inter font family.' }
$font = [System.Drawing.Font]::new($family,100,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
$bitmap = [System.Drawing.Bitmap]::new(1,1)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$format = [System.Drawing.StringFormat]::GenericTypographic.Clone()
$format.FormatFlags = $format.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces
$probe = [System.Drawing.Drawing2D.GraphicsPath]::new()
$probe.AddString('H',$family,0,100,[System.Drawing.PointF]::new(0,0),$format)
$cap = $probe.GetBounds().Height
$baseline = $probe.GetBounds().Bottom
$output = [System.Text.StringBuilder]::new()
[void]$output.AppendLine('// Generated from Inter, Copyright The Inter Project Authors, SIL Open Font License 1.1.')
[void]$output.AppendLine('// See Data/Fonts/Inter-LICENSE.txt. Regenerate using Tools/BuildVectorFont.ps1.')
[void]$output.AppendLine('using System.Collections.Generic; namespace HoloMap { public static partial class VectorFont { static readonly Dictionary<int,string> Outlines = new Dictionary<int,string> {')
$codes = @((32..126) + (160..383) + (880..1023) + (1024..1279) + @(176,8226,8211,8212,8216,8217,8220,8221,8230,8364,8592,8593,8594,8595,8722,8730,8804,8805)) | Sort-Object -Unique
foreach ($code in $codes) {
    $character = [char]$code
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddString([string]$character,$family,0,100,[System.Drawing.PointF]::new(0,0),$format)
    $path.Flatten($null,1.15)
    $advance = [int][Math]::Round($graphics.MeasureString([string]$character,$font,[System.Drawing.PointF]::new(0,0),$format).Width / $cap * 1000)
    $parts = [System.Collections.Generic.List[string]]::new()
    $contour = [System.Collections.Generic.List[string]]::new()
    for ($index=0; $index -lt $path.PointCount; $index++) {
        if (($path.PathTypes[$index] -band 7) -eq 0 -and $contour.Count -gt 0) { $parts.Add(($contour -join ',')); $contour.Clear() }
        $point = $path.PathPoints[$index]
        $contour.Add([string][int][Math]::Round($point.X / $cap * 1000))
        $contour.Add([string][int][Math]::Round(($baseline-$point.Y) / $cap * 1000))
        if (($path.PathTypes[$index] -band 128) -ne 0) { if ($contour.Count -ge 6) {$parts.Add(($contour -join ','))}; $contour.Clear() }
    }
    if ($contour.Count -ge 6) { $parts.Add(($contour -join ',')) }
    [void]$output.AppendLine(('{{{0},"{1};{2}"}},' -f $code,$advance,[HoloFontBake]::Bake(($parts -join '|'))))
    $path.Dispose()
}
[void]$output.AppendLine('}; } }')
[System.IO.File]::WriteAllText("$PSScriptRoot/../Mod/Data/Scripts/HoloMap/VectorFontData.cs",$output.ToString())
$directory = "$PSScriptRoot/../Mod/Data/Fonts"
[void][System.IO.Directory]::CreateDirectory($directory)
$licenseTarget = [System.IO.Path]::GetFullPath((Join-Path $directory 'Inter-LICENSE.txt'))
if ($FontLicensePath -ine $licenseTarget) {
    Copy-Item -LiteralPath $FontLicensePath -Destination $licenseTarget -Force
}
$probe.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $font.Dispose(); $collection.Dispose()
