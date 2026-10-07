using System;
using System.Linq;
using System.Reflection;
using VRageMath;

namespace HDRClientRenderer
{
    // Read the real main backbuffer size, not mutable secondary-capture viewport
    // globals. No native resource creation, mapping or video readback occurs here.
    internal sealed class ClientPixelBudgetNative
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly PropertyInfo backbuffer, size, frame;
        internal ClientPixelBudgetNative(Assembly assembly)
        {
            var renderer = assembly.GetType("VRageRender.MyRender11", true);
            backbuffer = renderer.GetProperty("Backbuffer", Static);
            frame = assembly.GetType("VRageRender.MyCommon", true).GetProperty("FrameCounter", Static);
            size = backbuffer == null ? null : backbuffer.PropertyType.GetProperty("Size", Instance);
            if (backbuffer == null || frame == null || size == null || size.PropertyType != typeof(Vector2I) || frame.PropertyType != typeof(long))
                throw new InvalidOperationException("Shared pixel budget viewport ABI changed.");
        }
        internal ClientPixelBudget.Frame Read()
        {
            var main = backbuffer.GetValue(null);
            if (main == null) return default(ClientPixelBudget.Frame);
            var pixels = (Vector2I)size.GetValue(main);
            return new ClientPixelBudget.Frame((long)frame.GetValue(null), pixels.X, pixels.Y);
        }
        internal static ClientPixelBudgetNative TryBind()
        {
            var renderer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRageRender.MyRender11", false)).FirstOrDefault(t => t != null);
            return renderer == null ? null : new ClientPixelBudgetNative(renderer.Assembly);
        }
    }
}
