using HDRClientRenderer;
using VRageMath;

internal static partial class Program
{
    static void PortalPerspectiveChecks()
    {
        // Equal-size paired windows, matching the demo's translated plane.
        // A two-metre landmark sits ten metres behind the remote opening.
        double previousPixels=double.PositiveInfinity,previousWindowFraction=0;
        foreach(double distance in new[]{2d,4d,8d})
        {
            var descriptor=Descriptor();((double[])descriptor[5])[0]=3;((double[])descriptor[11])[0]=3;
            PortalProvider.Declaration declaration;string error;
            Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Perspective retreat declaration parses");
            PortalRayMapSpec spec;
            Check(declaration.Snapshot(4,1,MatrixD.CreateTranslation(0,0,distance),ReverseProjection(),out spec),"Perspective retreat snapshot derives virtual eye");
            Check(Close(spec.CapturePose.Translation,new Vector3D(0,0,distance-10)),"Remote eye retreats by the same distance as the local eye");
            double apertureX=distance/(distance+10),u=.5+apertureX/3;
            PortalMappedRay ray;
            Check(spec.Ray(u,.5,out ray),"Retreat landmark ray crosses paired windows");
            double travel=(-20-ray.Origin.Z)/ray.Direction.Z;
            Check(travel>0&&Close(ray.Origin+ray.Direction*travel,new Vector3D(1,0,-20)),"Transported ray hits the fixed landmark edge without magnification");
            PortalRaySample sampled;
            Check(spec.Sample(ray,out sampled)&&sampled.Exact&&Close(sampled.ImageUv.X,.5+.5/(distance+10)),"Compositor samples the same landmark ray as the native perspective capture");
            double screenWidth=2/(distance+10),windowFraction=2*apertureX/3;
            Check(screenWidth<previousPixels&&windowFraction>previousWindowFraction,"Landmark shrinks in player pixels while occupying more of the receding window");
            System.Console.WriteLine("Portal retreat: eye="+distance+"m; landmark width="+(screenWidth*1000).ToString("F2")+"px in a 2000px-wide viewport with M11=1; opening fraction="+(windowFraction*100).ToString("F2")+"%");
            previousPixels=screenWidth;previousWindowFraction=windowFraction;
        }
    }
}
