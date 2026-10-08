using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace HDRJavaScriptRuntime
{
    /// <summary>A realm-owned callable capability. It never exposes a JS object or CLR reflection.</summary>
    public sealed class JavaScriptCallback : IDisposable
    {
        private JavaScriptRealm _owner;
        private JsValue _function;

        internal JavaScriptCallback(JavaScriptRealm owner, JsValue function)
        { _owner=owner; _function=function; }

        public bool Current { get { return _owner != null && _owner.Current && _function != null; } }
        public bool IsOwnedBy(IJavaScriptRealm realm) { return Current && object.ReferenceEquals(_owner,realm); }
        public object Invoke(params object[] arguments)
        {
            if (!Current) throw new InvalidOperationException("JavaScript callback has been revoked.");
            return _owner.InvokeCallback(_function,arguments);
        }
        public void Dispose() { _owner=null; _function=null; }
    }

    /// <summary>Source-vendored Jint 2.11.10 ES5.1 interpreter with explicit Workshop-compatible host capabilities.</summary>
    public sealed class JavaScriptRealm : IJavaScriptRealm
    {
        public const string Profile = "HDR.JavaScript/ES5.1-Prototype1";
        private Engine _engine;
        private readonly JavaScriptLimits _limits;
        private readonly List<JavaScriptCallback> _callbacks = new List<JavaScriptCallback>();
        private readonly HashSet<string> _hostNames = new HashSet<string>();
        private bool _busy;
        private int _hostCalls;

        public JavaScriptRealm(JavaScriptLimits limits)
        {
            _limits=(limits ?? new JavaScriptLimits()).Copy();
            _limits.Validate();
            _engine=new Engine(o => o.MaxStatements(_limits.MaxStatements)
                .LimitRecursion(_limits.MaxRecursionDepth)
                .TimeoutInterval(TimeSpan.FromMilliseconds(_limits.TimeoutMilliseconds)));
            _engine.HdrMaxOperations=_limits.MaxOperations;
            _engine.HdrMaxStringCharacters=_limits.MaxStringCharacters;
            _engine.HdrMaxParseDepth=_limits.MaxParseDepth;
            _engine.HdrMaxArrayLength=_limits.MaxArrayLength;
            _engine.HdrMaxObjectProperties=_limits.MaxObjectProperties;
            _engine.HdrMaxCreatedObjects=_limits.MaxCreatedObjects;
            _engine.HdrMaxAllocationUnitsPerOperation=_limits.MaxAllocationUnitsPerOperation;
            _engine.HdrMaxAllocationUnitsPerRealm=_limits.MaxAllocationUnitsPerRealm;
        }

        public bool Current { get { return _engine != null; } }
        public JavaScriptLimits Limits { get { return _limits.Copy(); } }

        public void RegisterHostFunction(string name, Func<object[],object> callback)
        {
            RequireCurrent();
            if (_busy) throw new InvalidOperationException("Cannot register a host function while JavaScript is executing.");
            if (!Identifier(name) || callback == null) throw new InvalidOperationException("Host functions require an identifier and an explicit callback.");
            if (!_hostNames.Contains(name) && _hostNames.Count >= _limits.MaxObjectProperties)
                throw new InvalidOperationException("JavaScript host-function quota exceeded.");
            _hostNames.Add(name);
            _engine.Global.FastAddProperty(name,new ClrFunctionInstance(_engine,(thisValue,arguments) => {
                if (_limits.MaxHostCallsPerOperation > 0 && ++_hostCalls > _limits.MaxHostCallsPerOperation)
                    throw new InvalidOperationException("HDR JavaScript: host callback limit exceeded.");
                var values=new object[arguments.Length];
                for (var i=0;i<values.Length;i++) values[i]=ToHost(arguments[i],true);
                return ToJavaScript(callback(values));
            }),false,false,false);
        }

        public void Execute(string source)
        {
            RequireCurrent();
            if (source == null || source.Length > _limits.MaxSourceCharacters)
                throw new InvalidOperationException("HDR JavaScript: source character limit exceeded.");
            Begin();
            try { _engine.HdrCharge(2L * source.Length); _engine.Execute(source); }
            finally { End(); }
        }

        public object InvokeNamed(string name, params object[] arguments)
        {
            RequireCurrent();
            if (!Identifier(name)) throw new InvalidOperationException("JavaScript callback names must be identifiers.");
            Begin();
            try { return Call(_engine.GetValue(name),arguments); }
            finally { End(); }
        }

        internal object InvokeCallback(JsValue function, object[] arguments)
        {
            RequireCurrent();
            Begin();
            try { return Call(function,arguments); }
            finally { End(); }
        }

        private object Call(JsValue function, object[] arguments)
        {
            var callable=function.TryCast<ICallable>();
            if (callable == null) throw new InvalidOperationException("JavaScript callback is not callable.");
            arguments=arguments ?? new object[0];
            if (arguments.Length > _limits.MaxArrayLength) throw new InvalidOperationException("JavaScript callback argument limit exceeded.");
            var values=new JsValue[arguments.Length];
            for (var i=0;i<values.Length;i++) values[i]=ToJavaScript(arguments[i]);
            return ToHost(callable.Call(JsValue.Undefined,values),false);
        }

        private void Begin()
        {
            if (_busy) throw new InvalidOperationException("Nested JavaScript realm execution is unavailable; queue callbacks for the next update.");
            _engine.HdrBeginOperation(); _busy=true; _hostCalls=0;
        }
        private void End() { _engine.HdrEndOperation(); _busy=false; }
        private void RequireCurrent()
        { if (_engine == null) throw new InvalidOperationException("JavaScript realm has been disposed."); }

        private JsValue ToJavaScript(object value)
        {
            if (value == null) return JsValue.Null;
            if (value is string) return new JsValue((string)value);
            if (value is bool) return new JsValue((bool)value);
            if (value is double) return new JsValue((double)value);
            if (value is int) return new JsValue((int)value);
            throw new InvalidOperationException("HDR JavaScript: host values must be string, boolean, double or null.");
        }
        private object ToHost(JsValue value, bool allowCallback)
        {
            if (value.IsNull() || value.IsUndefined()) return null;
            if (value.IsString()) return value.AsString();
            if (value.IsBoolean()) return value.AsBoolean();
            if (value.IsNumber()) return value.AsNumber();
            if (allowCallback && value.TryCast<ICallable>() != null) {
                for (var i=0;i<_callbacks.Count;i++) if (!_callbacks[i].Current) { _callbacks.RemoveAt(i--); }
                if (_callbacks.Count >= _limits.MaxObjectProperties) throw new InvalidOperationException("JavaScript callback handle quota exceeded.");
                var handle=new JavaScriptCallback(this,value); _callbacks.Add(handle); return handle;
            }
            throw new InvalidOperationException("HDR JavaScript: objects and arrays cannot cross the host boundary; serialize explicit data with JSON.");
        }
        private static bool Identifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            for (var i=0;i<value.Length;i++) {
                var c=value[i];
                if (!(c=='_' || c=='$' || c>='A' && c<='Z' || c>='a' && c<='z' || i>0 && c>='0' && c<='9')) return false;
            }
            return true;
        }
        public void Dispose()
        {
            if (_busy) throw new InvalidOperationException("Cannot dispose JavaScript while a callback is running.");
            for (var i=0;i<_callbacks.Count;i++) _callbacks[i].Dispose();
            _callbacks.Clear(); _hostNames.Clear(); _engine=null;
        }
    }
}
