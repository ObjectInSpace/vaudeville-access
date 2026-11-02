using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using UnityEngine.Accessibility;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Startup self-check for everything the hand-assembled hierarchy relies on. The game build stripped Unity's public
    /// accessibility API; the mod works by writing private fields and calling engine-internal methods directly. A game
    /// update could remove or rename any of them, and without this check the result would be silence: the one failure
    /// a blind player cannot diagnose. Each missing member is named, so a break is fixable in minutes.
    /// </summary>
    public static class NativeApiCheck
    {
        public static List<string> Run()
        {
            var missing = new List<string>();

            IntPtr hierarchy = ClassOf<AccessibilityHierarchy>(missing);
            Fields(hierarchy, "AccessibilityHierarchy", missing, "nodes", "m_RootNodes");

            IntPtr node = ClassOf<AccessibilityNode>(missing);
            Fields(node, "AccessibilityNode", missing,
                // Native names; the interop exposes the backing fields as _id_k__BackingField etc.
                "childList", "<id>k__BackingField", "<parent>k__BackingField", "m_Hierarchy", "m_IsActive",
                "m_Label", "m_Value", "m_Hint", "m_Role", "m_State", "m_Frame",
                "invoked", "focusChanged", "incremented", "decremented", "dismissed");
            Methods(node, "AccessibilityNode", missing, ("AllocateNative", 0), ("IsInActiveHierarchy", 0));

            IntPtr service = ClassOf(typeof(AccessibilityHierarchyService), missing);
            Methods(service, "AccessibilityHierarchyService", missing, ("get_activeHierarchy", 0), ("set_activeHierarchy", 1));

            IntPtr manager = ClassOf<AccessibilityManager>(missing);
            Methods(manager, "AccessibilityManager", missing,
                ("GetExclusiveLock", 0), ("get_isSupportedPlatform", 0), ("IsScreenReaderEnabled", 0));

            // Engine calls (ICalls) have no method entry to look up. The interop binds each to a static delegate field
            // in the type's static constructor (run by ClassOf); a null field means the engine call is gone.
            ClassOf(typeof(AccessibilityNodeManager), missing);
            EngineCalls(typeof(AccessibilityNodeManager), missing,
                "SetLabel_Injected", "SetValue_Injected", "SetHint_Injected", "SetIsActive");

            IntPtr dispatcher = ClassOf<AssistiveSupport.NotificationDispatcher>(missing);
            Methods(dispatcher, "AssistiveSupport.NotificationDispatcher", missing,
                ("SendLayoutChanged", 1), ("SendScreenChanged", 1));
            IntPtr assistive = ClassOf(typeof(AssistiveSupport), missing);
            Methods(assistive, "AssistiveSupport", missing, ("get_notificationDispatcher", 0));

            return missing;
        }

        private static IntPtr ClassOf<T>(List<string> missing) => ClassOf(typeof(T), missing);

        // Static classes cannot be generic arguments, so the class is looked up by its native name after running the
        // interop type's static constructor (which also binds its engine calls).
        private static IntPtr ClassOf(Type type, List<string> missing)
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                string name = type.DeclaringType != null ? type.DeclaringType.Name + "/" + type.Name : type.Name;
                IntPtr klass = type.DeclaringType != null
                    ? NestedClass(type.DeclaringType.Name, type.Name)
                    : IL2CPP.GetIl2CppClass("UnityEngine.AccessibilityModule.dll", "UnityEngine.Accessibility", type.Name);
                if (klass == IntPtr.Zero) missing.Add($"class {name}");
                return klass;
            }
            catch (Exception e)
            {
                missing.Add($"class {type.Name} ({e.GetBaseException().GetType().Name}: {e.GetBaseException().Message})");
                return IntPtr.Zero;
            }
        }

        private static IntPtr NestedClass(string outer, string inner)
        {
            IntPtr outerClass = IL2CPP.GetIl2CppClass("UnityEngine.AccessibilityModule.dll", "UnityEngine.Accessibility", outer);
            return outerClass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.GetIl2CppNestedType(outerClass, inner);
        }

        private static void EngineCalls(Type interopType, List<string> missing, params string[] names)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            foreach (string name in names)
            {
                var field = interopType.GetField(name + "DelegateField", flags);
                if (field == null || field.GetValue(null) == null)
                    missing.Add($"{interopType.Name}.{name} (engine call)");
            }
        }

        private static void Fields(IntPtr klass, string owner, List<string> missing, params string[] names)
        {
            if (klass == IntPtr.Zero) return;
            foreach (string name in names)
                if (IL2CPP.il2cpp_class_get_field_from_name(klass, name) == IntPtr.Zero)
                    missing.Add($"{owner}.{name} (field)");
        }

        private static void Methods(IntPtr klass, string owner, List<string> missing, params (string name, int args)[] methods)
        {
            if (klass == IntPtr.Zero) return;
            foreach (var (name, args) in methods)
                if (IL2CPP.il2cpp_class_get_method_from_name(klass, name, args) == IntPtr.Zero)
                    missing.Add($"{owner}.{name}/{args} (method)");
        }
    }
}
