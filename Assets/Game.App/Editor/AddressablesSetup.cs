using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.App.Editor
{
    /// <summary>
    /// M6 Addressables 化（batchmode 可跑，也可菜单触发）：
    /// 1. Resources/{avatars,items,frames,models} 整目录移出 Resources → Assets/RemoteAssets/（AssetDatabase.MoveAsset 保 GUID）；
    /// 2. 全部资产标记为 addressable（地址 = 类别/文件名，与 AssetService key 一致）；
    /// 3. 生成 Assets/Game.Presentation/AssetCatalog.cs（key 常量表，供预热枚举）。
    /// </summary>
    public static class AddressablesSetup
    {
        private static readonly string[] Categories = { "avatars", "items", "frames", "models" };

        private const string RemoteRoot = "Assets/RemoteAssets";
        private const string CatalogPath = "Assets/Game.Presentation/AssetCatalog.cs";

        [MenuItem("Tools/Addressables/迁移 Resources 资源并标记")]
        public static void SetupFromMenu() => Setup(exitOnDone: false);

        /// <summary>-executeMethod 入口（无参数）。</summary>
        public static void SetupForBatch() => Setup(exitOnDone: true);

        public static void Setup(bool exitOnDone = true)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Directory.CreateDirectory("Assets/AddressableAssetsData");
                AssetDatabase.Refresh();
                settings = AddressableAssetSettings.Create(
                    "Assets/AddressableAssetsData", "AddressableAssets", true, true);
                AddressableAssetSettingsDefaultObject.Settings = settings;
            }

            var keys = new List<string>();

            foreach (var category in Categories)
            {
                var srcDir = $"Assets/Resources/{category}";
                var dstDir = $"{RemoteRoot}/{category}";
                if (Directory.Exists(srcDir))
                {
                    Directory.CreateDirectory(dstDir);
                    AssetDatabase.Refresh(); // 让 Unity 感知新目录（MoveAsset 需要父目录 GUID）
                    foreach (var file in Directory.GetFiles(srcDir))
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.EndsWith(".meta"))
                        {
                            continue;
                        }

                        var src = $"{srcDir}/{fileName}".Replace('\\', '/');
                        var dst = $"{dstDir}/{fileName}".Replace('\\', '/');
                        if (File.Exists(dst))
                        {
                            Debug.LogWarning($"[M6] skip (exists): {dst}");
                            continue;
                        }

                        var moved = AssetDatabase.MoveAsset(src, dst);
                        if (!string.IsNullOrEmpty(moved))
                        {
                            Debug.LogError($"[M6] move failed {src}: {moved}");
                        }
                    }

                    // 清掉空目录与 meta
                    if (Directory.GetFiles(srcDir).Length == 0)
                    {
                        File.Delete(srcDir + ".meta");
                        Directory.Delete(srcDir);
                    }
                }

                if (!Directory.Exists(dstDir))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(dstDir))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".meta") ||
                        fileName.EndsWith(".png.meta") ||
                        !IsValidAsset(fileName))
                    {
                        continue;
                    }

                    var assetPath = $"{dstDir}/{fileName}".Replace('\\', '/');
                    var guid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }

                    var key = $"{category}/{Path.GetFileNameWithoutExtension(fileName)}";
                    var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
                    entry.SetAddress(key, false);
                    keys.Add(key);
                }
            }

            AssetDatabase.SaveAssets();
            WriteCatalog(keys);
            AssetDatabase.Refresh();

            Debug.Log($"[M6] addressables setup done, {keys.Count} entries");
            if (exitOnDone)
            {
                EditorApplication.Exit(0);
            }
        }

        private static bool IsValidAsset(string fileName)
        {
            return fileName.EndsWith(".png") || fileName.EndsWith(".jpg") ||
                   fileName.EndsWith(".prefab") || fileName.EndsWith(".fbx") ||
                   fileName.EndsWith(".mat");
        }

        private static void WriteCatalog(List<string> keys)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by AddressablesSetup />");
            sb.AppendLine("namespace Game.Presentation");
            sb.AppendLine("{");
            sb.AppendLine("    public static class AssetCatalog");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly string[] AllKeys = new string[]");
            sb.AppendLine("        {");
            foreach (var key in keys)
            {
                sb.AppendLine($"            \"{key}\",");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(CatalogPath, sb.ToString().Replace("\r\n", "\n"));
            Debug.Log($"[M6] catalog written: {CatalogPath} ({keys.Count} keys)");
        }
    }
}
