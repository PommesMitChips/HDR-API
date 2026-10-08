using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

// CPU test harness only. Never part of the mod/analyzer candidate source set.
internal static class RuntimeTests
{
    private static int assertions;
    private static readonly List<string> cases = new List<string>();
    private static readonly List<string> failures = new List<string>();

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private static Engine Limited()
    {
        return new Engine(options => options.MaxStatements(10000).LimitRecursion(32).TimeoutInterval(TimeSpan.FromSeconds(1)));
    }

    private static void Number(Engine engine, string expression, double expected)
    {
        engine.Execute("var __fixture = (" + expression + ");");
        Check(engine.GetValue("__fixture").IsNumber(), expression + " returns a number");
        Check(Math.Abs(engine.GetValue("__fixture").AsNumber() - expected) < 0.000001, expression + " has expected value");
    }

    private static void Text(Engine engine, string expression, string expected)
    {
        engine.Execute("var __fixture = (" + expression + ");");
        Check(engine.GetValue("__fixture").IsString(), expression + " returns a string");
        Check(engine.GetValue("__fixture").AsString() == expected, expression + " has expected text");
    }

    private static void Case(string name, Action body)
    {
        cases.Add(name);
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures.Add(name); Console.Error.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void Language()
    {
        var engine = Limited();
        Number(engine, "1 + 2 * 3", 7);
        Number(engine, "Math.max(3, 9, 4)", 9);
        Number(engine, "parseInt('2a', 16)", 42);
        Number(engine, "Math.floor(7.9)", 7);
        Number(engine, "(true ? 11 : 99)", 11);
        Text(engine, "'camera-' + (1 + 1)", "camera-2");
        Text(engine, "'Sphere'.toLowerCase()", "sphere");
        engine.Execute("var count = 0; for (var i = 0; i < 10; i++) { if (i === 5) continue; count += i; }");
        Check(engine.GetValue("count").AsNumber() == 40, "for/continue executes actual statements");
        engine.Execute("var n = 0; while (n < 4) { n++; } do { n--; } while(n > 2);");
        Check(engine.GetValue("n").AsNumber() == 2, "while/do loops execute");
        engine.Execute("var kind = ''; switch(n) { case 2: kind = 'two'; break; default: kind = 'other'; }");
        Check(engine.GetValue("kind").AsString() == "two", "switch semantics");
    }

    private static void ObjectsAndJson()
    {
        var engine = Limited();
        engine.Execute("var values = [1, 2, 3]; values.push(4); var mapped = values.map(function(x) { return x * x; });");
        Text(engine, "mapped.join(',')", "1,4,9,16");
        Number(engine, "values.length", 4);
        Number(engine, "values.reduce(function(total,x){return total+x;},0)", 10);
        engine.Execute("var state = JSON.parse('{\"camera\":2,\"label\":\"aft\"}'); state.enabled = true;");
        Number(engine, "state.camera", 2);
        Text(engine, "state.label", "aft");
        Text(engine, "JSON.stringify([state.camera,state.label,state.enabled])", "[2,\"aft\",true]");
        engine.Execute("function Thing(value) { this.value = value; } Thing.prototype.read = function() { return this.value; }; var thing = new Thing(23);");
        Number(engine, "thing.read()", 23);
        Text(engine, "Object.keys({a:1,b:2}).sort().join(',')", "a,b");
    }

    private static void ClosuresAndTypedHost()
    {
        var engine = Limited();
        string lastText = null;
        string lastProvider = null;
        string lastSource = null;
        var callbackCount = 0;
        engine.SetValue("setText", new ClrFunctionInstance(engine, (self, args) => {
            Check(args.Length == 2 && args[0].IsString() && args[1].IsString(), "typed text callback receives JS primitives");
            Check(args[0].AsString() == "status", "typed text callback receives node ID");
            lastText = args[1].AsString();
            callbackCount++;
            return JsValue.Undefined;
        }));
        engine.SetValue("attachSource", new ClrFunctionInstance(engine, (self, args) => {
            Check(args.Length == 3 && args[0].IsString() && args[1].IsString() && args[2].IsString(), "typed source callback arguments");
            Check(args[0].AsString() == "pov", "typed source callback receives container ID");
            lastProvider = args[1].AsString();
            lastSource = args[2].AsString();
            return JsValue.True;
        }));
        engine.Execute("function makeSwitcher() { var selected = 0; return function(delta) { selected = (selected + delta + 2) % 2; setText('status', 'Camera ' + (selected + 1)); return attachSource('pov','camera-panorama',selected === 0 ? 'camera-a' : 'camera-b'); }; } var next = makeSwitcher(); var click = next(1);");
        Check(lastText == "Camera 2", "JS closure changes text through typed callback");
        Check(lastProvider == "camera-panorama" && lastSource == "camera-b", "JS closure switches camera source");
        Check(engine.GetValue("click").AsBoolean(), "host result returns to JS");
        engine.Execute("next(1);");
        Check(lastText == "Camera 1" && lastSource == "camera-a", "closure persists between event dispatches");
        Check(callbackCount == 2, "exactly two text callback invocations");
        Number(engine, "(function(x){return function(y){return x+y;};})(5)(7)", 12);
        Text(engine, "typeof setText.GetType", "undefined");
    }

    private static void StatementLimits()
    {
        foreach (var script in new[] { "while(true) {}", "for (;;) { var x = 1; }", "var x=0; do { x++; } while(true);" })
        {
            var engine = new Engine(options => options.MaxStatements(100).LimitRecursion(16).TimeoutInterval(TimeSpan.FromSeconds(1)));
            var timer = Stopwatch.StartNew();
            bool stopped = false;
            try { engine.Execute(script); }
            catch (StatementsCountOverflowException) { stopped = true; }
            Check(stopped, "statement budget stops " + script);
            Check(timer.Elapsed < TimeSpan.FromSeconds(1), "statement budget is prompt");
            engine.Execute("var recovered = 42;");
            Check(engine.GetValue("recovered").AsNumber() == 42, "bounded failure does not corrupt subsequent evaluation");
        }
    }

    private static void TimeoutAndRecursion()
    {
        var engine = new Engine(options => options.MaxStatements(0).LimitRecursion(16).TimeoutInterval(TimeSpan.FromMilliseconds(20)));
        engine.HdrMaxOperations = 0;
        var timer = Stopwatch.StartNew();
        bool timedOut = false;
        try { engine.Execute("while(true) { }"); }
        catch (InvalidOperationException error) { timedOut = error.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0; }
        Check(timedOut, "wall-clock timeout stops loop when statement budget is disabled");
        Check(timer.Elapsed < TimeSpan.FromSeconds(2), "interpreter timeout is independently bounded");
        engine = new Engine(options => options.MaxStatements(10000).LimitRecursion(8).TimeoutInterval(TimeSpan.FromSeconds(1)));
        bool recursionStopped = false;
        try { engine.Execute("function recurse(){return recurse();} recurse();"); }
        catch (RecursionDepthOverflowException) { recursionStopped = true; }
        catch (InvalidOperationException error) { recursionStopped = error.Message.Contains("depth limit"); }
        Check(recursionStopped, "recursion guard fires before CLR stack overflow");
        var fresh = Limited();
        Number(fresh, "(function factorial(x){return x<=1?1:x*factorial(x-1);})(5)", 120);
    }

    private static void ErrorsAndIsolation()
    {
        var first = Limited();
        var second = Limited();
        first.Execute("var privateCounter = 11; var caught = ''; try { throw new Error('expected'); } catch(e) { caught = e.message; }");
        Check(first.GetValue("caught").AsString() == "expected", "JS try/catch retains errors");
        Text(second, "typeof privateCounter", "undefined");
        bool scriptError = false;
        try { first.Execute("throw new Error('isolated');"); }
        catch (JavaScriptException) { scriptError = true; }
        Check(scriptError, "unhandled JS error reaches host boundary");
        Number(second, "6 * 7", 42);
        first.SetValue("hostFailure", new ClrFunctionInstance(first, (self, args) => { throw new InvalidOperationException("expected host failure"); }));
        bool hostError = false;
        try { first.Execute("hostFailure();"); }
        catch (InvalidOperationException error) { hostError = error.Message == "expected host failure"; }
        Check(hostError, "typed host callback failure reaches explicit host boundary");
        first.Execute("privateCounter += 1;");
        Check(first.GetValue("privateCounter").AsNumber() == 12, "host error preserves interpreter state");
    }

    private static void NoAmbientClr()
    {
        var engine = Limited();
        Text(engine, "typeof System", "undefined");
        Text(engine, "typeof importNamespace", "undefined");
        Text(engine, "typeof clr", "undefined");
        Text(engine, "typeof require", "undefined");
        Text(engine, "typeof fetch", "undefined");
        Text(engine, "typeof window", "undefined");
    }

    private static void NumbersAtBounds()
    {
        var engine = Limited();
        engine.Execute("var huge=Number('1e400'); var invalid=Number('notnumber'); var hex=Number('0xFFFFFFFF'); var parsed=JSON.parse('1e400'); var literal=1e400; var signed=1/(-1e-400);");
        Check(double.IsPositiveInfinity(engine.GetValue("huge").AsNumber()), "overflowing numeric string follows JS Infinity");
        Check(double.IsNaN(engine.GetValue("invalid").AsNumber()), "invalid numeric string follows JS NaN");
        Check(engine.GetValue("hex").AsNumber() == 4294967295.0, "large unsigned hexadecimal value preserved");
        Check(double.IsPositiveInfinity(engine.GetValue("parsed").AsNumber()), "JSON numeric overflow follows JS Infinity");
        Check(double.IsPositiveInfinity(engine.GetValue("literal").AsNumber()), "numeric literal overflow follows JS Infinity");
        Check(double.IsNegativeInfinity(engine.GetValue("signed").AsNumber()), "underflow retains negative-zero sign");
    }

    public static int Main(string[] args)
    {
        try
        {
            Case("actual JavaScript arithmetic and control flow", Language);
            Case("arrays objects prototypes and JSON", ObjectsAndJson);
            Case("closures and deliberate typed host callbacks", ClosuresAndTypedHost);
            Case("finite statement limits and recovery", StatementLimits);
            Case("timeout and recursion limits", TimeoutAndRecursion);
            Case("script host errors and realm isolation", ErrorsAndIsolation);
            Case("no ambient CLR or browser services", NoAmbientClr);
            Case("numeric overflow underflow and unsigned hex", NumbersAtBounds);
            DocumentTests.Run(Check, Case);
            RealmTests.Run(Check, Case);
            HtmlAdapterTests.Run(Check, Case);
            ServiceTests.Run(Check, Case);
            if (args.Length != 1) throw new ArgumentException("Runtime receipt output path required.");
            string json = "{\"Passed\":" + (failures.Count == 0 ? "true" : "false") + ",\"Assertions\":" + assertions + ",\"Cases\":" + cases.Count + ",\"Failures\":" + failures.Count + ",\"GameContextInitialized\":false,\"PluginRequired\":false}";
            File.WriteAllText(args[0], json);
            Console.WriteLine("JavaScript runtime checks: " + assertions + " assertions, " + cases.Count + " cases, " + failures.Count + " failures.");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
