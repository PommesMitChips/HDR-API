using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
namespace HDRClientRenderer
{
    // Portal DrawScene runs before the primary view. Sprite LCD messages and
    // one-frame flare allocations belong to that primary view and are not
    // replayable. Exclude their producers/consumers before native list sizing.
    internal sealed class PortalCaptureBillboardIsolationNative : IDisposable
    {
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        const string PatchId="HDRClientRenderer.PortalCaptureBillboardIsolation";
        static readonly object InstallLock=new object();
        static PortalCaptureBillboardIsolationNative active;
        readonly Func<bool> isPortalCapture;
        readonly List<MethodBase> patched=new List<MethodBase>();
        MethodInfo prepare,bucket;MethodInfo[] persistent,flares;Type billboard;
        FieldInfo counts,indices,temp;
        Func<object,bool> ordinaryMaterial;
        Harmony harmony;bool installed,disposed;string failure;
        internal bool Ready{get{return installed&&!disposed&&ReferenceEquals(Volatile.Read(ref active),this);}}
        internal string Reason{get{return failure??(disposed?"Portal billboard isolation disposed.":Ready?"Portal capture omits flare production and sprite-backed billboards before native list sizing.":"Portal billboard isolation is not installed.");}}
        internal PortalCaptureBillboardIsolationNative(Assembly assembly,Func<bool> isPortalCapture)
        {
            if(isPortalCapture==null)throw new ArgumentNullException("isPortalCapture");this.isPortalCapture=isPortalCapture;
            try
            {
                PortalCaptureCoverage.ValidateRuntime();
                Func<string,Type> type=n=>DirectCameraCaptureNative.FindType(assembly,n);
                var renderer=type("VRageRender.MyBillboardRenderer");
                if(assembly==null||renderer.Assembly!=assembly)throw new InvalidOperationException("Portal billboard isolation requires the installed renderer assembly.");
                billboard=type("VRageRender.MyBillboard");var material=type("VRageRender.MyTransparentMaterial");
                counts=Field(renderer,"m_bucketCounts",typeof(int[]),true);indices=Field(renderer,"m_bucketIndices",typeof(int[]),true);
                temp=Field(renderer,"m_tempBuffer",billboard.MakeArrayType(),true);
                var materialId=Field(billboard,"Material",type("VRage.Utils.MyStringId"),false);
                var textureType=material.GetField("TextureType",All);
                if(textureType==null||textureType.IsStatic||!textureType.FieldType.IsEnum||Enum.GetUnderlyingType(textureType.FieldType)!=typeof(int)||!Enum.IsDefined(textureType.FieldType,1))throw new InvalidOperationException("Portal sprite material discriminator ABI unavailable.");
                var getMaterial=Method(type("VRageRender.MyTransparentMaterials"),"GetMaterial",material,true,materialId.FieldType);
                prepare=Method(renderer,"PrepareList",typeof(int),true);bucket=Method(renderer,"GetBillboardBucket",typeof(int),true,billboard);
                var callbacks=renderer.GetNestedType("<>c",All);
                if(callbacks==null)throw new InvalidOperationException("Portal persistent billboard selectors unavailable.");
                persistent=new[]{Method(callbacks,"<PrepareList>b__39_0",typeof(void),false,billboard),Method(callbacks,"<PrepareList>b__39_1",typeof(void),false,billboard)};
                var flare=type("VRageRender.MyFlareRenderer");var light=type("VRage.Render11.Scene.Components.MyLightComponent");
                flares=new[]{Method(flare,"Draw",typeof(void),true,typeof(IEnumerable<>).MakeGenericType(light),typeof(float)),Method(flare,"Draw",typeof(void),true,light,typeof(float),typeof(bool)),Method(flare,"Draw",typeof(void),true,type("VRageRender.FlareId"),typeof(VRageMath.Vector3D),typeof(VRageMath.Vector3D),typeof(VRageMath.Vector4),typeof(float))};
                if(flare.GetMethods(All).Count(m=>m.Name=="Draw")!=3)throw new InvalidOperationException("Portal flare producer coverage changed.");
                Certify(prepare,"778CCBCE58EA2D50BBB268391A7890047B7AFBCA0C2CF1A4649C4C3FDC55A796");
                Certify(bucket,"A9BE6B336511C6CD6193BBFBC93944893B2293942C93CB06201433BEED5292A9");
                Certify(persistent[0],"60D7B490982A61AE94A2724A68B30BFA100FD6BAEE9D7CDAC0E2AFEB9E30944A");
                Certify(persistent[1],"5CCC4A44CCD6CD176C29C7933DDC44E5FA630B765BD2E5E60B08D43C71394755");
                Certify(flares[0],"39DEB103FE28736CE6793ED0A696EAFC083F832CA906507E9AC95084CCA3CEA9");
                Certify(flares[1],"11D16755674E6663CAF07E28C7C84C4BED9C478C120D1E9D9A0F4D89DE195AE9");
                Certify(flares[2],"66545A40686EB76ED1FB74AC9527CF1CFAF7BD0908FE3F71C9BE6750367A1946");
                Certify(getMaterial,"DD5997A8E873CEBAAE37A09E984750CD4A289B33E031FE13A916422D0FF8B8A4");
                Certify(Method(renderer,"GatherInternal",typeof(void),true,type("VRage.Render11.RenderContext.MyRenderContext")),"D358F3AEEABBD45F82500527CAF5482AF473E5A2073DD7C86E9DD91FE6D0E277");
                Certify(Method(renderer,"OnFrameStart",typeof(void),true),"EDCFC1F8BBBE4C78227371F264866782218E90DD3FB62421205ABF18C8F02951");
                Certify(Method(type("VRage.Render11.Sprites.MySpritesManager"),"AcquireDrawMessages",type("VRage.Render11.Sprites.MySpriteMessageData"),false,typeof(string)),"3EE4351951523411C2DDE0529FE526D6D19E1E1D749595B34BC523877EADCCB0");
                Plan(PatchProcessor.GetOriginalInstructions(prepare).ToList(),prepare);
                // Trusted native field loads, without reflection/boxing per item.
                var read=new DynamicMethod("HDR_PortalOrdinaryBillboardMaterial",typeof(bool),new[]{typeof(object)},typeof(PortalCaptureBillboardIsolationNative),true);var il=read.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,billboard);il.Emit(OpCodes.Ldfld,materialId);il.Emit(OpCodes.Call,getMaterial);il.Emit(OpCodes.Ldfld,textureType);
                il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ceq);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ceq);il.Emit(OpCodes.Ret);
                ordinaryMaterial=(Func<object,bool>)read.CreateDelegate(typeof(Func<object,bool>));
            }
            catch(Exception ex){failure="Portal billboard isolation unavailable: "+ex.GetBaseException().Message;}
        }
        internal bool TryInstall()
        {
            lock(InstallLock)
            {
                if(disposed||failure!=null)return false;if(Ready)return true;
                if(active!=null&&!ReferenceEquals(active,this)){failure="Another portal billboard isolation owner is installed.";return false;}
                try
                {
                    harmony=new Harmony(PatchId);Volatile.Write(ref active,this);
                    foreach(var method in flares){patched.Add(method);harmony.Patch(method,prefix:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationNative).GetMethod("BeforeFlare",All)){priority=Priority.First});}
                    foreach(var method in persistent){patched.Add(method);harmony.Patch(method,prefix:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationNative).GetMethod("BeforePersistentBillboard",All)){priority=Priority.First});}
                    patched.Add(prepare);harmony.Patch(prepare,transpiler:new HarmonyMethod(typeof(PortalCaptureBillboardIsolationNative).GetMethod("FilterPreparation",All)){priority=Priority.Last});
                    if(patched.Any(m=>{var info=Harmony.GetPatchInfo(m);return info==null||!info.Owners.Contains(PatchId);}))throw new InvalidOperationException("Portal billboard isolation hooks were not retained.");
                    installed=true;return true;
                }
                catch(Exception ex){failure="Portal billboard isolation installation failed: "+ex.GetBaseException().Message;Unpatch();return false;}
            }
        }
        static bool BeforeFlare(){var owner=Volatile.Read(ref active);return owner==null||!owner.isPortalCapture();}
        static bool BeforePersistentBillboard(object __0){return Keep(__0);}
        static bool Keep(object value)
        {
            var owner=Volatile.Read(ref active);
            // A broken capture predicate or material lookup propagates to the
            // capture boundary; it must never fall through into a sprite queue.
            return owner==null||!owner.isPortalCapture()||owner.ordinaryMaterial(value);
        }
        sealed class FilterPoint{internal int Store,Advance,Local;}
        List<FilterPoint> Plan(List<CodeInstruction> code,MethodBase method)
        {
            var result=new List<FilterPoint>();var locals=method.GetMethodBody().LocalVariables;
            for(int i=0;i<code.Count;i++)if(code[i].Calls(bucket))
            {
                int store=i-1;while(store>=0&&store>=i-5&&!code[store].IsStloc())store--;
                int local=store<0?-1:Local(code[store]);
                if(local<0||local>=locals.Count||locals[local].LocalType!=billboard||store<1)throw new InvalidOperationException("Portal billboard selection local changed.");
                var source=code[store-1].operand as MethodInfo;
                bool once=source!=null&&source.Name=="GetAllocatedItem"&&source.ReturnType==billboard;
                bool read=source!=null&&source.Name=="get_Current"&&source.ReturnType==billboard&&source.DeclaringType==typeof(List<>).MakeGenericType(billboard).GetNestedType("Enumerator").MakeGenericType(billboard);
                if(!once&&!read)throw new InvalidOperationException("Portal billboard input source changed.");
                int p=store+1;bool writing=code[p].LoadsField(temp);
                if(writing)p++;
                if(!code[p++].LoadsField(writing?indices:counts)||!LoadLocal(code[p++],local)||!code[p++].Calls(bucket)||code[p++].opcode!=OpCodes.Ldelema||code[p++].opcode!=OpCodes.Dup||code[p++].opcode!=OpCodes.Ldind_I4)throw new InvalidOperationException("Portal billboard bucket entry changed.");
                int index=-1;
                if(writing){index=Local(code[p]);if(!code[p++].IsStloc()||index<0||!LoadLocal(code[p++],index))throw new InvalidOperationException("Portal billboard output index changed.");}
                if(!code[p++].LoadsConstant(1)||code[p++].opcode!=OpCodes.Add||code[p++].opcode!=OpCodes.Stind_I4)throw new InvalidOperationException("Portal billboard count mutation changed.");
                if(writing&&(!LoadLocal(code[p++],index)||!LoadLocal(code[p++],local)||code[p++].opcode!=OpCodes.Stelem_Ref))throw new InvalidOperationException("Portal billboard output placement changed.");
                if(read){if((code[p].opcode!=OpCodes.Ldloca&&code[p].opcode!=OpCodes.Ldloca_S)||!(code[p+1].operand is MethodInfo)||((MethodInfo)code[p+1].operand).Name!="MoveNext")throw new InvalidOperationException("Portal billboard read advance changed.");}
                else{int counter=Local(code[p]);if(!code[p].IsLdloc()||counter<0||!code[p+1].LoadsConstant(1)||code[p+2].opcode!=OpCodes.Add||!code[p+3].IsStloc()||Local(code[p+3])!=counter)throw new InvalidOperationException("Portal one-frame billboard advance changed.");}
                result.Add(new FilterPoint{Store=store,Advance=p,Local=local});
            }
            if(result.Count!=4||result.Count(p=>code[p.Store-1].operand is MethodInfo&&((MethodInfo)code[p.Store-1].operand).Name=="get_Current")!=2)throw new InvalidOperationException("Portal billboard count/write coverage changed.");
            return result;
        }
        static IEnumerable<CodeInstruction> FilterPreparation(IEnumerable<CodeInstruction> instructions,ILGenerator generator,MethodBase __originalMethod)
        {
            var owner=Volatile.Read(ref active);if(owner==null||__originalMethod!=owner.prepare)throw new InvalidOperationException("Portal billboard filter owner unavailable.");
            var code=instructions.ToList();var points=owner.Plan(code,__originalMethod);var keep=typeof(PortalCaptureBillboardIsolationNative).GetMethod("Keep",All);
            foreach(var point in points.OrderByDescending(p=>p.Store))
            {
                var advance=generator.DefineLabel();code[point.Advance].labels.Add(advance);
                code.InsertRange(point.Store+1,new[]{new CodeInstruction(OpCodes.Ldloc,point.Local),new CodeInstruction(OpCodes.Call,keep),new CodeInstruction(OpCodes.Brfalse,advance)});
            }
            return code;
        }
        static int Local(CodeInstruction i)
        {
            if(i.opcode==OpCodes.Stloc_0||i.opcode==OpCodes.Ldloc_0)return 0;if(i.opcode==OpCodes.Stloc_1||i.opcode==OpCodes.Ldloc_1)return 1;
            if(i.opcode==OpCodes.Stloc_2||i.opcode==OpCodes.Ldloc_2)return 2;if(i.opcode==OpCodes.Stloc_3||i.opcode==OpCodes.Ldloc_3)return 3;
            if(i.operand is LocalBuilder)return ((LocalBuilder)i.operand).LocalIndex;if(i.operand is LocalVariableInfo)return ((LocalVariableInfo)i.operand).LocalIndex;
            return i.operand is byte||i.operand is short||i.operand is int?Convert.ToInt32(i.operand):-1;
        }
        static bool LoadLocal(CodeInstruction i,int local){return i.IsLdloc()&&Local(i)==local;}
        static FieldInfo Field(Type type,string name,Type fieldType,bool isStatic)
        {var field=type.GetField(name,All);if(field==null||field.FieldType!=fieldType||field.IsStatic!=isStatic)throw new InvalidOperationException("Portal billboard field ABI changed: "+type.FullName+"."+name);return field;}
        static MethodInfo Method(Type type,string name,Type result,bool isStatic,params Type[] parameters)
        {var method=type.GetMethod(name,All,null,parameters,null);if(method==null||method.ReturnType!=result||method.IsStatic!=isStatic||method.GetMethodBody()==null)throw new InvalidOperationException("Portal billboard method ABI changed: "+type.FullName+"."+name);return method;}
        static void Certify(MethodInfo method,string hash)
        {using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(method.GetMethodBody().GetILAsByteArray())).Replace("-","")!=hash)throw new InvalidOperationException("Installed billboard/flare IL differs from the audited build: "+method.DeclaringType.FullName+"."+method.Name);}
        void Unpatch()
        {
            installed=false;if(ReferenceEquals(Volatile.Read(ref active),this))Volatile.Write(ref active,null);
            if(harmony!=null)foreach(var method in patched)harmony.Unpatch(method,HarmonyPatchType.All,PatchId);
            patched.Clear();harmony=null;
        }
        public void Dispose(){lock(InstallLock){if(disposed)return;disposed=true;Unpatch();}}
    }
}
