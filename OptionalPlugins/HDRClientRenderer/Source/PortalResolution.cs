using System;
using System.Collections.Generic;
using VRageMath;

namespace HDRClientRenderer
{
    // Immutable renderer-owned primary view. Native owner latches this before
    // isolation and never reads gameplay camera/entity APIs on render workers.
    internal sealed class PortalPrimaryView
    {
        internal readonly MatrixD Viewer,Projection;
        internal readonly long Frame,Epoch;
        internal readonly int Width,Height,PresentationWidth,PresentationHeight;
        internal PortalPrimaryView(MatrixD viewer,MatrixD projection,long frame,int width,int height,long epoch,int presentationWidth=0,int presentationHeight=0)
        {Viewer=viewer;Projection=projection;Frame=frame;Width=width;Height=height;Epoch=epoch;PresentationWidth=presentationWidth==0?width:presentationWidth;PresentationHeight=presentationHeight==0?height:presentationHeight;}
        internal bool Valid{get{return Frame>=0&&Epoch>=0&&Width>0&&Height>0&&Width<=16384&&Height<=16384&&PresentationWidth>0&&PresentationHeight>0&&PresentationWidth<=16384&&PresentationHeight<=16384&&PortalProjection.Rigid(Viewer)&&PortalProjection.ComplementaryProjection(Projection)&&Math.Abs(Projection.Determinant())>1e-18;}}
    }
    internal sealed class PortalResolutionPlan
    {
        internal readonly int CaptureSize,OutputWidth,OutputHeight,BucketSize,RequestedWidth,RequestedHeight;
        internal readonly MatrixD Projection;
        internal readonly double ProbeU,ProbeV,VisibleWidth,VisibleHeight;
        internal readonly bool Cropped,Reduced,Projective;
        internal int DrawWidth{get{return OutputWidth+(Projective?0:2*PortalResolution.Gutter);}}
        internal int DrawHeight{get{return OutputHeight+(Projective?0:2*PortalResolution.Gutter);}}
        internal int Pixels{get{return CaptureSize*CaptureSize+(Projective?0:DrawWidth*DrawHeight);}}
        internal Vector4 TextureUv{get{return Projective?new Vector4(0,0,1,1):new Vector4((float)PortalResolution.Gutter/BucketSize,(float)PortalResolution.Gutter/BucketSize,(float)OutputWidth/BucketSize,(float)OutputHeight/BucketSize);}}
        internal PortalResolutionPlan(int capture,int width,int height,int bucket,int requestedWidth,int requestedHeight,MatrixD projection,double probeU,double probeV,double visibleWidth,double visibleHeight,bool cropped,bool reduced,bool projective=false)
        {CaptureSize=capture;OutputWidth=width;OutputHeight=height;BucketSize=bucket;RequestedWidth=requestedWidth;RequestedHeight=requestedHeight;Projection=projection;ProbeU=probeU;ProbeV=probeV;VisibleWidth=visibleWidth;VisibleHeight=visibleHeight;Cropped=cropped;Reduced=reduced;Projective=projective;}
    }
    internal static class PortalResolution
    {
        internal const int Maximum=2048,Gutter=2,Minimum=64;
        struct Vertex
        {
            internal Vector4D Clip;internal Vector2D Uv;
            internal Vertex(Vector4D clip,Vector2D uv){Clip=clip;Uv=uv;}
        }
        internal static bool TryPlan(PortalRayMapSpec spec,PortalPrimaryView view,int requestedWidth,int requestedHeight,int previousBucket,int pixelLimit,out PortalResolutionPlan plan,bool projective=false)
        {
            plan=null;if(spec==null||view==null||!view.Valid||requestedWidth<Minimum||requestedHeight<Minimum||requestedWidth>4096||requestedHeight>4096||pixelLimit<256*256+(projective?0:(Minimum+2*Gutter)*(Minimum+2*Gutter))||projective&&!PortalProjectiveSampling.Eligible(spec))return false;
            double minX=-1,minY=-1,maxX=1,maxY=1,probeU=.5,probeV=.5;bool crop=false;
            if(spec.Entry.Kind==PortalSurfaceKind.Plane&&spec.Exit.Kind==PortalSurfaceKind.Plane&&spec.Transport==PortalRayTransport.Differential)
            {
                if(!PlaneBounds(spec.Entry,view,out minX,out minY,out maxX,out maxY,out probeU,out probeV))return false;
                crop=true;
            }
            double visibleWidth=Math.Max(1,(maxX-minX)*view.PresentationWidth*.5),visibleHeight=Math.Max(1,(maxY-minY)*view.PresentationHeight*.5);
            double aspect=(double)requestedWidth/requestedHeight;
            double extent=projective?Math.Max(visibleWidth,visibleHeight):aspect>=1?Math.Max(visibleWidth,visibleHeight*aspect):Math.Max(visibleHeight,visibleWidth/aspect);
            int padding=projective?0:2*Gutter;
            extent=Math.Min(Math.Max(requestedWidth,requestedHeight),extent);int desiredBucket=Bucket((int)Math.Ceiling(extent)+padding),bucket=desiredBucket;
            // Small camera movement need not change the public material bucket.
            // Keep its stable wrapper until demand falls below half its extent.
            if(IsBucket(previousBucket)&&((previousBucket>=bucket&&extent+padding>previousBucket/2)||(bucket>previousBucket&&extent+padding<previousBucket*1.1)))bucket=previousBucket;
            int width,height,capture;
            while(true)
            {
                if(projective){width=height=capture=bucket;}else{Layout(bucket,requestedWidth,requestedHeight,out width,out height);capture=Bucket(Math.Max(width,height));}
                if((long)capture*capture+(projective?0:(long)(width+2*Gutter)*(height+2*Gutter))<=pixelLimit)break;
                if(bucket==256)return false;bucket/=2;
            }
            var projection=spec.Projection;
            if(crop)
            {
                double padX=16d/view.PresentationWidth,padY=16d/view.PresentationHeight;
                minX=Math.Max(-1,minX-padX);maxX=Math.Min(1,maxX+padX);minY=Math.Max(-1,minY-padY);maxY=Math.Min(1,maxY+padY);
                projection=Crop(spec.Projection,minX,minY,maxX,maxY);
                if(!PortalProjection.ComplementaryProjection(projection)||Math.Abs(projection.Determinant())<1e-18)return false;
            }
            bool reduced=requestedWidth>Maximum-padding||requestedHeight>Maximum-padding||bucket<desiredBucket;
            plan=new PortalResolutionPlan(capture,width,height,bucket,requestedWidth,requestedHeight,projection,probeU,probeV,visibleWidth,visibleHeight,crop,reduced,projective);return true;
        }
        internal static int Bucket(int extent){return extent<=256?256:extent<=512?512:extent<=1024?1024:2048;}
        internal static void Layout(int bucket,int requestedWidth,int requestedHeight,out int width,out int height)
        {
            double aspect=(double)requestedWidth/requestedHeight;int maximum=bucket-2*Gutter;
            if(aspect>=1){width=Math.Min(requestedWidth,maximum);height=Math.Max(Minimum,Math.Min(requestedHeight,(int)Math.Round(width/aspect)));}
            else{height=Math.Min(requestedHeight,maximum);width=Math.Max(Minimum,Math.Min(requestedWidth,(int)Math.Round(height*aspect)));}
            width=Math.Min(maximum,width);height=Math.Min(maximum,height);
        }
        internal static bool IsBucket(int size){return size==256||size==512||size==1024||size==2048;}
        internal static MatrixD Crop(MatrixD projection,double minX,double minY,double maxX,double maxY)
        {
            double width=maxX-minX,height=maxY-minY;if(!PortalProjection.Positive(width)||!PortalProjection.Positive(height))return default(MatrixD);
            var remap=MatrixD.Identity;remap.M11=2/width;remap.M22=2/height;remap.M41=-(minX+maxX)/width;remap.M42=-(minY+maxY)/height;
            // Row-vector clip remap changes x/y only: z and w retain their native
            // complementary-depth semantics and outgoing geometric sight lines.
            return projection*remap;
        }
        static bool PlaneBounds(PortalSurfaceMap surface,PortalPrimaryView view,out double minX,out double minY,out double maxX,out double maxY,out double probeU,out double probeV)
        {
            minX=minY=double.PositiveInfinity;maxX=maxY=double.NegativeInfinity;probeU=probeV=.5;
            var polygon=new List<Vertex>(12);var inverse=MatrixD.Transpose(view.Viewer.GetOrientation());double[] us={0,1,1,0},vs={0,0,1,1};
            for(int i=0;i<4;i++)
            {
                PortalSurfaceSample sample;if(!surface.Evaluate(us[i],vs[i],0,out sample))return false;
                var point=Vector3D.TransformNormal(sample.Point-view.Viewer.Translation,inverse);var clip=Vector4D.Transform(new Vector4D(point,1),view.Projection);
                if(!PortalProjection.Finite(clip))return false;polygon.Add(new Vertex(clip,new Vector2D(us[i],vs[i])));
            }
            for(int plane=0;plane<7;plane++)
            {
                if(polygon.Count==0)return false;var output=new List<Vertex>(12);var previous=polygon[polygon.Count-1];double before=Distance(previous.Clip,plane);
                foreach(var current in polygon)
                {
                    double after=Distance(current.Clip,plane);
                    if((before>=0)!=(after>=0))
                    {
                        double t=before/(before-after);output.Add(new Vertex(previous.Clip+(current.Clip-previous.Clip)*t,previous.Uv+(current.Uv-previous.Uv)*t));
                    }
                    if(after>=0)output.Add(current);previous=current;before=after;
                }
                if(output.Count>16)return false;polygon=output;
            }
            if(polygon.Count<3)return false;Vector2D probe=Vector2D.Zero;
            foreach(var vertex in polygon)
            {
                if(vertex.Clip.W<=0)return false;double x=vertex.Clip.X/vertex.Clip.W,y=vertex.Clip.Y/vertex.Clip.W;
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);probe+=vertex.Uv;
            }
            probe/=polygon.Count;probeU=probe.X;probeV=probe.Y;return maxX-minX>1e-10&&maxY-minY>1e-10;
        }
        static double Distance(Vector4D p,int plane)
        {
            switch(plane){case 0:return p.W-1e-8;case 1:return p.X+p.W;case 2:return p.W-p.X;case 3:return p.Y+p.W;case 4:return p.W-p.Y;case 5:return p.Z;default:return p.W-p.Z;}
        }
    }
}
