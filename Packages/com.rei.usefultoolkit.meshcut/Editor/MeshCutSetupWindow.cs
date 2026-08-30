using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace UsefulToolkit.Editor.MeshCut
{
    /// <summary>
    /// MeshCut を使うためのシーンセットアップと、選択オブジェクトの切断可能化を行うウィンドウ。
    /// </summary>
    public class MeshCutSetupWindow : EditorWindow
    {
        private const string SystemRootName = "MeshCut System";
        private const string CacheObjectName = "MeshDataCache";
        private const string PoolObjectName = "FragmentPool";
        private const string BladeObjectName = "CutBlade";

        // シーン構築
        private GameObject _fragmentPrefab;
        private int _poolCapacity = 20;

        // CuttableObject の既定値
        private Material _capMaterial;
        private PhysicsMaterial _fragmentPhysicsMaterial;
        private int _colliderNum = 10;
        private bool _canMultiCut;
        private bool _moveUnderCache = true;

        private float _baseShrink = 0.95f;
        private float _densityShrinkMin = 0.85f;
        private int _densityThreshold = 10;
        private float _maxRadius = 0.5f;

        private Vector2 _scrollPos;

        [MenuItem("UsefulToolkit/Mesh Cut/Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshCutSetupWindow>("MeshCut Setup");
            window.minSize = new Vector2(360, 620);
            window.titleContent = new GUIContent("MeshCut Setup", EditorGUIUtility.IconContent("Settings").image);
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(10);
            DrawSceneSetupSection();
            EditorGUILayout.Space(10);
            DrawCuttableSection();
            EditorGUILayout.Space(10);
            DrawStatusSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40
            };
            headerStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label("USEFUL MESH CUT SETUP", headerStyle);
            EditorGUILayout.EndVertical();
        }

        // ────────────────────────────── シーン構築 ──────────────────────────────

        private void DrawSceneSetupSection()
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(
                new GUIContent(" 1. シーンの準備", EditorGUIUtility.IconContent("SceneAsset Icon").image),
                EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "MeshDataCache / FragmentPool / CutBlade(切断平面) を生成して相互参照を設定します。",
                MessageType.Info);

            EditorGUILayout.Space(5);

            _fragmentPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("破片プレハブ", "CuttableObject / MeshFilter / Renderer を持つプレハブ"),
                _fragmentPrefab, typeof(GameObject), false);

            _poolCapacity = EditorGUILayout.IntField(
                new GUIContent("プール生成数", "同時に存在できる破片の数。切断対象1つにつき2つ消費します"),
                _poolCapacity);
            _poolCapacity = Mathf.Max(2, _poolCapacity);

            if (_fragmentPrefab != null && _fragmentPrefab.GetComponent<CuttableObject>() == null)
            {
                EditorGUILayout.HelpBox(
                    "破片プレハブに CuttableObject が付いていません。下の「切断可能化」でプレハブにも付与してください。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            if (GUILayout.Button("シーンをセットアップ", GUILayout.Height(30)))
            {
                SetupScene();
            }

            EditorGUILayout.EndVertical();
        }

        private void SetupScene()
        {
            // 1. ルート
            GameObject systemRoot = GameObject.Find(SystemRootName);
            if (systemRoot == null)
            {
                systemRoot = new GameObject(SystemRootName);
                Undo.RegisterCreatedObjectUndo(systemRoot, "Create MeshCut System Root");
            }

            // 2. MeshDataCache (切断対象をこの下に置く)
            MeshDataCache cache = FindFirstObjectByType<MeshDataCache>(FindObjectsInactive.Include);
            if (cache == null)
            {
                var cacheObj = new GameObject(CacheObjectName);
                Undo.RegisterCreatedObjectUndo(cacheObj, "Create MeshDataCache");
                Undo.SetTransformParent(cacheObj.transform, systemRoot.transform, "Parent MeshDataCache");
                cache = Undo.AddComponent<MeshDataCache>(cacheObj);
            }

            // 3. FragmentPool (生成された破片がこの下に溜まる)
            MeshCutObjectPool pool = FindFirstObjectByType<MeshCutObjectPool>(FindObjectsInactive.Include);
            if (pool == null)
            {
                var poolObj = new GameObject(PoolObjectName);
                Undo.RegisterCreatedObjectUndo(poolObj, "Create FragmentPool");
                Undo.SetTransformParent(poolObj.transform, systemRoot.transform, "Parent FragmentPool");
                pool = Undo.AddComponent<MeshCutObjectPool>(poolObj);
            }

            var poolSerialized = new SerializedObject(pool);
            poolSerialized.Update();
            poolSerialized.FindProperty("_generateCapacity").intValue = _poolCapacity;
            if (_fragmentPrefab != null)
            {
                poolSerialized.FindProperty("_prefab").objectReferenceValue = _fragmentPrefab;
            }

            poolSerialized.ApplyModifiedProperties();

            // 4. CutBlade (切断平面)
            MultiCutBlade blade = FindFirstObjectByType<MultiCutBlade>(FindObjectsInactive.Include);
            if (blade == null)
            {
                var bladeObj = new GameObject(BladeObjectName);
                bladeObj.transform.position = new Vector3(0f, 2f, 0f);
                Undo.RegisterCreatedObjectUndo(bladeObj, "Create CutBlade");
                blade = Undo.AddComponent<MultiCutBlade>(bladeObj);
            }

            if (blade.GetComponent<BoxCollider>() == null)
            {
                // ContextMenuの「切断」で切断範囲として使う
                var box = Undo.AddComponent<BoxCollider>(blade.gameObject);
                box.isTrigger = true;
                box.size = new Vector3(5f, 0.1f, 5f);
            }

            var bladeSerialized = new SerializedObject(blade);
            bladeSerialized.Update();
            bladeSerialized.FindProperty("_pool").objectReferenceValue = pool;
            bladeSerialized.ApplyModifiedProperties();

            Selection.activeGameObject = systemRoot;
            Debug.Log("[UsefulToolkit.MeshCut] シーンのセットアップが完了しました。");
        }

        // ────────────────────────────── 切断可能化 ──────────────────────────────

        private void DrawCuttableSection()
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(
                new GUIContent(" 2. オブジェクトの切断可能化", EditorGUIUtility.IconContent("FilterSelectedOnly").image),
                EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Hierarchy で選択したオブジェクトに CuttableObject を付与して設定します。\n" +
                "Rigidbody は既に付いている場合のみ参照されます。",
                MessageType.Info);

            EditorGUILayout.Space(10);

            GUILayout.Label("基本設定", EditorStyles.miniBoldLabel);
            _capMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("断面マテリアル", "切断で露出する断面に使うマテリアル"),
                _capMaterial, typeof(Material), false);

            _fragmentPhysicsMaterial = (PhysicsMaterial)EditorGUILayout.ObjectField(
                new GUIContent("物理マテリアル", "破片の球コライダーに設定する物理マテリアル"),
                _fragmentPhysicsMaterial, typeof(PhysicsMaterial), false);

            _colliderNum = EditorGUILayout.IntField(
                new GUIContent("球コライダー数", "破片の形を近似する球コライダーの数(7以上)"),
                _colliderNum);
            _colliderNum = Mathf.Max(7, _colliderNum);

            _canMultiCut = EditorGUILayout.Toggle(
                new GUIContent("複数回切断を許可", "オフだと1回だけ切断でき、生まれた破片は切れません。この設定は破片へ引き継がれます"),
                _canMultiCut);

            _moveUnderCache = EditorGUILayout.Toggle(
                new GUIContent("MeshDataCacheの子へ移動", "MeshDataCacheの子にないオブジェクトはMeshIdが割り振られず切断結果が壊れます"),
                _moveUnderCache);

            EditorGUILayout.Space(10);

            GUILayout.Label("コライダー詳細設定", EditorStyles.miniBoldLabel);
            _baseShrink = EditorGUILayout.Slider("基本縮小率", _baseShrink, 0.5f, 1f);
            _densityShrinkMin = EditorGUILayout.Slider("低密度時の最小縮小率", _densityShrinkMin, 0.5f, 1f);
            _densityThreshold = EditorGUILayout.IntSlider("密度閾値", _densityThreshold, 1, 50);
            _maxRadius = EditorGUILayout.FloatField("最大半径", _maxRadius);

            if (_capMaterial == null)
            {
                EditorGUILayout.HelpBox("断面マテリアルが未設定です。切断後の断面が正しく描画されません。", MessageType.Warning);
            }

            EditorGUILayout.Space(15);

            int selectedCount = Selection.gameObjects.Length;
            GUI.enabled = selectedCount > 0;

            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };

            if (GUILayout.Button($"選択オブジェクトを切断可能化 ({selectedCount})", btnStyle, GUILayout.Height(40)))
            {
                AddCuttableToSelected();
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("選択オブジェクトから CuttableObject を削除"))
            {
                RemoveCuttableFromSelected();
            }

            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        private void AddCuttableToSelected()
        {
            MeshDataCache cache = FindFirstObjectByType<MeshDataCache>(FindObjectsInactive.Include);

            if (_moveUnderCache && cache == null)
            {
                Debug.LogError("[UsefulToolkit.MeshCut] シーンに MeshDataCache がありません。先に「シーンをセットアップ」を実行してください。");
                return;
            }

            int count = 0;

            foreach (GameObject obj in Selection.gameObjects)
            {
                // プレハブアセットは弾く
                if (EditorUtility.IsPersistent(obj) && PrefabUtility.IsPartOfPrefabAsset(obj))
                {
                    Debug.LogWarning($"[UsefulToolkit.MeshCut] {obj.name} はプレハブアセットのためスキップしました。");
                    continue;
                }

                var meshFilter = obj.GetComponent<MeshFilter>();
                var renderer = obj.GetComponent<Renderer>();

                if (meshFilter == null || renderer == null)
                {
                    Debug.LogWarning($"[UsefulToolkit.MeshCut] {obj.name} は MeshFilter または Renderer が無いためスキップしました。");
                    continue;
                }

                if (meshFilter.sharedMesh == null)
                {
                    Debug.LogWarning($"[UsefulToolkit.MeshCut] {obj.name} は Mesh が未設定のためスキップしました。");
                    continue;
                }

                var cuttable = obj.GetComponent<CuttableObject>();
                if (cuttable == null)
                {
                    cuttable = Undo.AddComponent<CuttableObject>(obj);
                }

                var so = new SerializedObject(cuttable);
                so.Update();

                so.FindProperty("Renderer").objectReferenceValue = renderer;
                so.FindProperty("Mesh").objectReferenceValue = meshFilter;
                so.FindProperty("Rig").objectReferenceValue = obj.GetComponent<Rigidbody>();

                if (_capMaterial != null)
                {
                    so.FindProperty("CapMaterial").objectReferenceValue = _capMaterial;
                }

                so.FindProperty("_canMultiCut").boolValue = _canMultiCut;
                so.FindProperty("_physicsMaterial").objectReferenceValue = _fragmentPhysicsMaterial;
                so.FindProperty("_colliderNum").intValue = _colliderNum;
                so.FindProperty("_baseShrink").floatValue = _baseShrink;
                so.FindProperty("_densityShrinkMin").floatValue = _densityShrinkMin;
                so.FindProperty("_densityThreshold").intValue = _densityThreshold;
                so.FindProperty("_maxRadius").floatValue = _maxRadius;

                so.ApplyModifiedProperties();

                if (_moveUnderCache && !obj.transform.IsChildOf(cache.transform))
                {
                    Undo.SetTransformParent(obj.transform, cache.transform, "Move Under MeshDataCache");
                }

                EditorUtility.SetDirty(obj);
                count++;
            }

            Debug.Log($"[UsefulToolkit.MeshCut] {count} 個のオブジェクトを切断可能化しました。");
        }

        private void RemoveCuttableFromSelected()
        {
            int count = 0;

            foreach (GameObject obj in Selection.gameObjects)
            {
                var cuttable = obj.GetComponent<CuttableObject>();
                if (cuttable == null) continue;

                Undo.DestroyObjectImmediate(cuttable);
                count++;
            }

            Debug.Log($"[UsefulToolkit.MeshCut] {count} 個のオブジェクトから CuttableObject を削除しました。");
        }

        // ────────────────────────────── ステータス ──────────────────────────────

        private void DrawStatusSection()
        {
            MeshDataCache cache = FindFirstObjectByType<MeshDataCache>(FindObjectsInactive.Include);
            MeshCutObjectPool pool = FindFirstObjectByType<MeshCutObjectPool>(FindObjectsInactive.Include);
            MultiCutBlade blade = FindFirstObjectByType<MultiCutBlade>(FindObjectsInactive.Include);

            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(
                new GUIContent(" 3. 状態", EditorGUIUtility.IconContent("console.infoicon").image),
                EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            DrawStatusRow("MeshDataCache", cache != null);
            DrawStatusRow("FragmentPool", pool != null);
            DrawStatusRow("CutBlade", blade != null);

            EditorGUILayout.Space(5);

            int registeredCount = cache != null ? CountCuttables(cache) : 0;
            int strayCount = CountStrayCuttables(cache);

            EditorGUILayout.LabelField("切断可能オブジェクト", $"{registeredCount} 個");

            if (strayCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"MeshDataCache の子になっていない CuttableObject が {strayCount} 個あります。" +
                    "これらは MeshId が割り振られないため、切断結果が壊れます。",
                    MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawStatusRow(string label, bool ready)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(140));

            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = ready ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);

            EditorGUILayout.LabelField(ready ? "● 配置済み" : "○ 未配置", style);
            EditorGUILayout.EndHorizontal();
        }

        private static int CountCuttables(MeshDataCache cache)
        {
            return cache.GetComponentsInChildren<CuttableObject>(true).Length;
        }

        private static int CountStrayCuttables(MeshDataCache cache)
        {
            var all = FindObjectsByType<CuttableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            MeshCutObjectPool pool = FindFirstObjectByType<MeshCutObjectPool>(FindObjectsInactive.Include);

            var stray = new List<CuttableObject>();

            foreach (CuttableObject cuttable in all)
            {
                // プール配下は破片用なのでキャッシュ登録の対象外
                if (pool != null && cuttable.transform.IsChildOf(pool.transform)) continue;
                if (cache != null && cuttable.transform.IsChildOf(cache.transform)) continue;

                stray.Add(cuttable);
            }

            return stray.Count;
        }
    }
}
