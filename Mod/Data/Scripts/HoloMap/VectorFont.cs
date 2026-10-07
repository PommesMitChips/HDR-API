using System;
using System.Collections.Generic;
using System.Globalization;
using VRageMath;

namespace HoloMap
{
    // Immutable packaged outlines and a bounded, client-local glyph cache. No font files or URLs are loaded at runtime.
    public static partial class VectorFont
    {
        public sealed class PositionedGlyph { public Geometry Mesh; public Vector3D Offset; }
        sealed class Glyph { public double Advance; public Geometry Mesh; }
        static readonly Dictionary<int,Glyph> Cache = new Dictionary<int,Glyph>();
        static Glyph Read(int code)
        {
            if(!Outlines.ContainsKey(code))code='?';
            Glyph result;if(Cache.TryGetValue(code,out result))return result;
            string[] parts=Outlines[code].Split(';');
            result=new Glyph{Advance=int.Parse(parts[0],CultureInfo.InvariantCulture)/1000.0};
            if(parts[1].Length>0)
            {
                string[] numbers=parts[1].Split(',');var points=new List<Vector3D>();
                for(int i=0;i+1<numbers.Length;i+=2)
                {
                    points.Add(new Vector3D(int.Parse(numbers[i],CultureInfo.InvariantCulture)/1000000.0,int.Parse(numbers[i+1],CultureInfo.InvariantCulture)/1000000.0,0));
                }
                string[] encoded=parts[2].Length==0?new string[0]:parts[2].Split(',');var triangles=new int[encoded.Length];
                for(int i=0;i<triangles.Length;i++)triangles[i]=int.Parse(encoded[i],CultureInfo.InvariantCulture);
                result.Mesh=new Geometry(points.ToArray(),new int[0],triangles);
            }
            if(Cache.Count>=192)Cache.Clear();Cache[code]=result;return result;
        }
        static IEnumerable<int> Characters(string text)
        {
            for(int i=0;i<text.Length;i++)
            {int code=text[i];if(char.IsHighSurrogate(text[i])&&i+1<text.Length&&char.IsLowSurrogate(text[i+1])){code=0x10000+(text[i]-0xd800)*1024+(text[++i]-0xdc00);}yield return code;}
        }
        public static double Width(string text)
        {double width=0;foreach(int code in Characters(text))width+=Read(code=='\t'?' ':code).Advance*(code=='\t'?4:1);return width;}
        public static List<PositionedGlyph> Layout(string text,string anchor="middle",double lineHeight=1.3)
        {
            if(text==null||text.Length>64)throw new ArgumentException("Text requires at most 64 characters.");
            if(anchor!="start"&&anchor!="middle"&&anchor!="end")throw new ArgumentException("Text anchor must be start, middle or end.");
            if(!Geometry.Finite(lineHeight)||lineHeight<1||lineHeight>4)throw new ArgumentException("Text line height must be 1–4.");
            var result=new List<PositionedGlyph>();string[] lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            using(GeometryWork.Begin())for(int row=0;row<lines.Length;row++)
            {
                double width=Width(lines[row]),x=anchor=="start"?0:anchor=="end"?-width:-width/2;
                foreach(int code in Characters(lines[row]))
                {
                    Glyph glyph=Read(code=='\t'?' ':code);
                    if(glyph.Mesh!=null)result.Add(new PositionedGlyph{Mesh=glyph.Mesh,Offset=new Vector3D(x,-0.5-row*lineHeight,0)});
                    x+=glyph.Advance*(code=='\t'?4:1);
                }
            }
            return result;
        }
        public static Geometry Text(string text,double height,string anchor="middle",double lineHeight=1.3)
        {
            if(text==null||text.Length>64||!Geometry.Finite(height)||height<=0||height>1000000)throw new ArgumentException("Text requires at most 64 characters and positive finite height.");
            if(anchor!="start"&&anchor!="middle"&&anchor!="end")throw new ArgumentException("Text anchor must be start, middle or end.");
            if(!Geometry.Finite(lineHeight)||lineHeight<1||lineHeight>4)throw new ArgumentException("Text line height must be 1–4.");
            using(GeometryWork.Begin())
            {
                var builder=new MeshEffects.Builder();string[] lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
                for(int row=0;row<lines.Length;row++)
                {
                    double width=Width(lines[row]),x=anchor=="start"?0:anchor=="end"?-width:-width/2;
                    foreach(int code in Characters(lines[row]))
                    {
                        Glyph glyph=Read(code=='\t'?' ':code);
                        if(glyph.Mesh!=null)for(int i=0;i<glyph.Mesh.Triangles.Length;i+=3)
                        {
                            var a=glyph.Mesh.Points[glyph.Mesh.Triangles[i]];var b=glyph.Mesh.Points[glyph.Mesh.Triangles[i+1]];var c=glyph.Mesh.Points[glyph.Mesh.Triangles[i+2]];
                            var offset=new Vector3D(x,-0.5-row*lineHeight,0);
                            builder.Triangle(MeshEffects.V((a+offset)*height),MeshEffects.V((b+offset)*height),MeshEffects.V((c+offset)*height),Vector4.One);
                        }
                        x+=glyph.Advance*(code=='\t'?4:1);
                    }
                }
                return builder.Finish().Geometry;
            }
        }
        public static bool Supports(int code){return Outlines.ContainsKey(code);}
    }
}
