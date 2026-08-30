using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UsefulToolkit.EngineService;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// UsefulToolkitの初期化を行う常駐シーンを作成する。
    /// SceneLoaderとUsefulToolkitRuntimeInitializerを配置したシーンを保存し、
    /// そのシーン用のCompositerを生成したうえで、コンパイル完了後にシーンへ組み込む。
    /// </summary>
    public static class PersistentSceneCreator
    {
        private const string RootObjectName = "UsefulToolkit System";
        private const string DefaultSceneName = "UsefulToolkitPersistent";

        // ドメインリロードを挟んで続きを行うため、途中経過をSessionStateへ預ける
        private const string PendingScenePathKey = "UsefulToolkit.PersistentSceneCreator.ScenePath";
        private const string PendingClassNameKey = "UsefulToolkit.PersistentSceneCreator.ClassName";

        /// <summary>
        /// 常駐シーンを作成し、Compositerの生成まで行う。
        /// Compositerコンポーネントの取り付けは、コンパイルが終わってから自動で続行される。
        /// </summary>
        [MenuItem("UsefulToolkit/Create/Persistent Scene", false, 20)]
        public static void CreatePersistentScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string scenePath = EditorUtility.SaveFilePanelInProject(
                "常駐シーンの保存先", DefaultSceneName, "unity",
                "UsefulToolkitの初期化を行う常駐シーンを保存する場所を選んでください。");

            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            var scene = BuildScene(scenePath);
            if (!scene.IsValid())
            {
                return;
            }

            RegisterToBuildSettings(scenePath);

            string saveDirectory = ToDirectory(scenePath);
            var result = GameCompositerGenerator.GenerateTo(scene, saveDirectory);
            string className = Path.GetFileNameWithoutExtension(result.FilePath);

            SessionState.SetString(PendingScenePathKey, scenePath);
            SessionState.SetString(PendingClassNameKey, className);

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 常駐シーンを新規作成し、初期化に必要なコンポーネントを配置して保存する。
        /// </summary>
        /// <param name="scenePath">保存先のシーンパス</param>
        /// <returns>作成したシーン。保存に失敗した場合は無効なシーン</returns>
        private static UnityEngine.SceneManagement.Scene BuildScene(string scenePath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(RootObjectName);
            var sceneLoader = root.AddComponent<SceneLoader>();
            var runtimeInitializer = root.AddComponent<UsefulToolkitRuntimeInitializer>();

            var serializedInitializer = new SerializedObject(runtimeInitializer);
            serializedInitializer.FindProperty("_sceneLoader").objectReferenceValue = sceneLoader;
            serializedInitializer.ApplyModifiedPropertiesWithoutUndo();

            if (EditorSceneManager.SaveScene(scene, scenePath))
            {
                return scene;
            }

            EditorUtility.DisplayDialog("エラー", $"シーンを {scenePath} へ保存できませんでした。", "OK");
            return default;
        }

        /// <summary>
        /// 常駐シーンをBuildSettingsへ登録する。登録済みの場合は何もしない。
        /// 先頭へ挿入すると既存シーンのビルドインデックスがずれるため、追加位置は利用者に選ばせる。
        /// </summary>
        /// <param name="scenePath">登録するシーンパス</param>
        private static void RegisterToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(registered => registered.path == scenePath))
            {
                return;
            }

            var newScene = new EditorBuildSettingsScene(scenePath, true);

            if (scenes.Count == 0)
            {
                scenes.Add(newScene);
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "BuildSettingsへの登録",
                "常駐シーンをBuildSettingsのどこへ追加しますか？\n\n" +
                "先頭へ追加すると起動時に最初に読まれるシーンになりますが、\n" +
                "既存シーンのビルドインデックスが1つずつ後ろへずれます。\n" +
                "ビルドインデックスはSceneGroupの保存内容と対応しているため、\n" +
                "既にSceneGroupを作成している場合は指し先が変わります。",
                "末尾へ追加", "追加しない", "先頭へ追加");

            switch (choice)
            {
                case 0:
                    scenes.Add(newScene);
                    break;
                case 2:
                    scenes.Insert(0, newScene);
                    break;
                default:
                    return;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// コンパイル完了後に、生成されたCompositerを常駐シーンへ取り付ける。
        /// 作成が進行中でない場合は何もしない。
        /// </summary>
        [DidReloadScripts]
        private static void AttachGeneratedCompositer()
        {
            string scenePath = SessionState.GetString(PendingScenePathKey, string.Empty);
            string className = SessionState.GetString(PendingClassNameKey, string.Empty);

            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(className))
            {
                return;
            }

            // 再入を防ぐため、処理の成否に関わらず先に取り下げる
            SessionState.EraseString(PendingScenePathKey);
            SessionState.EraseString(PendingClassNameKey);

            EditorApplication.delayCall += () => AttachCompositerTo(scenePath, className);
        }

        /// <summary>
        /// 指定した常駐シーンへCompositerを取り付け、RuntimeInitializerと繋いで保存する。
        /// </summary>
        /// <param name="scenePath">常駐シーンのパス</param>
        /// <param name="className">生成されたCompositerのクラス名</param>
        private static void AttachCompositerTo(string scenePath, string className)
        {
            var compositerType = TypeCache.GetTypesDerivedFrom<GameCompositer>()
                .FirstOrDefault(type => !type.IsAbstract && type.Name == className);

            if (compositerType == null)
            {
                Debug.LogWarning($"[UsefulToolkit] Compositer [{className}] が見つからなかった為、" +
                                 $"{scenePath} への取り付けを中止しました。" +
                                 "コンパイルエラーを解消してから UsefulToolkit/Generate/Scene Compositer を実行してください。");
                return;
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            var scene = activeScene.path == scenePath
                ? activeScene
                : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            if (!scene.IsValid())
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} を開けなかった為、Compositerの取り付けを中止しました。");
                return;
            }

            var root = scene.GetRootGameObjects().FirstOrDefault(target => target.name == RootObjectName);
            var runtimeInitializer = root == null ? null : root.GetComponent<UsefulToolkitRuntimeInitializer>();

            if (runtimeInitializer == null)
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} に {RootObjectName} が見つからなかった為、" +
                                 "Compositerの取り付けを中止しました。");
                return;
            }

            if (root.GetComponent<GameCompositer>() == null)
            {
                var compositer = root.AddComponent(compositerType) as GameCompositer;

                var serializedCompositer = new SerializedObject(compositer);
                serializedCompositer.FindProperty("_runtimeInitializer").objectReferenceValue = runtimeInitializer;
                serializedCompositer.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[UsefulToolkit] 常駐シーン {scenePath} を作成しました。" +
                      $"({RootObjectName} : {className} / UsefulToolkitRuntimeInitializer / SceneLoader)");
        }

        /// <summary>
        /// シーンパスから、Assets配下のディレクトリパスを取り出す。
        /// </summary>
        /// <param name="scenePath">シーンパス</param>
        private static string ToDirectory(string scenePath)
        {
            string directory = Path.GetDirectoryName(scenePath);
            return string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace('\\', '/');
        }
    }
}
