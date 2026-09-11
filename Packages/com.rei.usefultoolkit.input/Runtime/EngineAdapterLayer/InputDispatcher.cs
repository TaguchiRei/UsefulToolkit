using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace UsefulToolkit.EngineAdapter.Input
{
    /// <summary>
    /// InputActionAssetを直接扱うEngineAdapterLayer。
    /// InputStateの内容をInputActionAssetへ反映し、InputActionへのコールバック登録を仲介する。
    ///
    /// このクラスはInputStateを参照しない。反映すべき内容は<see cref="Apply"/>の引数として
    /// 渡されるものが全てであり、エンジンがStateとは別に状態を持つことはない。
    /// </summary>
    public sealed class InputDispatcher : InitializableMonoBehaviour, IInputEngineBridge
    {
        [SerializeField] private InputActionAsset _actionAsset;

        /// <summary>
        /// 解決済みのInputAction。見つからなかった組み合わせはnullを入れて覚える。
        /// InputActionAssetの中身は実行中に変わらない前提でキャッシュしている。
        /// </summary>
        private readonly Dictionary<(Enum Map, Enum Action), InputAction> _actionCache = new();

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

        public IDisposable Subscribe<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged
        {
            var inputAction = FindAction(map, action);

            if (inputAction == null) return BoardDispose.Empty;

            void OnCallback(InputAction.CallbackContext context) =>
                handler(new InputContext<TValue>(ToInputPhase(context.phase), context.ReadValue<TValue>()));

            inputAction.started += OnCallback;
            inputAction.performed += OnCallback;
            inputAction.canceled += OnCallback;

            return new BoardDispose(() =>
            {
                inputAction.started -= OnCallback;
                inputAction.performed -= OnCallback;
                inputAction.canceled -= OnCallback;
            });
        }

        /// <summary>
        /// 指定されたActionMapだけを有効にする。列挙に含まれないActionMapは無効化する。
        /// 既に有効なActionMapには触れないため、進行中の入力は中断されない。
        /// 入力が無効な間はどのActionMapも有効にしない。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        public void Apply(bool inputEnabled, IReadOnlyList<string> activeActionMaps)
        {
            if (_actionAsset == null) return;

            var targets = inputEnabled ? activeActionMaps : null;

            foreach (var actionMap in _actionAsset.actionMaps)
            {
                bool shouldEnable = targets != null && Contains(targets, actionMap.name);

                if (shouldEnable == actionMap.enabled) continue;

                if (shouldEnable) actionMap.Enable();
                else actionMap.Disable();
            }

            WarnUnknownActionMaps(targets);
        }

        /// <summary>
        /// 全ActionMapを一度無効化してから、指定されたActionMapだけを有効にする。
        /// 有効なままになるActionMapも張り直すため、進行中の入力は打ち切られる。
        /// 入力が無効な間はどのActionMapも有効にしない。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        public void ApplyExclusive(bool inputEnabled, IReadOnlyList<string> activeActionMaps)
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
        /// 要求されたActionMap名のうち、InputActionAssetに存在しないものを警告する。
        /// 差分適用ではFindActionMapを通らないため、ここで別途調べる。
        /// </summary>
        /// <param name="actionMapNames">要求されたActionMap名。nullなら何もしない</param>
        private void WarnUnknownActionMaps(IReadOnlyList<string> actionMapNames)
        {
            if (actionMapNames == null) return;

            for (int i = 0; i < actionMapNames.Count; i++)
            {
                if (_actionAsset.FindActionMap(actionMapNames[i]) == null)
                {
                    UsefulLogger.LogWarning($"ActionMap [{actionMapNames[i]}] が見つかりませんでした。", this);
                }
            }
        }

        /// <summary>
        /// 名前の一覧に指定の名前が含まれるか。LINQを避けて確保を起こさない。
        /// </summary>
        /// <param name="names">調べる一覧</param>
        /// <param name="name">探す名前</param>
        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name) return true;
            }

            return false;
        }

        /// <summary>
        /// InputActionAssetから指定されたActionを取得する。
        /// 一度調べた組み合わせはキャッシュから返す。
        /// 見つからない場合はnullを返し、同じ組み合わせにつき一度だけ警告を出す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        private InputAction FindAction(Enum map, Enum action)
        {
            if (_actionAsset == null || map == null || action == null) return null;

            // ReadValueは毎フレーム呼ばれうる経路のため、名前の解決と探索、警告は組み合わせごとに一度だけ行う
            if (_actionCache.TryGetValue((map, action), out var cached)) return cached;

            string mapName = EnumNameCache.GetName(map);
            string actionName = EnumNameCache.GetName(action);
            var inputAction = _actionAsset.FindActionMap(mapName)?.FindAction(actionName);

            _actionCache[(map, action)] = inputAction;

            if (inputAction == null)
            {
                UsefulLogger.LogWarning($"[{mapName}.{actionName}] が見つかりませんでした。", this);
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

    }
}
