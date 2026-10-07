// Whole PB script. HDR API 0.9.0; anchor Console/Projector named HDR Display.
// Run "ellipsoid" (default) or "mesh". All geometry remains client rendered.
Func<string,object[],object> _hdr;
object H(string op,params object[] args)
{
 if(_hdr==null){var p=Me.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API 0.9.0 and reload.");_hdr=p.As<Func<string,object[],object>>().GetValue(Me);}
 return _hdr(op,args);
}
public void Main(string argument,UpdateType update)
{
 try
 {
  if(H("findtarget","HDR Display")==null)throw new Exception("Name a Console or Projector HDR Display.");
  H("screen","surface",MatrixD.CreateTranslation(0,1.5,0),4,2,4,2);
  H("screen-two-sided",true,.9,.35);H("screen-background","#15394b60");
  H("screen-quality",.02);H("screen-mapping","angular");
  if((argument??"").Trim().Equals("mesh",StringComparison.OrdinalIgnoreCase))
  {
   H("screen-mesh",new[]{new Vector3D(-1.5,-1,0),new Vector3D(1.5,-1,0),new Vector3D(1.5,1,.4),new Vector3D(-1.5,1,-.4)},new[]{0,1,2,0,2,3},new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)},"outside");
   H("text","title","AUTHORED UV MESH",0,.3,0,.18,"white");
  }
  else
  {
   H("screen-ellipsoid",new Vector3D(2,1,1.5),Math.PI,Math.PI/2,"outside");
   H("text","title","ELLIPSOID 2 / 1 / 1.5",0,.3,0,.18,"white");
  }
  H("circle","ring",0,-.3,0,.4,"orange",64,.01);
  Echo("General surface demo ready. Run mesh or ellipsoid. Back opacity: 35%. No projected widget input is enabled.");
 }
 catch(Exception error){Echo(error.Message);}
}
