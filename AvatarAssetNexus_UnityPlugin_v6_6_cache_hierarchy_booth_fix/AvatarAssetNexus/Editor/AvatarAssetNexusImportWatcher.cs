using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarAssetNexus
{
    public class AvatarAssetNexusImportWatcher : AssetPostprocessor
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".fbx", ".obj", ".blend", ".mat",
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr",
            ".anim", ".controller", ".overridecontroller",
            ".shader", ".shadergraph", ".wav", ".mp3", ".ogg", ".asset", ".unity"
        };

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var paths = importedAssets == null
                ? new List<string>()
                : importedAssets.Where(IsTrackableImportedAsset).Distinct().ToList();

            if (paths.Count == 0) return;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var db = AvatarAssetNexusDatabase.LoadOrCreate();
                    db.SetRecentImportedAssets(paths);
                    db.Save();

                    foreach (var window in Resources.FindObjectsOfTypeAll<AvatarAssetNexusWindow>())
                    {
                        window.Repaint();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            };
        }

        private static bool IsTrackableImportedAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (path.StartsWith("Assets/AvatarAssetNexus/", StringComparison.Ordinal)) return false;
            if (path.StartsWith("Assets/AvatarAssetNexusData/", StringComparison.Ordinal)) return false;
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            if (AssetDatabase.IsValidFolder(path)) return true;
            var ext = Path.GetExtension(path);
            return SupportedExtensions.Contains(ext);
        }
    }
}
