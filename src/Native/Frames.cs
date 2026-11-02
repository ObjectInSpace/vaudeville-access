using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Screen rectangles for UI elements, in the coordinates accessibility nodes use: pixels, top-left origin. Same
    /// calculation as Unity's accessibility sample (a11y-public-sample, AccessibilityManager.GetFrame).
    /// </summary>
    public static class Frames
    {
        public static Rect Of(RectTransform rt)
        {
            var corners = new Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera? cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                minX = Math.Min(minX, s.x); maxX = Math.Max(maxX, s.x);
                minY = Math.Min(minY, s.y); maxY = Math.Max(maxY, s.y);
            }
            return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        }
    }
}
