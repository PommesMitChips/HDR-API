using System;
using System.Reflection;
using System.Reflection.Emit;

namespace HDRClientRenderer
{
    // Harmony serializes patch methods by metadata identity. A closed generic
    // reference-type prefix can lose its instantiation and fail only when its
    // cast executes on CoreCLR. Emit one nongeneric method with the exact engine
    // parameter type instead; its helper exchanges ordinary managed objects.
    internal static class DirectCameraCaptureBridge
    {
        internal static MethodInfo Reference(Type argument, MethodInfo selector, string name)
        {
            if (argument == null || argument.IsValueType || selector == null || !selector.IsStatic ||
                selector.ContainsGenericParameters || selector.ReturnType != typeof(object) ||
                selector.GetParameters().Length != 1 || selector.GetParameters()[0].ParameterType != typeof(object))
                throw new InvalidOperationException("Invalid native reference capture bridge.");
            var method = new DynamicMethod(name, typeof(void), new[] { argument.MakeByRefType() },
                typeof(DirectCameraCaptureBridge), true);
            method.DefineParameter(1, ParameterAttributes.None, "__0");
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Call, selector); il.Emit(OpCodes.Castclass, argument);
            il.Emit(OpCodes.Stind_Ref); il.Emit(OpCodes.Ret);
            return method;
        }
        internal static MethodInfo Result(Type result, MethodInfo selector, string name)
        {
            if (result == null || selector == null || !selector.IsStatic || selector.ContainsGenericParameters ||
                selector.ReturnType != typeof(bool) || selector.GetParameters().Length != 1 ||
                selector.GetParameters()[0].ParameterType != typeof(object).MakeByRefType())
                throw new InvalidOperationException("Invalid native result capture bridge.");
            var method = new DynamicMethod(name, typeof(bool), new[] { result.MakeByRefType() },
                typeof(DirectCameraCaptureBridge), true);
            method.DefineParameter(1, ParameterAttributes.None, "__result");
            var il = method.GetILGenerator(); var boxed = il.DeclareLocal(typeof(object));
            var original = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, boxed); il.Emit(OpCodes.Call, selector); il.Emit(OpCodes.Brfalse, original);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, boxed);
            if (result.IsValueType) { il.Emit(OpCodes.Unbox_Any, result); il.Emit(OpCodes.Stobj, result); }
            else { il.Emit(OpCodes.Castclass, result); il.Emit(OpCodes.Stind_Ref); }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(original); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
