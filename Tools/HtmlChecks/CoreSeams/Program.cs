using System.Reflection;
using System.Runtime.Loader;
internal static class Program
{
    static void Main()
    {
        string game=Environment.GetEnvironmentVariable("SE_BIN")??@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{string path=Path.Combine(game,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        int assertions=CoreSeamTests.Run();Console.WriteLine("PASS "+assertions+" core seam assertions");
    }
}
