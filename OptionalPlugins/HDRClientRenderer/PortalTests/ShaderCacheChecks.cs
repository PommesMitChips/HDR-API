using System.Text;
using System.Threading;
using HDRClientRenderer;
internal static class ShaderCacheChecks
{
    static PortalShaderBytecodeCache.Pair Pair(byte marker=7)
    {var bytes=new byte[128];Encoding.ASCII.GetBytes("DXBC").CopyTo(bytes,0);bytes[4]=marker;return new PortalShaderBytecodeCache.Pair(bytes,bytes);}
    static void Validate(PortalShaderBytecodeCache.Pair pair)
    {if(pair.Original[4]!=7||pair.Clipped[4]!=7)throw new InvalidOperationException("hostile reflected ABI fixture");}
    internal static void Installed(PortalClipShaders compiler,Action<bool,string> check)
    {
        int owner=Environment.CurrentManagedThreadId;var first=compiler.Prepare(new PortalClipDescriptor(null,"Transparent/Billboards.hlsl","parallel-installed",null));
        var second=compiler.Prepare(new PortalClipDescriptor(null,"Foliage/Foliage.hlsl","parallel-installed",null));byte[][] results=new byte[2][];Exception failure=null;bool cpuOnly=true;
        using(var ready=new CountdownEvent(2))using(var release=new ManualResetEventSlim())using(var completed=new CountdownEvent(2))
        {
            for(int index=0;index<2;index++){int slot=index;ThreadPool.QueueUserWorkItem(_=>{try{if(Environment.CurrentManagedThreadId==owner)cpuOnly=false;ready.Signal();if(!release.Wait(5000))throw new Exception("Installed compile fixture timeout");results[slot]=compiler.CompilePrepared(slot==0?first:second);}catch(Exception ex){Interlocked.CompareExchange(ref failure,ex,null);}finally{completed.Signal();}});}
            check(ready.Wait(5000),"Two installed compiler jobs receive frozen inputs on separate CPU lanes");release.Set();if(!completed.Wait(60000))throw new Exception("Installed parallel compiler fixture timed out");
            if(failure!=null)throw failure;check(cpuOnly&&results.All(b=>b!=null&&Encoding.ASCII.GetString(b,0,4)=="DXBC"),"Installed native compiler/reflection is CPU-only for two simultaneous sealed-source jobs");
            check(results[0].SequenceEqual(compiler.CompilePrepared(first))&&results[1].SequenceEqual(compiler.CompilePrepared(second)),"Parallel installed variants match reflected persisted cache reloads");
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        string temporaryRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string directory=Path.GetFullPath(Path.Combine(temporaryRoot,"HDRClientRenderer-PortalTests-"+Guid.NewGuid().ToString("N")));
        if(!directory.StartsWith(temporaryRoot,StringComparison.OrdinalIgnoreCase))throw new Exception("Cache fixture must stay in the temporary directory.");
        try
        {
            Directory.CreateDirectory(directory);
            var cache=new PortalShaderBytecodeCache(directory);string key=PortalShaderBytecodeCache.Key("source","family","compiler","engine","assets");int builds=0;
            Func<PortalShaderBytecodeCache.Pair> build=()=>{Interlocked.Increment(ref builds);return Pair();};
            var first=cache.GetOrCompile(key,build,Validate);first.Clipped[4]=99;
            var restarted=new PortalShaderBytecodeCache(directory).GetOrCompile(key,build,Validate);
            check(builds==1&&restarted.Clipped[4]==7,"DPAPI cache survives owner recreation and returns private cloned bytecode");
            string file=Path.Combine(directory,key+".pcache");byte[] protectedBytes=File.ReadAllBytes(file);
            check(Encoding.ASCII.GetString(protectedBytes,0,4)!="DXBC","Persistent shader artifacts contain DPAPI envelopes instead of writable raw DXBC");
            var distinct=new HashSet<string>();foreach(string changed in new[]{"source","family","compiler","engine","assets","injection"})distinct.Add(PortalShaderBytecodeCache.Key(key,changed));
            check(distinct.Count==6&&!distinct.Contains(key),"Effective source, family, compiler, engine, assets and injection identities produce distinct sealed keys");
            protectedBytes[protectedBytes.Length/2]^=0x55;File.WriteAllBytes(file,protectedBytes);cache.GetOrCompile(key,build,Validate);
            check(builds==2,"Corrupted DPAPI envelope rebuilds trusted source instead of publishing cached bytecode");
            File.WriteAllBytes(file,Encoding.ASCII.GetBytes("DXBC raw writable malicious shader"));cache.GetOrCompile(key,build,Validate);
            check(builds==3,"Raw local DXBC is never accepted as a cache hit");
            File.WriteAllBytes(file,new byte[]{1,2,3});cache.GetOrCompile(key,build,Validate);check(builds==4,"Truncated cache entry is a rebuildable miss");
            string otherKey=PortalShaderBytecodeCache.Key("different effective source");File.Copy(file,Path.Combine(directory,otherKey+".pcache"));cache.GetOrCompile(otherKey,build,Validate);
            check(builds==5,"DPAPI source-key entropy rejects swapping a valid entry under another source key");
            string hostileKey=PortalShaderBytecodeCache.Key("reflection reject");cache.GetOrCompile(hostileKey,()=>Pair(9),p=>{});cache.GetOrCompile(hostileKey,build,Validate);
            check(builds==6,"A valid protected envelope still must pass current reflection and output checks");
            using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var completed=new CountdownEvent(2))
            {
                string concurrent=PortalShaderBytecodeCache.Key("dedup");int concurrentBuilds=0;Exception failure=null;
                Func<PortalShaderBytecodeCache.Pair> slow=()=>{Interlocked.Increment(ref concurrentBuilds);started.Set();if(!release.Wait(5000))throw new Exception("Cache fixture timeout");return Pair();};
                for(int i=0;i<2;i++)ThreadPool.QueueUserWorkItem(_=>{try{cache.GetOrCompile(concurrent,slow,Validate);}catch(Exception ex){failure=ex;}finally{completed.Signal();}});
                check(started.Wait(5000),"Same-source cache fixture starts an actual CPU compilation");release.Set();check(completed.Wait(5000)&&failure==null&&concurrentBuilds==1,"Concurrent same-source CPU jobs compile once and reuse validated protected bytes");
            }
            string quota=Path.Combine(directory,"quota");var small=new PortalShaderBytecodeCache(quota,2,4096);
            for(int i=0;i<5;i++)small.GetOrCompile(PortalShaderBytecodeCache.Key("quota",i.ToString()),build,Validate);
            var files=new DirectoryInfo(quota).GetFiles("*.pcache");check(files.Length<=2&&files.Sum(f=>f.Length)<=4096,"Persistent cache evicts to both entry and byte quotas");
            string blocked=Path.Combine(directory,"not-a-directory");File.WriteAllText(blocked,"fixture");int fallback=0;
            var noDisk=new PortalShaderBytecodeCache(blocked);noDisk.GetOrCompile(key,()=>{fallback++;return Pair();},Validate);noDisk.GetOrCompile(key,()=>{fallback++;return Pair();},Validate);
            check(fallback==2,"Unwritable cache location preserves successful trusted compilation");
            var tooSmall=new PortalShaderBytecodeCache(Path.Combine(directory,"too-small"),1,1);tooSmall.GetOrCompile(key,build,Validate);
            check(!Directory.Exists(Path.Combine(directory,"too-small")),"Oversize artifacts cannot create cache files beyond the byte budget");
        }
        finally{if(directory.StartsWith(temporaryRoot,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
