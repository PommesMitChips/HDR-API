using System;
using System.Collections.Generic;
using HDRJavaScriptRuntime;

internal static class RealmTests
{
    private static Action<bool, string> check;

    public static void Run(Action<bool, string> assert, Action<string, Action> runCase)
    {
        check = assert;
        runCase("typed realm values callbacks and revocation", TypedRealm);
        runCase("realm workload and allocation escape guards", BuiltinLimits);
        runCase("realm profile omissions and bounded parser", ProfileAndParsing);
        runCase("realm source limits host-call limits and reentrancy", HostAndLifecycle);
        runCase("native parsing and sparse scan work quotas", NativeWork);
        runCase("flat expressions bound functions and binding quotas", StructuralDepths);
        runCase("native replacement expansion and call argument guards", NativeGrowth);
        runCase("realm catch and call-depth state recovers after abort", AbortRecovery);
        runCase("single deadline includes parser work before execution", ParseDeadline);
    }

    private static JavaScriptLimits Limits()
    { return new JavaScriptLimits { TimeoutMilliseconds = 1000 }; }

    private static void Reject(Action action, string fragment)
    {
        bool rejected = false;
        string actual = "no exception";
        try { action(); }
        catch (Exception error) { actual = error.Message; rejected = error.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0; }
        check(rejected, "expected explicit limit/profile error containing " + fragment + "; actual: " + actual);
    }

    private static void TypedRealm()
    {
        var limits = Limits();
        var realm = new JavaScriptRealm(limits);
        limits.MaxStatements = 1;
        check(realm.Limits.MaxStatements == 50000, "realm copies caller limit configuration");
        realm.Execute("function add(a,b){return a+b;} function serialize(){return JSON.stringify({camera:2,enabled:true});}");
        check((double)realm.InvokeNamed("add", 20.0, 22.0) == 42, "typed invocation executes real JS");
        check((string)realm.InvokeNamed("serialize") == "{\"camera\":2,\"enabled\":true}", "complex JS data crosses host as explicit JSON");
        object[] received = null;
        realm.RegisterHostFunction("capture", args => { received = args; return null; });
        realm.Execute("capture('a',true,3,null,function(x){return x+1;});");
        check(received.Length == 5 && (string)received[0] == "a" && (bool)received[1] && (double)received[2] == 3 && received[3] == null, "host receives scalar values deliberately");
        var callback = received[4] as JavaScriptCallback;
        check(callback != null && callback.Current && callback.IsOwnedBy(realm), "function crosses as opaque realm-owned callback");
        check((double)callback.Invoke(41.0) == 42, "opaque callback executes actual closure");
        var other = new JavaScriptRealm(Limits());
        check(!callback.IsOwnedBy(other), "callback cannot claim foreign realm ownership");
        callback.Dispose();
        check(!callback.Current && !callback.IsOwnedBy(realm), "explicit callback disposal revokes ownership");
        Reject(() => callback.Invoke(1.0), "revoked");
        realm.Execute("capture(function(){return 'current';});");
        var retained = (JavaScriptCallback)received[0];
        realm.Dispose(); realm.Dispose();
        check(!realm.Current && !retained.Current, "realm disposal revokes all retained callbacks idempotently");
        Reject(() => realm.Execute("var x=1;"), "disposed");
        other.Dispose();
    }

