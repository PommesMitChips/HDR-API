using System;
using VRage;

namespace HoloMap
{
    public struct UiPointerSample
    {
        public long Frame;
        public int Flags, Pressed, Held, Released;
        public double X, Y;
        public bool Active { get { return (Flags & 18) == 18 && (Flags & (4|32|64)) == 0; } }
    }
    // Local mod-message delegate ABI, not a network ABI. A successful request is
    // pending until the provider reports Applied|PointerValid from native routing.
    public sealed class PointerInputBridge
    {
        public const long Discovery = 481770140, Registration = 481770141;
        public const string Version = "HDR.Pointer/1";
        Func<string,object[],object> _endpoint;
        long _token;
        long _generation;
        public long Token { get { return _token; } }
        public bool Ready { get { return _endpoint != null; } }
        public void Receive(object data)
        {
            if(!(data is MyTuple<string,int,Func<string,object[],object>>))return;
            var api=(MyTuple<string,int,Func<string,object[],object>>)data;
            if(api.Item1!=Version)return;
            if(api.Item2==0){if(ReferenceEquals(_endpoint,api.Item3)){Release();_endpoint=null;_generation++;}return;}
            if(api.Item2!=1||api.Item3==null)return;
            if(ReferenceEquals(_endpoint,api.Item3))return;
            Release();_endpoint=api.Item3;_generation++;
        }
        public bool Acquire(long owner,string key)
        {
            if(owner==0||string.IsNullOrEmpty(key)||key.Length>192||_endpoint==null||_token!=0)return false;
            try
            {
                var endpoint=_endpoint;long generation=_generation;
                var result=endpoint("acquire",new object[]{owner,key});
                if(!(result is MyTuple<long,int>))return false;
                var lease=(MyTuple<long,int>)result;
                if(generation!=_generation||!ReferenceEquals(endpoint,_endpoint))
                {try{if(lease.Item1>0)endpoint("release",new object[]{lease.Item1});}catch{}return false;}
                if(lease.Item1<=0||(lease.Item2&(4|32|64))!=0)return false;
                _token=lease.Item1;return true;
            }
            catch{return false;}
        }
        public bool Sample(out UiPointerSample sample)
        {
            sample=new UiPointerSample();if(_endpoint==null||_token<=0)return false;
            try
            {
                var endpoint=_endpoint;long generation=_generation,token=_token;
                var result=endpoint("sample",new object[]{token});
                if(generation!=_generation||token!=_token||!ReferenceEquals(endpoint,_endpoint))return false;
                if(!(result is MyTuple<long,int,MyTuple<double,double>,MyTuple<int,int,int>>))return false;
                var value=(MyTuple<long,int,MyTuple<double,double>,MyTuple<int,int,int>>)result;
                sample=new UiPointerSample{Frame=value.Item1,Flags=value.Item2,X=value.Item3.Item1,Y=value.Item3.Item2,
                    Pressed=value.Item4.Item1,Held=value.Item4.Item2,Released=value.Item4.Item3};
                if(!UiDragWire.Finite(sample.X)||!UiDragWire.Finite(sample.Y)||sample.X<0||sample.X>1||sample.Y<0||sample.Y>1
                    ||((sample.Pressed|sample.Held|sample.Released)&~31)!=0)return false;
                return sample.Frame>=0;
            }
            catch{return false;}
        }
        public void Release()
        {long token=_token;_token=0;try{if(token>0&&_endpoint!=null)_endpoint("release",new object[]{token});}catch{}}
        public void Close(){Release();_endpoint=null;_generation++;}
    }
}
