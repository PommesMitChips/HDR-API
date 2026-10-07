using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
namespace HDRClientRenderer
{
    // Explicit non-inlined caller boundary. Runtime function-pointer arguments
    // make the managed calli opaque to caller JIT/R2R inlining. This invokes the
    // current patched renderer entry, preserving other owners' Harmony hooks,
    // without reflection invocation, argument arrays or boxing on any draw.
    internal static class PortalCaptureDrawBridges
    {
        delegate void D0(object context,IntPtr pointer);
        delegate void D2(object context,int a,int b,IntPtr pointer);
        delegate void D3(object context,int a,int b,int c,IntPtr pointer);
        delegate void D4(object context,int a,int b,int c,int d,IntPtr pointer);
        delegate void D5(object context,int a,int b,int c,int d,int e,IntPtr pointer);
        delegate void DI(object context,object buffer,int offset,IntPtr pointer);
        static D0 d0;static D2 d2;static D3 d3;static D4 d4;static D5 d5;static DI di;
        static IntPtr p0,p2,p3,p4,p5,pi;
        static readonly Dictionary<MethodInfo,MethodInfo> bridges=new Dictionary<MethodInfo,MethodInfo>();
        internal static void Initialize(IEnumerable<MethodInfo> methods)
        {
            bridges.Clear();
            foreach(var method in methods)
            {
                string name=method.Name;Type signature;
                switch(name){case "Draw":signature=typeof(D2);break;case "DrawAuto":signature=typeof(D0);break;case "DrawIndexed":signature=typeof(D3);break;case "DrawInstanced":signature=typeof(D4);break;case "DrawIndexedInstanced":signature=typeof(D5);break;case "DrawIndexedInstancedIndirect":signature=typeof(DI);break;default:throw new InvalidOperationException("Unknown portal renderer draw bridge.");}
                var args=signature.GetMethod("Invoke").GetParameters().Select(p=>p.ParameterType).ToArray();var original=method.GetParameters();
                if(method.IsStatic||method.IsVirtual||method.ReturnType!=typeof(void)||args.Length!=original.Length+2)throw new InvalidOperationException("Portal managed draw bridge ABI changed.");
                var thunk=new DynamicMethod("HDR.Portal.ManagedDraw."+name,typeof(void),args,typeof(PortalCaptureDrawBridges),true);var il=thunk.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,method.DeclaringType);
                for(int i=0;i<original.Length;i++){il.Emit(OpCodes.Ldarg,(short)(i+1));if(args[i+1]!=original[i].ParameterType)il.Emit(OpCodes.Castclass,original[i].ParameterType);}
                il.Emit(OpCodes.Ldarg,(short)(args.Length-1));il.EmitCalli(OpCodes.Calli,CallingConventions.Standard|CallingConventions.HasThis,typeof(void),original.Select(p=>p.ParameterType).ToArray(),null);il.Emit(OpCodes.Ret);
                var target=thunk.CreateDelegate(signature);var pointer=method.MethodHandle.GetFunctionPointer();
                switch(name){case "Draw":d2=(D2)target;p2=pointer;break;case "DrawAuto":d0=(D0)target;p0=pointer;break;case "DrawIndexed":d3=(D3)target;p3=pointer;break;case "DrawInstanced":d4=(D4)target;p4=pointer;break;case "DrawIndexedInstanced":d5=(D5)target;p5=pointer;break;case "DrawIndexedInstancedIndirect":di=(DI)target;pi=pointer;break;}
                bridges.Add(method,typeof(PortalCaptureDrawBridges).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static));
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static void Draw(object c,int a,int b){d2(c,a,b,p2);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DrawAuto(object c){d0(c,p0);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DrawIndexed(object c,int a,int b,int d){d3(c,a,b,d,p3);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DrawInstanced(object c,int a,int b,int d,int e){d4(c,a,b,d,e,p4);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DrawIndexedInstanced(object c,int a,int b,int d,int e,int f){d5(c,a,b,d,e,f,p5);}
        [MethodImpl(MethodImplOptions.NoInlining)] static void DrawIndexedInstancedIndirect(object c,object b,int o){di(c,b,o,pi);}
        internal static IEnumerable<CodeInstruction> RedirectCallers(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;foreach(var instruction in instructions)
            {
                var method=instruction.operand as MethodInfo;MethodInfo bridge;
                if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)&&method!=null&&bridges.TryGetValue(method,out bridge))
                {var replaced=new CodeInstruction(OpCodes.Call,bridge);replaced.labels.AddRange(instruction.labels);replaced.blocks.AddRange(instruction.blocks);count++;yield return replaced;}
                else yield return instruction;
            }
            if(count==0)throw new InvalidOperationException("Portal enclosing caller lost its audited render draw call site.");
        }
    }
}
