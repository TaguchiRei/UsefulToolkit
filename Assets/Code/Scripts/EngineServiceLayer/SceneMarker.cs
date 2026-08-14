using System.Collections.Generic;
using UnityEngine;

namespace Sandbox.EngineService
{
    /// <summary>
    /// シーン遷移テスト用の目印。各シーンに1つずつ置いておくと、
    /// 「今どのシーンが読み込まれているか」と「そのシーンがいつ読み込まれたか」が画面に出る。
    ///
    /// 生存時間が続いていれば前の遷移から読み直されていない、0に戻っていれば読み直された、
    /// という判断ができる。SceneLoadServiceの差分ロードとForceReloadの確認はこれで行う。
    ///
    /// 自身のRendererを色付けするだけで、State/BlackBoardには一切触らない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneMarker : MonoBehaviour
    {
        /// <summary> 表示順を読み込み順に揃えるための登録簿 </summary>
        private static readonly List<SceneMarker> Markers = new();

        [SerializeField] private Color _color = Color.white;

        private float _loadedTime;

        private void Awake()
        {
            _loadedTime = Time.realtimeSinceStartup;

            if (TryGetComponent<Renderer>(out var renderer))
            {
                // テスト用なので生成したマテリアルの後始末はしていない
                renderer.material.color = _color;
            }
        }

        private void OnEnable()
        {
            if (!Markers.Contains(this)) Markers.Add(this);
        }

        private void OnDisable()
        {
            Markers.Remove(this);
        }

        private void OnGUI()
        {
            var index = Markers.IndexOf(this);
            if (index < 0) return;

            var elapsed = Time.realtimeSinceStartup - _loadedTime;
            var rect = new Rect(Screen.width - 260f, 10f + index * 24f, 250f, 22f);

            var previousColor = GUI.color;
            GUI.color = _color;
            GUI.Box(rect, $"{gameObject.scene.name} : 生存 {elapsed:F1} 秒");
            GUI.color = previousColor;
        }
    }
}
