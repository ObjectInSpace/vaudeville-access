using System;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VaudevilleAccess.Diagnostics
{
    /// <summary>
    /// Writes every Input System action the game defines, with its bindings, so mod hotkeys are chosen against the
    /// game's real key set rather than guessed (a fallback key once stole the arrows in another mod). Covers every
    /// loaded InputActionAsset plus each PlayerInput's active map. Output: UserData\VaudevilleAccess\dumps\bindings_*.txt.
    /// </summary>
    public static class BindingDump
    {
        public static void Write(string reason)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Input bindings: {reason}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                int assets = 0, bindings = 0;

                foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
                {
                    assets++;
                    sb.AppendLine();
                    sb.AppendLine($"## Asset '{asset.name}' enabled={asset.enabled}");
                    var maps = asset.actionMaps;
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        sb.AppendLine($"  Map '{map.name}' enabled={map.enabled}");
                        var actions = map.actions;
                        for (int a = 0; a < actions.Count; a++)
                        {
                            var action = actions[a];
                            sb.AppendLine($"    Action '{action.name}' type={action.type} enabled={action.enabled}");
                            var list = action.bindings;
                            for (int b = 0; b < list.Count; b++)
                            {
                                var binding = list[b];
                                bindings++;
                                string kind = binding.isComposite ? "composite " : binding.isPartOfComposite ? "  part " : "";
                                sb.AppendLine($"      {kind}{binding.name} path={binding.path} effective={binding.effectivePath} groups={binding.groups}");
                            }
                        }
                    }
                }

                foreach (var player in Resources.FindObjectsOfTypeAll<PlayerInput>())
                {
                    string map = player.currentActionMap != null ? player.currentActionMap.name : "none";
                    sb.AppendLine($"PlayerInput on '{player.gameObject.name}': currentActionMap={map} scheme={player.currentControlScheme}");
                }

                sb.AppendLine();
                sb.AppendLine($"-- {assets} asset(s), {bindings} binding(s).");
                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "VaudevilleAccess", "dumps");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"bindings_{DateTime.Now:HHmmss}_{reason}.txt");
                File.WriteAllText(path, sb.ToString());
                MelonLogger.Msg($"[BindingDump] {assets} asset(s), {bindings} binding(s) -> {path}");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[BindingDump] failed: {e}");
            }
        }
    }
}
