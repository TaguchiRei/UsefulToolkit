using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.EngineService.Input
{
    /// <summary>
    /// InputActionAssetを直接扱うEngineServiceLayer。
    /// InputStateの内容をInputActionAssetへ反映し、InputActionのコールバックを
    /// 入力ソースとしてInputStateのチャンネルへ橋渡しする。
    ///
    /// InputStateはBlackBoardから取得するため、Applicationの初期化
    /// (InputStateの生成と登録)がこのクラスのInitializeより先に済んでいる必要がある。
    /// </summary>
    public sealed class InputDispatcher : InitializableMonoBehaviour, IInputEngineBridge
    {
        [SerializeField] private InputActionAsset _actionAsset;

        private IInputState _inputState;
        private readonly List<IDisposable> _bindings = new();

        /// <summary>
        /// InputStateの取得元となるBlackBoardを渡す。Initializeより前に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">InputBoardを保持しているBlackBoard</param>
        public void SetBlackBoard(IBlackBoard blackBoard)
        {
            if (blackBoard == null)
            {
                UsefulLogger.LogError("BlackBoardがnullの為、InputStateを取得できません。", this);
                return;
            }

            if (!blackBoard.TryGetStateBoard<InputBoard>(out var inputBoard))
            {
                UsefulLogger.LogError(
                    "InputBoard がBlackBoardに登録されていません。" +
                    "常駐シーンのRoot Compositorが生成・配置されているか確認してください。", this);
                return;
            }

            if (!inputBoard.TryGetGameState<IInputState>(out var inputState))
            {
                UsefulLogger.LogError(
                    "InputState が登録されていません。" +
                    "Applicationの初期化をこのクラスのInitializeより先に実行してください。", this);
                return;
            }

            _inputState = inputState;
        }

        /// <summary>
        /// InputActionAssetを有効化し、InputStateの現在の内容をエンジンへ反映する。
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            if (_inputState == null)
            {
                UsefulLogger.LogError(
                    "InputStateが設定されていない為、入力を扱えません。Initializeより前にSetBlackBoardを呼んでください。", this);
                return;
            }

            if (_actionAsset == null)
            {
                UsefulLogger.LogError("InputActionAssetが設定されていない為、入力を扱えません。", this);
                return;
            }

            // 生成時点のStateの内容をここで初めてエンジンへ反映する
            ApplyInputEnabled(_inputState.InputEnabled);

            if (_inputState.ActiveActionMaps.Count == 0)
            {
                UsefulLogger.LogWarning(
                    "有効なActionMapが一つもない為、この時点ではどのActionも反応しません。" +
                    "IInputDispatcher.SwitchActionMapまたはEnableActionMapで有効化してください。", this);
            }
        }

        private void OnDestroy()
        {
            foreach (var binding in _bindings)
            {
                binding.Dispose();
            }

            _bindings.Clear();

            if (_actionAsset != null) _actionAsset.Disable();
        }

        #region IInputEngineBridge実装

        public InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            var inputAction = FindAction(map, action);

            if (inputAction == null)
            {
                return new InputContext<TValue>(InputActionPhase.Disabled, default);
            }

            return new InputContext<TValue>(inputAction.phase, inputAction.ReadValue<TValue>());
        }

        public void BindAction<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            if (_inputState == null) return;

            var inputAction = FindAction(map, action);
            if (inputAction == null) return;

            var source = new InputActionSource<TValue>(inputAction);
            _bindings.Add(_inputState.Dispatcher.RegisterExternalInputSource(map, action, source));
        }

        /// <summary>
        /// 指定されたActionMapだけを有効にする。列挙に含まれないActionMapは無効化する。
        /// 入力が無効な間はどのActionMapも有効にしない。
        /// </summary>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        public void ApplyActiveActionMaps(IReadOnlyList<string> activeActionMaps)
        {
            if (_actionAsset == null) return;

            foreach (var actionMap in _actionAsset.actionMaps)
            {
                actionMap.Disable();
            }

            if (activeActionMaps == null) return;
            if (_inputState != null && !_inputState.InputEnabled) return;

            for (int i = 0; i < activeActionMaps.Count; i++)
            {
                var target = _actionAsset.FindActionMap(activeActionMaps[i]);

                if (target == null)
                {
                    UsefulLogger.LogWarning($"ActionMap [{activeActionMaps[i]}] が見つかりませんでした。", this);
                    continue;
                }

                target.Enable();
            }
        }

        /// <summary>
        /// 入力全体の有効・無効を反映する。有効化する範囲はInputStateが持つActiveActionMapsに従う。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        public void ApplyInputEnabled(bool inputEnabled)
        {
            if (_actionAsset == null) return;

            if (!inputEnabled)
            {
                _actionAsset.Disable();
                return;
            }

            ApplyActiveActionMaps(_inputState?.ActiveActionMaps);
        }

        #endregion

        /// <summary>
        /// InputActionAssetから指定されたActionを取得する。見つからない場合は警告を出してnullを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        private InputAction FindAction(Enum map, Enum action)
        {
            if (_actionAsset == null) return null;

            var inputAction = _actionAsset.FindActionMap(map.ToString())?.FindAction(action.ToString());

            if (inputAction == null)
            {
                UsefulLogger.LogWarning($"[{map}.{action}] が見つかりませんでした。", this);
            }

            return inputAction;
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
