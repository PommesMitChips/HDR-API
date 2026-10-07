// Optional PB convenience wrapper. Paste inside your Program class.
// All frames use anchor-local meters; native captures run independently per viewer.
public sealed class PortalApi
{
 readonly Func<string,object[],object> draw;
 public PortalApi(IMyProgrammableBlock caller)
 {var p=caller.GetProperty("HDR.Draw");if(p==null)throw new Exception("Enable HDR API and reload the world.");draw=p.As<Func<string,object[],object>>().GetValue(caller);if(draw==null)throw new Exception("HDR drawing endpoint is unavailable.");}
 public void Select(IMyProjector anchor,string screen){draw("target",new object[]{anchor});draw("screen",new object[]{screen});}
 public void PairPlane(MatrixD exit,double width,double height,string transport="differential",string accuracy="strict")
 {draw("screen-portal-plane",new object[]{exit,width,height,transport,accuracy});}
 public void PairEllipsoid(MatrixD exit,Vector3D radii,string transport="differential",string accuracy="strict")
 {draw("screen-portal-ellipsoid",new object[]{exit,radii,transport,accuracy});}
 public void PairMesh(MatrixD exit,Vector3D[] points,int[] triangles,Vector2[] uv,string transport="differential",string accuracy="strict")
 {draw("screen-portal-mesh",new object[]{exit,points,triangles,uv,transport,accuracy});}
 public void CaptureShell(MatrixD pose,Vector3D radii){draw("screen-portal-shell",new object[]{pose,radii});}
 public void Appearance(double normalScale=1,double saturation=1,double brightness=1){draw("screen-portal-settings",new object[]{normalScale,saturation,brightness});}
 public void Charts(int entry,int exit){draw("screen-portal-charts",new object[]{entry,exit});}
 public void ExitSide(string side){draw("screen-portal-exit-side",new object[]{side});}
 public void Clear(){draw("screen-portal-clear",new object[0]);}
}