    private static void BuiltinLimits()
    {
        var limits = Limits(); limits.MaxArrayLength = 16;
        using (var realm = new JavaScriptRealm(limits))
        {
            Reject(() => realm.Execute("var tooLarge=new Array(17);"), "array");
            realm.Execute("function valid(){return [1,2,3].join(',');}");
            check((string)realm.InvokeNamed("valid") == "1,2,3", "bounded failure preserves subsequent valid JS");
        }
        limits = Limits(); limits.MaxStringCharacters = 24;
        using (var realm = new JavaScriptRealm(limits))
        {
            Reject(() => realm.Execute("var s='abcdefghijklmnop'; s=s+s;"), "string");
            Reject(() => realm.Execute("['12345678','12345678','12345678','12345678'].join('');"), "string");
            Reject(() => realm.Execute("JSON.stringify(['12345678','12345678','12345678']);"), "string");
        }
        limits = Limits(); limits.MaxObjectProperties = 128;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("var o={};for(var i=0;i<129;i++){o['p'+i]=i;}"), "property");
        limits = Limits(); limits.MaxAllocationUnitsPerOperation = 512;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("var a=[]; for(var i=0;i<20;i++){a.push({x:i});}"), "allocation");
        limits = Limits(); limits.MaxOperations = 40; limits.MaxStatements = 0;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("var a=[5,4,3,2,1];for(var i=0;i<100;i++){a.sort(function(x,y){return x-y;});}"), "operation");
        limits = Limits(); limits.MaxCreatedObjects = 256;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("for(var i=0;i<300;i++){var o={x:i};}"), "object");
    }

    private static void ProfileAndParsing()
    {
        using (var realm = new JavaScriptRealm(Limits()))
        {
            Reject(() => realm.Execute("eval('1+1');"), "unavailable");
            Reject(() => realm.Execute("new Function('return 1;');"), "unavailable");
            Reject(() => realm.Execute("new RegExp('a');"), "unavailable");
            Reject(() => realm.Execute("/a/.test('a');"), "unavailable");
            Reject(() => realm.Execute("new Date();"), "not in this profile");
            realm.Execute("function healthy(){return JSON.parse('{\"x\":42}').x;}");
            check((double)realm.InvokeNamed("healthy") == 42, "supported JSON remains functional after profile rejections");
        }
        var limits = Limits(); limits.MaxParseDepth = 8;
        using (var realm = new JavaScriptRealm(limits))
        {
            Reject(() => realm.Execute("var deep=" + new string('(', 30) + "1" + new string(')', 30) + ";"), "depth");
            Reject(() => realm.Execute("JSON.parse('" + new string('[', 30) + "1" + new string(']', 30) + "');"), "depth");
        }
    }

    private static void HostAndLifecycle()
    {
        var limits = Limits(); limits.MaxSourceCharacters = 32;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute(new string(' ', 33)), "source");
        limits = Limits(); limits.MaxHostCallsPerOperation = 2;
        using (var realm = new JavaScriptRealm(limits))
        {
            var calls = 0;
            realm.RegisterHostFunction("host", args => { calls++; return null; });
            Reject(() => realm.Execute("host();host();host();"), "host callback");
            check(calls == 2, "third callback rejected before host side effect");
            realm.Execute("host();");
            check(calls == 3, "host-call quota resets for next operation");
        }
        using (var realm = new JavaScriptRealm(Limits()))
        {
            realm.RegisterHostFunction("reenter", args => { realm.Execute("var forbidden=1;"); return null; });
            Reject(() => realm.Execute("reenter();"), "Nested");
            realm.Execute("function recovered(){return 42;}");
            check((double)realm.InvokeNamed("recovered") == 42, "reentrancy rejection restores realm state");
            realm.RegisterHostFunction("objectHost", args => new Dictionary<string, string>());
            Reject(() => realm.Execute("objectHost();"), "host values");
            realm.RegisterHostFunction("scalarHost", args => null);
            Reject(() => realm.Execute("scalarHost({danger:1});"), "objects and arrays");
        }
    }

    private static void NativeWork()
    {
        using (var realm = new JavaScriptRealm(Limits()))
        {
            realm.Execute("function badPrefix(){return parseInt(Array(8192).join('x'),10);}function hugeExponent(){return parseFloat('1e1000000000');}function maxExponent(){return parseFloat('1e2147483647');}");
            check(double.IsNaN((double)realm.InvokeNamed("badPrefix")), "invalid long parseInt prefix terminates with NaN");
            check(double.IsPositiveInfinity((double)realm.InvokeNamed("hugeExponent")), "billion exponent parseFloat is bounded and returns Infinity");
            check(double.IsPositiveInfinity((double)realm.InvokeNamed("maxExponent")), "max Int32 exponent parseFloat is bounded and returns Infinity");
        }
        var limits = Limits(); limits.MaxOperations = 512;
        using (var realm = new JavaScriptRealm(limits))
        {
            bool entered = false;
            realm.RegisterHostFunction("entered", values => { entered = true; return null; });
            realm.Execute("function scan(){entered();return Array.prototype.indexOf.call({length:1024},1);}");
            Reject(() => realm.InvokeNamed("scan"), "operation");
            check(entered, "tiny native scan quota is tested after builtin entry, not merely during parsing");
        }
        limits = Limits(); limits.MaxOperations = 512;
        using (var realm = new JavaScriptRealm(limits))
        {
            bool entered = false;
            realm.RegisterHostFunction("entered", values => { entered = true; return null; });
            realm.Execute("function digits(s){entered();return parseInt(s,10);}");
            Reject(() => realm.InvokeNamed("digits", new string('9', 1024)), "operation");
            check(entered, "native digit parse quota applies within named invocation");
        }
    }

    private static void StructuralDepths()
    {
        var limits = Limits(); limits.MaxParseDepth = 32;
        using (var realm = new JavaScriptRealm(limits))
        {
            string expression = "1"; for (int i = 0; i < 80; i++) expression += "+1";
            Reject(() => realm.Execute("var result=" + expression + ";"), "depth");
        }
        limits = Limits(); limits.MaxRecursionDepth = 8;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("var f=function(){return 1;};for(var i=0;i<20;i++){f=f.bind(null);}f();"), "depth");
        limits = Limits(); limits.MaxObjectProperties = 128;
        using (var realm = new JavaScriptRealm(limits))
        {
            string locals = "function many(){";
            for (int i = 0; i < 129; i++) locals += "var p" + i + "=1;";
            Reject(() => realm.Execute(locals + "return 1;}many();"), "lexical binding");
        }
        using (var realm = new JavaScriptRealm(Limits()))
        {
            realm.Execute("function prototypes(){var a={v:42};for(var i=0;i<100;i++){a=Object.create(a);}return a.v;}");
            check((double)realm.InvokeNamed("prototypes") == 42, "iterative prototype lookup handles safe long chains without CLR recursion");
        }
    }

    private static void NativeGrowth()
    {
        var limits = Limits(); limits.MaxStringCharacters = 24;
        using (var realm = new JavaScriptRealm(limits))
            Reject(() => realm.Execute("'aaaaaaaaaaaaaaaa'.replace('a',\"$'$'$'\");"), "string");
        limits = Limits(); limits.MaxArrayLength = 16;
        using (var realm = new JavaScriptRealm(limits))
        {
            string arguments = "1"; for (int i = 1; i < 17; i++) arguments += ",1";
            Reject(() => realm.Execute("function many(){return 1;}many(" + arguments + ");"), "array length");
            Reject(() => realm.Execute("function Many(){}new Many(" + arguments + ");"), "array length");
        }
        using (var realm = new JavaScriptRealm(Limits()))
            Reject(() => realm.Execute("/a/" + new string('x', 10000) + ";"), "unavailable");
    }

    private static void AbortRecovery()
    {
        var limits = Limits(); limits.MaxStatements = 100;
        using (var realm = new JavaScriptRealm(limits))
        {
            Reject(() => realm.Execute("var outer='saved';try{throw 'error';}catch(e){while(true){}}"), "statements");
            realm.Execute("function scope(){return typeof e;}function previous(){return outer;}");
            check((string)realm.InvokeNamed("scope") == "undefined", "quota abort restores catch lexical scope");
            check((string)realm.InvokeNamed("previous") == "saved", "quota abort preserves outer environment");
        }
        using (var realm = new JavaScriptRealm(Limits()))
        {
            realm.Execute("function fail(){throw new Error('caught');}for(var i=0;i<100;i++){try{fail();}catch(e){}}function recovered(){return 42;}");
            check((double)realm.InvokeNamed("recovered") == 42, "sequential caught JS exceptions do not leak call-depth stack");
        }
    }

    private static void ParseDeadline()
    {
        var limits = Limits(); limits.MaxOperations = 0; limits.MaxStatements = 0; limits.TimeoutMilliseconds = 1;
        using (var realm = new JavaScriptRealm(limits))
        {
            bool entered = false;
            realm.RegisterHostFunction("mark", values => { entered = true; return null; });
            Reject(() => realm.Execute("/*" + new string('x', 60000) + "*/mark();"), "timeout");
            check(!entered, "large comment scanning consumes the same operation deadline before script/host execution");
        }
    }
}
