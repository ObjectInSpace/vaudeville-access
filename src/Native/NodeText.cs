using UnityEngine.Accessibility;
using UnityEngine.Bindings;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Sets a node's label/value/hint and pushes it to the native node, replacing the interop property setters.
    ///
    /// Those setters (<c>node.label = …</c>) write the managed field and then call
    /// <c>AccessibilityNodeManager.SetLabel(int, string)</c>, whose interop body pins the string through
    /// <c>Il2CppSystem.ReadOnlySpan&lt;char&gt;.GetPinnableReference()</c> — missing in this build, so it throws
    /// MissingMethodException after the field is already written. The screen reader can still PULL the new text,
    /// but is never told it changed. The engine call underneath, <c>SetLabel_Injected(int, ref ManagedSpanWrapper)</c>,
    /// is bound correctly and only needs a {pointer, length} view of the characters, which a pinned managed string
    /// provides. Null/empty go through Unity's own <c>StringMarshaller.TryMarshalEmptyOrNullString</c>, as the
    /// original binding does.
    /// </summary>
    internal static class NodeText
    {
        public static void SetLabel(AccessibilityNode node, string? text)
        {
            node.m_Label = text;
            if (node.IsInActiveHierarchy()) Push(node.id, text, Field.Label);
        }

        public static void SetValue(AccessibilityNode node, string? text)
        {
            node.m_Value = text;
            if (node.IsInActiveHierarchy()) Push(node.id, text, Field.Value);
        }

        public static void SetHint(AccessibilityNode node, string? text)
        {
            node.m_Hint = text;
            if (node.IsInActiveHierarchy()) Push(node.id, text, Field.Hint);
        }

        private enum Field { Label, Value, Hint }

        private static unsafe void Push(int nodeId, string? text, Field field)
        {
            var span = default(ManagedSpanWrapper);
            if (StringMarshaller.TryMarshalEmptyOrNullString(text, ref span))
            {
                Send(nodeId, ref span, field);
                return;
            }
            fixed (char* chars = text)
            {
                span = new ManagedSpanWrapper(chars, text!.Length);
                Send(nodeId, ref span, field);
            }
        }

        private static void Send(int nodeId, ref ManagedSpanWrapper span, Field field)
        {
            switch (field)
            {
                case Field.Label: AccessibilityNodeManager.SetLabel_Injected(nodeId, ref span); break;
                case Field.Value: AccessibilityNodeManager.SetValue_Injected(nodeId, ref span); break;
                case Field.Hint: AccessibilityNodeManager.SetHint_Injected(nodeId, ref span); break;
            }
        }
    }
}
