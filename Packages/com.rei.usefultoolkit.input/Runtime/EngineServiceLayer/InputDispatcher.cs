using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.EngineService.Input
{
    /// <summary>
    /// InputActionAssetを直接扱うEngineServiceLayer。
    /// InputStateの内容をInputActionAssetへ反映し、InputActionを入力ソースとして提供する。
    ///
    /// このクラスはInputStateを参照しない。反映すべき内容は<see cref="Apply"/>の引数として
    /// 渡されるものが全てであり、エンジンがStateとは別に状態を持つことはない。
    /// </summary>
    public sealed class InputDispatcher : InitializableMonoBehaviour, IInputEngineBridge
    {
        [SerializeField] private InputActionAsset _actionAsset;

        /// <summary>
        /// InputActionAssetが設定されているか確認する。
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            if (_actionAsset == null)
            {
                UsefulLogger.LogError("InputActionAssetが設定されていない為、入力を扱えません。", this);
            }
        }

        private void OnDestroy()
        {
            if (_actionAsset != null) _actionAsset.Disable();
        }

        #region IInputEngineBridge実装

        public InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            var inputAction = FindAction(map, action);

            if (inputAction == null)
            {
                return new InputContext<TValue>(InputPhase.Disabled, default);
            }

            return new InputContext<TValue>(ToInputPhase(inputAction.phase), inputAction.ReadValue<TValue>());
        }

        public bool TryCreateInputSource<TValue>(Enum map, Enum action, out IExternalInputSource<TValue> source)
            where TValue : unmanaged
        {
            source = null;

            var inputAction = FindAction(map, action);
            if (inputAction == null) return false;

            source = new InputActionSource<TValue>(inputAction);
            return true;
        }

        /// <summary>
        /// 指定されたActionMapだけを有効にする。列挙に含まれないActionMapは無効化する。
        /// 入力が無効な間はどのActionMapも有効にしない。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        public void Apply(bool inputEnabled, IReadOnlyList<string> activeActionMaps)
        {
            if (_actionAsset == null) return;

            foreach (var actionMap in _actionAsset.actionMaps)
            {
                actionMap.Disable();
            }

            if (!inputEnabled || activeActionMaps == null) return;

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

        #endregion

        /// <summary>
        /// InputActionAssetから指定されたActionを取得する。見つからない場合は警告を出してnullを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        private InputAction FindAction(Enum map, Enum action)
        {
            if (_actionAsset == null || map == null || action == null) return null;

            var inputAction = _actionAsset.FindActionMap(map.ToString())?.FindAction(action.ToString());

            if (inputAction == null)
            {
                UsefulLogger.LogWarning($"[{map}.{action}] が見つかりませんでした。", this);
            }

            return inputAction;
        }

        /// <summary>
        /// InputSystemのphaseを、BlackBoardLayerが扱うphaseへ変換する。
        /// </summary>
        /// <param name="phase">InputSystem側のphase</param>
        private static InputPhase ToInputPhase(InputActionPhase phase)
        {
            return phase switch
            {
                InputActionPhase.Waiting => InputPhase.Waiting,
                InputActionPhase.Started => InputPhase.Started,
                InputActionPhase.Performed => InputPhase.Performed,
                InputActionPhase.Canceled => InputPhase.Canceled,
                _ => InputPhase.Disabled
            };
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
                _handler?.Invoke(new InputContext<TValue>(ToInputPhase(ctx.phase), ctx.ReadValue<TValue>()));
        }
    }
}
