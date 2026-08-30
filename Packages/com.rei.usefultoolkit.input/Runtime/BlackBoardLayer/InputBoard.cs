using System.Collections.Generic;
using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 入力をEventBoard形式で公開するChildBoard。
    /// Application側はGetChannelで取得したIEventChannelをRegisterするだけでよく、
    /// InputSystemやInputActionAssetの存在を一切意識しない。
    ///
    /// キーにはEnum(具体的にはInputActionEnumGeneratorが生成するActionMaps/XxxActions)を使う。
    /// 生成されたenumはコンシューマー側プロジェクト(Assets/配下)に出力されるため、
    /// このパッケージ自体はどのenum型にも依存しない——呼び出し側が自分のenum値を渡すだけでよい。
    /// </summary>
    public sealed class InputBoard : ChildEventBoardBase
    {
        private readonly Dictionary<(Enum map, Enum action), object> _channels = new();
        private readonly ActionChannel<Enum> _actionMapChannel = new();

        /// <summary>Application側: 指定したAction用のチャンネルを取得しRegisterする。</summary>
        public IActionChannel<InputContext<TValue>> GetChannel<TValue>(Enum map, Enum action)
            where TValue : unmanaged
        {
            return GetOrCreateChannel<TValue>(map, action);
        }

        /// <summary>
        /// Application側: 現在有効にするActionMapを切り替える。ActionMapを「今どれか」という
        /// 値として保持するState(Getter)は持たず、切替の発生そのものをEventBoardで通知する
        /// 一方向の設計とする——このイベントの発行者はApplicationのみとする(Single Writer相当)。
        /// リプレイ機構はないため、InputEngineService側がGetActionMapChannel().Registerを
        /// 済ませる前に呼ばれた切替は届かない。Initialization層はInputEngineServiceの初期化を
        /// Applicationより先に済ませること。
        /// </summary>
        public void SwitchActionMap(Enum map)
        {
            _actionMapChannel.Invoke(map);
        }

        /// <summary>EngineServiceLayer側: ActionMap切替イベントを購読する。</summary>
        public IActionChannel<Enum> GetActionMapChannel()
        {
            return _actionMapChannel;
        }

        /// <summary>
        /// EngineServiceLayerに属する入力ソース(IExternalInputSource&lt;TValue&gt;)を、指定した
        /// (map, action)のチャンネルへ橋渡しする。InputEngineService(InputSystem由来)や
        /// MobileInputEngineService(タッチ由来)を含め、すべての入力ソースはこの経路からのみ
        /// InputBoardへ値を送り込める。EventChannel.Publishを直接呼ぶのはこのメソッド内の
        /// ブリッジだけで、IExternalInputSource実装側はチャンネルへの参照を持たない。
        /// </summary>
        public IDisposable RegisterExternalInputSource<TValue>(Enum map, Enum action, IExternalInputSource<TValue> source)
            where TValue : unmanaged
        {
            var channel = GetOrCreateChannel<TValue>(map, action);

            void Handler(InputContext<TValue> context) => channel.Invoke(context);

            source.RegisterAction(Handler);

            return new BoardDispose(() => source.UnRegisterAction(Handler));
        }

        private ActionChannel<InputContext<TValue>> GetOrCreateChannel<TValue>(Enum map, Enum action)
            where TValue : unmanaged
        {
            var key = (map, action);
            if (_channels.TryGetValue(key, out var raw) && raw is ActionChannel<InputContext<TValue>> channel)
                return channel;

            var created = new ActionChannel<InputContext<TValue>>();
            _channels[key] = created;
            return created;
        }
    }
}
