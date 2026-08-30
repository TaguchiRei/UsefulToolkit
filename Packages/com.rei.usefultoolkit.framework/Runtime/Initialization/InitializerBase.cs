using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 担当するクラス群の生成と初期化を受け持つクラスの基底。呼ばれる順序は次の通り。
    ///
    /// 1. Awake  : 他のInitializerが初期化に必要とするクラスを生成し、
    ///             GameCompositer.TryRegisterContentで登録する。
    /// 2. Inject : このInitializerの初期化に必要なクラスがCompositerから渡される
    ///             (IInjectableを実装している場合のみ)。
    /// 3. Initialize : 揃った依存を使って初期化を行う。
    ///
    /// BlackBoardはほぼ全てのInitializerが必要とするうえ、IInjectableが扱う
    /// 「Initializer間で受け渡すクラス」とは役割が違うため、コンテナを経由せず
    /// Initializeの引数として直接渡される。
    /// </summary>
    public abstract class InitializerBase : MonoBehaviour
    {
        public bool Initialized { get; internal set; } = false;

        /// <param name="blackBoard">このシーンのBlackBoard。Stateの登録先として使う。</param>
        public virtual void Initialize(IBlackBoard blackBoard)
        {
            Initialized = true;
        }
    }
}
