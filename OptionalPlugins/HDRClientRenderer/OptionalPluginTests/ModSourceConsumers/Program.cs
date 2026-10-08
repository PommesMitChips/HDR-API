using HDRClientRenderer;
using VRage;

static class Program
{
    static int assertions;
    static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
    static object[] Lcd(object anchor,object source,object declaration,long owner=1,long context=1,long revision=1)
    {return new object[]{1,"local-mod",new MyTuple<string,long,long,long,long>("test",owner,context,revision,revision),anchor,"lcd-texture","42:0",new object[]{source},new MyTuple<int,int,double,bool>(256,144,30,true),null,declaration};}
    static void Main()
    {
        int nativeRequests=0,releases=0,authorizations=0;bool permission=true;
        var anchor=new object();var source=new object();var declaration=new object();var binding=new object();
        object[] descriptor=Lcd(anchor,source,declaration);
        Func<object,object[]> callback=token=>ReferenceEquals(token,binding)?descriptor:null;
        var registry=new ModSourceConsumerRegistry(state=>{authorizations++;return permission;},key=>key!=long.MaxValue-1,evidence=>releases++);
        object NativeFrame(object token){var consumer=registry.Resolve(token);if(consumer==null)return null;nativeRequests++;return registry.Own(consumer,new object());}
        Check(NativeFrame(binding)==null&&nativeRequests==0,"Missing negotiated service performs no native requests.");
        registry.Attach(callback);
        Check(NativeFrame(new object())==null&&nativeRequests==0,"Unregistered context cannot request native work.");
        permission=false;Check(NativeFrame(binding)==null&&nativeRequests==0,"Viewer/construct/source permission denial precedes native work.");
        permission=true;var evidence=(ModSourceConsumerRegistry.Evidence)NativeFrame(binding);var consumer=evidence.Consumer;
        Check(nativeRequests==1&&authorizations>=2,"Current authorized context reaches existing backend once.");
        Check(consumer.Key==long.MaxValue-2&&registry.IsLocalKey(consumer.Key),"Consumer keys skip real entity identities.");
        Check(registry.Valid(binding,evidence),"Evidence belongs to its exact current context.");
        Check(!registry.Valid(new object(),evidence),"Evidence cannot be transplanted to another binding.");
        Check(ReferenceEquals(registry.Own(consumer,evidence.Native),evidence),"Repeated native lease returns one owned wrapper.");
        ((object[])descriptor[6])[0]=new object();
        Check(!registry.Valid(binding,evidence)&&releases==1,"Entity ID rebinding invalidates and retires retained resources.");
        Check(registry.IsLocalKey(consumer.Key)&&!registry.TryKey(consumer.Key,out _),"Retired local keys cannot fall through as PB callers.");
        var replacement=registry.Resolve(binding);Check(replacement!=null&&replacement.Key!=consumer.Key,"Rebound source receives a distinct local lifetime.");
        // The policy owns a detached array; mutating a caller-returned identity
        // array cannot retroactively update a retained descriptor.
        Check(ReferenceEquals(consumer.State.Sources[0],source),"Retained source identity arrays are detached.");
        var current=registry.Own(replacement,new object());
        descriptor=Lcd(anchor,((object[])descriptor[6])[0],declaration,2);
        Check(!registry.Valid(binding,current)&&releases==2,"Owner replacement revokes evidence even with the same context handle.");
        var latest=registry.Resolve(binding);var hidden=registry.Own(latest,new object());
        descriptor[7]=new MyTuple<int,int,double,bool>(256,144,30,false);
        registry.Prune();Check(!registry.Valid(binding,hidden)&&releases==3,"Hidden or renderer-disabled descriptor revokes native work and resources.");
        int before=nativeRequests;Check(NativeFrame(binding)==null&&before==nativeRequests,"Inactive descriptor cannot schedule a backend.");
        descriptor=Lcd(anchor,source,declaration);var released=(ModSourceConsumerRegistry.Evidence)NativeFrame(binding);
        Check(registry.Release(released)&&!registry.Release(released)&&!registry.Valid(binding,released),"Release is idempotent and revoked evidence stays invalid.");
        descriptor=Lcd(anchor,source,declaration);var unloaded=(ModSourceConsumerRegistry.Evidence)NativeFrame(binding);
        registry.Attach(token=>null);Check(!registry.Valid(binding,unloaded)&&unloaded.Released,"Core callback replacement retires all retained resources.");
        registry.Attach(callback);descriptor=Lcd(anchor,source,declaration);
        descriptor[4]="unknown";Check(NativeFrame(binding)==null,"Unsupported native kind cannot reach a backend.");
        descriptor=Lcd(anchor,source,declaration);descriptor[5]="42:32";Check(NativeFrame(binding)==null,"LCD surface index is bounded.");
        descriptor=Lcd(anchor,source,declaration);descriptor[8]=new object();Check(NativeFrame(binding)==null,"LCD descriptor cannot inject a capture payload.");
        var cameras=new object[]{new object(),new object()};var data=new MyTuple<MyTuple<double,double,double,int>,int>(new MyTuple<double,double,double,int>(105,8,1.15,1024),0);
        descriptor=new object[]{1,"local-mod",new MyTuple<string,long,long,long,long>("camera",1,1,1,1),anchor,"camera-panorama","42,43",cameras,new MyTuple<int,int,double,bool>(1024,512,30,true),data,declaration};
        Check(registry.Resolve(binding)!=null,"Real registered multi-camera descriptor is accepted.");
        descriptor[5]="42,42";Check(NativeFrame(binding)==null,"Duplicate camera entities are rejected.");
        descriptor[5]="42,43";descriptor[6]=new object[]{cameras[0]};Check(NativeFrame(binding)==null,"Every camera requires a registered physical identity.");
        descriptor[6]=cameras;descriptor[8]=new MyTuple<MyTuple<double,double,double,int>,int>(new MyTuple<double,double,double,int>(105,8,1.15,1000),0);Check(NativeFrame(binding)==null,"Capture resolution remains the existing allowed set.");
        descriptor[8]=data;var cameraLease=(ModSourceConsumerRegistry.Evidence)NativeFrame(binding);descriptor[8]=new MyTuple<MyTuple<double,double,double,int>,int>(new MyTuple<double,double,double,int>(110,8,1.15,1024),0);
        Check(!registry.Valid(binding,cameraLease),"Capture configuration changes invalidate prior leases even if a revision is reused.");
        var portal=new object[19];for(int i=0;i<portal.Length;i++)portal[i]=0;portal[4]=new double[]{1,2,3};
        descriptor=new object[]{1,"local-mod",new MyTuple<string,long,long,long,long>("portal",1,1,1,1),anchor,"native-portal","entry",new object[]{anchor},new MyTuple<int,int,double,bool>(512,512,30,true),portal,declaration};
        var portalConsumer=registry.Resolve(binding);Check(portalConsumer!=null,"Declared portal metadata is accepted for existing provider validation.");
        ((double[])portal[4])[0]=9;
        Check(ReferenceEquals(registry.Resolve(binding),portalConsumer),"Moving portal matrices preserve the consumer while existing provider checks its new declaration.");
        Check(((double[])((object[])portalConsumer.State.Data)[4])[0]==9,"Current portal data is detached and refreshed.");
        descriptor[9]=new object();Check(!ReferenceEquals(registry.Resolve(binding),portalConsumer),"Portal declaration replacement creates a distinct consumer lifetime.");
        registry.Clear();Check(NativeFrame(binding)==null,"World unload clears the authority callback.");
        Console.WriteLine($"Mod source consumer CPU checks passed: {assertions} assertions; no GPU allocation or native pointer access.");
        ActualEndpointChecks.Run();
    }
}
