using System;
using Jint.Native.Object;
using Jint.Runtime;

namespace Jint.Native.Function
{
    /// <summary>Explicit profile omission supporting both normal calls and new expressions.</summary>
    public sealed class HdrUnavailableFunction : FunctionInstance, IConstructor
    {
        private readonly string _reason;
        public HdrUnavailableFunction(Engine engine, string reason) : base(engine,new string[0],null,false)
        { _reason=reason; Prototype=engine.Function.PrototypeObject; }
        public override JsValue Call(JsValue thisObject, JsValue[] arguments)
        { throw new InvalidOperationException(_reason); }
        public ObjectInstance Construct(JsValue[] arguments)
        { throw new InvalidOperationException(_reason); }
    }
}
