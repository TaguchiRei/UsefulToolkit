using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsefulToolkit.External.Input
{
    /// <summary>
    /// 外部入力スロットが運ぶ値の型。生成される仮想デバイスのコントロール種別に対応する。
    /// </summary>
    public enum ExternalInputValueType
    {
        /// <summary> Vector2。スティックやドラッグ量 </summary>
        Vector2,

        /// <summary> Vector3。位置など </summary>
        Vector3,

        /// <summary> Quaternion。姿勢など </summary>
        Quaternion,

        /// <summary> float。トリガーの引き量など </summary>
        Axis,

        /// <summary> bool。押されているか </summary>
        Button,

        /// <summary> int </summary>
        Integer,
    }

    /// <summary>
    /// 外部入力スロット1つ分の宣言。
    ///
    /// <see cref="Id"/> は生成器がリネームを検出するために使う。名前だけではリネームと
    /// 「削除して別名で追加」を区別できず、InputActionAsset のバインディングを
    /// 追従させられなくなるため、一度振ったIdは変更しないこと。
    /// </summary>
    [Serializable]
    public sealed class ExternalInputSlot
    {
        [SerializeField]
        [HideInInspector]
        private string _id;

        [SerializeField]
        [Tooltip("生成されるコントロール名。InputActionAsset からは <UsefulInput>/この名前 でバインドする。")]
        private string _name;

        [SerializeField]
        [Tooltip("このスロットが運ぶ値の型。書き込み時の型と一致していなければ拒否される。")]
        private ExternalInputValueType _valueType = ExternalInputValueType.Vector2;

        /// <summary> リネーム検出に使う不変の識別子 </summary>
        public string Id => _id;

        /// <summary> 生成されるコントロール名 </summary>
        public string Name => _name;

        /// <summary> このスロットが運ぶ値の型 </summary>
        public ExternalInputValueType ValueType => _valueType;

        /// <summary>
        /// Idが未設定なら新しく振る。既に振られている場合は何もしない。
        /// </summary>
        internal void EnsureId()
        {
            if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// 外部入力スロットの宣言。ここに並べたスロットから、
    /// <c>UsefulToolkit/Input/Generate External Input Device</c> が仮想デバイスと
    /// 書き込み用のenumを生成する。
    ///
    /// スロットの意味づけはこのアセットではなく InputActionAsset 側が持つ。
    /// ここで宣言するのは「値の受け口」だけで、それをどの Action に繋ぐかは
    /// 生成後に InputActionAsset でバインドして決める。
    /// </summary>
    [CreateAssetMenu(
        fileName = "ExternalInputDefinition",
        menuName = "UsefulToolkit/Input/External Input Definition")]
    public sealed class ExternalInputDefinition : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        [Tooltip("外部入力の受け口。名前は生成されるコントロール名になる。")]
        private List<ExternalInputSlot> _slots = new();

        /// <summary> 宣言されたスロット </summary>
        public IReadOnlyList<ExternalInputSlot> Slots => _slots;

        /// <summary>
        /// 保存の直前に、Idが未設定のスロットへIdを振る。
        /// 追加直後のスロットにもIdが入るよう、Inspectorでの編集を保存する経路で必ず通す。
        /// </summary>
        public void OnBeforeSerialize()
        {
            EnsureIds();
        }

        public void OnAfterDeserialize()
        {
        }

        private void OnValidate()
        {
            EnsureIds();
        }

        private void EnsureIds()
        {
            if (_slots == null) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i]?.EnsureId();
            }
        }
    }
}
