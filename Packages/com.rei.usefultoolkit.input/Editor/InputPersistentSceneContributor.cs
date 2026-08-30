using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.Initialize;
using UsefulToolkit.EngineService.Input;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 常駐シーンの生成時に、入力システムの初期化コンポーネントを
    /// "UsefulToolkit System" ルートへ載せる <see cref="IPersistentSceneContributor"/> 実装。
    ///
    /// <see cref="InputEngineService"/>(InputSystem 経路)と、それを BlackBoard 上の
    /// InputBoard へ繋ぐ <see cref="InputInitializer"/> を追加し、両者を結線する。
    /// InputActionAsset の割り当ては Inspector での手作業とする。
    /// タッチ入力用の MobileInputEngineService は GraphicRaycaster を要し用途も限られるため、
    /// ここでは追加せず利用者が手動で載せる。
    /// </summary>
    internal sealed class InputPersistentSceneContributor : IPersistentSceneContributor
    {
        public int Order => 0;

        public void Contribute(GameObject systemRoot)
        {
            if (systemRoot.GetComponent<InputInitializer>() != null)
            {
                return;
            }

            var engineService = systemRoot.GetComponent<InputEngineService>();
            if (engineService == null)
            {
                engineService = systemRoot.AddComponent<InputEngineService>();
            }

            var initializer = systemRoot.AddComponent<InputInitializer>();

            var serializedInitializer = new SerializedObject(initializer);
            serializedInitializer.FindProperty("_inputEngineService").objectReferenceValue = engineService;
            serializedInitializer.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
