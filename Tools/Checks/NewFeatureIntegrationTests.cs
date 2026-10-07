using System.Collections;
using System.Reflection;
using HoloMap;
using VRageMath;
using VRage.Game.GUI.TextPanel;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRage.ModAPI;

// Cross-feature checks go through real scene replication and client compilation.
// Rendering is inspected before submitting native game draw calls.
internal static class NewFeatureIntegrationTests
{
    static int _checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception("Feature integration: "+message);_checks++;}
    static void Reject(Action action,string message){try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Expected feature rejection: "+message);}
    static object Call(object target,string method,params object[] args)=>ClientReplicationTests.Call(target,method,args);
    static object Field(object target,string name)=>ClientReplicationTests.Field(target,name);
    static void Set(object target,string name,object value)=>ClientReplicationTests.SetField(target,name,value);
    public static int Run()
    {
        _checks=0;
        VectorFonts();
        ProjectionVolumes();
        RadialColorSeams();
        LcdCanvas();
        ReplicatedDisplayConfiguration();
        RichSvg();
        LcdContentSelection();
        EndpointDisplayReleaseAndVolume();
        RadialLcdNativeTriangles();
        SeamlessRectangleLcd();
        LcdBackgroundCommand();
        return _checks;
    }
    static void LcdBackgroundCommand()
    {
        using var gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=true};var registry=new Dictionary<long,object>();Color background=Color.Black;byte alpha=255;int writes=0;
        var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{
            if(m.Name=="get_EntityId")return 10L;if(m.Name=="get_OwnerId")return 77L;if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;
            throw new Exception("Unexpected LCD background caller: "+m.Name);});
        object panel=null;panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;if(m.Name=="HasPlayerAccess")return true;
            if(m.Name=="get_SurfaceCount")return 1;if(m.Name=="GetSurface")return panel;
            if(m.Name=="set_ScriptBackgroundColor"){background=(Color)a[0];writes++;return null;}
            if(m.Name=="set_BackgroundAlpha"){alpha=(byte)a[0];writes++;return null;}
            throw new Exception("Unexpected LCD background panel: "+m.Name);});
        registry[10]=caller;registry[20]=panel;
        gateway.Install("Entities",(m,a)=>{if(m.Name=="GetEntityById")return registry.TryGetValue((long)a[0],out var value)?value:null;throw new Exception("Unexpected background entity API: "+m.Name);});
        var session=new HoloMapSession();var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);draw("target",new[]{panel});
        draw("lcd-background",new object[]{"transparent"});Check(background==Color.Transparent&&alpha==0&&writes==2,"background command sets both native clear color and the independent alpha control once");
        draw("lcd-background",new object[]{new Vector4(0,1,0,.5f)});Check(background.G==255&&alpha==128&&writes==4,"partial background alpha is explicit and quantized to the native byte");
        Reject(()=>draw("lcd-background",new object[]{"black",2}),"nonexistent LCD surface rejected before writes");
        Reject(()=>draw("lcd-background",new object[]{new Vector4(float.NaN,0,0,1)}),"nonfinite background paint rejected before writes");
        Check(writes==4,"invalid background commands preserve native settings");
    }
    static void SeamlessRectangleLcd()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();var data=packet.Scenes[0];data.View[1]=0;
        data.Lcd=new HoloLcdData{Width=2,Height=2};
        data.Items[0].Declaration=new HoloGeometryDeclaration{SvgSource="<svg viewBox='0 0 2 2'><g fill='#12303c' fill-opacity='0.4' stroke='#44ddee' stroke-width='0.008'><rect x='0.35' y='0.25' width='1.3' height='0.22'/><rect x='0.35' y='0.75' width='1.3' height='0.22'/><rect x='0.35' y='1.25' width='1.3' height='0.22'/></g></svg>",SvgSegments=12};
        Call(session,"ApplySnapshot",packet);object scene=((IDictionary)Field(session,"_scenes"))[20L];var sprites=new List<MySprite>();
        object item=((IDictionary)Field(scene,"Items"))["10:line"];Call(session,"ClientDisplayItem",scene,item,1);Call(session,"CompileQueuedDisplay");
        var panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return new Vector2(512);
            if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";
            if(m.Name=="DrawFrame")return new MySpriteDrawFrame(f=>f.AddToList(sprites));
            if(m.Name=="get_EntityId")return 20L;if(m.Name=="get_Closed")return false;
            throw new Exception("Unexpected rectangle LCD method: "+m.Name);});
        var args=new object[]{scene,panel,20000,2};Call(session,"DrawLcd",args);
        var fills=sprites.Where(s=>s.Color.HasValue&&s.Color.Value.R==0x12&&s.Color.Value.G==0x30&&s.Color.Value.B==0x3c).ToArray();
        Check(fills.Length==3&&fills.All(s=>s.Data=="SquareSimple"),"three actual SVG UI button fills emit three whole quads with no internal triangle-mask diagonal");
        Check(fills.All(s=>s.Color.Value.A==102),"seam removal preserves translucent fill alpha rather than making it opaque or drawing overlapping triangles");
        Check(!sprites.Any(s=>s.Data=="SquareSimple"&&s.Size==new Vector2(512)&&s.Color==Color.Black),"transparent LCD artwork is not covered by an application-forced opaque backdrop");
    }
    static void RadialLcdNativeTriangles()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();var data=packet.Scenes[0];data.View[1]=0;
        data.Lcd=new HoloLcdData{Width=24,Height=24};
        data.Items[0].Declaration=new HoloGeometryDeclaration{SvgSource="<svg viewBox='0 0 20 20'><defs><radialGradient id='r'><stop offset='0' stop-color='white'/><stop offset='1' stop-color='blue'/></radialGradient></defs><circle cx='10' cy='10' r='10' fill='url(#r)'/></svg>",SvgSegments=12};
        Call(session,"ApplySnapshot",packet);object scene=((IDictionary)Field(session,"_scenes"))[20L];object item=((IDictionary)Field(scene,"Items"))["10:line"];
        var compiler=typeof(HoloMapSession).GetMethod("ClientDisplayItem",BindingFlags.NonPublic|BindingFlags.Instance);compiler.Invoke(session,new[]{scene,item,(object)1});Call(session,"CompileQueuedDisplay");object compiled=compiler.Invoke(session,new[]{scene,item,(object)1});
        Check(compiled!=null,"common radial SVG circle compiles inside local geometry budget");
        var geometry=(Geometry)Field(compiled,"Geometry");Vector2 size=new Vector2(512,512);int expected=0;
        for(int i=0;i<geometry.Triangles.Length;i+=3){Vector2 Point(int index)=>LcdProjection.Point(geometry.Points[geometry.Triangles[index]],24,24,size,1,1,0,0);expected+=LcdTriangleGeometry.Decompose(Point(i),Point(i+1),Point(i+2)).Length;}
        var sprites=new List<MySprite>();object panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";
            if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return size;
            if(m.Name=="DrawFrame")return new MySpriteDrawFrame(f=>f.AddToList(sprites));throw new Exception("Unexpected radial LCD panel method: "+m.Name);});
        Call(session,"DrawLcd",scene,panel,20000,1);int actual=sprites.Count(s=>s.Data=="HDRAPI_TriangleLeft"||s.Data=="HDRAPI_TriangleRight");
        Check(expected>2000&&expected+1<=4096,"common radial circle's exact triangle pieces fit one native LCD frame (pieces="+expected+")");
        Check(actual==expected,"LCD draws every radial geometry triangle without budget truncation (expected="+expected+", actual="+actual+")");
        Check(sprites.Count==actual+1&&sprites.Any(s=>s.Type==SpriteType.CLIP_RECT&&s.Position==Vector2.Zero&&s.Size==size),"native radial triangle frame includes viewport clipping and respects the native background instead of adding an opaque quad");
    }
    static void EndpointDisplayReleaseAndVolume()
    {
        using var gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=true};
        bool secondAccess=true;var registry=new Dictionary<long,object>();
        object Caller(long id)=>DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>{
            if(m.Name=="get_EntityId")return id;if(m.Name=="get_OwnerId")return 77L;if(m.Name=="get_Closed")return false;if(m.Name=="IsSameConstructAs")return true;throw new Exception("Unexpected display caller: "+m.Name);});
        object Panel(long id)=>DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_EntityId")return id;if(m.Name=="get_Closed")return false;if(m.Name=="HasPlayerAccess")return id!=21||secondAccess;throw new Exception("Unexpected display panel: "+m.Name);});
        MatrixD world=MatrixD.CreateRotationZ(0.3)*MatrixD.CreateTranslation(50,70,90);
        object Projector(long id,string subtype)=>DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>{
            if(m.Name=="get_EntityId")return id;if(m.Name=="get_Closed")return false;if(m.Name=="HasPlayerAccess")return true;
            if(m.Name=="get_BlockDefinition")return new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Projector),subtype);
            if(m.Name=="get_WorldMatrix")return world;
            throw new Exception("Unexpected display projector: "+m.Name);});
        registry[10]=Caller(10);registry[11]=Caller(11);registry[20]=Panel(20);registry[21]=Panel(21);registry[30]=Panel(30);registry[40]=Projector(40,"LargeProjector");registry[50]=Projector(50,"Console");
        gateway.Install("Entities",(m,a)=>{if(m.Name=="GetEntityById")return registry.TryGetValue((long)a[0],out var value)?value:null;throw new Exception("Unexpected display entities: "+m.Name);});
        var packet=ClientReplicationTests.Snapshot();packet.Scenes.Add(ClientReplicationTests.Scene(21));packet.Scenes.Add(ClientReplicationTests.Scene(30));
        for(int i=0;i<2;i++)packet.Scenes[i].Lcd=new HoloLcdData{Columns=2,Rows=1,Column=i,Width=4,Height=2,SourceId=20,CallerId=10};
        packet.Scenes[2].Lcd=new HoloLcdData{CallerId=99};var session=new HoloMapSession();Call(session,"ApplySnapshot",packet);
        var scenes=(IDictionary)Field(session,"_scenes");object unrelated=scenes[30L];
        var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",registry[10]);draw("target",new[]{registry[20]});
        Reject(()=>draw("range",new object[]{1}),"LCD does not accept a 3D projector range");
        secondAccess=false;Reject(()=>draw("lcd-release",new object[0]),"release preauthorizes every member of an LCD group");
        Check(scenes.Count==3&&ReferenceEquals(unrelated,scenes[30L]),"failed group release cannot remove any scene");secondAccess=true;
        var other=(Func<string,object[],object>)Call(session,"DrawEndpoint",registry[11]);
        Reject(()=>other("target",new[]{registry[20]}),"another PB cannot select and release an owned LCD group");
        draw("lcd-release",new object[0]);Check(scenes.Count==1&&ReferenceEquals(unrelated,scenes[30L]),"release removes all owned group tiles and preserves unrelated displays");
        draw("target",new[]{registry[40]});Reject(()=>draw("table-volume",new object[0]),"ordinary projector cannot accept a table frustum");
        draw("range",new object[]{0});Check((double)Field(scenes[40L],"VolumeRange")==0,"ordinary projector supports disabling its range clip");
        draw("target",new[]{registry[50]});draw("table-volume",new object[]{true,2,1,1,2,2,0.6});draw("range",new object[]{0.6});
        object consoleScene=scenes[50L];var volume=(DisplayVolume)Call(session,"GetDisplayVolume",consoleScene,registry[50]);
        Vector3D World(double x,double y,double z)=>Vector3D.Transform(new Vector3D(x,y,z),world);
        Check(volume.Contains(World(0,0.8,0))&&!volume.Contains(World(0.9,0.8,0)),"Console getter combines table envelope with range centered on display offset");
        Check(!volume.Contains(World(0,0.5,0)),"Console range cannot bypass table's lower clipping plane");
        draw("range",new object[]{0});volume=(DisplayVolume)Call(session,"GetDisplayVolume",consoleScene,registry[50]);
        Check(volume.Contains(World(0.9,0.8,0))&&!volume.Contains(World(0,0.5,0)),"disabling Console range retains the independently configured table envelope");
    }
    static void LcdContentSelection()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();
        ContentType content=ContentType.NONE;string script="Clock";
        var panel=(IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_ContentType")return content;if(m.Name=="set_ContentType"){content=(ContentType)a[0];return null;}
            if(m.Name=="get_Script")return script;if(m.Name=="set_Script"){script=(string)a[0];return null;}
            throw new Exception("Unexpected content panel method: "+m.Name);});
        Func<IMyTerminalBlock,long> nativeGetter=b=>(long)content;
        Action<IMyTerminalBlock,long> nativeSetter=(b,v)=>{throw new Exception("Shared native setter must never be changed or called by HDR selection.");};
        Action<List<MyTerminalControlComboBoxItem>> nativeOptions=list=>{
            foreach(long key in new[]{0L,1L,3L})list.Add(new MyTerminalControlComboBoxItem{Key=key,Value=MyStringId.GetOrCompute("Native"+key)});};
        var native=(IMyTerminalControlCombobox)DrawTestProxy.Make(typeof(IMyTerminalControlCombobox),(m,a)=>{
            if(m.Name=="get_Id")return "Content";if(m.Name=="get_Getter")return nativeGetter;if(m.Name=="get_Setter")return nativeSetter;
            if(m.Name=="get_ComboBoxContent")return nativeOptions;if(m.Name=="get_Enabled"||m.Name=="get_Visible")return new Func<IMyTerminalBlock,bool>(b=>true);
            if(m.Name=="get_SupportsMultipleBlocks")return true;
            throw new Exception("Native control must remain untouched: "+m.Name);});
        var properties=new Dictionary<string,object>();
        var hdr=(IMyTerminalControlCombobox)DrawTestProxy.Make(typeof(IMyTerminalControlCombobox),(m,a)=>{
            if(m.Name=="get_Id")return "HDR.Content";
            if(m.Name.StartsWith("set_")){properties[m.Name.Substring(4)]=a[0];return null;}
            if(m.Name.StartsWith("get_")){object value;return properties.TryGetValue(m.Name.Substring(4),out value)?value:null;}
            throw new Exception("Unexpected HDR control method: "+m.Name);});
        Delegate callback=null;int added=0,removed=0;
        gateway.Install("TerminalControls",(m,a)=>{
            if(m.Name=="CreateControl")return hdr;
            if(m.Name=="AddControl"){Check(a[0]==hdr,"only the dedicated HDR selector is registered");added++;return null;}
            if(m.Name=="RemoveControl"){Check(a[0]==hdr,"cleanup removes only the HDR selector");removed++;return null;}
            if(m.Name=="add_CustomControlGetter"){callback=(Delegate)a[0];return null;}
            if(m.Name=="remove_CustomControlGetter"){Check(callback.Equals(a[0]),"cleanup unregisters the actual-list callback");callback=null;return null;}
            throw new Exception("HDR must not guess the block's native controls at registration: "+m.Name);});
        var session=new HoloMapSession();Call(session,"RegisterLcdContentControl");
        Check(added==1&&callback!=null,"dedicated content control and actual-block callback are registered");
        var list=new List<IMyTerminalControl>{native,hdr};callback.DynamicInvoke(panel,list);
        Check(list.Count==1&&list[0]==hdr,"actual LCD's Content control is replaced in its visible list");
        var inherited=(IMyTerminalControl)DrawTestProxy.Make(typeof(IMyTerminalControlCombobox),(m,a)=>m.Name=="get_Id"?"Content":throw new Exception("Inherited Content control must not be touched."));
        list=new List<IMyTerminalControl>{inherited,native,hdr};callback.DynamicInvoke(panel,list);
        Check(list.Count==2&&list[0]==inherited&&list[1]==hdr,"replacement uses the actual final Content control rather than an inherited block's selector");
        var items=new List<MyTerminalControlComboBoxItem>();hdr.ComboBoxContent(items);
        Check(items.Select(i=>i.Key).SequenceEqual(new[]{0L,1L,3L,2L})&&items.Last().Value.String=="HDR API","LCD preserves native Content options and adds an engine-valid HDR entry");
        hdr.Setter(panel,2);Check(content==ContentType.SCRIPT&&script=="HDRAPI"&&hdr.Getter(panel)==2,"LCD HDR selection uses native SCRIPT with its dedicated marker");
        hdr.Setter(panel,3);Check(content==ContentType.SCRIPT&&script==""&&hdr.Getter(panel)==3,"ordinary Script mode clears the HDR marker");
        hdr.Setter(panel,1);Check(content==ContentType.TEXT_AND_IMAGE,"ordinary LCD text/images remains selectable");
        hdr.Setter(panel,0x484F4C4F);Check(content==ContentType.TEXT_AND_IMAGE,"retired and arbitrary invalid keys cannot reach the renderer");
        var embedded=(IMyTerminalBlock)DrawTestProxy.Make(typeof(ContentTestProvider),(m,a)=>{
            if(m.Name=="get_SurfaceCount")return 1;if(m.Name=="GetSurface")return panel;
            throw new Exception("Unexpected embedded LCD provider method: "+m.Name);});
        list=new List<IMyTerminalControl>{native,hdr};callback.DynamicInvoke(embedded,list);
        Check(list.Count==1&&list[0]==hdr,"Console-style single embedded LCD receives HDR Content too");
        hdr.Setter(embedded,2);Check(content==ContentType.SCRIPT&&script=="HDRAPI","embedded LCD HDR selection addresses its real surface");
        var panels=(Sandbox.Game.EntityComponents.MyMultiTextPanelComponent)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Sandbox.Game.EntityComponents.MyMultiTextPanelComponent));
        typeof(Sandbox.Game.EntityComponents.MyMultiTextPanelComponent).GetField("m_selectedPanel",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panels,2);
        var container=DrawTestProxy.Make(typeof(VRage.Game.Components.Interfaces.IMyEntityComponentContainer),(m,a)=>m.Name=="Get"?panels:null);
        int selectedSurface=-1;
        var multi=(IMyTerminalBlock)DrawTestProxy.Make(typeof(ContentTestProvider),(m,a)=>{
            if(m.Name=="get_SurfaceCount")return 4;if(m.Name=="get_Components")return container;
            if(m.Name=="GetSurface"){selectedSurface=(int)a[0];return panel;}
            throw new Exception("Unexpected multi-screen provider method: "+m.Name);});
        list=new List<IMyTerminalControl>{native,hdr};callback.DynamicInvoke(multi,list);hdr.Setter(multi,2);
        Check(list.Count==1&&list[0]==hdr&&selectedSurface==2&&script=="HDRAPI","four-screen Console uses the selected screen instead of guessing surface zero");
        var unsupported=(IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyTerminalBlock),(m,a)=>{throw new Exception("No-screen block must not be queried.");});
        list=new List<IMyTerminalControl>{native,hdr};callback.DynamicInvoke(unsupported,list);hdr.Setter(unsupported,2);
        Check(list.Count==1&&list[0]==native,"blocks without LCDs retain their original controls");
        nativeOptions=l=>{foreach(long key in new[]{0L,1L,2L,3L})l.Add(new MyTerminalControlComboBoxItem{Key=key,Value=MyStringId.GetOrCompute("Foreign")});};
        list=new List<IMyTerminalControl>{native,hdr};callback.DynamicInvoke(panel,list);
        Check(list.Count==1&&list[0]==native,"another mod occupying safe slot retains its native selector");
        Check(native.Getter==nativeGetter&&native.Setter==nativeSetter,"LCD and embedded selection never mutate shared native delegates");
        Call(session,"UnregisterLcdContentControl");Check(removed==1&&callback==null,"unload removes HDR control and callback");
        content=unchecked((ContentType)0x484F4C4F);script="";
        Check(HoloMapSession.RepairLegacyLcdSurface((Sandbox.ModAPI.Ingame.IMyTextSurface)panel)&&content==ContentType.SCRIPT&&script=="HDRAPI","retired saved key recovers before LCD updates");
        content=ContentType.TEXT_AND_IMAGE;script="Clock";
        Check(!HoloMapSession.RepairLegacyLcdSurface((Sandbox.ModAPI.Ingame.IMyTextSurface)panel)&&script=="Clock","recovery preserves unrelated LCD content");
    }
    static void ReplicatedDisplayConfiguration()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();
        HoloSnapshot Packet()
        {
            var packet=ClientReplicationTests.Snapshot();packet.Scenes.Add(ClientReplicationTests.Scene(21));
            for(int i=0;i<2;i++){var scene=packet.Scenes[i];scene.Volume=new[]{5d,1,0.25,2,0.5,0.75,1,1.5};scene.Lcd=new HoloLcdData{Columns=2,Rows=1,Column=i,Row=0,Width=4,Height=2,SourceId=20,CallerId=10};}
            return packet;
        }
        var valid=Packet();byte[] bytes=gateway.Serialize(valid);HoloSnapshot roundTrip=MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(bytes);
        Check(roundTrip.Scenes[0].Volume.SequenceEqual(valid.Scenes[0].Volume)&&roundTrip.Scenes[1].Lcd.Column==1&&roundTrip.Scenes[1].Lcd.SourceId==20,"projection envelope and LCD group survive actual protobuf serialization");
        var session=new HoloMapSession();Call(session,"ApplySnapshot",roundTrip);var scenes=(IDictionary)Field(session,"_scenes");object first=scenes[20L],second=scenes[21L];
        Check((double)Field(first,"VolumeRange")==5&&(bool)Field(first,"TableVolumeEnabled")&&((double[])Field(first,"TableVolume")).SequenceEqual(new[]{0.25,2,0.5,0.75,1,1.5}),"receiver stores configured range and table frustum");
        Check((long)Field(second,"LcdSourceId")==20&&(int)Field(second,"LcdColumn")==1&&(double)Field(second,"LcdWidth")==4,"receiver stores shared LCD canvas and tile placement");
        void Invalid(Action<HoloSnapshot> mutate,string message)
        {
            var packet=Packet();mutate(packet);Reject(()=>Call(session,"ApplySnapshot",packet),message);
            Check(ReferenceEquals(first,scenes[20L])&&ReferenceEquals(second,scenes[21L]),message+" preserves both previous scene instances atomically");
        }
        Invalid(p=>p.Scenes[1].Volume[0]=double.NaN,"nonfinite replicated projection range");
        Invalid(p=>p.Scenes[1].Volume[0]=26,"replicated range above maximum");
        Invalid(p=>p.Scenes[1].Volume[1]=0.5,"invalid volume enable flag");
        Invalid(p=>p.Scenes[1].Volume[3]=0,"zero frustum height");
        Invalid(p=>p.Scenes[1].Volume[6]=0.25,"frustum taper below base width");
        Invalid(p=>p.Scenes[1].Lcd.Column=2,"replicated tile index outside columns");
        Invalid(p=>p.Scenes[1].Lcd.Width=double.PositiveInfinity,"nonfinite LCD canvas");
        Invalid(p=>p.Scenes[1].Lcd.Column=0,"duplicate tile coordinate in shared canvas");
        Invalid(p=>p.Scenes[1].Lcd.CallerId=99,"shared LCD group crosses ownership");
        Invalid(p=>p.Scenes[1].Lcd.Width=8,"shared LCD tiles disagree on canvas dimensions");
        Invalid(p=>{p.Scenes[0].Lcd.SourceId=21;p.Scenes[1].Lcd.SourceId=20;},"cyclic LCD source references");
        Invalid(p=>p.Scenes[1].Layers.Add(new HoloLayerData{CallerId=10,Name="menu",Order=1025,Opacity=1}),"layer stacking order exceeds replicated limit");
        var after=Packet();foreach(var data in after.Scenes)data.Lcd.Width=8;
        var build=typeof(HoloMapSession).GetMethod("BuildDelta",BindingFlags.NonPublic|BindingFlags.Static);var merge=typeof(HoloMapSession).GetMethod("MergeDelta",BindingFlags.NonPublic|BindingFlags.Static);
        var delta=(HoloSnapshot)build.Invoke(null,new object[]{valid,after,5L});
        Check(delta.Scenes.Count==2&&delta.Scenes.All(s=>s.Items.Count==0),"canvas configuration updates replicate as metadata without geometry");
        var merged=(HoloSnapshot)merge.Invoke(null,new object[]{valid,delta,5L});Call(session,"ApplySnapshot",merged);
        Check((double)Field(scenes[20L],"LcdWidth")==8&&(double)Field(scenes[21L],"LcdWidth")==8,"metadata delta updates all shared canvas tiles together");
    }
    static void LcdCanvas()
    {
        Vector2 pixels=new Vector2(512,512);
        var point=LcdProjection.Point(new Vector3D(0.25,0.25,100),2,2,pixels,1,1,0,0);
        Check(point==new Vector2(320,192),"LCD projection puts positive Y above center and positive X to the right");
        Check(point==LcdProjection.Point(new Vector3D(0.25,0.25,-100),2,2,pixels,1,1,0,0),"LCD projection flattens depth orthographically");
        Vector2 left=LcdProjection.Point(Vector3D.Zero,4,2,pixels,2,1,0,0),right=LcdProjection.Point(Vector3D.Zero,4,2,pixels,2,1,1,0);
        Check(left.X==512&&right.X==0&&left.Y==right.Y,"group tiles share one continuous canvas at their boundary");
        Reject(()=>LcdProjection.Validate(2,2,3,3,0,0),"LCD tile count budget");
        Reject(()=>LcdProjection.Validate(2,2,2,1,2,0),"LCD tile index bounds");
        Reject(()=>LcdProjection.Validate(double.NaN,2,1,1,0,0),"LCD finite canvas dimensions");
        LcdProjection.ValidateLayout(new[]{new Vector3D(0,1,0),new Vector3D(1,1,0),new Vector3D(0,0,0),new Vector3D(1,0,0)},2,2);
        Check(true,"LCD layout accepts an evenly spaced rectangle");
        Reject(()=>LcdProjection.ValidateLayout(new[]{Vector3D.Zero,Vector3D.Zero},2,1),"duplicate physical LCD slot");
        Reject(()=>LcdProjection.ValidateLayout(new[]{new Vector3D(0,1,0),new Vector3D(1,1,0),new Vector3D(0,0,0),new Vector3D(1.5,0,0)},2,2),"irregular LCD group spacing");
        var sprites=new List<MySprite>();
        var frame=new MySpriteDrawFrame(f=>f.AddToList(sprites));
        var triangle=typeof(HoloMapSession).GetMethod("LcdTriangle",BindingFlags.NonPublic|BindingFlags.Static);
        var args=new object[]{frame,new Vector2(-100000,-100000),new Vector2(100000,-100000),new Vector2(0,100000),Color.White,Vector2.Zero,pixels,7};
        triangle.Invoke(null,args);frame.Dispose();
        Check(sprites.Count>0&&sprites.Count<=7&&(int)args[7]>=0,"LCD scanline rasterization respects sprite budget even for enormous offscreen triangle (sprites="+sprites.Count+", remaining="+args[7]+")");
        Check(sprites.All(s=>(s.Data=="HDRAPI_TriangleLeft"||s.Data=="HDRAPI_TriangleRight")&&s.Position.HasValue&&s.Size.HasValue&&Geometry.Finite(s.Position.Value.X)&&Geometry.Finite(s.Position.Value.Y)&&s.Size.Value.X>0&&s.Size.Value.Y>0),"LCD triangles use finite native alpha textures whose viewport is clipped by the containing frame");
        using var gateway=new ClientReplicationTests.GatewayScope();var session=new HoloMapSession();var packet=ClientReplicationTests.Snapshot();
        var data=packet.Scenes[0];data.Items[0].Layer="menu";data.Items[0].Opacity=0.25f;data.Items[0].Style[3]=0.8f;
        data.Layers.Add(new HoloLayerData{CallerId=10,Name="menu",Visible=true,Opacity=0.5f});Call(session,"ApplySnapshot",packet);
        object scene=((IDictionary)Field(session,"_scenes"))[20L];object item=((IDictionary)Field(scene,"Items"))["10:line"];
        var compiler=typeof(HoloMapSession).GetMethod("ClientDisplayItem",BindingFlags.NonPublic|BindingFlags.Instance);
        compiler.Invoke(session,new[]{scene,item,(object)1});Call(session,"CompileQueuedDisplay");
        sprites.Clear();
        object panel=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>{
            if(m.Name=="get_ContentType")return ContentType.SCRIPT;if(m.Name=="get_Script")return "HDRAPI";
            if(m.Name=="get_SurfaceSize"||m.Name=="get_TextureSize")return pixels;
            if(m.Name=="DrawFrame")return new MySpriteDrawFrame(f=>f.AddToList(sprites));
            throw new Exception("Unexpected LCD method: "+m.Name);});
        Call(session,"DrawLcd",scene,panel,20000,1);
        var content=sprites.Where(s=>s.Color.HasValue&&(s.Color.Value.R!=0||s.Color.Value.G!=0||s.Color.Value.B!=0)).ToArray();
        Check(content.Length>0&&content.All(s=>Math.Abs(s.Color.Value.A/255.0-0.1)<0.01),"LCD renderer combines layer, object and stroke transparency");
        Check(sprites.Any(s=>s.Type==SpriteType.CLIP_RECT&&s.Position==Vector2.Zero&&s.Size==pixels),"LCD screen frame clips native triangle textures and lines to the viewport");
        ((IDictionary)Field(scene,"Layers"))["10:menu"].GetType().GetField("Visible").SetValue(((IDictionary)Field(scene,"Layers"))["10:menu"],false);
        sprites.Clear();Call(session,"DrawLcd",scene,panel,20000,1);Check(sprites.All(s=>!s.Color.HasValue||s.Color.Value.R==0&&s.Color.Value.G==0&&s.Color.Value.B==0),"hidden menu layer produces only LCD background and clipping sprites");
        var red=ClientReplicationTests.Item("background");red.Layer="back";red.Style[0]=1;red.Style[1]=0;red.Style[2]=0;data.Items.Add(red);
        data.Layers[0].Order=10;data.Layers[0].Visible=true;data.Layers.Add(new HoloLayerData{CallerId=10,Name="back",Visible=true,Opacity=1,Order=-10});
        Call(session,"ApplySnapshot",packet);scene=((IDictionary)Field(session,"_scenes"))[20L];
        for(int tick=1;tick<=2;tick++){Set(session,"_ticks",tick);foreach(DictionaryEntry entry in (IDictionary)Field(scene,"Items"))compiler.Invoke(session,new[]{scene,entry.Value,(object)1});Call(session,"CompileQueuedDisplay");}
        sprites.Clear();Call(session,"DrawLcd",scene,panel,20000,1);
        content=sprites.Where(s=>s.Color.HasValue&&(s.Color.Value.R!=0||s.Color.Value.G!=0||s.Color.Value.B!=0)).ToArray();
        Check(content.Length==2&&content[0].Color.Value.R>0&&content[1].Color.Value.G>0,"LCD menu layers render in explicit stacking order rather than insertion order");
    }
    static double Area(Geometry geometry)
    {double sum=0;for(int i=0;i<geometry.Triangles.Length;i+=3){var a=geometry.Points[geometry.Triangles[i]];var b=geometry.Points[geometry.Triangles[i+1]];var c=geometry.Points[geometry.Triangles[i+2]];sum+=Vector3D.Cross(b-a,c-a).Length()/2;}return sum;}
    static void RichSvg()
    {
        var rounded=Svg.Parse("<svg><rect width='10' height='4' rx='1' fill='red'/></svg>",6);
        Check(Area(rounded.Geometry)>36&&Area(rounded.Geometry)<40,"rounded rectangle preserves its straight spans and clips curved corners");
        string reused="<svg><defs><path id='p' d='M0 0H2V2H0Z'/></defs><use href='#p' x='1'/><use href='#p' x='5' fill='red'/></svg>";
        var uses=Svg.Parse(reused);Check(Math.Abs(Area(uses.Geometry)-8)<1e-7,"reusable definitions expand at each placement");
        Check(uses.Colors.Any(c=>c.X==1&&c.Y==0)&&uses.Colors.Any(c=>c.X==0&&c.Y==0),"use instances inherit independent fill styles");
        var meet=Svg.Parse("<svg><defs><symbol id='s' viewBox='0 0 2 2'><rect width='2' height='2'/></symbol></defs><use href='#s' width='4' height='2'/></svg>");
        var stretched=Svg.Parse("<svg><defs><symbol id='s' viewBox='0 0 2 2' preserveAspectRatio='none'><rect width='2' height='2'/></symbol></defs><use href='#s' width='4' height='2'/></svg>");
        Check(Math.Abs(Area(meet.Geometry)-4)<1e-7&&Math.Abs(Area(stretched.Geometry)-8)<1e-7,"symbol viewBox supports aspect-preserving and stretched placement");
        var mask=Svg.Parse("<svg><defs><mask id='m' maskUnits='userSpaceOnUse' mask-type='alpha'><rect width='5' height='10' fill='white' opacity='0.4'/></mask></defs><rect width='10' height='10' fill='cyan' mask='url(#m)'/></svg>");
        Check(Math.Abs(Area(mask.Geometry)-50)<1e-7&&mask.Colors.All(c=>Math.Abs(c.W-0.4)<1e-6),"uniform alpha mask clips coverage and scales transparency");
        var filter=Svg.Parse("<svg><defs><filter id='f'><feColorMatrix type='matrix' values='0 0 0 0 0  0 0 0 0 1  0 0 0 0 0  0 0 0 0.5 0'/></filter></defs><rect width='2' height='2' fill='red' filter='url(#f)'/></svg>");
        Check(filter.Colors.All(c=>c.X==0&&c.Y==1&&c.Z==0&&c.W==0.5),"bounded color matrix transforms RGB and alpha");
        var text=Svg.Parse("<svg viewBox='0 0 100 100'><text x='20' y='40' font-size='10' fill='white'>aéΩ</text></svg>");
        Check(text.Geometry.Triangles.Length>0,"SVG text uses lowercase and packaged Unicode outlines");
        var spans=Svg.Parse("<svg><text x='10' y='20' font-size='4'>a<tspan fill='red'>b</tspan><tspan x='10' dy='15'>c</tspan></text></svg>");
        Check(spans.Colors.Any(c=>c.X==1&&c.Y==0)&&spans.Geometry.Points.Max(p=>p.Y)-spans.Geometry.Points.Min(p=>p.Y)>15,"styled tspan runs preserve independent color and explicit baseline offsets");
        var entity=Svg.Parse("<svg><text font-size='4'>&#x3b1;</text></svg>");
        var literal=Svg.Parse("<svg><text font-size='4'>α</text></svg>");Check(entity.Geometry.Points.SequenceEqual(literal.Geometry.Points),"numeric Unicode entities decode into packaged glyphs");
        Check(Math.Abs(Svg.ParseColor("#ff000080").W-128/255f)<1e-6&&Math.Abs(Svg.ParseColor("rgba(255,0,0,0.5)").W-0.5)<1e-6,"SVG alpha colors accept hex and RGBA forms");
        foreach(string bad in new[]{
            "<svg><use href='https://example.com/asset.svg#p'/></svg>",
            "<svg><defs><g id='cycle'><use href='#cycle'/></g></defs><use href='#cycle'/></svg>",
            "<svg><defs><g id='a'><use href='#b'/></g><g id='b'><use href='#a'/></g></defs><use href='#a'/></svg>",
            "<svg><defs><filter id='f'><feGaussianBlur stdDeviation='2'/></filter></defs><rect width='2' height='2' filter='url(#f)'/></svg>",
            "<svg><defs><filter id='f'><feColorMatrix values='NaN'/></filter></defs><rect width='2' height='2' filter='url(#f)'/></svg>"})
            Reject(()=>Svg.Parse(bad),"bounded SVG local resources and cycles");
        string expansion="<svg><defs><g id='p'><rect width='1' height='1'/></g></defs>"+string.Concat(Enumerable.Repeat("<use href='#p'/>",513))+"</svg>";
        Reject(()=>Svg.Parse(expansion),"SVG reference expansion budget");
    }
    static void RadialColorSeams()
    {
        var points=new[]{new Vector3D(-0.8,-0.8,0),new Vector3D(0.8,-0.8,0),new Vector3D(0.8,0.8,0),new Vector3D(-0.8,0.8,0)};
        var uv=points.Select(p=>new Vector2((float)((p.X+0.8)/1.6),(float)((p.Y+0.8)/1.6))).ToArray();
        SurfaceMesh Make(int[] triangles)=>MeshEffects.Solid(new Geometry(points,new int[0],triangles),new Vector4(1,1,1,0.6f),Vector4.Zero,uv:uv);
        Vector4 Palette(double t)=>new Vector4((float)(1-Math.Min(1,t)),(float)Math.Min(1,t),0,0.5f);
        var first=MeshEffects.RadialGradient(Make(new[]{0,1,2,0,2,3}),p=>p,Palette,2);
        var second=MeshEffects.RadialGradient(Make(new[]{0,1,3,1,2,3}),p=>p,Palette,2);
        Vector4 At(SurfaceMesh mesh,Vector3D p)
        {
            for(int i=0;i<mesh.Geometry.Triangles.Length;i+=3)
            {
                var a=mesh.Geometry.Points[mesh.Geometry.Triangles[i]];var b=mesh.Geometry.Points[mesh.Geometry.Triangles[i+1]];var c=mesh.Geometry.Points[mesh.Geometry.Triangles[i+2]];
                double x=Vector3D.Cross(b-a,p-a).Z,y=Vector3D.Cross(c-b,p-b).Z,z=Vector3D.Cross(a-c,p-c).Z;
                if(x>=-1e-9&&y>=-1e-9&&z>=-1e-9||x<=1e-9&&y<=1e-9&&z<=1e-9)return mesh.Colors[i/3];
            }
            throw new Exception("Radial test sample not covered: "+p);
        }
        foreach(var p in new[]{new Vector3D(0.2,0.11,0),new Vector3D(0.51,0.1,0),new Vector3D(-0.34,0.31,0)})
            Check(At(first,p)==At(second,p),"radial band color is independent of source triangulation at "+p);
        Check(first.Colors.Select(c=>c.X).Distinct().Count()>8,"radial fill uses many shared color bands");
        Check(first.Colors.All(c=>Math.Abs(c.W-0.3f)<1e-6),"radial clipping preserves source and palette alpha multiplication");
        Check(first.UV!=null&&first.UV.Length==first.Geometry.Points.Length,"radial tessellation retains image UVs");
        Check(first.Geometry.Points.Zip(first.UV,(p,t)=>Math.Abs(t.X-(p.X+0.8)/1.6)+Math.Abs(t.Y-(p.Y+0.8)/1.6)).All(e=>e<1e-5),"radial subdivisions interpolate UVs from original triangles");
    }
    static void VectorFonts()
    {
        bool Covers(Geometry mesh,double x,double y)
        {
            var p=new Vector3D(x,y,0);
            for(int i=0;i<mesh.Triangles.Length;i+=3)
            {
                var a=mesh.Points[mesh.Triangles[i]];var b=mesh.Points[mesh.Triangles[i+1]];var c=mesh.Points[mesh.Triangles[i+2]];
                double area=Vector3D.Cross(b-a,c-a).Z;if(Math.Abs(area)<1e-12)continue;double sign=area>0?1:-1;
                if(sign*Vector3D.Cross(b-a,p-a).Z>=-1e-10&&sign*Vector3D.Cross(c-b,p-b).Z>=-1e-10&&sign*Vector3D.Cross(a-c,p-c).Z>=-1e-10)return true;
            }
            return false;
        }
        var d=VectorFont.Text("D",1,"start");var r=VectorFont.Text("R",1,"start");
        Check(Covers(d,.231,.461667),"D overlapping stem and curve are filled, rather than XOR-cut");
        Check(Covers(r,.243846,-.110258),"R overlapping stroke junction is filled, rather than XOR-cut");
        Check(!Covers(d,.45,0),"D counter remains hollow with nonzero winding");
        Geometry upper=VectorFont.Text("A",1,"start"),lower=VectorFont.Text("a",1,"start");
        Check(upper.Triangles.Length>0&&lower.Triangles.Length>0&&!upper.Points.SequenceEqual(lower.Points),"lowercase has its own scalable glyph outline");
        foreach(int code in new[]{(int)'é',(int)'Ω',(int)'Ж',(int)'°'})
            Check(VectorFont.Supports(code)&&VectorFont.Text(char.ConvertFromUtf32(code),1).Triangles.Length>0,"packaged Unicode glyph renders: "+code);
        Check(VectorFont.Text("🚀",1).Points.SequenceEqual(VectorFont.Text("?",1).Points),"unsupported supplementary character produces one fallback glyph");
        var multiline=VectorFont.Text("a\na",1,"start");
        Check(multiline.Points.Max(p=>p.Y)-multiline.Points.Min(p=>p.Y)>1.3,"multiline layout gives separate baselines");
        Check(multiline.Points.SequenceEqual(VectorFont.Text("a\r\na",1,"start").Points),"CRLF and LF use the same multiline layout");
        var scaled=VectorFont.Text("a",2,"start");
        Check(lower.Points.Select(p=>p*2).SequenceEqual(scaled.Points),"font scales coordinates without bitmap pixel steps");
        var centered=VectorFont.Text("a",1,"middle");double shift=VectorFont.Width("a")/2;
        Check(lower.Points.Zip(centered.Points,(a,b)=>(a-b-new Vector3D(shift,0,0)).LengthSquared()).All(d=>d<1e-20),"middle text anchor applies half the glyph advance");
        Reject(()=>VectorFont.Text(new string('a',65),1),"text character budget");
        Reject(()=>VectorFont.Text("a",double.NaN),"nonfinite font height");
        Reject(()=>VectorFont.Text("a",1,"invalid"),"invalid text anchor");
        Reject(()=>VectorFont.Text("a",1,"middle",0.5),"line height budget");
        foreach(string sample in new[]{new string('W',32),new string('a',32),"ABCDEFGHIJKLMNOPQRSTUVWXYZ012345","Mining arm status: ready 1234567"})
        {var mesh=VectorFont.Text(sample,0.1);Check(mesh.Points.Length<=Geometry.MaxPoints&&mesh.Triangles.Length/3<=Geometry.MaxPrimitives,"representative long vector text fits geometry budgets");}
        var outlines=(IDictionary)typeof(VectorFont).GetField("Outlines",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        foreach(int code in outlines.Keys)
        {
            var glyph=VectorFont.Text(char.ConvertFromUtf32(code),1);
            Check(glyph.Points.Length<=Geometry.MaxPoints&&glyph.Triangles.Length/3<=Geometry.MaxPrimitives,"single packaged glyph remains within geometry budget: "+code);
        }
    }
    static void ProjectionVolumes()
    {
        MatrixD anchor=MatrixD.CreateRotationZ(0.4)*MatrixD.CreateTranslation(100,200,300);
        var volume=new DisplayVolume(anchor,DisplayVolume.Frustum(0,2,1,1,2,2));
        Vector3D World(double x,double y,double z)=>Vector3D.Transform(new Vector3D(x,y,z),anchor);
        Check(volume.Contains(World(1.4,1,0))&&!volume.Contains(World(1.6,1,0)),"table envelope expands from its smaller base");
        Check(volume.Contains(World(2,2,0))&&!volume.Contains(World(0,-0.01,0))&&!volume.Contains(World(0,2.01,0)),"table envelope includes boundaries and clips above and below height");
        var a=World(-3,1,0);var b=World(3,1,0);
        Check(volume.ClipLine(ref a,ref b),"line crossing tilted table volume survives");
        Check((a-World(-1.5,1,0)).Length()<1e-8&&(b-World(1.5,1,0)).Length()<1e-8,"tilted line clips to exact frustum side planes");
        var reverseA=World(3,1,0);var reverseB=World(-3,1,0);volume.ClipLine(ref reverseA,ref reverseB);
        Check((reverseA-b).Length()<1e-8&&(reverseB-a).Length()<1e-8,"reversing line preserves clipped endpoints in reverse order");
        var polygon=volume.ClipTriangle(new DisplayVolume.Vertex(World(-3,1,0),new Vector2(0,0)),new DisplayVolume.Vertex(World(3,1,0),new Vector2(1,0)),new DisplayVolume.Vertex(World(0,1,3),new Vector2(0.5f,1)));
        Check(polygon.Count>=3&&polygon.All(v=>volume.Contains(v.Position)),"clipped filled polygon stays inside tilted projection envelope");
        Check(polygon.All(v=>v.UV.X>=0&&v.UV.X<=1&&v.UV.Y>=0&&v.UV.Y<=1)&&polygon.Any(v=>v.UV.X>0&&v.UV.X<0.5),"triangle clipping interpolates image UVs along new edges");
        polygon=volume.ClipTriangle(new DisplayVolume.Vertex(World(0,3,0),Vector2.Zero),new DisplayVolume.Vertex(World(1,3,0),Vector2.UnitX),new DisplayVolume.Vertex(World(0,3,1),Vector2.UnitY));
        Check(polygon.Count==0,"triangle wholly above table volume is omitted");
        var range=new DisplayVolume(MatrixD.Identity,DisplayVolume.Range(2));a=new Vector3D(-4,0,0);b=new Vector3D(4,0,0);
        Check(range.ClipLine(ref a,ref b)&&a.Length()<=2+1e-9&&b.Length()<=2+1e-9,"projector range clips both endpoints within configured radius");
        Check(range.Contains(Vector3D.Zero)&&!range.Contains(new Vector3D(2.1,0,0)),"projector range contains center and excludes distant positions");
    }
}


public interface ContentTestProvider : Sandbox.ModAPI.IMyTerminalBlock, Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider {}

