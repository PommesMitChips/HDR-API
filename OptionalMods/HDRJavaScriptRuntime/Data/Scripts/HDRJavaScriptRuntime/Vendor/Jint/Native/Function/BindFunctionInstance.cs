using System.Linq;
using Jint.Native.Object;
using Jint.Runtime;

namespace Jint.Native.Function
{
    public class BindFunctionInstance : FunctionInstance, IConstructor
    {
        public BindFunctionInstance(Engine engine) : base(engine, new string[0], null, false)
        {
        }

        public JsValue TargetFunction { get; set; }

        public JsValue BoundThis { get; set; }

        public JsValue[] BoundArgs { get; set; }

        public override JsValue Call(JsValue thisObject, JsValue[] arguments)
        {
            Engine.HdrNativeEnter();
            try {
            Engine.HdrCheckArray((long)BoundArgs.Length + arguments.Length);
            Engine.HdrCharge(8L * (BoundArgs.Length + arguments.Length));

            var f = TargetFunction.TryCast<FunctionInstance>(x =>
            {
                throw new JavaScriptException(Engine.TypeError);
            });

            return f.Call(BoundThis, BoundArgs.Concat(arguments).ToArray());
        
            } finally { Engine.HdrNativeLeave(); }
        }

        public ObjectInstance Construct(JsValue[] arguments)
        {
            Engine.HdrNativeEnter();
            try {
            Engine.HdrCheckArray((long)BoundArgs.Length + arguments.Length);
            Engine.HdrCharge(8L * (BoundArgs.Length + arguments.Length));

            var target = TargetFunction.TryCast<IConstructor>(x =>
            {
                throw new JavaScriptException(Engine.TypeError);
            });

            return target.Construct(BoundArgs.Concat(arguments).ToArray());
        
            } finally { Engine.HdrNativeLeave(); }
        }

        public override bool HasInstance(JsValue v)
        {
            Engine.HdrNativeEnter();
            try {

            var f = TargetFunction.TryCast<FunctionInstance>(x =>
            {
                throw new JavaScriptException(Engine.TypeError);
            });
              
            return f.HasInstance(v);
        
            } finally { Engine.HdrNativeLeave(); }
        }
    }
}
