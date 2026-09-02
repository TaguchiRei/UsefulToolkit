using System;
using System.Collections.Generic;

namespace UsefulToolkit.Utility
{
    /// <summary>
    /// ボックス化済みのenumから名前の文字列を取得する際のキャッシュ。
    /// Enum.ToString()は呼ぶたびに文字列を確保する為、毎フレーム呼ばれうる経路ではこちらを使う。
    ///
    /// 辞書には渡されたボックスがそのままキーとして残るが、enumの値の種類は有限な為、
    /// キャッシュの件数も有限になる。メインスレッドからの利用のみを想定しており排他は行わない。
    /// </summary>
    public static class EnumNameCache
    {
        private static readonly Dictionary<Enum, string> Names = new();

        /// <summary>
        /// enumの値に対応する名前を取得する。
        /// </summary>
        /// <param name="value">名前を取得するenumの値</param>
        /// <exception cref="ArgumentNullException">valueがnullのときに出力</exception>
        public static string GetName(Enum value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            if (Names.TryGetValue(value, out var name)) return name;

            name = value.ToString();
            Names[value] = name;

            return name;
        }
    }

    /// <summary>
    /// <see cref="EnumNameCache"/>のボックス化しない版。
    /// enumの型が静的に決まる場所ではこちらを使うと、引数の受け渡しでも確保が起きない。
    /// </summary>
    /// <typeparam name="TEnum">名前を取得するenumの型</typeparam>
    public static class EnumNameCache<TEnum> where TEnum : struct, Enum
    {
        private static readonly Dictionary<TEnum, string> Names = BuildNames();

        /// <summary>
        /// enumの値に対応する名前を取得する。
        /// TEnumに定義されていない値の場合のみ、Enum.ToString()の結果を返す(この場合は確保が起きる)。
        /// </summary>
        /// <param name="value">名前を取得するenumの値</param>
        public static string GetName(TEnum value)
        {
            return Names.TryGetValue(value, out var name) ? name : value.ToString();
        }

        /// <summary>
        /// TEnumに定義されている全ての値と名前の対応を作る。
        /// </summary>
        private static Dictionary<TEnum, string> BuildNames()
        {
            var values = (TEnum[])Enum.GetValues(typeof(TEnum));
            var names = Enum.GetNames(typeof(TEnum));
            var result = new Dictionary<TEnum, string>(values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                // 同じ値へ複数の名前が付いている場合は、先に定義された方を採用する
                result.TryAdd(values[i], names[i]);
            }

            return result;
        }
    }
}
