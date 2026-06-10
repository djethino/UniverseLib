using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UniverseLib.Runtime;
using UniverseLib.Utility;
#if CPP
#if INTEROP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
#else
using UnhollowerBaseLib;
#endif
#endif

namespace UniverseLib;

public static class ReflectionExtensions
{
#if MONO
    // ILRepack-friendly access to UnityEngine.Object's private m_CachedPtr field.
    // Uses AccessTools.FieldRefAccess (DynamicMethod with skipVisibility) — direct IL access,
    // no boxing of IntPtr, ~100x faster than FieldInfo.GetValue. Critical because
    // ReferenceEqual is called frequently by UniverseLib for object equality checks.
    static readonly AccessTools.FieldRef<UnityEngine.Object, IntPtr> m_CachedPtr_ref
        = AccessTools.FieldRefAccess<UnityEngine.Object, IntPtr>("m_CachedPtr");
#endif

    /// <summary>
    /// Get the true underlying Type of the provided object.
    /// </summary>
    public static Type GetActualType(this object obj)
        => ReflectionUtility.Instance.Internal_GetActualType(obj);

    /// <summary>
    /// Attempt to cast the provided object to it's true underlying Type.
    /// </summary>
    public static object TryCast(this object obj)
        => ReflectionUtility.Instance.Internal_TryCast(obj, ReflectionUtility.Instance.Internal_GetActualType(obj));

    /// <summary>
    /// Attempt to cast the provided object to the provided Type <paramref name="castTo"/>.
    /// </summary>
    public static object TryCast(this object obj, Type castTo)
        => ReflectionUtility.Instance.Internal_TryCast(obj, castTo);

    /// <summary>
    /// Attempt to cast the provided object to Type <typeparamref name="T"/>.
    /// </summary>
    public static T TryCast<T>(this object obj)
    {
        try
        {
            return (T)ReflectionUtility.Instance.Internal_TryCast(obj, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    // ------- Misc extensions --------

    public static Type[] TryGetTypes(this Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch(ReflectionTypeLoadException rtle)
        {
            return ReflectionUtility.TryExtractTypesFromException(rtle);
        }
        catch
        {
            try
            {
                return asm.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return ReflectionUtility.TryExtractTypesFromException(e);
            }
            catch
            {
                return ArgumentUtility.EmptyTypes;
            }
        }
    }

    /// <summary>
    /// Check if the two objects are reference-equal, including checking for UnityEngine.Object-equality and Il2CppSystem.Object-equality.
    /// </summary>
    public static bool ReferenceEqual(this object objA, object objB)
    {
        if (object.ReferenceEquals(objA, objB))
            return true;

        if (objA is UnityEngine.Object unityA && objB is UnityEngine.Object unityB)
        {
#if MONO
            if (unityA && unityB && m_CachedPtr_ref(unityA) == m_CachedPtr_ref(unityB))
                return true;
#else
            if (unityA && unityB && unityA.m_CachedPtr == unityB.m_CachedPtr)
                return true;
#endif
        }

#if CPP
        if (objA is Il2CppSystem.Object cppA && objB is Il2CppSystem.Object cppB
            && cppA.ToIl2CppPointer() == cppB.ToIl2CppPointer())
        {
                return true;
        }
#endif

        return false;
    }

    /// <summary>
    /// Helper to display a simple "{ExceptionType}: {Message}" of the exception, and optionally use the inner-most exception.
    /// </summary>
    public static string ReflectionExToString(this Exception e, bool innerMost = true)
    {
        if (e == null)
        {
            return "The exception was null.";
        }

        if (innerMost)
        {
            e = e.GetInnerMostException();
        }

        return $"{e.GetType()}: {e.Message}";
    }

    /// <summary>
    /// Get the inner-most exception from the provided exception, if there are any. This is recursive.
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    public static Exception GetInnerMostException(this Exception e)
    {
        while (e != null)
        {
            if (e.InnerException == null)
            {
                break;
            }
#if CPP
            if (e.InnerException is System.Runtime.CompilerServices.RuntimeWrappedException)
            {
                break;
            }
#endif
            e = e.InnerException;
        }

        return e;
    }

#if CPP
    /// <summary>
    /// Returns the Pointer to any given Il2Cpp Object.
    /// </summary>
    internal static IntPtr ToIl2CppPointer<T>(this T obj)
        where T : Il2CppObjectBase
    {
        // Get Pointer from Unhollower/Il2CppInterop instead of .Pointer
        // This ensures greater compatibility with any variation in behavior
        return IL2CPP.Il2CppObjectBaseToPtr(obj);
    }

    // Il2CppInterop changed parameters from uint to nint
    // We call IL2CPP.il2cpp_gchandle_get_target using Reflection to automatically handle value conversion
    private static MethodInfo _gcHandleGetTarget = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_gchandle_get_target), BindingFlags.Public | BindingFlags.Static);
    internal static IntPtr GetTargetPtr(this IntPtr gcHandle)
        => (IntPtr)_gcHandleGetTarget.Invoke(null, [gcHandle]);
#endif
}
