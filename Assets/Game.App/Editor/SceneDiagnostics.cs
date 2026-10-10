using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.App.Editor
{
    /// <summary>场景诊断：列出 missing scripts 与关键组件挂载情况（菜单或 batchmode 均可跑）。</summary>
    public static class SceneDiagnostics
    {
        [MenuItem("Tools/场景诊断/检查 Missing Scripts")]
        public static void CheckAllFromMenu()
        {
            CheckAll(exitOnDone: false);
        }

        public static void CheckAll(bool exitOnDone = true)
        {
            foreach (var scenePath in new[]
                     {
                         "Assets/Scenes/LoginScene.unity",
                         "Assets/Scenes/MainScene.unity",
                         "Assets/Scenes/BattleScene.unity",
                     })
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Debug.Log($"==== {scenePath} ====");

                var allObjects = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                    .Select(t => t.gameObject)
                    .Distinct()
                    .ToArray();

                int missing = 0;
                foreach (var go in allObjects)
                {
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (comp == null)
                        {
                            missing++;
                            Debug.LogWarning($"[Missing] {GetPath(go.transform)}");
                        }
                    }
                }

                foreach (var typeName in new[]
                         {
                             "GameEntry", "LoginController", "LoginView", "DevSettingsController", "DevSettingsView",
                             "MainSceneController", "MainSceneView", "BattleController", "BattleView",
                         })
                {
                    var found = allObjects.Count(go => go.GetComponent(typeName) != null);
                    Debug.Log($"[Component] {typeName}: {found}");
                }

                Debug.Log($"[Missing] total={missing}");
            }

            Debug.Log("[SceneDiagnostics] done");
            if (exitOnDone)
            {
                EditorApplication.Exit(0);
            }
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
