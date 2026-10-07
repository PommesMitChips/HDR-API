using System;
using System.Collections.Generic;
using System.Globalization;
using VRageMath;

namespace HoloMap
{
    // Deliberately bounded SVG subset. No XML resolver, scripts, external resources or CSS engine.
    public sealed class SvgMesh
    {
        public Geometry Geometry;
        public Vector4[] Colors;
    }
    public static partial class Svg
    {
        public const int MaxCharacters = 65536;
        public static Vector4 ParseColor(string value){return Paint(value);}
        sealed class Style
        {
            public Vector4 Fill = new Vector4(0, 0, 0, 1), Stroke;
            public double Width = 1;
            public float Opacity = 1, FillOpacity = 1, StrokeOpacity = 1;
            public MatrixD Transform = MatrixD.Identity;
            public Style Copy() { return new Style { Fill=Fill, Stroke=Stroke, Width=Width, Opacity=Opacity, FillOpacity=FillOpacity, StrokeOpacity=StrokeOpacity, Transform=Transform }; }
        }
        sealed class Contour
        {
            public readonly List<Vector3D> Points = new List<Vector3D>();
            public bool Closed;
        }
        sealed class Frame { public string Name; public Style Style; }
        sealed class Builder
        {
            public readonly List<Vector3D> Points = new List<Vector3D>();
            public readonly List<int> Triangles = new List<int>();
            public readonly List<Vector4> Colors = new List<Vector4>();
            public void Add(Geometry g, Vector4 color, MatrixD transform)
            {
                if (Points.Count + g.Points.Length > Geometry.MaxPoints || Colors.Count + g.Triangles.Length / 3 > Geometry.MaxPrimitives)
                    throw new ArgumentException("SVG exceeds the object geometry budget; simplify or split it.");
                int offset = Points.Count;
                foreach (var p in g.Points) Points.Add(Vector3D.Transform(p, transform));
                foreach (int i in g.Triangles) Triangles.Add(offset + i);
                for (int i = 0; i < g.Triangles.Length; i += 3) Colors.Add(color);
            }
        }
        public static SvgMesh Parse(string source, int curveSegments = 12)
        { using (GeometryWork.Begin()) return ParseCore(source, curveSegments); }
        static SvgMesh ParseCore(string source, int curveSegments)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > MaxCharacters || curveSegments < 2 || curveSegments > 32)
                throw new ArgumentException("SVG requires 1–65536 characters and 2–32 curve segments.");
            GeometryWork.Charge(source.Length / 8 + 1);
            if(source.IndexOf("<defs",StringComparison.Ordinal)>=0||source.IndexOf("<use",StringComparison.Ordinal)>=0||source.IndexOf("<symbol",StringComparison.Ordinal)>=0||source.IndexOf("mask",StringComparison.Ordinal)>=0||source.IndexOf("filter",StringComparison.Ordinal)>=0||source.IndexOf("<text",StringComparison.Ordinal)>=0||source.IndexOf("<linearGradient",StringComparison.Ordinal)>=0||source.IndexOf("<radialGradient",StringComparison.Ordinal)>=0||source.IndexOf("clip-path",StringComparison.Ordinal)>=0||source.IndexOf("fill-rule",StringComparison.Ordinal)>=0)
                return ParseAdvanced(source,curveSegments);
            var stack = new List<Frame>(); var mesh = new Builder(); bool root = false, finished = false;
            double cx = 0, cy = 0; int pos = 0, tags = 0;
            while (pos < source.Length)
            {
                int open = source.IndexOf('<', pos);
                if (open < 0) { if (source.Substring(pos).Trim().Length != 0) throw new ArgumentException("SVG text is unsupported."); break; }
                if (source.Substring(pos, open - pos).Trim().Length != 0) throw new ArgumentException("SVG text is unsupported; use PutLabel.");
                if (++tags > 256) throw new ArgumentException("SVG element budget exceeded.");
                if (source.IndexOf("<!--", open, StringComparison.Ordinal) == open)
                { int end = source.IndexOf("-->", open + 4, StringComparison.Ordinal); if (end < 0) throw new ArgumentException("Unclosed SVG comment."); pos = end + 3; continue; }
                if (source.IndexOf("<?xml", open, StringComparison.Ordinal) == open && !root)
                { int end = source.IndexOf("?>", open + 5, StringComparison.Ordinal); if (end < 0) throw new ArgumentException("Unclosed XML declaration."); pos = end + 2; continue; }
                int close = TagEnd(source, open + 1); string tag = source.Substring(open + 1, close - open - 1).Trim(); pos = close + 1;
                if (tag.StartsWith("!") || tag.StartsWith("?")) throw new ArgumentException("SVG declarations/DTD/entities are unsupported.");
                if (tag.StartsWith("/"))
                {
                    string name = tag.Substring(1).Trim();
                    if (stack.Count == 0 || stack[stack.Count - 1].Name != name) throw new ArgumentException("Mismatched SVG closing tag.");
                    stack.RemoveAt(stack.Count - 1); if (stack.Count == 0) finished = true; continue;
                }
                if (finished) throw new ArgumentException("SVG requires one root element.");
                bool self = tag.EndsWith("/"); if (self) tag = tag.Substring(0, tag.Length - 1).TrimEnd();
                int n = 0; while (n < tag.Length && !char.IsWhiteSpace(tag[n])) n++;
                string element = tag.Substring(0, n); var attributes = Attributes(tag.Substring(n));
                if (!root && element != "svg") throw new ArgumentException("SVG must start with svg.");
                if (root && element == "svg") throw new ArgumentException("Nested SVG viewports are unsupported.");
                var style = stack.Count == 0 ? new Style() : stack[stack.Count - 1].Style.Copy();
                Apply(style, attributes);
                if (element == "svg")
                {
                    root = true;
                    string box;
                    if (attributes.TryGetValue("viewBox", out box))
                    { var v = Numbers(box); if (v.Count != 4 || v[2] <= 0 || v[3] <= 0) throw new ArgumentException("Invalid SVG viewBox."); cx = v[0] + v[2] / 2; cy = v[1] + v[3] / 2; }
                    else { cx = Attr(attributes, "width", 0) / 2; cy = Attr(attributes, "height", 0) / 2; }
                }
                else if (element != "g")
                {
                    var contours = Shape(element, attributes, curveSegments);
                    if(contours.Count>1&&style.Fill.W>0)
                    {
                        var rings=new Vector3D[contours.Count][];for(int i=0;i<rings.Length;i++)rings[i]=Clean(contours[i]);
                        var fill=style.Fill;fill.W*=style.Opacity*style.FillOpacity;mesh.Add(PlanarFill.Tessellate(rings,false,false),fill,style.Transform);
                        var strokes=style.Copy();strokes.Fill=Vector4.Zero;foreach(var contour in contours)Draw(mesh,contour,strokes);
                    }
                    else foreach (var contour in contours) Draw(mesh, contour, style);
                }
                if (!self) { if (stack.Count >= 16) throw new ArgumentException("SVG nesting exceeds 16."); stack.Add(new Frame { Name = element, Style = style }); }
                else if (element == "svg") finished = true;
            }
            if (!root || stack.Count != 0 || mesh.Points.Count == 0) throw new ArgumentException("SVG is incomplete or contains no visible geometry.");
            // Center on viewBox; SVG down becomes map up, in the local XY plane.
            for (int i = 0; i < mesh.Points.Count; i++) { var p = mesh.Points[i]; mesh.Points[i] = new Vector3D(p.X - cx, cy - p.Y, 0); }
            var points = mesh.Points.ToArray(); Geometry.ValidatePoints(points);
            return new SvgMesh { Geometry = new Geometry(points, new int[0], mesh.Triangles.ToArray()), Colors = mesh.Colors.ToArray() };
        }
        static int TagEnd(string s, int p)
        {
            char quote = '\0';
            for (; p < s.Length; p++) { char c = s[p]; if (quote != '\0') { if (c == quote) quote = '\0'; } else if (c == '\'' || c == '"') quote = c; else if (c == '>') return p; }
            throw new ArgumentException("Unclosed SVG tag.");
        }
        static Dictionary<string, string> Attributes(string s)
        {
            var a = new Dictionary<string, string>(); int p = 0;
            while (p < s.Length)
            {
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++; if (p == s.Length) break;
                int start = p; while (p < s.Length && !char.IsWhiteSpace(s[p]) && s[p] != '=') p++;
                string key = s.Substring(start, p - start); while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                if (key.Length == 0 || p == s.Length || s[p++] != '=') throw new ArgumentException("Malformed SVG attribute.");
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                if (p == s.Length || (s[p] != '\'' && s[p] != '"')) throw new ArgumentException("SVG attributes must be quoted.");
                char q = s[p++]; start = p; while (p < s.Length && s[p] != q) p++;
                if (p == s.Length || a.ContainsKey(key)) throw new ArgumentException("Unclosed/duplicate SVG attribute.");
                string value = s.Substring(start, p++ - start);
                if (value.IndexOf('&') >= 0 || key.StartsWith("on") || ((key == "href" || key == "xlink:href") && (!value.StartsWith("#",StringComparison.Ordinal)||value.Length<2||value.Length>65))) throw new ArgumentException("SVG entities, events and external resource references are unsupported.");
                a.Add(key, value);
            }
            return a;
        }
        static void Apply(Style s, Dictionary<string, string> a)
        {
            var paint = new Dictionary<string, string>(a); string inline;
            if (a.TryGetValue("style", out inline)) foreach (string part in inline.Split(';'))
            { if (part.Trim().Length == 0) continue; int colon = part.IndexOf(':'); if (colon < 0) throw new ArgumentException("Invalid inline SVG style."); paint[part.Substring(0, colon).Trim()] = part.Substring(colon + 1).Trim(); }
            foreach (var pair in paint)
            {
                string k = pair.Key, v = pair.Value.Trim();
                if (k == "fill") s.Fill = Paint(v);
                else if (k == "stroke") s.Stroke = Paint(v);
                else if (k == "stroke-width") { s.Width = Scalar(v); if (s.Width < 0) throw new ArgumentException("Negative SVG stroke width."); }
                else if (k == "opacity") s.Opacity *= Alpha(v);
                else if (k == "fill-opacity") s.FillOpacity = Alpha(v);
                else if (k == "stroke-opacity") s.StrokeOpacity = Alpha(v);
                else if (k == "transform") s.Transform = Transform(v) * s.Transform;
                else if (k == "style" || k == "id" || k == "xmlns" || k == "version" || k == "viewBox" || k == "width" || k == "height"
                    || k == "x" || k == "y" || k == "x1" || k == "y1" || k == "x2" || k == "y2" || k == "cx" || k == "cy" || k == "r"
                    || k == "rx" || k == "ry" || k == "d" || k == "points") { }
                else if (k == "fill-rule" && (v == "nonzero" || v == "evenodd")) { }
                else if (k == "stroke-linecap" && v == "butt") { }
                else if (k == "stroke-linejoin" && v == "bevel") { }
                else throw new ArgumentException("Unsupported SVG attribute/style: " + k);
            }
        }
        static float Alpha(string s) { double n = Scalar(s); if (n < 0 || n > 1) throw new ArgumentException("SVG opacity must be in [0,1]."); return (float)n; }
        static Vector4 Paint(string s)
        {
            if (s == "none") return Vector4.Zero;
            if(s=="transparent")return Vector4.Zero;if(s=="orange")return new Vector4(1,165/255f,0,1);if(s=="gray"||s=="grey")return new Vector4(128/255f,128/255f,128/255f,1);
            if (s == "black") return new Vector4(0,0,0,1); if (s == "white") return Vector4.One;
            if (s == "red") return new Vector4(1,0,0,1); if (s == "green") return new Vector4(0,0.502f,0,1);
            if (s == "blue") return new Vector4(0,0,1,1); if (s == "cyan") return new Vector4(0,1,1,1);
            if (s == "yellow") return new Vector4(1,1,0,1); if (s == "magenta") return new Vector4(1,0,1,1);
            if (s.StartsWith("#") && (s.Length == 4 || s.Length == 7))
            { int n; if (!int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("Invalid SVG color.");
                return s.Length == 4 ? new Vector4(((n >> 8) & 15)/15f, ((n >> 4)&15)/15f, (n&15)/15f, 1) : new Vector4(((n>>16)&255)/255f, ((n>>8)&255)/255f, (n&255)/255f, 1); }
            if(s.StartsWith("#")&&(s.Length==5||s.Length==9)){uint n;if(!uint.TryParse(s.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out n))throw new ArgumentException("Invalid SVG color.");return s.Length==5?new Vector4(((n>>12)&15)/15f,((n>>8)&15)/15f,((n>>4)&15)/15f,(n&15)/15f):new Vector4(((n>>24)&255)/255f,((n>>16)&255)/255f,((n>>8)&255)/255f,(n&255)/255f);}
            if (s.StartsWith("rgb(") && s.EndsWith(")"))
            { var n = Numbers(s.Substring(4, s.Length - 5)); if (n.Count == 3 && n[0]>=0 && n[0]<=255 && n[1]>=0 && n[1]<=255 && n[2]>=0 && n[2]<=255) return new Vector4((float)n[0]/255,(float)n[1]/255,(float)n[2]/255,1); }
            if(s.StartsWith("rgba(")&&s.EndsWith(")")){var n=Numbers(s.Substring(5,s.Length-6));if(n.Count==4&&n[0]>=0&&n[0]<=255&&n[1]>=0&&n[1]<=255&&n[2]>=0&&n[2]<=255&&n[3]>=0&&n[3]<=1)return new Vector4((float)n[0]/255,(float)n[1]/255,(float)n[2]/255,(float)n[3]);}
            throw new ArgumentException("SVG paint supports none, basic names, #RGB[A], #RRGGBB[AA], rgb and rgba.");
        }
        sealed class Reader
        {
            public string S; public int P;
            public Reader(string s) { S = s; }
            public void Skip() { while (P < S.Length && (char.IsWhiteSpace(S[P]) || S[P] == ',')) P++; }
            public bool End { get { Skip(); return P == S.Length; } }
            public double Number()
            {
                Skip(); int start = P; if (P < S.Length && (S[P]=='+' || S[P]=='-')) P++;
                bool digits = false; while (P<S.Length && S[P]>='0' && S[P]<='9') { digits=true; P++; }
                if (P<S.Length && S[P]=='.') { P++; while (P<S.Length && S[P]>='0' && S[P]<='9') { digits=true; P++; } }
                if (P<S.Length && (S[P]=='e'||S[P]=='E')) { P++; if(P<S.Length && (S[P]=='+'||S[P]=='-'))P++; int exponent=P; while(P<S.Length && S[P]>='0'&&S[P]<='9')P++; if(P==exponent)throw new ArgumentException("Invalid SVG exponent."); }
                double n; if (!digits || !double.TryParse(S.Substring(start,P-start),NumberStyles.Float,CultureInfo.InvariantCulture,out n) || !Geometry.Finite(n) || Math.Abs(n)>1000000) throw new ArgumentException("Invalid/out-of-range SVG number.");
                return n;
            }
            public Vector3D Point() { double x=Number(); return new Vector3D(x,Number(),0); }
        }
        static List<double> Numbers(string s) { var r=new Reader(s); var n=new List<double>(); while(!r.End) { n.Add(r.Number()); if(n.Count>4096)throw new ArgumentException("SVG number budget exceeded."); } return n; }
        static double Scalar(string s) { var n=Numbers(s); if(n.Count!=1)throw new ArgumentException("SVG lengths use numeric user units, without units or percentages."); return n[0]; }
        static double Attr(Dictionary<string,string> a,string name,double fallback=0) { string s; return a.TryGetValue(name,out s)?Scalar(s):fallback; }
        static string Required(Dictionary<string,string> a,string name) { string s; if(!a.TryGetValue(name,out s))throw new ArgumentException("SVG requires " + name); return s; }
        public static MatrixD Transform(string s)
        {
            var result=MatrixD.Identity; int p=0;
            while(p<s.Length)
            {
                while(p<s.Length && (char.IsWhiteSpace(s[p])||s[p]==','))p++; if(p==s.Length)break;
                int start=p; while(p<s.Length && char.IsLetter(s[p]))p++; string name=s.Substring(start,p-start);
                while(p<s.Length && char.IsWhiteSpace(s[p]))p++; if(p==s.Length||s[p++]!='(')throw new ArgumentException("Invalid SVG transform.");
                start=p; while(p<s.Length && s[p]!=')')p++; if(p==s.Length)throw new ArgumentException("Unclosed SVG transform.");
                var n=Numbers(s.Substring(start,p++-start)); var m=MatrixD.Identity;
                if(name=="translate" && (n.Count==1||n.Count==2))m=MatrixD.CreateTranslation(n[0],n.Count==2?n[1]:0,0);
                else if(name=="scale" && (n.Count==1||n.Count==2))m=MatrixD.CreateScale(n[0],n.Count==2?n[1]:n[0],1);
                else if(name=="rotate" && (n.Count==1||n.Count==3)) { m=MatrixD.CreateRotationZ(n[0]*Math.PI/180); if(n.Count==3)m=MatrixD.CreateTranslation(-n[1],-n[2],0)*m*MatrixD.CreateTranslation(n[1],n[2],0); }
                else if(name=="matrix" && n.Count==6) { m.M11=n[0];m.M12=n[1];m.M21=n[2];m.M22=n[3];m.M41=n[4];m.M42=n[5]; }
                else if((name=="skewX"||name=="skewY")&&n.Count==1) { double t=Math.Tan(n[0]*Math.PI/180); if(!Geometry.Finite(t)||Math.Abs(t)>1000000)throw new ArgumentException("Invalid SVG skew."); if(name=="skewX")m.M21=t;else m.M12=t; }
                else throw new ArgumentException("Unsupported/invalid SVG transform: "+name);
                result=m*result; // SVG lists postmultiply column matrices; VRage uses row vectors.
            }
            return result;
        }
        static List<Contour> Shape(string name,Dictionary<string,string> a,int segments)
        {
            if(name=="path")return Path(Required(a,"d"),segments);
            var c=new Contour(); var list=new List<Contour>{c};
            if(name=="rect")
            {
                double x=Attr(a,"x"),y=Attr(a,"y"),w=Attr(a,"width"),h=Attr(a,"height");if(w<=0||h<=0)throw new ArgumentException("SVG rectangle dimensions must be positive.");
                double rx=Attr(a,"rx",Attr(a,"ry")),ry=Attr(a,"ry",rx);if(rx<0||ry<0)throw new ArgumentException("SVG rounded rectangle radii cannot be negative.");rx=Math.Min(rx,w/2);ry=Math.Min(ry,h/2);
                if(rx==0||ry==0){c.Points.Add(new Vector3D(x,y,0));c.Points.Add(new Vector3D(x+w,y,0));c.Points.Add(new Vector3D(x+w,y+h,0));c.Points.Add(new Vector3D(x,y+h,0));}
                else for(int corner=0;corner<4;corner++)for(int i=0;i<=Math.Min(segments,31);i++){double t=(-Math.PI/2+corner*Math.PI/2)+i*Math.PI/(2*Math.Min(segments,31));double cx=corner<2?x+w-rx:x+rx,cy=corner==0||corner==3?y+ry:y+h-ry;AddPoint(c,new Vector3D(cx+rx*Math.Cos(t),cy+ry*Math.Sin(t),0));}
                c.Closed=true;
            }
            else if(name=="circle"||name=="ellipse")
            {
                double x=Attr(a,"cx"),y=Attr(a,"cy"),rx=Attr(a,name=="circle"?"r":"rx"),ry=name=="circle"?rx:Attr(a,"ry");if(rx<=0||ry<=0)throw new ArgumentException("SVG radii must be positive.");
                int count=segments*4;for(int i=0;i<count;i++){double t=2*Math.PI*i/count;c.Points.Add(new Vector3D(x+rx*Math.Cos(t),y+ry*Math.Sin(t),0));}c.Closed=true;
            }
            else if(name=="line") { c.Points.Add(new Vector3D(Attr(a,"x1"),Attr(a,"y1"),0));c.Points.Add(new Vector3D(Attr(a,"x2"),Attr(a,"y2"),0)); }
            else if(name=="polygon"||name=="polyline")
            { var r=new Reader(Required(a,"points"));while(!r.End){AddPoint(c,r.Point());}c.Closed=name=="polygon"; }
            else throw new ArgumentException("Unsupported SVG element: "+name);
            return list;
        }
        static void AddPoint(Contour c,Vector3D p)
        { GeometryWork.Charge();if(c.Points.Count>=128)throw new ArgumentException("SVG contour exceeds 128 sampled vertices; reduce curveSegments or split paths."); if(c.Points.Count==0||(p-c.Points[c.Points.Count-1]).LengthSquared()>1e-20)c.Points.Add(p); }
        static List<Contour> Path(string data,int segments)
        {
            var r=new Reader(data);var list=new List<Contour>();Contour c=null;Vector3D p=Vector3D.Zero,control=p;char cmd='\0',previous='\0';int commands=0;
            while(!r.End)
            {
                GeometryWork.Charge();
                if(++commands>1024)throw new ArgumentException("SVG path command budget exceeded.");
                if(char.IsLetter(r.S[r.P]))cmd=r.S[r.P++];else if(cmd=='\0')throw new ArgumentException("SVG path requires a command.");
                bool relative=char.IsLower(cmd);char op=char.ToUpperInvariant(cmd);var origin=p;
                if(op=='M') { p=r.Point()+(relative?p:Vector3D.Zero);c=new Contour();list.Add(c);AddPoint(c,p);cmd=relative?'l':'L'; }
                else
                {
                    if(c==null)throw new ArgumentException("SVG path must begin with M.");
                    if(c.Closed && op!='Z') { c=new Contour();list.Add(c);AddPoint(c,p); }
                    if(op=='Z') { c.Closed=true;p=c.Points[0];cmd='\0'; }
                    else if(op=='L') {p=r.Point()+(relative?p:Vector3D.Zero);AddPoint(c,p);}
                    else if(op=='H') {p.X=r.Number()+(relative?p.X:0);AddPoint(c,p);}
                    else if(op=='V') {p.Y=r.Number()+(relative?p.Y:0);AddPoint(c,p);}
                    else if(op=='C'||op=='S'||op=='Q'||op=='T')
                    {
                        bool cubic=op=='C'||op=='S';var offset=relative?origin:Vector3D.Zero;
                        var a=(op=='S'||op=='T')?(((op=='S'&&(previous=='C'||previous=='S'))||(op=='T'&&(previous=='Q'||previous=='T')))?2*origin-control:origin):r.Point()+offset;
                        var b=cubic?r.Point()+offset:a;var end=r.Point()+offset;
                        for(int i=1;i<=segments;i++){double t=(double)i/segments,u=1-t;AddPoint(c,cubic?u*u*u*origin+3*u*u*t*a+3*u*t*t*b+t*t*t*end:u*u*origin+2*u*t*a+t*t*end);}
                        control=cubic?b:a;p=end;
                    }
                    else if(op=='A')
                    {
                        double rx=Math.Abs(r.Number()),ry=Math.Abs(r.Number()),angle=r.Number()*Math.PI/180,large=r.Number(),sweep=r.Number();
                        if((large!=0&&large!=1)||(sweep!=0&&sweep!=1))throw new ArgumentException("SVG arc flags must be 0 or 1.");
                        var end=r.Point()+(relative?origin:Vector3D.Zero);Arc(c,origin,end,rx,ry,angle,large==1,sweep==1,segments);p=end;
                    }
                    else throw new ArgumentException("Unsupported SVG path command: "+cmd);
                }
                previous=op;
            }
            return list;
        }
        static void Arc(Contour c,Vector3D start,Vector3D end,double rx,double ry,double angle,bool large,bool sweep,int segments)
        {
            if((end-start).LengthSquared()<1e-20)return;if(rx==0||ry==0){AddPoint(c,end);return;}
            double co=Math.Cos(angle),si=Math.Sin(angle),dx=(start.X-end.X)/2,dy=(start.Y-end.Y)/2,x=co*dx+si*dy,y=-si*dx+co*dy;
            double ratio=x*x/(rx*rx)+y*y/(ry*ry);if(ratio>1){double scale=Math.Sqrt(ratio);rx*=scale;ry*=scale;}
            double numerator=rx*rx*ry*ry-rx*rx*y*y-ry*ry*x*x,denominator=rx*rx*y*y+ry*ry*x*x;
            double f=(large==sweep?-1:1)*Math.Sqrt(Math.Max(0,numerator/denominator)),cx=f*rx*y/ry,cy=-f*ry*x/rx;
            double wx=co*cx-si*cy+(start.X+end.X)/2,wy=si*cx+co*cy+(start.Y+end.Y)/2;
            double a=Math.Atan2((y-cy)/ry,(x-cx)/rx),b=Math.Atan2((-y-cy)/ry,(-x-cx)/rx),delta=b-a;
            if(sweep&&delta<0)delta+=2*Math.PI;if(!sweep&&delta>0)delta-=2*Math.PI;
            int count=Math.Max(1,(int)Math.Ceiling(Math.Abs(delta)/(Math.PI/2)*segments));
            for(int i=1;i<=count;i++){double t=a+delta*i/count;AddPoint(c,i==count?end:new Vector3D(wx+co*rx*Math.Cos(t)-si*ry*Math.Sin(t),wy+si*rx*Math.Cos(t)+co*ry*Math.Sin(t),0));}
        }
        static void Draw(Builder b,Contour c,Style s)
        {
            var points=c.Points;
            if(points.Count>1&&(points[0]-points[points.Count-1]).LengthSquared()<1e-20){points.RemoveAt(points.Count-1);c.Closed=true;}
            var fill=s.Fill;fill.W*=s.Opacity*s.FillOpacity;
            if(fill.W>0&&points.Count>=3)
            { var face=new int[points.Count];for(int i=0;i<face.Length;i++)face[i]=i;b.Add(Geometry.Polygons(points.ToArray(),new[]{face}),fill,s.Transform); }
            var stroke=s.Stroke;stroke.W*=s.Opacity*s.StrokeOpacity;
            if(stroke.W<=0||s.Width==0||points.Count<2)return;
            int count=c.Closed?points.Count:points.Count-1;
            for(int i=0;i<count;i++)
            {
                var a=points[i];var z=points[(i+1)%points.Count];var delta=z-a;if(delta.LengthSquared()<1e-20)continue;
                var perpendicular=Vector3D.Normalize(new Vector3D(-delta.Y,delta.X,0))*s.Width/2;
                b.Add(new Geometry(new[]{a+perpendicular,a-perpendicular,z-perpendicular,z+perpendicular},new int[0],new[]{0,1,2,0,2,3}),stroke,s.Transform);
                // Bevel joins bridge segment strips; endpoints use butt caps.
                if(c.Closed||i<count-1)
                { var next=points[(i+2)%points.Count]-z;if(next.LengthSquared()>1e-20){var q=Vector3D.Normalize(new Vector3D(-next.Y,next.X,0))*s.Width/2;
                    b.Add(new Geometry(new[]{z,z+perpendicular,z+q,z-perpendicular,z-q},new int[0],new[]{0,1,2,0,3,4}),stroke,s.Transform); } }
            }
        }
    }
}
