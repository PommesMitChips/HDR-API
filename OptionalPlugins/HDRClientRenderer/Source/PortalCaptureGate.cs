using System;
using System.Collections.Generic;
using System.Threading;
namespace HDRClientRenderer
{
    // Global capture lifetime covers deferred worker callbacks. A frame is only
    // publishable after the native owner has joined every worker and proves all
    // relevant draw contexts were observed. No shader callback throws outward.
    internal sealed class PortalCaptureGate
    {
        readonly object gate=new object();int incomplete,open;string failure;
        readonly HashSet<PortalClipFamily> observed=new HashSet<PortalClipFamily>();
        internal readonly int Epoch;internal readonly long Generation;
        internal PortalCaptureGate(int epoch,long generation,int samples,bool completeContextCoverage,bool customProjectionBillboardsAbsent)
        {
            Epoch=epoch;Generation=generation;open=1;
            if(samples!=1)Reject("Portal clipping currently requires a single-sample capture.");
            if(!completeContextCoverage)Reject("Portal capture pass/context coverage has not been verified.");
            if(!customProjectionBillboardsAbsent)Reject("Portal custom-projection billboard coverage has not been verified.");
        }
        internal bool Complete{get{lock(gate)return open==0&&incomplete==0;}}
        internal string Failure{get{lock(gate)return failure;}}
        internal void Reject(string message){lock(gate){incomplete=1;if(failure==null)failure=message??"Unverified portal geometry shader.";}}
        internal bool Observe(PortalClipFamily family,bool privateVariant,bool cutoffBound)
        {
            lock(gate)
            {
                if(open==0){Reject("Portal callback outlived the capture scope.");return false;}
                if(family==PortalClipFamily.Unknown||!privateVariant||!cutoffBound){Reject("Portal geometry variant or threshold binding is unavailable: "+family);return false;}
                observed.Add(family);return incomplete==0;
            }
        }
        internal bool Finish(int epoch,long generation,bool workersJoined)
        {
            lock(gate)
            {
                if(epoch!=Epoch||generation!=Generation||!workersJoined)Reject("Portal generation changed or capture workers have not joined.");
                if(open==0)Reject("Portal capture completed twice.");open=0;return incomplete==0;
            }
        }
    }
}
