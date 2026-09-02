using System.Linq;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.Initialize;
using UsefulToolkit.EngineService.Input;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 常駐シーンの生成時に、入力システムのコンポーネントを
    /// "UsefulToolkit System" ルートへ載せる <see cref="IPersistentSceneContributor"/> 実装。
    ///
    /// <see cref="InputDispatcher"/>(InputSystem 経路)を追加し、
    /// <see cref="InputInitializerTemplateProvider"/> が生成した Initializer と結線する。
    /// 生成された派生クラスの型はこのアセンブリから参照できないため、
    /// 抽象基底の <see cref="InputInitializerBase"/> で取得する。
    /// InputActionAsset の割り当ては Inspector での手作業とする。
    /// タッチ入力用の MobileInputEngineService は GraphicRaycaster を要し用途も限られるため、
    /// ここでは追加せず利用者が手動で載せる。
    /// </summary>
    internal sealed class InputPersistentSceneContributor : IPersistentSceneContributor
    {
        public int Order => 0;

        public void Contribute(GameObject systemRoot)
        {
            // スクリプトが欠損しているコンポーネントはnullで返るため、実体のあるものを選ぶ
            var initializer = systemRoot.GetComponents<InputInitializerBase>()
                .FirstOrDefault(target => target != null);

            if (initializer == null)
            {
                Debug.LogWarning(
                    "[UsefulToolkit] InputInitializer が見つからなかった為、InputDispatcher の結線を行いませんでした。" +
                    "生成された InputInitializer のコンパイルが通っているか確認してください。");
                return;
            }

            var dispatcher = systemRoot.GetComponent<InputDispatcher>();
            if (dispatcher == null)
            {
                dispatcher = systemRoot.AddComponent<InputDispatcher>();
            }

            var serializedInitializer = new SerializedObject(initializer);
            var property = serializedInitializer.FindProperty("_inputDispatcher");

            // 利用者が別の InputDispatcher を割り当てている場合はそれを尊重する
            if (property.objectReferenceValue == null)
            {
                property.objectReferenceValue = dispatcher;
                serializedInitializer.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
