using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
class Program {
 const string DefaultBin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
 static void Main(string[] args) {
 string bin=Environment.GetEnvironmentVariable("SE_BIN")??DefaultBin;
 AssemblyLoadContext.Default.Resolving+=(c,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
 Run(bin,args);
 }
 static void Run(string bin,string[] args){
 string content=Path.GetFullPath(Path.Combine(bin,"../Content"));
 var supported=new HashSet<string>{"LargeLCDPanel","SmallLCDPanel","LargeLCDPanelWide","SmallLCDPanelWide","TransparentLCDLarge","TransparentLCDSmall"};
 var records=new List<object>();var code=new List<string>();
 foreach(string file in new[]{"CubeBlocks_LCDPanels.sbc","CubeBlocks_DecorativePack2.sbc"})
 foreach(var def in XDocument.Load(Path.Combine(content,"Data/CubeBlocks",file)).Descendants("Definition")){
 string subtype=(string)def.Element("Id")?.Element("SubtypeId");if(!supported.Contains(subtype))continue;
 string model=(string)def.Element("Model");var importer=new MyModelImporter();importer.ImportData(Path.Combine(content,model));var root=importer.GetTagData();
 string geometry=root.TryGetValue("GeometryDataAsset",out object asset)?(string)asset+".mwm":model;
 if(geometry!=model){importer=new MyModelImporter();importer.ImportData(Path.Combine(content,geometry));}
 var tags=importer.GetTagData();var points=(HalfVector4[])tags["Vertices"];var uv=(HalfVector2[])tags["TexCoords0"];var normals=(Byte4[])tags["Normals"];
 int rotation=0;
 foreach(var area in def.Element("ScreenAreas").Elements("ScreenArea")){
 string name=(string)area.Attribute("Name");var parts=((List<MyMeshPartInfo>)tags["MeshParts"]).Where(p=>p.GetMaterialName()==name).ToArray();
 if(parts.Length!=1)throw new Exception("Ambiguous or missing material "+subtype+" "+name);
 var part=parts[0];var ids=part.m_indices.Distinct().Where(i=>UnpackNormal(normals[i]).Z>.999).ToArray();
 if(ids.Length!=4)throw new Exception("Not exactly one front screen quad: "+subtype+" "+name+" vertices="+ids.Length);
 var corners=new Vector3D[4];var found=new bool[4];
 foreach(int i in ids){var tex=uv[i].ToVector2();int x=(int)Math.Round(tex.X),y=(int)Math.Round(tex.Y);if(x<0||x>1||y<0||y>1||Math.Abs(tex.X-x)>1e-5||Math.Abs(tex.Y-y)>1e-5)throw new Exception("Unsupported UV coordinate");int corner=y*2+x;if(found[corner])throw new Exception("Duplicated UV corner");var packed=points[i].ToVector4();corners[corner]=new Vector3D(packed.X,packed.Y,packed.Z)*packed.W;found[corner]=true;}
 var halfRight=(corners[1]-corners[0])*.5;var halfUp=(corners[0]-corners[2])*.5;var center=(corners[0]+corners[1]+corners[2]+corners[3])*.25;
 if(Vector3D.DistanceSquared(corners[0]+corners[3],corners[1]+corners[2])>1e-16||Math.Abs(Vector3D.Dot(halfRight,halfUp))>1e-10)throw new Exception("Screen is not an affine rectangle");
 var normal=Vector3D.Normalize(Vector3D.Cross(halfRight,halfUp));if(normal.Z<.999)throw new Exception("Unexpected front normal");
 int frontTriangleIndices=0;double areaSum=0;
 for(int i=0;i<part.m_indices.Count;i+=3){int count=0;var tri=new Vector3D[3];for(int j=0;j<3;j++){int index=part.m_indices[i+j];if(ids.Contains(index)){count++;var p=points[index].ToVector4();tri[j]=new Vector3D(p.X,p.Y,p.Z)*p.W;}}if(count==0)continue;if(count!=3)throw new Exception("Front/back shared triangle");frontTriangleIndices+=3;areaSum+=Vector3D.Cross(tri[1]-tri[0],tri[2]-tri[0]).Length()*.5;}
 if(frontTriangleIndices!=6||Math.Abs(areaSum-4*halfRight.Length()*halfUp.Length())>1e-10)throw new Exception("Unexpected screen topology/coverage");
 records.Add(new{subtype,model,geometry,material=name,rotation,technique=part.Technique.ToString(),center=Vec(center),halfRight=Vec(halfRight),halfUp=Vec(halfUp),normal=Vec(normal),uvCorners=corners.Select(Vec).ToArray(),modelSha256=Hash(Path.Combine(content,model)),geometrySha256=Hash(Path.Combine(content,geometry)),frontOnly=true,uvCornerTolerance=1e-5});
 code.Add("   new Entry(\""+model.Replace('\\','/').ToLowerInvariant()+"\", \""+name+"\", "+rotation+", "+Cs(center)+", "+Cs(halfRight)+", "+Cs(halfUp)+"),");rotation++;
 }
 }
 if(records.Count!=24)throw new Exception("Expected 24 calibrated model/rotation records, got "+records.Count);
 string output=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath("artifacts/screen-calibration.json");
 File.WriteAllText(output,JsonSerializer.Serialize(new{source="Installed SE1 models; public MyModelImporter; position unpack xyz*w; normal unpack matches VertexTransformations.hlsli; front-facing UV corners only",records},new JsonSerializerOptions{WriteIndented=true}));
 File.WriteAllLines(Path.ChangeExtension(output,"entries.txt"),code);
 if(args.Length>1){string source=File.ReadAllText(Path.GetFullPath(args[1]));foreach(string line in code)if(!source.Contains(line.Trim()))throw new Exception("Production calibration differs from installed model: "+line);}
 Console.WriteLine("Exported "+records.Count+" validated front-face calibrations to "+output+(args.Length>1?"; every generated entry matches production source":""));
 }
 static double[] Vec(Vector3D v){return new[]{v.X,v.Y,v.Z};}
 static string Cs(Vector3D v){return "new Vector3D("+string.Join(",",Vec(v).Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+")";}
 static string Hash(string path){return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));}
 static Vector3D UnpackNormal(Byte4 value){var b=value.ToVector4();int x=(int)b.X+256*(int)b.Y,y=(int)b.Z+256*(int)b.W;double sign=x>32767?1:-1;x&=32767;double nx=2*x/32767.0-1,ny=2*y/32767.0-1;return new Vector3D(nx,ny,sign*Math.Sqrt(Math.Max(0,1-nx*nx-ny*ny)));}
}
