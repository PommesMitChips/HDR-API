using System;
using Jint.Parser;
using Jint.Runtime;
using Jint.Runtime.Environments;

namespace Jint.Native.Function
{
    public class EvalFunctionInstance: FunctionInstance
    {
        private readonly Engine _engine;

        public EvalFunctionInstance(Engine engine, string[] parameters, LexicalEnvironment scope, bool strict) : base(engine, parameters, scope, strict)
        {
            _engine = engine;
            Prototype = Engine.Function.PrototypeObject;
            FastAddProperty("length", 1, false, false, false);
        }

        public override JsValue Call(JsValue thisObject, JsValue[] arguments)
        { throw new InvalidOperationException("HDR JavaScript: eval is unavailable; execute bounded source through the host."); }

        public JsValue Call(JsValue thisObject, JsValue[] arguments, bool directCall)
        {
            throw new InvalidOperationException("HDR JavaScript: direct eval is unavailable; use bounded host execution.");
        }
    }
}
