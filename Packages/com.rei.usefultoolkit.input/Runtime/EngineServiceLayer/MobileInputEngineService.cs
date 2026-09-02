using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using UsefulToolkit.Initialization;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace UsefulToolkit.EngineService.Input
{
    /// <summary>
    /// タッチ入力(スクリーンドラッグ)をInputDispatcherと同じInputStateへ橋渡しする
    /// EngineServiceLayer。IExternalInputSource&lt;Vector2&gt;を自ら実装し、
    /// IInputController.RegisterExternalInputSourceでInputDispatcherと同じ(map, action)
    /// チャンネルへ登録するだけで済む——Application側から見ればどちらの入力ソースが
    /// 発火したかは区別されない。
    /// </summary>
    public sealed class MobileInputEngineService : InitializableMonoBehaviour, IExternalInputSource<Vector2>
    {
        [SerializeField] private GraphicRaycaster _rayCaster;

        [SerializeField]
        [Tooltip("タッチを受け付ける UI に付いているタグ。ここに当たった時だけ入力として扱う。")]
        private string _touchAreaTag = "TouchArea";

        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _map;
        private Enum _action;
        private Action<InputContext<Vector2>> _onInput;
        private IDisposable _registration;

        private EventSystem _eventSystem;
        private PointerEventData _eventData;

        private bool _isTracking;
        private int _trackedTouchId = -1;
        private Vector2 _legacyPosition;

        private readonly List<RaycastResult> _raycastResults = new();

        /// <summary>
        /// 入力ソースの登録先と、入力を流してよいかの判定に使う読み取り面を渡す。
        /// Initializeより前に呼ぶこと。
        /// </summary>
        /// <param name="inputState">InputStateの読み取り面</param>
        /// <param name="inputController">InputStateの操作面</param>
        public void SetInput(IInputState inputState, IInputController inputController)
        {
            _inputState = inputState;
            _inputController = inputController;
        }

        /// <summary>タッチ入力をどの(ActionMap, Action)としてInputStateへ橋渡しするかを指定する。</summary>
        public void Bind(Enum map, Enum action)
        {
            _map = map;
            _action = action;
        }

        public override void Initialize()
        {
            base.Initialize();

            if (_inputState == null || _inputController == null || _map == null || _action == null)
            {
                UsefulLogger.LogError(
                    "InputState/InputController/Bindが設定されていません。" +
                    "Initialize()より前にSetInput/Bindを呼んでください。", this);
                return;
            }

            if (_rayCaster == null)
            {
                UsefulLogger.LogError("GraphicRaycasterが設定されていない為、タッチ範囲を判定できません。", this);
                return;
            }

            _registration = _inputController.RegisterExternalInputSource(_map, _action, this);

            _eventSystem = EventSystem.current;
            _eventData = new PointerEventData(_eventSystem);

            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
            // エディタ上でマウスクリックをタッチとして扱うシミュレーションを有効化する
            TouchSimulation.Enable();
#endif
        }

        private void OnDestroy()
        {
            _registration?.Dispose();

            EnhancedTouchSupport.Disable();
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
        }

        public void RegisterAction(Action<InputContext<Vector2>> handler) => _onInput += handler;
        public void UnRegisterAction(Action<InputContext<Vector2>> handler) => _onInput -= handler;

        private void Update()
        {
            if (_inputState == null || _action == null || _rayCaster == null) return;

            // エンジン側がStateの写しである以上、タッチ入力もStateの可否に従う
            if (!_inputState.InputEnabled || !_inputState.IsActionMapActive(_map))
            {
                // 追跡中に無効化された場合は、指を離した時と同じく打ち切りを通知して状態を戻す
                if (_isTracking)
                {
                    _isTracking = false;
                    _trackedTouchId = -1;
                    RaiseInput(InputPhase.Canceled, Vector2.zero);
                }

                return;
            }

            var touches = Touch.activeTouches;

            if (_isTracking)
            {
                Touch? trackingTouch = null;

                foreach (var touch in touches)
                {
                    if (touch.touchId != _trackedTouchId) continue;
                    trackingTouch = touch;
                    break;
                }

                // 指が離れた
                if (!trackingTouch.HasValue || trackingTouch.Value.ended)
                {
                    _isTracking = false;
                    _trackedTouchId = -1;
                    RaiseInput(InputPhase.Canceled, Vector2.zero);
                    return;
                }

                var currentPosition = trackingTouch.Value.screenPosition;
                var delta = currentPosition - _legacyPosition;
                _legacyPosition = currentPosition;
                RaiseInput(InputPhase.Performed, delta);
                return;
            }

            // 新規入力検出
            foreach (var touch in touches)
            {
                if (!touch.began) continue;

                var position = touch.screenPosition;
                if (!IsInsideTouchArea(position)) continue;

                _trackedTouchId = touch.touchId;
                _isTracking = true;
                _legacyPosition = position;
                RaiseInput(InputPhase.Started, Vector2.zero);
                break;
            }
        }

        private void RaiseInput(InputPhase phase, Vector2 value)
        {
            _onInput?.Invoke(new InputContext<Vector2>(phase, value));
        }

        /// <summary>入力範囲内にあるか、ボタンなどと被っていないかを調べる。</summary>
        private bool IsInsideTouchArea(Vector2 screenPosition)
        {
            _eventData.position = screenPosition;
            _raycastResults.Clear();
            _rayCaster.Raycast(_eventData, _raycastResults);

            if (_raycastResults.Count == 0) return false;

            return _raycastResults[0].gameObject != null && _raycastResults[0].gameObject.CompareTag(_touchAreaTag);
        }
    }
}
