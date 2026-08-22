using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.EngineService;
using Sandbox.External;
using Sandbox.Initialization;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.SceneSupport;

namespace Sandbox.Editor
{
    /// <summary>
    /// シーン遷移テストの下準備を一括で行うエディタ拡張。
    /// 手作業でシーンを開いて回る必要がないよう、次をまとめて実行する。
    ///
    /// 1. Assets/Level/Scenes/Runtime 以下のシーンをBuild Settingsへ登録する
    /// 2. 各シーンに、どのシーンが読み込まれているか分かる目印(SceneMarker付きのCube)を置く
    /// 3. 常駐シーン(Persistent)にSceneFlowTestBootstrap/SceneFlowTestGuiを置く
    /// 4. シーンenum(BuildScenes)を再生成する
    ///
    /// 何度実行しても同じ状態になるよう、既にある目印は作り直さず位置と色だけ更新する。
    /// </summary>
    public static class SceneFlowTestSetup
    {
        private const string ScenesRootPath = "Assets/Level/Scenes/Runtime";
        private const string PersistentSceneName = "Persistent";
        private const string MarkerObjectSuffix = " Marker";
        private const string BootstrapObjectName = "SceneFlowTest";

        [MenuItem("UsefulToolkit/Test/Setup Scene Flow Test", false, 400)]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scenePaths = CollectScenePaths();

            if (scenePaths.Count == 0)
            {
                Debug.LogError($"[SceneFlowTestSetup] {ScenesRootPath} にシーンが見つかりませんでした。");
                return;
            }

            RegisterToBuildSettings(scenePaths);

            for (var index = 0; index < scenePaths.Count; index++)
            {
                SetupScene(scenePaths[index], index, scenePaths.Count);
            }

            // Build Settingsを書き換えた後なので、ここでenumを作り直す
            SceneEnumGenerator.Generate();

            // 最後に常駐シーンを開いた状態にしておく(ここからプレイモードに入る想定)
            var persistentScenePath = scenePaths.FirstOrDefault(IsPersistentScene);
            if (persistentScenePath != null)
            {
                EditorSceneManager.OpenScene(persistentScenePath, OpenSceneMode.Single);
            }

            Debug.Log($"[SceneFlowTestSetup] 準備が完了しました。対象シーン数: {scenePaths.Count}\n" +
                      $"この後、SceneFlowアセットを作成してノードを組み(Bootノードの常駐シーンに {PersistentSceneName} を指定し、" +
                      $"Bootノードから起動ノードへ線を引く)、{PersistentSceneName} シーンの " +
                      $"{BootstrapObjectName} に割り当ててください。");
        }

        /// <summary>
        /// 対象シーンを収集する。常駐シーンを先頭に置くのは、Build Settingsの並びが
        /// そのままBuildScenes enumの並びになるため、常駐シーンを0番で固定したいから。
        /// </summary>
        private static List<string> CollectScenePaths()
        {
            if (!AssetDatabase.IsValidFolder(ScenesRootPath)) return new List<string>();

            var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { ScenesRootPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToList();

            var persistentIndex = scenePaths.FindIndex(IsPersistentScene);

            if (persistentIndex > 0)
            {
                var persistentScenePath = scenePaths[persistentIndex];
                scenePaths.RemoveAt(persistentIndex);
                scenePaths.Insert(0, persistentScenePath);
            }

            return scenePaths;
        }

        /// <summary>
        /// Build Settingsへ登録する。既存の登録は消さず、足りないものだけ追加する。
        /// </summary>
        private static void RegisterToBuildSettings(IReadOnlyList<string> scenePaths)
        {
            var buildScenes = EditorBuildSettings.scenes.ToList();
            var registeredPaths = new HashSet<string>(buildScenes.Select(scene => scene.path));

            var addedCount = 0;

            foreach (var scenePath in scenePaths)
            {
                if (registeredPaths.Contains(scenePath)) continue;

                buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
                registeredPaths.Add(scenePath);
                addedCount++;
            }

            if (addedCount == 0) return;

            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"[SceneFlowTestSetup] Build Settingsへ {addedCount} 件のシーンを追加しました。");
        }

        /// <summary>
        /// 1シーン分の中身を用意する。
        /// 常駐シーン以外からカメラとライトを取り除くのは、Additiveで複数シーンを読むと
        /// カメラとAudioListenerが重複して警告と描画の混乱が起きるため。
        /// カメラとライトは常駐シーン(Lighting役)側だけに残す。
        /// </summary>
        private static void SetupScene(string scenePath, int index, int sceneCount)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            var isPersistent = IsPersistentScene(scenePath);

