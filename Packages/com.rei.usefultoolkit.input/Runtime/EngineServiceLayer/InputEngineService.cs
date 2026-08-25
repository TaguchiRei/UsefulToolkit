using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulToolkit.Architecture;
using UsefulToolkit.BlackBoard.Input;

namespace UsefulToolkit.EngineService.Input
{
    /// <summary>
    /// InputActionAssetを直接扱うEngineServiceLayer。BindしたAction分だけ、対応する
    /// InputBoardのチャンネルへPublishする——アセット内の全Actionを無条件に橋渡しはしない
    /// (Applicationにとって意味のある入力だけを、Initialization層が明示的に選んでBindする)。
    ///
    /// InputBoardはBlackBoardLayer側のプレーンなC#クラスなのでInspectorからは割り当てられない。
    /// Initialization層がBlackBoardから取得したインスタンスを、Initializeより前に
    /// SetInputBoardで渡すこと。
    ///
    /// ActionMap切替はInputBoard.GetActionMapChannelを購読する形で受け取る(State不使用)。
    /// EventBoardにはリプレイ機構がないため、Applicationが先にSwitchActionMapを呼んでいても
    /// このInitializeが済むまでは届かない——Initialization層はInputEngineServiceの初期化順序を
    /// Applicationより先にすること。
    /// </summary>
    public sealed class InputEngineService : InitializableMonoBehaviour
    {
        [SerializeField] private InputActionAsset _actionAsset;

        private InputBoard _inputBoard;
        private readonly List<IDisposable> _bindings = new();
        private IDisposable _actionMapSubscription;

        public void SetInputBoard(InputBoard inputBoard)
        {
            _inputBoard = inputBoard;
        }

        public override void Initialize()
        {
            base.Initialize();

            if (_inputBoard == null)
            {
                Debug.LogError(
                    $"[InputEngineService] InputBoardが設定されていません。Initialize()より前にSetInputBoardを呼んでください。");
                return;
            }

            if (_actionAsset == null)
            {
                Debug.LogError("[InputEngineService] InputActionAssetが設定されていません。");
                return;
            }

            _actionAsset.Enable();

            _actionMapSubscription = _inputBoard.GetActionMapChannel().Register(SwitchActionMap);
        }

        private void OnDestroy()
        {
            foreach (var binding in _bindings)
            {
                binding.Dispose();
            }

            _bindings.Clear();
            _actionMapSubscription?.Dispose();

            if (_actionAsset != null) _actionAsset.Disable();
        }

        /// <summary>
        /// 指定したActionMapのみを有効にする。ActionMap切替イベントを受けて呼ばれる、
        /// EngineServiceLayer内部専用の処理——Applicationから直接呼び出す経路は用意しない。
        /// </summary>
        private void SwitchActionMap(Enum map)
        {
            foreach (var actionMap in _actionAsset.actionMaps)
            {
                actionMap.Disable();
            }

            var target = _actionAsset.FindActionMap(map.ToString());
            if (target == null)
            {
                Debug.LogWarning($"[InputEngineService] ActionMap '{map}' が見つかりませんでした。");
                return;
            }

            target.Enable();
        }

        /// <summary>
        /// 指定したActionMap/ActionをInputBoard上の対応するチャンネルへ橋渡しする。
        /// InputActionEnumGeneratorが生成したActionMaps/XxxActions enumを渡すことで、
        /// 呼び出し側は文字列を一切書かずに済む。
        /// </summary>
        public void Bind<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            if (_inputBoard == null || _actionAsset == null) return;

            var inputAction = _actionAsset.FindActionMap(map.ToString())?.FindAction(action.ToString());
            if (inputAction == null)
            {
                Debug.LogWarning($"[InputEngineService] {map}.{action} が見つかりませんでした。");
                return;
            }

            var source = new InputActionSource<TValue>(inputAction);
            _bindings.Add(_inputBoard.RegisterExternalInputSource(map, action, source));
        }

        /// <summary>InputActionのstarted/performed/canceledをIExternalInputSourceとして橋渡しするアダプタ。</summary>
        private sealed class InputActionSource<TValue> : IExternalInputSource<TValue> where TValue : unmanaged
        {
            private readonly InputAction _action;
            private Action<InputContext<TValue>> _handler;

            public InputActionSource(InputAction action)
            {
                _action = action;
            }

            public void RegisterAction(Action<InputContext<TValue>> handler)
            {
                _handler = handler;
                _action.started += OnCallback;
                _action.performed += OnCallback;
                _action.canceled += OnCallback;
            }

            public void UnRegisterAction(Action<InputContext<TValue>> handler)
            {
                _action.started -= OnCallback;
                _action.performed -= OnCallback;
                _action.canceled -= OnCallback;
                _handler = null;
            }

            private void OnCallback(InputAction.CallbackContext ctx) =>
                _handler?.Invoke(new InputContext<TValue>(ctx.phase, ctx.ReadValue<TValue>()));
        }
    }
}
