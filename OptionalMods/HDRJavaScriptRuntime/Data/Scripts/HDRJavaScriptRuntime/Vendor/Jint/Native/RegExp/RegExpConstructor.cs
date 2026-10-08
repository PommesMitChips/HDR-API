using System;
using System.Text.RegularExpressions;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;

namespace Jint.Native.RegExp
{
    public sealed class RegExpConstructor : FunctionInstance, IConstructor
    {
        public RegExpConstructor(Engine engine)
            : base(engine, null, null, false)
        {
        }

        public static RegExpConstructor CreateRegExpConstructor(Engine engine)
        {
            var obj = new RegExpConstructor(engine);
            obj.Extensible = true;

            // The value of the [[Prototype]] internal property of the RegExp constructor is the Function prototype object 
            obj.Prototype = engine.Function.PrototypeObject;
            obj.PrototypeObject = RegExpPrototype.CreatePrototypeObject(engine, obj);

            obj.FastAddProperty("length", 2, false, false, false);

            // The initial value of RegExp.prototype is the RegExp prototype object
            obj.FastAddProperty("prototype", obj.PrototypeObject, false, false, false);

            return obj;
        }

        public void Configure()
        {
        }

        public override JsValue Call(JsValue thisObject, JsValue[] arguments)
        {
            var pattern = arguments.At(0);
            var flags = arguments.At(1);

            if (pattern != Undefined.Instance && flags == Undefined.Instance && TypeConverter.ToObject(Engine, pattern).Class == "Regex")
            {
                return pattern;
            }

            return Construct(arguments);
        }

        /// <summary>
        /// http://www.ecma-international.org/ecma-262/5.1/#sec-7.8.5
        /// http://www.ecma-international.org/ecma-262/5.1/#sec-15.10.4
        /// </summary>
        /// <param name="arguments"></param>
        /// <returns></returns>
        public ObjectInstance Construct(JsValue[] arguments)
        { throw new InvalidOperationException("HDR JavaScript: RegExp is unavailable in the bounded prototype profile."); }

        public RegExpInstance Construct(string regExp)
        { throw new InvalidOperationException("HDR JavaScript: regular-expression literals are unavailable in the bounded prototype profile."); }

        private RegexOptions ParseOptions(RegExpInstance r, string flags)
        {
            for (int k = 0; k < flags.Length; k++)
            {
                HdrExecutionGuard.Step();
                var c = flags[k];
                if (c == 'g')
                {
                    if (r.Global)
                    {
                        throw new JavaScriptException(Engine.SyntaxError);
                    }

                    r.Global = true;
                }
                else if (c == 'i')
                {
                    if (r.IgnoreCase)
                    {
                        throw new JavaScriptException(Engine.SyntaxError);
                    }

                    r.IgnoreCase = true;
                }
                else if (c == 'm')
                {
                    if (r.Multiline)
                    {
                        throw new JavaScriptException(Engine.SyntaxError);
                    }

                    r.Multiline = true;
                }
                else
                {
                    throw new JavaScriptException(Engine.SyntaxError);
                }
            }

            var options = RegexOptions.ECMAScript;

            if (r.Multiline)
            {
                options = options | RegexOptions.Multiline;
            }

            if (r.IgnoreCase)
            {
                options = options | RegexOptions.IgnoreCase;
            }

            return options;
        }

        public RegExpPrototype PrototypeObject { get; private set; }
    }
}