            if (!isPersistent)
            {
                RemoveCamerasAndLights(scene);
            }

            var markerObject = EnsureMarkerObject(scene, sceneName, index, sceneCount);

            if (isPersistent)
            {
                EnsureBootstrapObject(scene);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[SceneFlowTestSetup] {sceneName} を準備しました。", markerObject);
        }

        private static void RemoveCamerasAndLights(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var hasCamera = rootObject.GetComponentInChildren<Camera>(true) != null;
                var hasLight = rootObject.GetComponentInChildren<Light>(true) != null;

                if (!hasCamera && !hasLight) continue;

                Object.DestroyImmediate(rootObject);
            }
        }

        /// <summary>
        /// シーンの目印を用意する。既にあれば作り直さず、位置と色だけ更新する。
        /// Cubeの位置をシーンごとにずらすのは、複数シーンが同時に読み込まれたとき
        /// 重なって見分けが付かなくならないようにするため。
        /// </summary>
        private static GameObject EnsureMarkerObject(
            UnityEngine.SceneManagement.Scene scene, string sceneName, int index, int sceneCount)
        {
            var markerName = sceneName + MarkerObjectSuffix;

            var markerObject = scene.GetRootGameObjects()
                .FirstOrDefault(rootObject => rootObject.name == markerName);

            if (markerObject == null)
            {
                markerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                markerObject.name = markerName;
            }

            // 横一列に並べる。カメラの既定位置(0, 1, -10)から見える範囲に収める。
            var offsetX = (index - (sceneCount - 1) * 0.5f) * 1.6f;
            markerObject.transform.position = new Vector3(offsetX, 1f, 0f);
            markerObject.transform.rotation = Quaternion.identity;

            if (!markerObject.TryGetComponent<SceneMarker>(out var marker))
            {
                marker = markerObject.AddComponent<SceneMarker>();
            }

            var serializedMarker = new SerializedObject(marker);
            serializedMarker.FindProperty("_color").colorValue =
                Color.HSVToRGB(sceneCount == 0 ? 0f : (float)index / sceneCount, 0.6f, 1f);
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            return markerObject;
        }

        /// <summary> 常駐シーンにテスト用のBootstrap/GUIを用意し、SceneFlowアセットを割り当てる </summary>
        private static void EnsureBootstrapObject(UnityEngine.SceneManagement.Scene scene)
        {
            var bootstrapObject = scene.GetRootGameObjects()
                .FirstOrDefault(rootObject => rootObject.name == BootstrapObjectName);

            // シーンをSingleで開いているので、newしたGameObjectはそのままこのシーンに入る
            if (bootstrapObject == null)
            {
                bootstrapObject = new GameObject(BootstrapObjectName);
            }

            if (!bootstrapObject.TryGetComponent<SceneFlowTestBootstrap>(out var bootstrap))
            {
                bootstrap = bootstrapObject.AddComponent<SceneFlowTestBootstrap>();
            }

            if (!bootstrapObject.TryGetComponent<SceneFlowTestGui>(out _))
            {
                bootstrapObject.AddComponent<SceneFlowTestGui>();
            }

            AssignFlowAsset(bootstrap);
        }

        /// <summary>
        /// プロジェクト内のSceneFlowアセットをBootstrapへ割り当てる。
        /// 見つからない場合と複数ある場合は割り当てず、警告を出して手動指定に任せる。
        /// </summary>
        private static void AssignFlowAsset(SceneFlowTestBootstrap bootstrap)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(SandboxSceneFlowAsset)}");

            if (guids.Length != 1)
            {
                Debug.LogWarning(
                    $"[SceneFlowTestSetup] SceneFlowアセットが {guids.Length} 件見つかりました。" +
                    $"1件のときだけ自動で割り当てます。{BootstrapObjectName} へ手動で指定してください。");
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var flowAsset = AssetDatabase.LoadAssetAtPath<SandboxSceneFlowAsset>(assetPath);

            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("_flowAsset").objectReferenceValue = flowAsset;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[SceneFlowTestSetup] SceneFlowアセットを割り当てました: {assetPath}");
        }

        private static bool IsPersistentScene(string scenePath)
        {
            return Path.GetFileNameWithoutExtension(scenePath) == PersistentSceneName;
        }
    }
}
