using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using Sandbox.Game.Components;
using VRage.Game.Entity.UseObject;

internal static class UiIntegrationTests
{
    static int _checks;
    static void Check(bool value,string label){if(!value)throw new Exception("UI integration: "+label);_checks++;}
    static void Reject(Action action,string label){try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Expected UI rejection: "+label);}
    static object Call(object target,string name,params object[] args)=>ClientReplicationTests.Call(target,name,args);
    static object Field(object target,string name)=>ClientReplicationTests.Field(target,name);
    static void Set(object target,string name,object value)=>ClientReplicationTests.SetField(target,name,value);
    static object Static(string name,params object[] args)
    {try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    static UiDisplay Display()=>new UiDisplay{CallerId=10,TargetId=20,Revision=1,
        Bundles=new List<UiBundle>{new UiBundle{Id="main"},new UiBundle{Id="options",Visible=false}},
        Widgets=new List<UiWidget>{new UiWidget{Id="open",Bundle="main",X=0,Y=0,Width=.5,Height=.5,ActionKind="menu",Argument="options",Label="Open"},
            new UiWidget{Id="option",Bundle="options",X=0,Y=0,Width=.5,Height=.5,ActionKind="pb",Argument="fixed-command",Label="Options",Visible=false}}};
    static HoloSnapshot Snapshot()=>new HoloSnapshot{Scenes=new List<HoloSceneData>{ClientReplicationTests.Scene()},Ui=new List<UiDisplay>{Display()}};
    public static int Run(){_checks=0;HitQuads();UiReplication();PrivateMenuRendering();NativeProxyLifecycle();return _checks;}
    static void HitQuads()
    {
        Vector3D center=Vector3D.Zero,right=Vector3D.UnitX,up=new Vector3D(.5,1,0),hit;
        Check(UiHitGeometry.Quad(new Vector3D(1.35,.9,2),new Vector3D(0,0,-10),center,right,up,2,out hit)&&hit==new Vector3D(1.35,.9,0),"skewed quad uses normalized ray and Gram basis coordinates");
        Check(UiHitGeometry.Quad(new Vector3D(1.5,1,2),Vector3D.Forward,center,right,up,2,out hit),"quad boundary is inclusive at interaction range");
        Check(!UiHitGeometry.Quad(new Vector3D(1.5001,1,2),Vector3D.Forward,center,right,up,2,out hit),"point beyond skewed quad is rejected");
        Check(!UiHitGeometry.Quad(new Vector3D(0,0,2),Vector3D.UnitX,center,right,up,3,out hit),"parallel ray cannot activate UI");
        Check(!UiHitGeometry.Quad(new Vector3D(0,0,2),Vector3D.Backward,center,right,up,3,out hit),"intersection behind the ray is rejected");
        Check(!UiHitGeometry.Quad(new Vector3D(0,0,2),Vector3D.Forward,center,right,up,1.99,out hit),"nearby physical plane still obeys maximum interaction distance");
        Check(!UiHitGeometry.Quad(new Vector3D(0,0,2),Vector3D.Forward,center,right,right,3,out hit),"degenerate quad fails closed");
        Check(!UiHitGeometry.Quad(new Vector3D(double.NaN,0,2),Vector3D.Forward,center,right,up,3,out hit),"nonfinite ray fails closed");
        Check(!UiHitGeometry.Quad(new Vector3D(0,0,2),Vector3D.Forward,center,right,up,double.PositiveInfinity,out hit),"nonfinite interaction range fails closed");
        MatrixD transform=MatrixD.CreateFromYawPitchRoll(.3,.2,.4)*MatrixD.CreateTranslation(100,200,300);
        Check(UiHitGeometry.Quad(Vector3D.Transform(new Vector3D(1.35,.9,2),transform),Vector3D.TransformNormal(Vector3D.Forward,transform),transform.Translation,Vector3D.TransformNormal(right,transform),Vector3D.TransformNormal(up,transform),3,out hit),"translated tilted quad preserves hit coordinates");
    }
    static void UiReplication()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();var packet=Snapshot();
        var copy=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(packet));
        Check(!copy.Ui[0].Bundles[1].Visible&&!copy.Ui[0].Widgets[1].Visible,"protobuf preserves explicitly false UI visibility flags");
        var session=new HoloMapSession();Call(session,"ApplySnapshot",copy);var scenes=(IDictionary)Field(session,"_scenes");object scene=scenes[20L];var prior=(IDictionary)Field(session,"_uiDisplays");object ui=prior["10:20"];
        void Invalid(Action<HoloSnapshot> change,string label){var malformed=Snapshot();change(malformed);Reject(()=>Call(session,"ApplySnapshot",malformed),label);Check(ReferenceEquals(scenes[20L],scene)&&ReferenceEquals(((IDictionary)Field(session,"_uiDisplays"))["10:20"],ui),label+" preserves scene and UI atomically");}
        Invalid(s=>s.Ui[0].TargetId=21,"UI references missing target scene");
        Invalid(s=>s.Ui.Add(UiRules.Copy(s.Ui[0])),"duplicate UI display key");
        Invalid(s=>s.Ui[0].Widgets[0].X=25,"widget corner exceeds display radius");
        Invalid(s=>s.Ui[0].Widgets[0].ActionKind="execute","untrusted action kind");
        Invalid(s=>s.Ui[0].Widgets[0].Width=double.NaN,"nonfinite UI bounds");
        var before=Snapshot();var after=Snapshot();after.Ui[0].Revision=2;after.Ui[0].Widgets[0].Label="Changed";
        var delta=(HoloSnapshot)Static("BuildDelta",before,after,5L);
        Check(delta.UiChanged&&delta.Scenes.Count==0,"UI-only metadata change does not resend display geometry");
        var deltaCopy=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(delta));
        var merged=(HoloSnapshot)Static("MergeDelta",before,deltaCopy,5L);Call(session,"ApplySnapshot",merged);
        Check((UiDisplay)Call(session,"GetUiDisplay",10L,20L) is UiDisplay updated&&updated.Revision==2&&updated.Widgets[0].Label=="Changed","UI update survives actual delta serialization and application");
        var unchanged=(HoloSnapshot)Static("BuildDelta",before,before,5L);unchanged=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(unchanged));
        Check(!unchanged.UiChanged&&((HoloSnapshot)Static("MergeDelta",before,unchanged,5L)).Ui.Count==1,"absent UI delta leaves existing declarations unchanged");
        after=Snapshot();after.Ui=null;var cleared=(HoloSnapshot)Static("BuildDelta",before,after,5L);cleared=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(gateway.Serialize(cleared));
        Check(cleared.UiChanged&&((HoloSnapshot)Static("MergeDelta",before,cleared,5L)).Ui==null,"explicit UI clear remains distinct from unchanged through protobuf");
        Call(session,"ApplySnapshot",before);var published=(HoloSnapshot)Call(session,"CaptureSnapshot");var live=(UiDisplay)Call(session,"GetUiDisplay",10L,20L);live.Widgets[0].Argument="mutated";live.Bundles[0].Visible=false;
        Check(published.Ui[0].Widgets[0].Argument=="options"&&published.Ui[0].Bundles[0].Visible,"published UI metadata is deeply copied");
        var largest=new List<UiDisplay>();for(int d=0;d<16;d++){var display=new UiDisplay{CallerId=long.MaxValue-d,TargetId=long.MaxValue-32-d%8,Revision=long.MaxValue};for(int b=0;b<8;b++)display.Bundles.Add(new UiBundle{Id=("bundle"+b).PadRight(24,'b')});for(int w=0;w<8;w++)display.Widgets.Add(new UiWidget{Id=("widget"+w).PadRight(24,'w'),Bundle=("bundle"+w).PadRight(24,'b'),Width=.1,Height=.1,ActionKind="pb",Argument=new string('一',256),Label=new string('界',64)});UiRules.Display(display);largest.Add(display);}
        Check(gateway.Serialize(largest).Length<UiRules.ReplicationReserve,"maximal 128-widget UI metadata fits reserved publication space");
    }
    static void PrivateMenuRendering()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=Snapshot();packet.Ui[0].Widgets[1].Visible=true;
        var data=packet.Scenes[0];data.Items[0].Layer="ui-options";data.Layers.Add(new HoloLayerData{CallerId=10,Name="ui-options",Visible=false,Opacity=.6f});
        data.Labels.Add(new HoloLabelData{CallerId=10,Id="caption",Text="Options",Position=new[]{0d,0,0},Style=new[]{1f,1,1,1,.1f},Visible=true,Layer="ui-options"});
        Call(session,"ApplySnapshot",packet);object scene=((IDictionary)Field(session,"_scenes"))[20L];var sprites=new List<MySprite>();var size=new Vector2(512,512);
        object panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return size;if(m.Name=="DrawFrame")return new MySpriteDrawFrame(f=>f.AddToList(sprites));throw new Exception("Unexpected private menu panel: "+m.Name);});
        Call(session,"DrawLcd",scene,panel,20000,1);Check(sprites.Count==1&&sprites[0].Type==SpriteType.CLIP_RECT,"unopened submenu draws only a viewport clip, with no UI artwork, caption or opaque backdrop");
        var menuType=typeof(HoloMapSession).GetNestedType("UiLocalMenu",BindingFlags.NonPublic);object menu=Activator.CreateInstance(menuType,true);Set(menu,"Revision",1L);Set(menu,"Bundle","options");Set(menu,"LastActionTick",0);((IDictionary)Field(session,"_uiLocalMenus"))["10:20"]=menu;
        Set(session,"_ticks",1);sprites.Clear();Call(session,"DrawLcd",scene,panel,20000,1);Call(session,"CompileQueuedDisplay");sprites.Clear();Call(session,"DrawLcd",scene,panel,20000,1);
        Check(sprites.Any(s=>s.Type==SpriteType.TEXT&&s.Data=="Options")&&sprites.Any(s=>s.Type==SpriteType.TEXTURE&&s.Color.HasValue&&s.Color.Value.G>0),"viewer-local submenu reveals geometry and caption through actual LCD renderer");
        Check((float)Call(session,"ClientLayerAlpha",scene,10L,"ui-options")==.6f,"private submenu preserves configured layer opacity");
        object layer=((IDictionary)Field(scene,"Layers"))["10:ui-options"];Check(!(bool)Field(layer,"Visible")&&!((UiDisplay)Call(session,"GetUiDisplay",10L,20L)).Bundles[1].Visible,"private submenu does not change shared layer or bundle visibility");
        var other=new HoloMapSession();Call(other,"ApplySnapshot",packet);object otherScene=((IDictionary)Field(other,"_scenes"))[20L];sprites.Clear();Call(other,"DrawLcd",otherScene,panel,20000,1);Check(sprites.Count==1&&sprites[0].Type==SpriteType.CLIP_RECT,"another viewer retains the shared hidden submenu state");
        Set(menu,"Revision",2L);Check((float)Call(session,"ClientLayerAlpha",scene,10L,"ui-options")==0,"stale local menu revision cannot reveal changed UI");
    }
    static void NativeProxyLifecycle()
    {
        var session=new HoloMapSession();var component=new MyUseObjectsComponent();int forwarded=0;
        IMyUseObject Original()=> (IMyUseObject)DrawTestProxy.Make(typeof(IMyUseObject),(m,a)=>{
            if(m.Name=="get_PrimaryAction")return UseActionEnum.Manipulate;if(m.Name=="get_ContinuousUsage")return false;
            if(m.Name=="get_InteractiveDistance")return 7f;if(m.Name=="Use"){forwarded++;return null;}
            throw new Exception("Unexpected native use operation: "+m.Name);});
        IMyUseObject original=Original(),foreign=Original();
        Type proxyType=typeof(HoloMapSession).GetNestedType("NativeUiUseObject",BindingFlags.NonPublic),bindingType=typeof(HoloMapSession).GetNestedType("NativeUiBinding",BindingFlags.NonPublic);
        var proxy=(IMyUseObject)Activator.CreateInstance(proxyType,new object[]{session,20L,original});
        object binding=Activator.CreateInstance(bindingType,true);Set(binding,"Component",component);Set(binding,"Detector",0u);Set(binding,"Proxy",proxy);
        component.DetectorInteractiveObjects[0]=new MyUseObjectsComponent.DetectorData(proxy,Matrix.Identity,"textpanel");
        Check(proxy.InteractiveDistance==7,"native LCD proxy retains vanilla interaction properties");proxy.Use(UseActionEnum.OpenTerminal,null);Check(forwarded==1,"native LCD proxy forwards unrelated native actions");
        Check((bool)Static("RestoreUiBinding",binding)&&ReferenceEquals(component.DetectorInteractiveObjects[0].UseObject,original),"cleanup restores exactly the native LCD use object it wrapped");
        component.DetectorInteractiveObjects[0]=new MyUseObjectsComponent.DetectorData(foreign,Matrix.Identity,"foreign");
        Check((bool)Static("RestoreUiBinding",binding)&&ReferenceEquals(component.DetectorInteractiveObjects[0].UseObject,foreign),"cleanup preserves another mod's later use object replacement");
        object target=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_WorldMatrix")return MatrixD.Identity;throw new Exception("Unexpected transient use target: "+m.Name);});
        Type holoType=typeof(HoloMapSession).GetNestedType("NativeHoloUiUseObject",BindingFlags.NonPublic);var transient=(IMyUseObject)Activator.CreateInstance(holoType,new[]{(object)session,target,Matrix.Identity,Matrix.Identity});
        binding=Activator.CreateInstance(bindingType,true);Set(binding,"Component",component);Set(binding,"Detector",0u);Set(binding,"HoloProxy",transient);
        component.DetectorInteractiveObjects[0]=new MyUseObjectsComponent.DetectorData(transient,Matrix.Identity,"hdr_ui_local");component.DetectorInteractiveObjects[1]=new MyUseObjectsComponent.DetectorData(foreign,Matrix.Identity,"foreign_tail");
        Check(!(bool)Static("RestoreUiBinding",binding)&&transient.SupportedActions==UseActionEnum.None&&component.DetectorInteractiveObjects.Count==2,"foreign appended detector leaves only an inert placeholder instead of changing native shape keys");
        component.DetectorInteractiveObjects.Remove(1);
        Check((bool)Static("RestoreUiBinding",binding)&&component.DetectorInteractiveObjects.Count==0,"transient detector is removed when it safely becomes the dictionary tail");
    }
}
