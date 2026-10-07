using System;
using System.Collections.Generic;
using VRageMath;

namespace HoloMap
{
    public static partial class Svg
    {
        sealed class Node { public string Name,Text="";public Dictionary<string,string> A;public List<Node> Children=new List<Node>();public List<object> Content=new List<object>(); }
        sealed class PaintStyle
        {
            public Style Style=new Style();public string FillGradient,StrokeGradient,Clip,Mask,Filter,Anchor="start";
            public bool EvenOdd;public double FontSize=16;
            public int GradientResolution=3;
            public Vector4 Viewport=new Vector4(0,0,1,1);
            public MatrixD MaskTransform=MatrixD.Identity;
            public PaintStyle Copy(){return new PaintStyle{Style=Style.Copy(),FillGradient=FillGradient,StrokeGradient=StrokeGradient,Mask=Mask,MaskTransform=MaskTransform,Filter=Filter,Anchor=Anchor,EvenOdd=EvenOdd,FontSize=FontSize,GradientResolution=GradientResolution,Viewport=Viewport};}
        }
        sealed class GradientPaint
        {
            public Func<Vector3D,double> Coordinate;
            public Func<Vector3D,Vector3D> Metric;
            public Func<double,Vector4> Palette;
            public bool Radial,Repeat;
        }
        static Vector3D[] Clean(Contour c)
        {var p=new List<Vector3D>(c.Points);if(p.Count>1&&(p[0]-p[p.Count-1]).LengthSquared()<1e-20)p.RemoveAt(p.Count-1);return p.ToArray();}
        static Node Tree(string source)
        {
            var stack=new List<Node>();Node root=null;int p=0,tags=0;
            while(p<source.Length)
            {
                int open=source.IndexOf('<',p);if(open<0)open=source.Length;
                string text=source.Substring(p,open-p);
                bool inText=stack.Count>0&&(stack[stack.Count-1].Name=="text"||stack[stack.Count-1].Name=="tspan");
                if(text.Length>0&&inText){var parent=stack[stack.Count-1];string decoded=CollapseTextWhitespace(TextEntities(text));parent.Text+=decoded;parent.Content.Add(decoded);}
                else if(text.Trim().Length>0)throw new ArgumentException("Text content is only supported inside text/tspan.");
                if(open==source.Length)break;
                if(source.IndexOf("<!--",open,StringComparison.Ordinal)==open){int end=source.IndexOf("-->",open+4,StringComparison.Ordinal);if(end<0)throw new ArgumentException("Unclosed SVG comment.");p=end+3;continue;}
                if(source.IndexOf("<?xml",open,StringComparison.Ordinal)==open&&root==null){int end=source.IndexOf("?>",open+5,StringComparison.Ordinal);if(end<0)throw new ArgumentException("Invalid XML declaration.");p=end+2;continue;}
                int close=TagEnd(source,open+1);string tag=source.Substring(open+1,close-open-1).Trim();p=close+1;
                if(++tags>256)throw new ArgumentException("SVG element budget exceeded.");
                if(tag.StartsWith("!")||tag.StartsWith("?"))throw new ArgumentException("SVG DTD/declarations are unsupported.");
                if(tag.StartsWith("/")){if(stack.Count==0||stack[stack.Count-1].Name!=tag.Substring(1).Trim())throw new ArgumentException("Mismatched SVG closing tag.");stack.RemoveAt(stack.Count-1);continue;}
                bool self=tag.EndsWith("/");if(self)tag=tag.Substring(0,tag.Length-1).TrimEnd();int n=0;while(n<tag.Length&&!char.IsWhiteSpace(tag[n]))n++;
                var node=new Node{Name=tag.Substring(0,n),A=Attributes(tag.Substring(n))};
                if(stack.Count==0){if(root!=null||node.Name!="svg")throw new ArgumentException("SVG requires one svg root.");root=node;}
                else{stack[stack.Count-1].Children.Add(node);stack[stack.Count-1].Content.Add(node);}
                if(!self){if(stack.Count>=16)throw new ArgumentException("SVG nesting exceeds 16.");stack.Add(node);}
            }
            if(root==null||stack.Count!=0)throw new ArgumentException("Incomplete SVG.");return root;
        }
        static string TextEntities(string text)
        {
            var result=new System.Text.StringBuilder();for(int i=0;i<text.Length;i++){
                if(text[i]!='&'){result.Append(text[i]);continue;}int end=text.IndexOf(';',i+1);if(end<0||end-i>16)throw new ArgumentException("Unsupported text entity.");string entity=text.Substring(i+1,end-i-1);i=end;
                if(entity=="amp")result.Append('&');else if(entity=="lt")result.Append('<');else if(entity=="gt")result.Append('>');else if(entity=="quot")result.Append('"');else if(entity=="apos")result.Append('\'');
                else if(entity.StartsWith("#",StringComparison.Ordinal)){bool hex=entity.StartsWith("#x",StringComparison.Ordinal);int value;if(!int.TryParse(entity.Substring(hex?2:1),hex?System.Globalization.NumberStyles.HexNumber:System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out value)||value<1||value>0x10ffff||(value>=0xd800&&value<=0xdfff)||(value<32&&value!=9&&value!=10&&value!=13))throw new ArgumentException("Invalid numeric text entity.");result.Append(char.ConvertFromUtf32(value));}
                else throw new ArgumentException("External and named custom entities are unsupported.");
            }return result.ToString();
        }
        static string CollapseTextWhitespace(string text)
        {var result=new System.Text.StringBuilder();bool space=false;foreach(char c in text){if(char.IsWhiteSpace(c)){if(!space)result.Append(' ');space=true;}else{result.Append(c);space=false;}}return result.ToString();}
        static void Definitions(Node n,Dictionary<string,Node> ids)
        {
            string id;if(n.A.TryGetValue("id",out id)){if(string.IsNullOrWhiteSpace(id)||id.Length>64||ids.ContainsKey(id))throw new ArgumentException("Duplicate/invalid SVG definition ID.");ids.Add(id,n);}
            foreach(var child in n.Children)Definitions(child,ids);
        }
        static Dictionary<string,string> Inline(Dictionary<string,string> a)
        {
            var result=new Dictionary<string,string>(a);string style;if(a.TryGetValue("style",out style))foreach(string part in style.Split(';'))
            {if(part.Trim().Length==0)continue;int colon=part.IndexOf(':');if(colon<0)throw new ArgumentException("Invalid SVG style.");result[part.Substring(0,colon).Trim()]=part.Substring(colon+1).Trim();}return result;
        }
        static string Ref(string value)
        {value=value.Trim();if(!value.StartsWith("url(#",StringComparison.Ordinal)||!value.EndsWith(")")||value.Length>72)throw new ArgumentException("SVG references must use local url(#id).");return value.Substring(5,value.Length-6);}
        static PaintStyle Styled(Node n,PaintStyle parent)
        {
            var s=parent.Copy();var attrs=Inline(n.A);attrs.Remove("style");
            var ordinary=new Dictionary<string,string>();
            foreach(var pair in attrs)
            {
                string k=pair.Key,v=pair.Value.Trim();
                if(k=="fill"&&v.StartsWith("url(")){s.FillGradient=Ref(v);s.Style.Fill=Vector4.One;}
                else if(k=="stroke"&&v.StartsWith("url(")){s.StrokeGradient=Ref(v);s.Style.Stroke=Vector4.One;}
                else if(k=="fill-rule"||k=="clip-rule"){if(v!="evenodd"&&v!="nonzero")throw new ArgumentException("Invalid SVG fill rule.");s.EvenOdd=v=="evenodd";}
                else if(k=="clip-path")s.Clip=v=="none"?null:Ref(v);
                else if(k=="font-size"){s.FontSize=Scalar(v);if(s.FontSize<=0)throw new ArgumentException("Font size must be positive.");}
                else if(k=="text-anchor"){if(v!="start"&&v!="middle"&&v!="end")throw new ArgumentException("Invalid text anchor.");s.Anchor=v;}
                else if(k=="font-family"){} // local built-in font; no external font loading
                else if(k=="mask")s.Mask=v=="none"?null:Ref(v);
                else if(k=="filter")s.Filter=v=="none"?null:Ref(v);
                else if(k=="dx"||k=="dy"){} // handled by bounded text layout
                else if(k=="href"||k=="xlink:href"||k=="xmlns:xlink"||k=="preserveAspectRatio"||k=="maskUnits"||k=="maskContentUnits"||k=="mask-type"||k=="filterUnits"||k=="color-interpolation-filters"){}
                else if(k=="data-gradient-resolution"){double r=Scalar(v);if(r<1||r>8||r!=(int)r)throw new ArgumentException("SVG gradient resolution must be an integer 1–8.");s.GradientResolution=(int)r;}
                else {if(k=="fill")s.FillGradient=null;if(k=="stroke")s.StrokeGradient=null;ordinary.Add(k,v);}
            }
            Apply(s.Style,ordinary);if(attrs.ContainsKey("mask"))s.MaskTransform=s.Style.Transform;
            if(s.Filter!=null&&(n.Name=="g"||n.Name=="svg"||n.Name=="symbol"||n.Name=="use"))throw new ArgumentException("Color filters apply to individual shapes/text only; group compositing filters are unsupported.");return s;
        }
        static SvgMesh ParseAdvanced(string source,int segments)
        {
            var root=Tree(source);var ids=new Dictionary<string,Node>();Definitions(root,ids);var output=new Builder();
            var style=Styled(root,new PaintStyle());double cx=0,cy=0;string box;
            if(root.A.TryGetValue("viewBox",out box)){var values=Numbers(box);if(values.Count!=4||values[2]<=0||values[3]<=0)throw new ArgumentException("Invalid SVG viewBox.");cx=values[0]+values[2]/2;cy=values[1]+values[3]/2;style.Viewport=new Vector4((float)values[0],(float)values[1],(float)values[2],(float)values[3]);}
            else{cx=Attr(root.A,"width")/2;cy=Attr(root.A,"height")/2;style.Viewport=new Vector4(0,0,(float)Math.Max(1,cx*2),(float)Math.Max(1,cy*2));}
            var rootClips=new List<Geometry>();
            if(style.Clip!=null){Node clip;if(!ids.TryGetValue(style.Clip,out clip)||clip.Name!="clipPath")throw new ArgumentException("Missing root SVG clipPath.");rootClips.Add(ClipDefinition(clip,style.Style.Transform,ids,segments));}
            var traversal=new Traversal();foreach(var child in root.Children)Render(child,style,rootClips,ids,segments,output,traversal,0);
            if(output.Points.Count==0)output.Points.Add(Vector3D.Zero);
            for(int i=0;i<output.Points.Count;i++){var p=output.Points[i];output.Points[i]=new Vector3D(p.X-cx,cy-p.Y,0);}
            var points=output.Points.ToArray();Geometry.ValidatePoints(points);return new SvgMesh{Geometry=new Geometry(points,new int[0],output.Triangles.ToArray()),Colors=output.Colors.ToArray()};
        }
        sealed class Traversal {public int Count;public HashSet<Node> Active=new HashSet<Node>();}
        static void Render(Node n,PaintStyle parent,List<Geometry> inherited,Dictionary<string,Node> ids,int segments,Builder output,Traversal traversal,int depth)
        {
            GeometryWork.Charge();if(++traversal.Count>512||depth>16)throw new ArgumentException("SVG expansion exceeds 512 elements or 16 reference levels.");
            if(n.Name=="defs"||n.Name=="linearGradient"||n.Name=="radialGradient"||n.Name=="clipPath"||n.Name=="mask"||n.Name=="filter"||n.Name=="symbol")return;
            var style=Styled(n,parent);var clips=new List<Geometry>(inherited);
            if(style.Clip!=null)
            {Node def;if(!ids.TryGetValue(style.Clip,out def)||def.Name!="clipPath")throw new ArgumentException("Missing SVG clipPath.");clips.Add(ClipDefinition(def,style.Style.Transform,ids,segments));}
            if(n.Name=="use")
            {
                string href;if(!n.A.TryGetValue("href",out href)&&!n.A.TryGetValue("xlink:href",out href))throw new ArgumentException("SVG use requires a local href.");
                Node target;if(!href.StartsWith("#",StringComparison.Ordinal)||!ids.TryGetValue(href.Substring(1),out target))throw new ArgumentException("Missing local SVG use definition.");
                if(!traversal.Active.Add(target))throw new ArgumentException("Cyclic SVG use reference.");
                try{
                    var placement=MatrixD.CreateTranslation(Attr(n.A,"x"),Attr(n.A,"y"),0);
                    if(target.Name=="symbol"){
                        string view;if(target.A.TryGetValue("viewBox",out view)){var v=Numbers(view);if(v.Count!=4||v[2]<=0||v[3]<=0)throw new ArgumentException("Invalid symbol viewBox.");double w=Attr(n.A,"width",v[2]),h=Attr(n.A,"height",v[3]);if(w<=0||h<=0)throw new ArgumentException("SVG use size must be positive.");double scale=Math.Min(w/v[2],h/v[3]);string aspect;if(!n.A.TryGetValue("preserveAspectRatio",out aspect))target.A.TryGetValue("preserveAspectRatio",out aspect);if(aspect!=null&&aspect!="none"&&aspect!="xMidYMid meet")throw new ArgumentException("Symbol aspect ratio supports none or xMidYMid meet.");double sx=aspect=="none"?w/v[2]:scale,sy=aspect=="none"?h/v[3]:scale;placement=MatrixD.CreateTranslation(-v[0],-v[1],0)*MatrixD.CreateScale(sx,sy,1)*MatrixD.CreateTranslation(aspect=="none"?0:(w-v[2]*scale)/2,aspect=="none"?0:(h-v[3]*scale)/2,0)*placement;}
                        style.Style.Transform=placement*style.Style.Transform;var symbolStyle=Styled(target,style);foreach(var child in target.Children)Render(child,symbolStyle,clips,ids,segments,output,traversal,depth+1);
                    }else{style.Style.Transform=placement*style.Style.Transform;Render(target,style,clips,ids,segments,output,traversal,depth+1);}
                }finally{traversal.Active.Remove(target);}return;
            }
            if(n.Name=="g"){foreach(var child in n.Children)Render(child,style,clips,ids,segments,output,traversal,depth+1);return;}
            var local=new List<Vector3D>();
            if(n.Name=="text")
            {
                RenderText(n,style,clips,ids,segments,output);return;
            }
            if(n.Children.Count!=0)throw new ArgumentException("SVG shapes cannot contain child elements.");
            var contours=Shape(n.Name,n.A,segments);foreach(var c in contours)local.AddRange(c.Points);
            if(style.Style.Fill.W>0)
            {
                var rings=new List<Vector3D[]>();foreach(var c in contours){var p=Clean(c);if(p.Length>=3)rings.Add(p);}
                if(rings.Count>0)
                {
                    Geometry fill;
                    if(rings.Count==1){var face=new int[rings[0].Length];for(int i=0;i<face.Length;i++)face[i]=i;fill=Geometry.Polygons(rings[0],new[]{face});}
                    else fill=PlanarFill.Tessellate(rings.ToArray(),style.EvenOdd,false);
                    AddPaint(fill,style.Style.Fill,style.FillGradient,style.Style.Opacity*style.Style.FillOpacity,style,local,clips,ids,output,segments);
                }
            }
            if(style.Style.Stroke.W>0)
            {
                var strokes=new Builder();var strokeStyle=style.Style.Copy();strokeStyle.Fill=Vector4.Zero;strokeStyle.Stroke=Vector4.One;strokeStyle.Opacity=strokeStyle.StrokeOpacity=1;strokeStyle.Transform=MatrixD.Identity;
                foreach(var c in contours)Draw(strokes,c,strokeStyle);
                var geometry=new Geometry(strokes.Points.ToArray(),new int[0],strokes.Triangles.ToArray());
                AddPaint(geometry,style.Style.Stroke,style.StrokeGradient,style.Style.Opacity*style.Style.StrokeOpacity,style,local,clips,ids,output,segments);
            }
        }
        sealed class TextRun {public string Text;public PaintStyle Style;public double X,Y,Width;public int Chunk;}
        static void RenderText(Node root,PaintStyle style,List<Geometry> clips,Dictionary<string,Node> ids,int segments,Builder output)
        {
            var runs=new List<TextRun>();double x=Attr(root.A,"x")+Attr(root.A,"dx"),y=Attr(root.A,"y")+Attr(root.A,"dy");int chunk=0,characters=0;
            TextRuns(root,style,runs,ref x,ref y,ref chunk,ref characters,0);
            for(int begin=0;begin<runs.Count;){int end=begin+1;while(end<runs.Count&&runs[end].Chunk==runs[begin].Chunk)end++;double width=runs[end-1].X+runs[end-1].Width-runs[begin].X;string anchor=runs[begin].Style.Anchor;double shift=anchor=="middle"?-width/2:anchor=="end"?-width:0;
                for(int i=begin;i<end;i++){var run=runs[i];var s=run.Style;if(s.Style.Stroke.W>0)throw new ArgumentException("SVG text supports fill only; export stroked glyphs as paths.");var geometry=MeshEffects.Text(run.Text,s.FontSize,"start");var points=new Vector3D[geometry.Points.Length];for(int p=0;p<points.Length;p++){var v=geometry.Points[p];points[p]=new Vector3D(run.X+shift+v.X,run.Y-v.Y-s.FontSize/2,0);}var g=new Geometry(points,new int[0],geometry.Triangles);AddPaint(g,s.Style.Fill,s.FillGradient,s.Style.Opacity*s.Style.FillOpacity,s,new List<Vector3D>(points),clips,ids,output,segments);}
                begin=end;
            }
        }
        static void TextRuns(Node n,PaintStyle style,List<TextRun> runs,ref double x,ref double y,ref int chunk,ref int characters,int depth)
        {
            GeometryWork.Charge();if(depth>16)throw new ArgumentException("SVG text nesting exceeds 16.");
            foreach(var content in n.Content){var child=content as Node;if(child!=null){if(child.Name!="tspan")throw new ArgumentException("Text children must be tspan.");var s=Styled(child,style);if(child.A.ContainsKey("x")||child.A.ContainsKey("y")){chunk++;if(child.A.ContainsKey("x"))x=Attr(child.A,"x");if(child.A.ContainsKey("y"))y=Attr(child.A,"y");}x+=Attr(child.A,"dx");y+=Attr(child.A,"dy");if(s.Clip!=null)throw new ArgumentException("tspan clip-path is unsupported; apply clipping to text.");TextRuns(child,s,runs,ref x,ref y,ref chunk,ref characters,depth+1);}
                else{string text=(string)content;characters+=text.Length;if(characters>64)throw new ArgumentException("SVG text/tspan content requires at most 64 characters.");double width=VectorFont.Width(text)*style.FontSize;runs.Add(new TextRun{Text=text,Style=style,X=x,Y=y,Width=width,Chunk=chunk});x+=width;}}
        }
        static void AddPaint(Geometry g,Vector4 paint,string gradient,float opacity,PaintStyle style,List<Vector3D> bounds,List<Geometry> clips,Dictionary<string,Node> ids,Builder output,int segments)
        {
            paint.W*=style.Filter==null?opacity:(style.Style.Opacity>0?opacity/style.Style.Opacity:0);var mesh=MeshEffects.Solid(g,paint,Vector4.Zero);
            if(gradient!=null)
            {
                Node def;if(!ids.TryGetValue(gradient,out def)||(def.Name!="linearGradient"&&def.Name!="radialGradient"))throw new ArgumentException("Missing SVG gradient.");
                var paintServer=Gradient(def,bounds,style.Viewport);
                mesh=paintServer.Radial?MeshEffects.RadialGradient(mesh,paintServer.Metric,paintServer.Palette,style.GradientResolution,paintServer.Repeat)
                    :MeshEffects.LinearGradient(mesh,paintServer.Coordinate,paintServer.Palette,style.GradientResolution*8,paintServer.Repeat);
            }
            var points=new Vector3D[mesh.Geometry.Points.Length];for(int i=0;i<points.Length;i++)points[i]=Vector3D.Transform(mesh.Geometry.Points[i],style.Style.Transform);
            mesh.Geometry=new Geometry(points,new int[0],mesh.Geometry.Triangles);
            foreach(var clip in clips)mesh=ClipMask(mesh,clip);
            if(style.Mask!=null)mesh=ApplyVectorMask(mesh,style.Mask,style.MaskTransform,ids,segments);
            if(style.Filter!=null){mesh=ApplyColorFilter(mesh,style.Filter,ids);for(int i=0;i<mesh.Colors.Length;i++)mesh.Colors[i].W*=style.Style.Opacity;}
            // Preserve per-triangle paints in the retained combined SVG object.
            if(output.Points.Count+mesh.Geometry.Points.Length>Geometry.MaxPoints||output.Colors.Count+mesh.Colors.Length>Geometry.MaxPrimitives)throw new ArgumentException("SVG style/clip geometry budget exceeded.");
            int offset=output.Points.Count;output.Points.AddRange(mesh.Geometry.Points);foreach(int i in mesh.Geometry.Triangles)output.Triangles.Add(offset+i);output.Colors.AddRange(mesh.Colors);
        }
        static Geometry ClipDefinition(Node def,MatrixD world,Dictionary<string,Node> ids,int segments)
        {
            string units;if(def.A.TryGetValue("clipPathUnits",out units)&&units!="userSpaceOnUse")throw new ArgumentException("clipPath supports userSpaceOnUse units.");
            var b=new MeshEffects.Builder();MatrixD matrix=world;string transform;if(def.A.TryGetValue("transform",out transform))matrix=Transform(transform)*matrix;
            string rule;bool evenOdd=def.A.TryGetValue("clip-rule",out rule)&&rule=="evenodd";
            foreach(var child in def.Children)ClipShapes(child,matrix,evenOdd,segments,b,0);
            var mask=b.Finish().Geometry;if(mask.Triangles.Length==0)return mask;
            if(mask.Triangles.Length/3>256)throw new ArgumentException("SVG clip triangle budget exceeded.");
            // Union all clip children before intersecting. Overlapping children must not paint twice.
            var contours=new Vector3D[mask.Triangles.Length/3][];
            for(int i=0;i<contours.Length;i++){var a=mask.Points[mask.Triangles[i*3]];var c=mask.Points[mask.Triangles[i*3+1]];var d=mask.Points[mask.Triangles[i*3+2]];contours[i]=Vector3D.Cross(c-a,d-a).Z<0?new[]{a,d,c}:new[]{a,c,d};}
            return PlanarFill.Tessellate(contours,false,false,256,768);
        }
        static void ClipShapes(Node n,MatrixD parent,bool evenOdd,int segments,MeshEffects.Builder b,int depth)
        {
            if(depth>16)throw new ArgumentException("Clip nesting budget exceeded.");var s=Styled(n,new PaintStyle{EvenOdd=evenOdd});var matrix=s.Style.Transform*parent;
            if(s.Clip!=null)throw new ArgumentException("Recursive clipPath references are unsupported.");
            if(n.Name=="g"){foreach(var child in n.Children)ClipShapes(child,matrix,s.EvenOdd,segments,b,depth+1);return;}
            var contours=Shape(n.Name,n.A,segments);var rings=new List<Vector3D[]>();foreach(var c in contours){var p=Clean(c);if(p.Length>=3)rings.Add(p);}
            if(rings.Count==0)return;var mesh=PlanarFill.Tessellate(rings.ToArray(),s.EvenOdd,false);
            for(int i=0;i<mesh.Triangles.Length;i+=3)b.Triangle(MeshEffects.V(Vector3D.Transform(mesh.Points[mesh.Triangles[i]],matrix)),MeshEffects.V(Vector3D.Transform(mesh.Points[mesh.Triangles[i+1]],matrix)),MeshEffects.V(Vector3D.Transform(mesh.Points[mesh.Triangles[i+2]],matrix)),Vector4.One);
        }
        static SurfaceMesh ClipMask(SurfaceMesh mesh,Geometry mask)
        {
            if(mask.Triangles.Length/3>256)throw new ArgumentException("SVG clip triangle budget exceeded.");var output=new MeshEffects.Builder();int work=0;
            for(int i=0;i<mesh.Geometry.Triangles.Length;i+=3)for(int j=0;j<mask.Triangles.Length;j+=3)
            {
                GeometryWork.Charge(4);if(++work>65536)throw new ArgumentException("SVG clipping work budget exceeded.");var a=mask.Points[mask.Triangles[j]];var b=mask.Points[mask.Triangles[j+1]];var c=mask.Points[mask.Triangles[j+2]];double sign=Vector3D.Cross(b-a,c-a).Z>=0?1:-1;
                var polygon=new List<MeshEffects.Vertex>{MeshEffects.V(mesh.Geometry.Points[mesh.Geometry.Triangles[i]]),MeshEffects.V(mesh.Geometry.Points[mesh.Geometry.Triangles[i+1]]),MeshEffects.V(mesh.Geometry.Points[mesh.Geometry.Triangles[i+2]])};
                var border=new[]{a,b,c};for(int k=0;k<3;k++){var p=border[k];var d=border[(k+1)%3]-p;polygon=MeshEffects.ClipPolygon(polygon,new Vector4((float)(-d.Y*sign),(float)(d.X*sign),0,(float)(sign*(d.Y*p.X-d.X*p.Y))));}
                for(int k=1;k+1<polygon.Count;k++)output.Triangle(polygon[0],polygon[k],polygon[k+1],mesh.Colors[i/3]);
            }
            return output.Finish();
        }
        static double Coordinate(Dictionary<string,string> a,string key,double fallback)
        {string s;if(!a.TryGetValue(key,out s))return fallback;s=s.Trim();return s.EndsWith("%")?Scalar(s.Substring(0,s.Length-1))/100:Scalar(s);}
        static SurfaceMesh ApplyVectorMask(SurfaceMesh mesh,string id,MatrixD transform,Dictionary<string,Node> ids,int segments)
        {
            Node mask;if(!ids.TryGetValue(id,out mask)||mask.Name!="mask")throw new ArgumentException("Missing SVG mask.");
            string units;if(mask.A.TryGetValue("maskContentUnits",out units)&&units!="userSpaceOnUse")throw new ArgumentException("Vector masks require userSpaceOnUse content units.");
            if(mask.A.TryGetValue("maskUnits",out units)&&units!="userSpaceOnUse")throw new ArgumentException("Vector masks require userSpaceOnUse mask units.");
            if(mask.A.ContainsKey("x")||mask.A.ContainsKey("y")||mask.A.ContainsKey("width")||mask.A.ContainsKey("height"))throw new ArgumentException("Vector mask regions are unsupported; use explicit mask shapes.");
            Vector4? uniform=null;var maskStyle=Styled(mask,new PaintStyle());foreach(var child in mask.Children)MaskPaint(child,maskStyle,ref uniform,0);
            string type;mask.A.TryGetValue("mask-type",out type);if(type!=null&&type!="alpha"&&type!="luminance")throw new ArgumentException("Invalid SVG mask type.");
            var color=uniform??Vector4.Zero;float opacity=color.W*(type=="alpha"?1:color.X*.2126f+color.Y*.7152f+color.Z*.0722f);
            mesh=ClipMask(mesh,ClipDefinition(mask,transform,ids,segments));for(int i=0;i<mesh.Colors.Length;i++)mesh.Colors[i].W*=opacity;return mesh;
        }
        static void MaskPaint(Node n,PaintStyle parent,ref Vector4? uniform,int depth)
        {
            GeometryWork.Charge();if(depth>16)throw new ArgumentException("Mask nesting exceeds 16.");var style=Styled(n,parent);
            if(style.Mask!=null||style.Filter!=null||style.FillGradient!=null||style.StrokeGradient!=null||style.Clip!=null||style.Style.Stroke.W>0)throw new ArgumentException("Vector masks support uniformly painted filled shapes only.");
            if(n.Name=="g"){foreach(var child in n.Children)MaskPaint(child,style,ref uniform,depth+1);return;}
            var color=style.Style.Fill;color.W*=style.Style.FillOpacity*style.Style.Opacity;if(uniform.HasValue&&uniform.Value!=color)throw new ArgumentException("Vector mask children must have the same paint and opacity; mixed masks are unsupported.");uniform=color;
        }
        static SurfaceMesh ApplyColorFilter(SurfaceMesh mesh,string id,Dictionary<string,Node> ids)
        {
            Node filter;if(!ids.TryGetValue(id,out filter)||filter.Name!="filter")throw new ArgumentException("Missing SVG filter.");
            if(filter.A.ContainsKey("x")||filter.A.ContainsKey("y")||filter.A.ContainsKey("width")||filter.A.ContainsKey("height"))throw new ArgumentException("Color filter regions are unsupported; use clip-path.");
            if(filter.Children.Count>8)throw new ArgumentException("Color filters support at most 8 stages.");
            foreach(var stage in filter.Children)
            {
                GeometryWork.Charge();if(stage.Name!="feColorMatrix"||stage.Children.Count!=0)throw new ArgumentException("Only bounded feColorMatrix SVG filters are supported; blur, shadows and external images are unsupported.");
                foreach(var attribute in stage.A)if(attribute.Key!="type"&&attribute.Key!="values"&&attribute.Key!="id")throw new ArgumentException("Unsupported color filter attribute: "+attribute.Key);
                string type;if(!stage.A.TryGetValue("type",out type))type="matrix";string values;stage.A.TryGetValue("values",out values);var numbers=values==null?new List<double>():Numbers(values);double[] matrix;
                if(type=="matrix"){if(numbers.Count!=20)throw new ArgumentException("Color matrix requires 20 numbers.");matrix=numbers.ToArray();}
                else if(type=="saturate"){if(numbers.Count!=1||numbers[0]<0||numbers[0]>1)throw new ArgumentException("Saturation requires a value in [0,1].");double s=numbers[0];matrix=new[]{.213+.787*s,.715-.715*s,.072-.072*s,0,0,.213-.213*s,.715+.285*s,.072-.072*s,0,0,.213-.213*s,.715-.715*s,.072+.928*s,0,0,0,0,0,1,0};}
                else if(type=="luminanceToAlpha"){if(numbers.Count!=0)throw new ArgumentException("luminanceToAlpha takes no values.");matrix=new[]{0d,0,0,0,0,0,0,0,0,0,0,0,0,0,0,.2125,.7154,.0721,0,0};}
                else throw new ArgumentException("Unsupported SVG color matrix type.");
                foreach(double value in matrix)if(!Geometry.Finite(value)||Math.Abs(value)>32)throw new ArgumentException("Color filter coefficient is outside the bounded range.");
                for(int i=0;i<mesh.Colors.Length;i++){GeometryWork.Charge();var c=mesh.Colors[i];var result=new Vector4();for(int row=0;row<4;row++){double v=matrix[row*5]*c.X+matrix[row*5+1]*c.Y+matrix[row*5+2]*c.Z+matrix[row*5+3]*c.W+matrix[row*5+4];result[row]=(float)Math.Max(0,Math.Min(1,v));}mesh.Colors[i]=result;}
            }return mesh;
        }
        static double GradientCoordinate(Dictionary<string,string> a,string key,string fallback,bool box,Vector4 viewport)
        {
            string value;if(!a.TryGetValue(key,out value))value=fallback;
            bool percentage=value.Trim().EndsWith("%");double number=percentage?Scalar(value.Trim().Substring(0,value.Trim().Length-1))/100:Scalar(value);
            if(box||!percentage)return number;
            if(key=="r")return number*Math.Sqrt(viewport.Z*viewport.Z+viewport.W*viewport.W)/Math.Sqrt(2);
            return key=="x1"||key=="x2"||key=="cx"||key=="fx"?viewport.X+number*viewport.Z:viewport.Y+number*viewport.W;
        }
        static GradientPaint Gradient(Node n,List<Vector3D> bounds,Vector4 viewport)
        {
            var attrs=Inline(n.A);var stops=new List<double>();var colors=new List<Vector4>();
            foreach(var child in n.Children)
            {if(child.Name!="stop")throw new ArgumentException("Gradient supports stop children only.");var a=Inline(child.A);double offset=Coordinate(a,"offset",0);if(offset<0||offset>1)throw new ArgumentException("Gradient stop offset must be 0–1.");offset=Math.Max(stops.Count==0?0:stops[stops.Count-1],offset);string color;var value=a.TryGetValue("stop-color",out color)?Paint(color):new Vector4(0,0,0,1);if(a.TryGetValue("stop-opacity",out color))value.W*=Alpha(color);stops.Add(offset);colors.Add(value);}
            if(stops.Count<1||stops.Count>16)throw new ArgumentException("Gradient requires 1–16 stops.");
            string units;bool box=!attrs.TryGetValue("gradientUnits",out units)||units=="objectBoundingBox";if(!box&&units!="userSpaceOnUse")throw new ArgumentException("Invalid gradient units.");
            var min=new Vector3D(double.MaxValue,double.MaxValue,0);var max=new Vector3D(double.MinValue,double.MinValue,0);foreach(var p in bounds){min.X=Math.Min(min.X,p.X);min.Y=Math.Min(min.Y,p.Y);max.X=Math.Max(max.X,p.X);max.Y=Math.Max(max.Y,p.Y);}
            var matrix=MatrixD.Identity;string transform;if(attrs.TryGetValue("gradientTransform",out transform))matrix=Transform(transform);
            if(Math.Abs(matrix.Determinant())<1e-15)throw new ArgumentException("Singular gradient transform.");var inverse=MatrixD.Invert(matrix);
            double x1=GradientCoordinate(attrs,"x1","0%",box,viewport),y1=GradientCoordinate(attrs,"y1","0%",box,viewport),x2=GradientCoordinate(attrs,"x2","100%",box,viewport),y2=GradientCoordinate(attrs,"y2","0%",box,viewport);
            double cx=GradientCoordinate(attrs,"cx","50%",box,viewport),cy=GradientCoordinate(attrs,"cy","50%",box,viewport),radius=GradientCoordinate(attrs,"r","50%",box,viewport);bool radial=n.Name=="radialGradient";
            if(radial&&radius<=0)throw new ArgumentException("Radial gradient radius must be positive.");
            if(attrs.ContainsKey("fx")&&Math.Abs(GradientCoordinate(attrs,"fx","50%",box,viewport)-cx)>1e-12
                ||attrs.ContainsKey("fy")&&Math.Abs(GradientCoordinate(attrs,"fy","50%",box,viewport)-cy)>1e-12)throw new ArgumentException("Offset radial focal points are unsupported.");
            string spread;attrs.TryGetValue("spreadMethod",out spread);if(spread!=null&&spread!="pad"&&spread!="repeat"&&spread!="reflect")throw new ArgumentException("Invalid gradient spread.");
            Func<Vector3D,Vector3D> metric=point=>{var p=point;if(box){p.X=(p.X-min.X)/Math.Max(1e-12,max.X-min.X);p.Y=(p.Y-min.Y)/Math.Max(1e-12,max.Y-min.Y);}p=Vector3D.Transform(p,inverse);return new Vector3D((p.X-cx)/radius,(p.Y-cy)/radius,0);};
            Func<Vector3D,double> coordinate=point=>
            {
                var p=point;if(box){p.X=(p.X-min.X)/Math.Max(1e-12,max.X-min.X);p.Y=(p.Y-min.Y)/Math.Max(1e-12,max.Y-min.Y);}p=Vector3D.Transform(p,inverse);
                double d=(x2-x1)*(x2-x1)+(y2-y1)*(y2-y1);double t=radial?Math.Sqrt((p.X-cx)*(p.X-cx)+(p.Y-cy)*(p.Y-cy))/radius:d<1e-20?1:((p.X-x1)*(x2-x1)+(p.Y-y1)*(y2-y1))/d;
                return t;
            };
            Func<double,Vector4> palette=t=>
            {
                if(spread=="repeat")t-=Math.Floor(t);else if(spread=="reflect"){t=t-Math.Floor(t/2)*2;if(t>1)t=2-t;}
                if(t<=stops[0])return colors[0];for(int i=1;i<stops.Count;i++)if(t<=stops[i])return MeshEffects.Mix(colors[i-1],colors[i],stops[i]==stops[i-1]?1:(t-stops[i-1])/(stops[i]-stops[i-1]));return colors[colors.Count-1];
            };
            return new GradientPaint{Coordinate=coordinate,Metric=metric,Palette=palette,Radial=radial,Repeat=spread=="repeat"||spread=="reflect"};
        }
    }
}
