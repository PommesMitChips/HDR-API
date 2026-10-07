using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace HDRClientRenderer
{
    // Rebuildable, current-user DPAPI artifacts. No plaintext DXBC is ever read
    // from disk. Every hit is also re-reflected against the sealed source key.
    internal sealed class PortalShaderBytecodeCache
    {
        internal const int MaxEntries=128,MaxBytes=64*1024*1024,MaxShaderBytes=4*1024*1024;
        internal const string AuditedShaderFingerprint="d5e71307bf45ddc01743603701153d534386012eedd9b0be005a9a7add66a2fb";
        internal sealed class Pair
        {
            internal readonly byte[] Original,Clipped;
            internal Pair(byte[] original,byte[] clipped){Original=(byte[])original.Clone();Clipped=(byte[])clipped.Clone();}
            internal Pair Clone(){return new Pair(Original,Clipped);}
        }
        readonly string directory;readonly int maxEntries,maxBytes;
        static readonly object disk=new object();static readonly object[] lanes=Enumerable.Range(0,32).Select(_=>new object()).ToArray();
        internal PortalShaderBytecodeCache(string path,int entries=MaxEntries,int bytes=MaxBytes)
        {directory=path;maxEntries=entries;maxBytes=bytes;if(entries<1||entries>MaxEntries||bytes<1||bytes>MaxBytes)throw new ArgumentOutOfRangeException("entries");}
        internal static string DefaultDirectory{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HDRClientRenderer","PortalShaderCache","v1");}}
        internal static string Hash(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
        internal static string Key(params string[] values)
        {using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8)){foreach(string value in values)writer.Write(value??"");writer.Flush();return Hash(stream.ToArray());}}
        internal Pair GetOrCompile(string key,Func<Pair> compile,Action<Pair> validate)
        {
            if(key==null||key.Length!=64||key.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new ArgumentException("Invalid sealed portal cache key.","key");
            lock(lanes[(key[0]*17+key[1])%lanes.Length])
            {
                Pair pair=Read(key);
                if(pair!=null){try{validate(pair);return pair.Clone();}catch{Remove(key);}}
                pair=compile();validate(pair);Write(key,pair);return pair.Clone();
            }
        }
        Pair Read(string key)
        {
            try
            {
                lock(disk)
                {
                    string path=Path.Combine(directory,key+".pcache");var info=new FileInfo(path);
                    if(!info.Exists||info.Length<1||info.Length>Math.Min(maxBytes,2L*MaxShaderBytes+16384))return null;
                    byte[] plain=Protect(File.ReadAllBytes(path),Encoding.ASCII.GetBytes(key),false);
                    if(plain.Length>2*MaxShaderBytes+256)return null;
                    using(var stream=new MemoryStream(plain,false))using(var reader=new BinaryReader(stream,Encoding.UTF8))
                    {
                        if(reader.ReadInt32()!=0x50434331||reader.ReadString()!=key)return null;
                        byte[] original=ReadShader(reader),clipped=ReadShader(reader);
                        if(stream.Position!=stream.Length)return null;return new Pair(original,clipped);
                    }
                }
            }
            catch{return null;}
        }
        static byte[] ReadShader(BinaryReader reader)
        {
            int length=reader.ReadInt32();if(length<4||length>MaxShaderBytes)throw new InvalidDataException("Portal cached shader quota.");
            string hash=reader.ReadString();if(hash.Length!=64)throw new InvalidDataException("Portal cache digest.");
            byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length||Hash(bytes)!=hash||Encoding.ASCII.GetString(bytes,0,4)!="DXBC")throw new InvalidDataException("Portal cache bytecode.");return bytes;
        }
        void Write(string key,Pair pair)
        {
            string temporary=null;
            try
            {
                if(pair.Original.Length<4||pair.Clipped.Length<4||pair.Original.Length>MaxShaderBytes||pair.Clipped.Length>MaxShaderBytes)return;
                byte[] plain;
                using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8))
                {writer.Write(0x50434331);writer.Write(key);foreach(byte[] bytes in new[]{pair.Original,pair.Clipped}){writer.Write(bytes.Length);writer.Write(Hash(bytes));writer.Write(bytes);}writer.Flush();plain=stream.ToArray();}
                byte[] encrypted=Protect(plain,Encoding.ASCII.GetBytes(key),true);if(encrypted.Length>maxBytes)return;
                lock(disk)
                {
                    Directory.CreateDirectory(directory);
                    var files=new DirectoryInfo(directory).EnumerateFiles("*.pcache").Take(MaxEntries*4+1).ToArray();if(files.Length>MaxEntries*4)return;
                    string target=Path.Combine(directory,key+".pcache");long resident=files.Sum(f=>f.Length);int count=files.Length;
                    foreach(var file in files.OrderBy(f=>f.LastWriteTimeUtc))
                    {if(count<maxEntries&&resident+encrypted.Length<=maxBytes)break;resident-=file.Length;count--;file.Delete();}
                    temporary=Path.Combine(directory,key+"."+Guid.NewGuid().ToString("N")+".tmp");File.WriteAllBytes(temporary,encrypted);
                    if(File.Exists(target))File.Delete(target);File.Move(temporary,target);temporary=null;
                }
            }
            catch{ }
            finally{if(temporary!=null)try{File.Delete(temporary);}catch{ }}
        }
        void Remove(string key){try{lock(disk)File.Delete(Path.Combine(directory,key+".pcache"));}catch{ }}
        [StructLayout(LayoutKind.Sequential)]struct Blob{internal int Length;internal IntPtr Data;}
        [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool CryptProtectData(ref Blob input,string description,ref Blob entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
        [DllImport("crypt32.dll",SetLastError=true)]static extern bool CryptUnprotectData(ref Blob input,IntPtr description,ref Blob entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
        [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr value);
        static byte[] Protect(byte[] bytes,byte[] entropy,bool encrypt)
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("Portal DPAPI cache requires Windows.");
            var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};var salt=new Blob{Length=entropy.Length,Data=Marshal.AllocHGlobal(entropy.Length)};Blob output=new Blob();
            try
            {
                Marshal.Copy(bytes,0,input.Data,bytes.Length);Marshal.Copy(entropy,0,salt.Data,entropy.Length);
                bool ok=encrypt?CryptProtectData(ref input,"HDR portal validated DXBC",ref salt,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,ref salt,IntPtr.Zero,IntPtr.Zero,1,out output);
                if(!ok||output.Length<1||output.Length>2*MaxShaderBytes+16384)throw new InvalidDataException("Portal DPAPI cache protection failed.");
                var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
            }
            finally{Marshal.FreeHGlobal(input.Data);Marshal.FreeHGlobal(salt.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
        }
    }
}
