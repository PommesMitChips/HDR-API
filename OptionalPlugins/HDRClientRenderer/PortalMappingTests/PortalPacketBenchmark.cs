#if PORTAL_PACKET_BENCHMARK
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using HDRClientRenderer;
using VRageMath;

// Opt-in CPU diagnostic, without production changes or native/GPU execution:
// dotnet run --project PortalMappingTests.csproj -c Release
//   -p:DefineConstants=PORTAL_PACKET_BENCHMARK -p:StartupObject=PortalPacketBenchmark
internal static class PortalPacketBenchmark
{
    static void Main()
    {
        const string bin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{var path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        PortalSurfaceMap entry,exit,shell;PortalRayMapSpec spec;
        var destination=MatrixD.CreateTranslation(0,0,-10);var projection=MatrixD.Identity;projection.M33=0;projection.M34=-1;projection.M43=.1;projection.M44=0;
        if(!PortalSurfaceMap.TryPlane(MatrixD.Identity,2,2,out entry)||!PortalSurfaceMap.TryPlane(destination,2,2,out exit)||!PortalSurfaceMap.TryEllipsoid(destination,new Vector3D(2),out shell)||
            !PortalRayMapSpec.TryCreate(1,1,entry,0,exit,0,shell,MatrixD.CreateTranslation(0,0,5),MatrixD.CreateTranslation(0,0,-5),projection,PortalRayTransport.Differential,PortalImageProjection.Perspective,PortalRayAccuracy.ExactCaptureLine,1,out spec))throw new Exception("Benchmark fixture is invalid.");
        PortalRayPacket packet;string error;PortalRayPacket.TryBuild(spec,64,64,out packet,out error);
        // Prime the larger workload first so background tiered JIT promotion of
        // shared kernels does not distort the small-grid/large-grid comparison.
        for(int warmup=0;warmup<5;warmup++)if(!PortalRayPacket.TryBuild(spec,512,512,out packet,out error))throw new Exception(error);
        foreach(int size in new[]{256,512})
        {
            for(int warmup=0;warmup<5;warmup++)if(!PortalRayPacket.TryBuild(spec,size,size,out packet,out error))throw new Exception(error);
            var elapsed=new double[5];long allocations=0;
            for(int i=0;i<elapsed.Length;i++)
            {
                long before=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
                if(!PortalRayPacket.TryBuild(spec,size,size,out packet,out error)||packet.ValidPixels!=size*size)throw new Exception(error??"Incomplete benchmark fixture");
                clock.Stop();elapsed[i]=clock.Elapsed.TotalMilliseconds;allocations+=GC.GetAllocatedBytesForCurrentThread()-before;
            }
            double sum=0;foreach(double duration in elapsed)sum+=duration;Array.Sort(elapsed);double average=sum/elapsed.Length;
            Console.WriteLine("CPU PortalRayPacket exact plane "+size+"x"+size+": affine="+packet.IsAffine+", upload="+packet.TextureWidth+"x"+packet.TextureHeight+"; average "+average.ToString("F4")+" ms; median "+elapsed[2].ToString("F4")+" ms; min/max "+elapsed[0].ToString("F4")+"/"+elapsed[4].ToString("F4")+" ms; allocated "+(allocations/elapsed.Length)+" bytes ("+(allocations/elapsed.Length/1048576d).ToString("F4")+" MiB)/build; at30Hz "+(average*30/1000).ToString("F4")+" CPU-seconds/second. No game frames or GPU objects.");
        }
    }
}
#endif
