using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VaudevilleAccess.Diagnostics
{
    /// <summary>
    /// Writes the live UI of every loaded scene to a text file, so screen adapters are written against real scene data
    /// rather than guesses from the decompiled code. For each object under a Canvas: path, active state, components,
    /// text, control state, screen rectangle, and which methods each button's onClick calls. Outside canvases, only
    /// objects carrying game scripts are listed (the Studio's clickable 3D objects among them), with their world
    /// position and whether they implement the game's raycast-target methods.
    ///
    /// Nothing is truncated or filtered by length: a dropped object and an absent one must not look the same.
    /// Files go to UserData\VaudevilleAccess\dumps.
    /// </summary>
    public static class UiDump
    {
        private static readonly string Dir = Path.Combine(MelonEnvironment.UserDataDirectory, "VaudevilleAccess", "dumps");

        /// <summary>Off for players; developer mode turns it on. Screen rebuilds call Write either way.</summary>
        public static bool Enabled { get; set; }

        public static string? Write(string reason)
        {
            if (!Enabled) return null;
            try
            {
                var sb = new StringBuilder();
                int listed = 0, visited = 0;
                string scenes = "";
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    scenes += (i == 0 ? "" : ", ") + SceneManager.GetSceneAt(i).name;

                sb.AppendLine($"UI dump: {reason}");
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}; scenes: {scenes}; screen {Screen.width}x{Screen.height}");
                DescribeEventSystem(sb);

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    sb.AppendLine();
                    sb.AppendLine($"## Scene '{scene.name}' (loaded={scene.isLoaded})");
                    foreach (var root in scene.GetRootGameObjects())
                        Walk(root.transform, 0, false, sb, ref listed, ref visited);
                }

                var persistent = PersistentRoots();
                if (persistent != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("## DontDestroyOnLoad");
                    foreach (var root in persistent)
                        Walk(root.transform, 0, false, sb, ref listed, ref visited);
                }

                sb.AppendLine();
                sb.AppendLine($"-- {visited} object(s) visited, {listed} listed.");

                Directory.CreateDirectory(Dir);
                string safeScenes = string.Join("+", scenes.Split(", ")).Replace(' ', '_');
                string path = Path.Combine(Dir, $"{DateTime.Now:HHmmss}_{safeScenes}_{reason}.txt");
                File.WriteAllText(path, sb.ToString());
                MelonLogger.Msg($"[UiDump] {visited} visited, {listed} listed -> {path}");
                return path;
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[UiDump] failed: {e}");
                return null;
            }
        }

        private static void DescribeEventSystem(StringBuilder sb)
        {
            var es = EventSystem.current;
            if (es == null) { sb.AppendLine("EventSystem: none"); return; }
            string selected = es.currentSelectedGameObject != null ? PathOf(es.currentSelectedGameObject.transform) : "none";
            string first = es.firstSelectedGameObject != null ? PathOf(es.firstSelectedGameObject.transform) : "none";
            string module = es.currentInputModule != null ? es.currentInputModule.GetIl2CppType().FullName : "none";
            sb.AppendLine($"EventSystem: '{es.name}' selected={selected} firstSelected={first} module={module}");
        }

        // Objects marked DontDestroyOnLoad live in a hidden scene; reach it through a temporary object placed there.
        private static Il2CppReferenceArray<GameObject>? PersistentRoots()
        {
            var probe = new GameObject("VaudevilleAccess_UiDumpProbe");
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(probe);
                return probe.scene.GetRootGameObjects();
            }
            catch { return null; }
            finally { UnityEngine.Object.Destroy(probe); }
        }

        private static void Walk(Transform t, int depth, bool underCanvas, StringBuilder sb, ref int listed, ref int visited)
        {
            visited++;
            var go = t.gameObject;
            if (go.name == "VaudevilleAccess_UiDumpProbe") return;

            var components = go.GetComponents<Component>();
            bool hasCanvas = false, hasGameScript = false;
            var types = new StringBuilder();
            foreach (var c in components)
            {
                if (c == null) continue;
                var type = c.GetIl2CppType();
                string name = type.Name;
                if (name == "Canvas") hasCanvas = true;
                if (IsGameScript(type)) hasGameScript = true;
                if (name != "Transform" && name != "RectTransform" && name != "CanvasRenderer")
                    types.Append(types.Length == 0 ? "" : ",").Append(name);
            }

            bool ui = underCanvas || hasCanvas;
            if (ui || hasGameScript)
            {
                listed++;
                string indent = new string(' ', depth * 2);
                string active = go.activeInHierarchy ? "" : (go.activeSelf ? " [inactive-parent]" : " [inactive]");
                sb.Append(indent).Append('"').Append(go.name).Append('"').Append(active);
                if (types.Length > 0) sb.Append(" {").Append(types).Append('}');
                string details = Details(go, components, ui);
                if (details.Length > 0) sb.Append(' ').Append(details);
                sb.AppendLine();
            }

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), depth + 1, ui, sb, ref listed, ref visited);
        }

        // Game classes compile into the global namespace (VSVE.* and Vaudeville.* for the newer code).
        private static bool IsGameScript(Il2CppSystem.Type type)
        {
            string ns = type.Namespace ?? "";
            if (!type.IsSubclassOf(Il2CppType.Of<MonoBehaviour>())) return false;
            return ns.Length == 0 || ns.StartsWith("VSVE") || ns.StartsWith("Vaudeville");
        }

        private static string Details(GameObject go, Il2CppArrayBase<Component> components, bool ui)
        {
            var sb = new StringBuilder();
            try
            {
                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null) sb.Append($"text=\"{Escape(tmp.text)}\" ");
                var legacy = go.GetComponent<Text>();
                if (legacy != null) sb.Append($"text=\"{Escape(legacy.text)}\" ");

                var selectable = go.GetComponent<Selectable>();
                if (selectable != null)
                    sb.Append($"interactable={selectable.interactable} nav={selectable.navigation.mode} ");

                var button = go.GetComponent<Button>();
                if (button != null) sb.Append($"onClick=[{Listeners(button.onClick)}] ");

                var toggle = go.GetComponent<Toggle>();
                if (toggle != null) sb.Append($"isOn={toggle.isOn} ");

                var slider = go.GetComponent<Slider>();
                if (slider != null) sb.Append($"value={slider.value} range={slider.minValue}..{slider.maxValue} whole={slider.wholeNumbers} ");

                var dropdown = go.GetComponent<TMP_Dropdown>();
                if (dropdown != null)
                {
                    sb.Append($"dropdownValue={dropdown.value} options=[");
                    for (int i = 0; i < dropdown.options.Count; i++)
                        sb.Append(i == 0 ? "" : " | ").Append(Escape(dropdown.options[i].text));
                    sb.Append("] ");
                }

                var input = go.GetComponent<TMP_InputField>();
                if (input != null)
                {
                    string placeholder = input.placeholder != null ? input.placeholder.TryCast<TMP_Text>()?.text ?? "" : "";
                    sb.Append($"inputText=\"{Escape(input.text)}\" placeholder=\"{Escape(placeholder)}\" " +
                              $"contentType={input.contentType} lineType={input.lineType} interactable={input.interactable} ");
                }

                var rt = go.GetComponent<RectTransform>();
                if (ui && rt != null) sb.Append($"screen={ScreenRect(rt)} ");

                if (!ui)
                {
                    var p = go.transform.position;
                    sb.Append($"pos=({p.x:0.##},{p.y:0.##},{p.z:0.##}) ");
                    if (go.GetComponent<Collider>() != null) sb.Append("collider ");
                    foreach (var c in components)
                    {
                        if (c == null) continue;
                        var type = c.GetIl2CppType();
                        if (IsGameScript(type) && type.GetMethod("activatePrimary") != null)
                            sb.Append($"raycastTarget={type.Name} ");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append($"<details failed: {e.GetType().Name}: {e.Message}> ");
            }
            return sb.ToString().TrimEnd();
        }

        private static string Listeners(UnityEngine.Events.UnityEventBase evt)
        {
            var sb = new StringBuilder();
            int n = evt.GetPersistentEventCount();
            for (int i = 0; i < n; i++)
            {
                var target = evt.GetPersistentTarget(i);
                string targetName = target != null ? $"{target.GetIl2CppType().Name}@{target.name}" : "null";
                sb.Append(i == 0 ? "" : "; ").Append(targetName).Append('.').Append(evt.GetPersistentMethodName(i));
            }
            if (n == 0) sb.Append("no persistent listeners");
            return sb.ToString();
        }

        private static string ScreenRect(RectTransform rt)
        {
            var r = Native.Frames.Of(rt);
            return $"({r.x:0},{r.y:0} {r.width:0}x{r.height:0})";
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Escape(string? s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
