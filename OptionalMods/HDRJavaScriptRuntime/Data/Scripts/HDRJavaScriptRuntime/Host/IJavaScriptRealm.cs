using System;

namespace HDRJavaScriptRuntime
{
    /// <summary>Explicit interpreter seam. Host values are scalar primitives or opaque callbacks owned by this realm.</summary>
    public interface IJavaScriptRealm : IDisposable
    {
        void RegisterHostFunction(string name, Func<object[], object> callback);
        void Execute(string source);
        object InvokeNamed(string name, params object[] arguments);
    }
}
