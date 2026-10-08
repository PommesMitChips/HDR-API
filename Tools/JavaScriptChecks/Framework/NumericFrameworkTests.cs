using System;
using System.IO;
using HDRJavaScriptRuntime;

// Independent CLR4 CPU probe. Uses the real port; never run in the analyzer host.
internal static class NumericFrameworkTests
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    public static int Main(string[] args)
    {
        try
        {
            using (var realm = new JavaScriptRealm(new JavaScriptLimits { TimeoutMilliseconds = 1000 }))
            {
                realm.Execute("function decimal(){return Number('1e400');}function invalid(){return Number('notnumber');}function hexadecimal(){return Number('0xFFFFFFFF');}function json(){return JSON.parse('1e400');}function literal(){return 1e400;}function negativeZero(){return 1/(-1e-400);}function negativeHuge(){return parseFloat('-1e1000000000');}function huge(){return parseFloat('1e2147483647');}function tiny(){return Number('1e-400');}function tinySigned(){return 1/Number('-1e-400');}");
                Check(double.IsPositiveInfinity((double)realm.InvokeNamed("decimal")), "CLR4 decimal overflow follows JS Infinity");
                Check(double.IsNaN((double)realm.InvokeNamed("invalid")), "CLR4 invalid numeric string follows NaN");
                Check((double)realm.InvokeNamed("hexadecimal") == 4294967295.0, "CLR4 unsigned hex preserved");
                Check(double.IsPositiveInfinity((double)realm.InvokeNamed("json")), "CLR4 JSON overflow follows Infinity");
                Check(double.IsPositiveInfinity((double)realm.InvokeNamed("literal")), "CLR4 literal overflow follows Infinity");
                Check(double.IsNegativeInfinity((double)realm.InvokeNamed("negativeZero")), "CLR4 literal negative underflow retains sign");
                Check(double.IsNegativeInfinity((double)realm.InvokeNamed("negativeHuge")), "CLR4 negative billion exponent terminates with Infinity");
                Check(double.IsPositiveInfinity((double)realm.InvokeNamed("huge")), "CLR4 max Int32 exponent terminates with Infinity");
                Check((double)realm.InvokeNamed("tiny") == 0, "CLR4 positive underflow is zero");
                Check(double.IsNegativeInfinity((double)realm.InvokeNamed("tinySigned")), "CLR4 string underflow retains negative-zero sign");
            }
            Check(typeof(object).Assembly.FullName.StartsWith("mscorlib, Version=4.0.0.0,"), "probe executes on actual CLR4 framework");
            File.WriteAllText(args[0], "{\"Passed\":true,\"Assertions\":" + assertions + ",\"Cases\":1,\"Failures\":0,\"GameContextInitialized\":false,\"PluginRequired\":false}");
            Console.WriteLine("CLR4 JavaScript numeric checks: " + assertions + " assertions, zero failures.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
