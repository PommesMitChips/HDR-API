using System;
using System.Collections.Generic;
using VRage;

namespace HDRJavaScriptRuntime
{
    public sealed partial class JavaScriptRuntimeSession
    {
        sealed class Arguments
        {
            readonly object[] values; int offset;
            public Arguments(object[] args) { values = args ?? new object[0]; if (values.Length > 12) throw new ArgumentException("Too many JavaScript command arguments."); }
            public bool Has { get { return offset < values.Length; } }
            object Next() { if (!Has) throw new ArgumentException("Missing JavaScript command argument."); return values[offset++]; }
            public string Text() { var value = Next() as string; if (value == null) throw new ArgumentException("JavaScript command requires a string."); return value; }
            public long Long() { var value = Next(); if (!(value is long)) throw new ArgumentException("JavaScript handle/generation requires Int64."); return (long)value; }
            public bool Flag() { var value = Next(); if (!(value is bool)) throw new ArgumentException("JavaScript command requires Boolean."); return (bool)value; }
            public double Number() { var value = Next(); double result; if (value is double) result = (double)value; else if (value is int) result = (int)value; else throw new ArgumentException("JavaScript size requires a finite number."); if (!Finite(result)) throw new ArgumentException("JavaScript size requires a finite number."); return result; }
            public string[] Strings() { var value = Next() as string[]; if (value == null) throw new ArgumentException("JavaScript declarations require string[]."); return (string[])value.Clone(); }
            public Func<string, object[], object> Endpoint() { var value = Next() as Func<string, object[], object>; if (value == null) throw new ArgumentException("HTML binding requires the actual caller-owned endpoint capability."); return value; }
            public Func<bool> Witness() { var value = Next() as Func<bool>; if (value == null) throw new ArgumentException("HTML binding requires its actual owner-generation witness."); return value; }
            public Func<MyTuple<bool, string>> Probe() { var value = Next() as Func<MyTuple<bool, string>>; if (value == null) throw new ArgumentException("Source choice requires an actual capability probe."); return value; }
            public MyTuple<string, object[]>[] Settings() { var value = Next() as MyTuple<string, object[]>[]; if (value == null) throw new ArgumentException("JavaScript settings require MyTuple<string,object[]>[]."); return value; }
            public object[] Scalars()
            {
                var args = Next() as object[]; if (args == null) throw new ArgumentException("JavaScript function arguments require object[].");
                var copy = (object[])args.Clone();
                foreach (var value in copy)
                    if (value != null && !(value is string) && !(value is bool) && !(value is int) && !(value is double))
                        throw new ArgumentException("JavaScript values must be null, string, boolean, Int32 or Double; serialize structured data explicitly.");
                    else if (value is double && !Finite((double)value)) throw new ArgumentException("JavaScript numeric arguments must be finite.");
                return copy;
            }
            public void End() { if (Has) throw new ArgumentException("Unexpected JavaScript command argument."); }
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static int Integer(object[] args)
        { if (args == null || args.Length != 1 || !(args[0] is int)) throw new ArgumentException("JavaScript limit requires one Int32 value."); return (int)args[0]; }
        static long Allocation(object[] args)
        { if (args == null || args.Length != 1 || !(args[0] is long)) throw new ArgumentException("JavaScript allocation limit requires one Int64 value."); return (long)args[0]; }
        static double Duration(object[] args)
        { if (args == null || args.Length != 1 || !(args[0] is double) || !Finite((double)args[0])) throw new ArgumentException("JavaScript time limit requires one finite Double value."); return (double)args[0]; }
        static void Configure(Owner owner, MyTuple<string, object[]>[] settings)
        {
            if (settings == null || settings.Length > 32) throw new ArgumentException("JavaScript configuration allows at most 32 named settings.");
            var limits = owner.Limits.Copy(); var host = owner.HostLimits.Copy(); int maxRealms = owner.MaxRealms;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setting in settings)
            {
                string key = setting.Item1; object[] a = setting.Item2;
                if (key == null || !names.Add(key)) throw new ArgumentException("JavaScript settings require distinct known names.");
                switch (key)
                {
                    case "statements": limits.MaxStatements = Integer(a); break;
                    case "operations": limits.MaxOperations = Integer(a); break;
                    case "recursion": limits.MaxRecursionDepth = Integer(a); break;
                    case "source-characters": limits.MaxSourceCharacters = Integer(a); break;
                    case "parse-depth": limits.MaxParseDepth = Integer(a); break;
                    case "string-characters": limits.MaxStringCharacters = Integer(a); break;
                    case "array-length": limits.MaxArrayLength = Integer(a); break;
                    case "object-properties": limits.MaxObjectProperties = Integer(a); break;
                    case "created-objects": limits.MaxCreatedObjects = Integer(a); break;
                    case "allocation-operation": limits.MaxAllocationUnitsPerOperation = Allocation(a); break;
                    case "allocation-realm": limits.MaxAllocationUnitsPerRealm = Allocation(a); break;
                    case "host-calls": limits.MaxHostCallsPerOperation = Integer(a); break;
                    case "timeout-ms": limits.TimeoutMilliseconds = Duration(a); break;
                    case "callbacks-per-update": host.CallbacksPerUpdate = Integer(a); break;
                    case "pending-events": host.PendingEvents = Integer(a); break;
                    case "timers": host.Timers = Integer(a); break;
                    case "event-handlers": host.EventHandlers = Integer(a); break;
                    case "mutations-per-dispatch": host.MutationsPerDispatch = Integer(a); break;
                    case "text-characters": host.TextCharacters = Integer(a); break;
                    case "maximum-timer-delay-ms": host.MaximumTimerDelayMs = Duration(a); break;
                    case "max-realms": maxRealms = Integer(a); break;
                    default: throw new ArgumentException("Unknown JavaScript limit: " + key);
                }
            }
            limits.Validate(); host = host.Copy();
            if (maxRealms < 0) throw new ArgumentException("JavaScript max-realms requires zero (unlimited) or a positive count.");
            owner.Limits = limits; owner.HostLimits = host; owner.MaxRealms = maxRealms;
        }
        static MyTuple<string, object[]>[] Settings(JavaScriptLimits l, JavaScriptHostLimits h, int maxRealms)
        {
            return new[] {
                Setting("statements",l.MaxStatements), Setting("operations",l.MaxOperations),
                Setting("recursion",l.MaxRecursionDepth), Setting("source-characters",l.MaxSourceCharacters),
                Setting("parse-depth",l.MaxParseDepth), Setting("string-characters",l.MaxStringCharacters),
                Setting("array-length",l.MaxArrayLength), Setting("object-properties",l.MaxObjectProperties),
                Setting("created-objects",l.MaxCreatedObjects), Setting("allocation-operation",l.MaxAllocationUnitsPerOperation),
                Setting("allocation-realm",l.MaxAllocationUnitsPerRealm), Setting("host-calls",l.MaxHostCallsPerOperation),
                Setting("timeout-ms",l.TimeoutMilliseconds), Setting("callbacks-per-update",h.CallbacksPerUpdate),
                Setting("pending-events",h.PendingEvents), Setting("timers",h.Timers), Setting("event-handlers",h.EventHandlers),
                Setting("mutations-per-dispatch",h.MutationsPerDispatch), Setting("text-characters",h.TextCharacters),
                Setting("maximum-timer-delay-ms",h.MaximumTimerDelayMs), Setting("max-realms",maxRealms)
            };
        }
        static MyTuple<string, object[]> Setting(string name, object value)
        { return new MyTuple<string, object[]>(name, new[] { value }); }
    }
}
