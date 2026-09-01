using System;
using System.Collections.Generic;
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
    /// 処理はドメインリロードを2回挟んだ3段階に分かれる。
    ///
    /// 1. シーンの骨組み(SceneLoader / UsefulToolkitRuntimeInitializer)を保存し、
    ///    <see cref="IInitializerTemplateProvider"/> が提供するInitializerのソースを生成する。
    /// 2. 生成されたInitializerをルートへ取り付け、<see cref="IPersistentSceneContributor"/> に
    ///    結線させたうえで、そのシーン用のCompositorを生成する。
    /// 3. 生成されたCompositorをシーンへ取り付ける。
    ///
    /// 段階1・2はソースを書き出した場合のみコンパイルを挟む。書き出しが無かった場合は
    /// <see cref="DidReloadScriptsAttribute"/> が呼ばれないため、その場で次の段階へ進む。
    /// </summary>
    public static class PersistentSceneCreator
    {
        private const string RootObjectName = "UsefulToolkit System";
        private const string DefaultSceneName = "UsefulToolkitPersistent";

        // ドメインリロードを挟んで続きを行うため、途中経過をSessionStateへ預ける
        private const string PendingStageKey = "UsefulToolkit.PersistentSceneCreator.Stage";
        private const string PendingScenePathKey = "UsefulToolkit.PersistentSceneCreator.ScenePath";
        private const string PendingDirectoryKey = "UsefulToolkit.PersistentSceneCreator.Directory";
        private const string PendingInitializerNamesKey = "UsefulToolkit.PersistentSceneCreator.InitializerNames";
        private const string PendingClassNameKey = "UsefulToolkit.PersistentSceneCreator.ClassName";

        /// <summary>生成したInitializerの取り付けとCompositor生成を行う段階。</summary>
        private const string StageAttachInitializers = "AttachInitializers";

        /// <summary>生成したCompositorの取り付けを行う段階。</summary>
        private const string StageAttachCompositor = "AttachCompositor";

        /// <summary>生成したInitializerのクラス名をSessionStateへ詰める際の区切り。</summary>
        private const char NameSeparator = ';';

        /// <summary>
        /// 常駐シーンを作成し、Initializerとそのシーン用のCompositorの生成まで行う。
        /// コンポーネントの取り付けは、コンパイルが終わってから自動で続行される。
        /// </summary>
        [MenuItem("UsefulToolkit/Scene/GenerateUsefulPersistentScene", false, 20)]
        public static void CreatePersistentScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string existingScenePath = FindExistingPersistentScenePath();
            if (!string.IsNullOrEmpty(existingScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "常駐シーンの更新",
                        $"既存の常駐シーン {existingScenePath} を最新状態に更新します。\n" +
                        "保存先の選択は行わず、このシーンとそのCompositorを再生成します。",
                        "更新", "キャンセル"))
                {
                    return;
                }

                var existingScene = RebuildExistingScene(existingScenePath);
                if (!existingScene.IsValid())
                {
                    return;
                }

                RegisterToBuildSettings(existingScenePath);
                GenerateInitializers(
                    existingScene,
                    existingScenePath,
                    FindExistingCompositorDirectory(existingScene.name, existingScenePath));
                return;
            }

            string scenePath = EditorUtility.SaveFilePanelInProject(
                "常駐シーンの保存先", DefaultSceneName, "unity",
                "UsefulToolkitの初期化を行う常駐シーンを保存する場所を選んでください。");

            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            string compositorDirectory = GameCompositorGenerator.SelectSaveDirectory();
            if (compositorDirectory == null)
            {
                return;
            }

            var scene = BuildScene(scenePath);
            if (!scene.IsValid())
            {
                return;
            }

            RegisterToBuildSettings(scenePath);
            GenerateInitializers(scene, scenePath, compositorDirectory);
        }

        /// <summary>
        /// 段階1。<see cref="IInitializerTemplateProvider"/> が提供するInitializerのソースを生成し、
        /// コンパイル完了後の取り付けを予約する。
        /// </summary>
        /// <param name="scene">保存済みの常駐シーン</param>
        /// <param name="scenePath">常駐シーンのパス</param>
        /// <param name="saveDirectory">スクリプトを生成するAssets配下のディレクトリ</param>
        private static void GenerateInitializers(
            UnityEngine.SceneManagement.Scene scene, string scenePath, string saveDirectory)
        {
            string compositorClassName = GameCompositorGenerator.ToCompositorClassName(scene.name);
            var result = InitializerTemplateGenerator.Generate(saveDirectory, compositorClassName, scene.name);

            // 1件も書き出していなければコンパイルが走らず[DidReloadScripts]も呼ばれないので、その場で続ける
            if (result.WrittenCount == 0)
            {
                AttachInitializersTo(scenePath, saveDirectory, result.ClassNames);
                return;
            }

            SessionState.SetString(PendingStageKey, StageAttachInitializers);
            SessionState.SetString(PendingScenePathKey, scenePath);
            SessionState.SetString(PendingDirectoryKey, saveDirectory);
            SessionState.SetString(
                PendingInitializerNamesKey, string.Join(NameSeparator.ToString(), result.ClassNames));

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 段階2。生成されたInitializerを常駐シーンのルートへ取り付け、Contributorに結線させたうえで
        /// Compositorを生成する。
        /// </summary>
        /// <param name="scenePath">常駐シーンのパス</param>
        /// <param name="saveDirectory">スクリプトを生成するAssets配下のディレクトリ</param>
        /// <param name="classNames">生成対象として宣言されたInitializerのクラス名</param>
        private static void AttachInitializersTo(
            string scenePath, string saveDirectory, IReadOnlyList<string> classNames)
        {
            var scene = OpenOrGetScene(scenePath);
            if (!scene.IsValid())
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} を開けなかった為、Initializerの取り付けを中止しました。");
                return;
            }

            var root = scene.GetRootGameObjects().FirstOrDefault(target => target.name == RootObjectName);
            if (root == null)
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} に {RootObjectName} が見つからなかった為、" +
                                 "Initializerの取り付けを中止しました。");
                return;
            }

            foreach (var className in classNames)
            {
                var initializerType = TypeCache.GetTypesDerivedFrom<InitializerBase>()
                    .FirstOrDefault(type => !type.IsAbstract && type.Name == className);

                if (initializerType == null)
                {
                    Debug.LogWarning(
                        $"[UsefulToolkit] 生成したInitializer [{className}] が見つかりませんでした。" +
                        "コンパイルエラーを解消してから再実行してください。");
                    continue;
                }

                if (root.GetComponent(initializerType) == null)
                {
                    root.AddComponent(initializerType);
                }
            }

            // 生成したInitializerが載った状態でContributorを呼ぶ。Contributorは生成型を参照できない為、
            // 抽象基底型でGetComponentして結線する前提になっている。
            InvokeContributors(root);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} を保存できなかった為、Compositorの生成を中止しました。");
                return;
            }

            GenerateCompositor(scene, scenePath, saveDirectory);
        }

        /// <summary>
        /// 段階3の準備。Compositorを生成し、コンパイル完了後の取り付けを予約する。
        /// </summary>
        /// <param name="scene">保存済みの常駐シーン</param>
        /// <param name="scenePath">常駐シーンのパス</param>
        /// <param name="saveDirectory">Compositorスクリプトを生成するAssets配下のディレクトリ</param>
        private static void GenerateCompositor(
            UnityEngine.SceneManagement.Scene scene, string scenePath, string saveDirectory)
        {
            string filePath = Path
                .Combine(saveDirectory, GameCompositorGenerator.ToCompositorClassName(scene.name) + ".cs")
                .Replace('\\', '/');

            string before = File.Exists(filePath) ? File.ReadAllText(filePath) : null;

            var result = GameCompositorGenerator.GenerateTo(scene, saveDirectory);
            string className = Path.GetFileNameWithoutExtension(result.FilePath);

            // 内容が変わっていなければコンパイルが走らず[DidReloadScripts]も呼ばれないので、その場で続ける
            bool changed = before == null || before != File.ReadAllText(result.FilePath);

            if (!changed)
            {
                AttachCompositorTo(scenePath, className);
                return;
            }

            SessionState.SetString(PendingStageKey, StageAttachCompositor);
            SessionState.SetString(PendingScenePathKey, scenePath);
            SessionState.SetString(PendingClassNameKey, className);

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 生成済みのCompositorスクリプトが置かれているディレクトリを探す。
        /// シーン名から求まるクラス名のMonoScriptを検索し、その所在フォルダを返す。
        /// 見つからない場合はシーンと同じフォルダを返す。
        /// </summary>
        /// <param name="sceneName">常駐シーンの名前</param>
        /// <param name="scenePath">常駐シーンのパス</param>
        private static string FindExistingCompositorDirectory(string sceneName, string scenePath)
        {
            string className = GameCompositorGenerator.ToCompositorClassName(sceneName);

            foreach (var guid in AssetDatabase.FindAssets($"{className} t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != className)
                {
                    continue;
                }

                var type = AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();
                if (type != null && typeof(GameCompositor).IsAssignableFrom(type))
                {
                    return ToDirectory(path);
                }
            }

            return ToDirectory(scenePath);
        }

        /// <summary>
        /// プロジェクト内から、<see cref="UsefulToolkitRuntimeInitializer"/> を参照しているシーンを探す。
        /// シーンを開かず、シーンファイルのテキストがそのスクリプトのGUIDを含むかで判定する。
        /// </summary>
        /// <returns>
        /// 該当シーンが1つだけ見つかった場合はそのパス。0件、または複数件の場合はnull。
        /// 複数件のときはコンソールへ一覧を警告出力する。
        /// </returns>
        private static string FindExistingPersistentScenePath()
        {
            string initializerGuid = FindRuntimeInitializerScriptGuid();
            if (string.IsNullOrEmpty(initializerGuid))
            {
                return null;
            }

            string guidToken = "guid: " + initializerGuid;

            var matched = AssetDatabase.FindAssets("t:SceneAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)
                               && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Where(path => File.Exists(path) && File.ReadAllText(path).Contains(guidToken))
                .Distinct()
                .ToList();

            if (matched.Count == 1)
            {
                return matched[0];
            }

            if (matched.Count > 1)
            {
                Debug.LogWarning(
                    "[UsefulToolkit] UsefulToolkitRuntimeInitializer を含むシーンが複数見つかりました。" +
                    "1つへ統合してから再実行してください。\n" +
                    string.Join("\n", matched));
            }

            return null;
        }

        /// <summary>
        /// <see cref="UsefulToolkitRuntimeInitializer"/> の MonoScript アセットのGUIDを取得する。
        /// </summary>
        private static string FindRuntimeInitializerScriptGuid()
        {
            foreach (var guid in AssetDatabase.FindAssets($"{nameof(UsefulToolkitRuntimeInitializer)} t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                if (script != null && script.GetClass() == typeof(UsefulToolkitRuntimeInitializer))
                {
                    return guid;
                }
            }

            return null;
        }

        /// <summary>
        /// 既存の常駐シーンを開き、ルートと初期化コンポーネントの不足分を補ってから保存する。
        /// 既に付いているコンポーネントや、Contributorが過去に追加した要素はそのまま残す。
        /// </summary>
        /// <param name="scenePath">既存の常駐シーンのパス</param>
        /// <returns>開いたシーン。開けなかった、または保存できなかった場合は無効なシーン</returns>
        private static UnityEngine.SceneManagement.Scene RebuildExistingScene(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("エラー", $"シーン {scenePath} を開けませんでした。", "OK");
                return default;
            }

            var root = scene.GetRootGameObjects().FirstOrDefault(target => target.name == RootObjectName);
            if (root == null)
            {
                root = new GameObject(RootObjectName);
            }

            var sceneLoader = root.GetComponent<SceneLoader>();
            if (sceneLoader == null)
            {
                sceneLoader = root.AddComponent<SceneLoader>();
            }

            var runtimeInitializer = root.GetComponent<UsefulToolkitRuntimeInitializer>();
            if (runtimeInitializer == null)
            {
                runtimeInitializer = root.AddComponent<UsefulToolkitRuntimeInitializer>();
            }

            var serializedInitializer = new SerializedObject(runtimeInitializer);
            serializedInitializer.FindProperty("_sceneLoader").objectReferenceValue = sceneLoader;
            serializedInitializer.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene, scenePath))
            {
                return scene;
            }

            EditorUtility.DisplayDialog("エラー", $"シーンを {scenePath} へ保存できませんでした。", "OK");
            return default;
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
        /// <see cref="IPersistentSceneContributor"/> を実装した全クラスを列挙し、
        /// 常駐シーンのルートへパッケージ固有のコンポーネントを追加させる。
        /// framework から input などの上位パッケージを参照せずに、
        /// 各パッケージが自分の Initializer を常駐シーンへ載せられるようにするための経路。
        /// この後 <see cref="GameCompositorGenerator"/> が走るため、ここで追加された
        /// InitializerBase は生成される Compositor に取り込まれる。
        /// </summary>
        /// <param name="root">常駐シーンのルート GameObject</param>
        private static void InvokeContributors(GameObject root)
        {
            var contributors = TypeCache.GetTypesDerivedFrom<IPersistentSceneContributor>()
                .Where(type => !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (IPersistentSceneContributor)Activator.CreateInstance(type))
                .OrderBy(contributor => contributor.Order);

            foreach (var contributor in contributors)
            {
                try
                {
                    contributor.Contribute(root);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[UsefulToolkit] {contributor.GetType().FullName} の常駐シーンへの寄与に失敗しました。");
                    Debug.LogException(exception);
                }
            }
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
        /// コンパイル完了後に、中断していた段階の続きを実行する。
        /// 作成が進行中でない場合は何もしない。
        /// </summary>
        [DidReloadScripts]
        private static void ContinuePendingCreation()
        {
            string stage = SessionState.GetString(PendingStageKey, string.Empty);
            if (string.IsNullOrEmpty(stage))
            {
                return;
            }

            string scenePath = SessionState.GetString(PendingScenePathKey, string.Empty);
            string saveDirectory = SessionState.GetString(PendingDirectoryKey, string.Empty);
            string initializerNames = SessionState.GetString(PendingInitializerNamesKey, string.Empty);
            string className = SessionState.GetString(PendingClassNameKey, string.Empty);

            // 処理の成否に関わらず、SessionStateのキーを先に消す
            SessionState.EraseString(PendingStageKey);
            SessionState.EraseString(PendingScenePathKey);
            SessionState.EraseString(PendingDirectoryKey);
            SessionState.EraseString(PendingInitializerNamesKey);
            SessionState.EraseString(PendingClassNameKey);

            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            switch (stage)
            {
                case StageAttachInitializers:
                    var classNames = initializerNames.Split(
                        new[] { NameSeparator }, StringSplitOptions.RemoveEmptyEntries);
                    EditorApplication.delayCall +=
                        () => AttachInitializersTo(scenePath, saveDirectory, classNames);
                    break;

                case StageAttachCompositor:
                    if (!string.IsNullOrEmpty(className))
                    {
                        EditorApplication.delayCall += () => AttachCompositorTo(scenePath, className);
                    }

                    break;
            }
        }

        /// <summary>
        /// 指定したシーンを取得する。既にアクティブなら開き直さない。
        /// </summary>
        /// <param name="scenePath">開くシーンのパス</param>
        private static UnityEngine.SceneManagement.Scene OpenOrGetScene(string scenePath)
        {
            var activeScene = EditorSceneManager.GetActiveScene();

            return activeScene.path == scenePath
                ? activeScene
                : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// 指定した常駐シーンへCompositorを取り付け、RuntimeInitializerと繋いで保存する。
        /// </summary>
        /// <param name="scenePath">常駐シーンのパス</param>
        /// <param name="className">生成されたCompositorのクラス名</param>
        private static void AttachCompositorTo(string scenePath, string className)
        {
            var CompositorType = TypeCache.GetTypesDerivedFrom<GameCompositor>()
                .FirstOrDefault(type => !type.IsAbstract && type.Name == className);

            if (CompositorType == null)
            {
                Debug.LogWarning($"[UsefulToolkit] Compositor [{className}] が見つからなかった為、" +
                                 $"{scenePath} への取り付けを中止しました。" +
                                 "コンパイルエラーを解消してから UsefulToolkit/Generate/Scene Compositor を実行してください。");
                return;
            }

            var scene = OpenOrGetScene(scenePath);

            if (!scene.IsValid())
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} を開けなかった為、Compositorの取り付けを中止しました。");
                return;
            }

            var root = scene.GetRootGameObjects().FirstOrDefault(target => target.name == RootObjectName);
            var runtimeInitializer = root == null ? null : root.GetComponent<UsefulToolkitRuntimeInitializer>();

            if (runtimeInitializer == null)
            {
                Debug.LogWarning($"[UsefulToolkit] {scenePath} に {RootObjectName} が見つからなかった為、" +
                                 "Compositorの取り付けを中止しました。");
                return;
            }

            if (root.GetComponent<GameCompositor>() == null)
            {
                var Compositor = root.AddComponent(CompositorType) as GameCompositor;

                var serializedCompositor = new SerializedObject(Compositor);
                serializedCompositor.FindProperty("_runtimeInitializer").objectReferenceValue = runtimeInitializer;
                serializedCompositor.ApplyModifiedPropertiesWithoutUndo();
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
