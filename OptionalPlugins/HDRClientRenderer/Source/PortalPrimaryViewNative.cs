using System;
using System.Reflection;
using VRageMath;
namespace HDRClientRenderer
{
    // Read only at an eligible primary-render boundary, before any secondary
    // view changes Environment. No game entities, access APIs or UI camera pose
    // are queried here. All values handed to a capture are immutable copies.
    internal sealed class PortalPrimaryViewNative
    {
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly FieldInfo environment,matrices,viewer,projection,cameraPosition;
        readonly PropertyInfo frame,viewport,backbuffer,backbufferResource,textureDescription,deviceSettings;
        readonly FieldInfo textureWidth,textureHeight;readonly MemberInfo configuredWidth,configuredHeight;
        readonly Func<object> readEnvironment;readonly Func<long> readFrame;readonly Func<Vector2I> readViewport,readPresentation;
        internal PortalPrimaryViewNative(Assembly assembly):this(assembly,null,null,null,null){}
        internal PortalPrimaryViewNative(Assembly assembly,Func<object> environmentReader,Func<long> frameReader,Func<Vector2I> viewportReader,Func<Vector2I> presentationReader=null)
        {
            Func<string,Type> type=name=>DirectCameraCaptureNative.FindType(assembly,name);var render=type("VRageRender.MyRender11");var poses=type("VRageRender.MyEnvironmentMatrices");
            environment=render.GetField("Environment",Static);matrices=environment==null?null:environment.FieldType.GetField("Matrices",Instance);
            viewer=poses.GetField("InvViewD",Instance);projection=poses.GetField("Projection",Instance);cameraPosition=poses.GetField("CameraPosition",Instance);
            frame=type("VRageRender.MyCommon").GetProperty("FrameCounter",Static);viewport=render.GetProperty("ViewportResolution",Static);
            backbuffer=render.GetProperty("Backbuffer",Static);backbufferResource=type("VRage.Render11.Resources.IResource").GetProperty("Resource",Instance);textureDescription=type("SharpDX.Direct3D11.Texture2D").GetProperty("Description",Instance);deviceSettings=render.GetProperty("DeviceSettings",Static);
            textureWidth=textureDescription==null?null:textureDescription.PropertyType.GetField("Width");textureHeight=textureDescription==null?null:textureDescription.PropertyType.GetField("Height");
            configuredWidth=deviceSettings==null?null:(MemberInfo)deviceSettings.PropertyType.GetField("BackBufferWidth",Instance)??deviceSettings.PropertyType.GetProperty("BackBufferWidth",Instance);
            configuredHeight=deviceSettings==null?null:(MemberInfo)deviceSettings.PropertyType.GetField("BackBufferHeight",Instance)??deviceSettings.PropertyType.GetProperty("BackBufferHeight",Instance);
            if(environment==null||matrices==null||viewer==null||viewer.FieldType!=typeof(MatrixD)||projection==null||projection.FieldType!=typeof(Matrix)||cameraPosition==null||cameraPosition.FieldType!=typeof(Vector3D)||frame==null||viewport==null||viewport.PropertyType!=typeof(Vector2I))
                throw new InvalidOperationException("Portal render-latched primary view ABI changed.");
            if(backbuffer==null||backbufferResource==null||textureDescription==null||textureWidth==null||textureHeight==null||configuredWidth==null||configuredHeight==null)throw new InvalidOperationException("Portal physical presentation-size ABI changed.");
            readEnvironment=environmentReader??(()=>environment.GetValue(null));readFrame=frameReader??(()=>(long)frame.GetValue(null));readViewport=viewportReader??(()=>(Vector2I)viewport.GetValue(null));readPresentation=presentationReader??(environmentReader==null?(Func<Vector2I>)ReadPresentation:()=>readViewport());
        }
        internal bool TrySnapshot(long epoch,out PortalPrimaryView snapshot)
        {
            snapshot=null;
            try
            {
                long before=readFrame();object env=readEnvironment(),pose=env==null?null:matrices.GetValue(env);if(pose==null)return false;
                var world=(MatrixD)viewer.GetValue(pose);var p=(MatrixD)(Matrix)projection.GetValue(pose);var camera=(Vector3D)cameraPosition.GetValue(pose);var size=readViewport();var display=readPresentation();
                if(!PortalProjection.Finite(camera)||!PositionMatches(world.Translation,camera)||before!=readFrame()||!ReferenceEquals(readEnvironment(),env)||!ReferenceEquals(matrices.GetValue(env),pose))return false;
                var result=new PortalPrimaryView(world,p,before,size.X,size.Y,epoch,display.X,display.Y);if(!result.Valid)return false;snapshot=result;return true;
            }
            catch{return false;}
        }
        Vector2I ReadPresentation()
        {
            object target=backbuffer.GetValue(null),native=target==null?null:backbufferResource.GetValue(target);
            return PresentationSize(native,native!=null&&textureDescription.DeclaringType.IsInstanceOfType(native)?null:deviceSettings.GetValue(null));
        }
        internal Vector2I PresentationSize(object native,object configured)
        {
            if(native!=null&&textureDescription.DeclaringType.IsInstanceOfType(native))
            {
                object description=textureDescription.GetValue(native);return new Vector2I((int)textureWidth.GetValue(description),(int)textureHeight.GetValue(description));
            }
            return new Vector2I(ReadInt(configuredWidth,configured),ReadInt(configuredHeight,configured));
        }
        static int ReadInt(MemberInfo member,object owner){return member is FieldInfo?(int)((FieldInfo)member).GetValue(owner):(int)((PropertyInfo)member).GetValue(owner);}
        internal static bool PositionMatches(Vector3D pose,Vector3D camera)
        {
            if(!PortalProjection.Finite(pose)||!PortalProjection.Finite(camera))return false;
            double magnitude=Math.Max(Math.Max(Math.Abs(camera.X),Math.Abs(camera.Y)),Math.Abs(camera.Z));
            return Vector3D.Distance(pose,camera)<=Math.Max(1e-5,magnitude*4e-15);
        }
    }
}
