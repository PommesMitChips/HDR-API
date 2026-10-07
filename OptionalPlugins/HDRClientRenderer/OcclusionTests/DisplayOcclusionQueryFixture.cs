using System;
using System.Collections.Generic;

namespace VRage.Render11.RenderContext { internal sealed class MyRenderContext { } }
namespace VRageRender
{
    internal static class MyRender11
    {
        internal static int ContextReads;
        internal static VRage.Render11.RenderContext.MyRenderContext RC
        { get { ContextReads++; return new VRage.Render11.RenderContext.MyRenderContext(); } }
    }
}
namespace VRage.Render11.Culling.Occlusion
{
    internal sealed class FixtureResource : IDisposable
    {
        internal int Releases;
        public void Dispose() { Releases++; MyOcclusionQuery.Events.Add("release"); }
    }
    internal sealed class MyOcclusionQuery
    {
        internal static readonly List<string> Events = new List<string>();
        internal static MyOcclusionQuery Last;
        internal static long NextResult;
        // Public to avoid unused-field warnings: production finds this metadata
        // exactly as it finds the private SharpDX field on the game query.
        public readonly FixtureResource m_query = new FixtureResource();
        internal readonly long Samples;
        internal MyOcclusionQuery(string name) { Samples = NextResult; Last = this; Events.Add("create"); }
        internal void Begin(VRage.Render11.RenderContext.MyRenderContext context) { Events.Add("begin"); }
        internal void End(VRage.Render11.RenderContext.MyRenderContext context) { Events.Add("end"); }
        internal long GetResult(bool stalling)
        { if (stalling) throw new Exception("Stalling query forbidden."); Events.Add("poll:false"); return Samples; }
    }
}
