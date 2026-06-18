using System;
using System.Text.RegularExpressions;

namespace SatisfactorySaveEditor.Util
{
    /// <summary>
    ///     Satisfactory のクラスパス／インスタンス名／ツリーのセグメント名を人間可読なラベルへ整形する（表示専用）。
    ///     例:
    ///       "/Game/.../Build_ConstructorMk1.Build_ConstructorMk1_C" -> "Constructor Mk1"
    ///       "FactoryGame"                                            -> "Factory Game"
    ///       "/Script/FactoryGame.FGWorldSettings"                    -> "World Settings"
    ///     セーブデータは一切変更しない。元の生名はツールチップ／コピーで参照できる。
    /// </summary>
    public static partial class FriendlyName
    {
        // クラス名の接頭辞（Satisfactory のアセット命名規則）。前方一致で 1 つだけ除去する。
        private static readonly string[] Prefixes =
            { "Build_", "Desc_", "Recipe_", "BP_", "Char_", "Foundation_", "Wall_", "SubclassOf_", "EResource_" };

        // 末尾の UAID / 連番（インスタンス識別子）。表示名からは省く。GeneratedRegex でコンパイル時生成。
        [GeneratedRegex(@"(_UAID_[0-9A-Fa-f]+(_[0-9]+)?|_[0-9]+)$")]
        private static partial Regex TrailingId();

        // camelCase / 数字境界を空白で区切る（ConstructorMk1 -> "Constructor Mk1"）。
        [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])")]
        private static partial Regex CamelSplit();

        // 現在の表示カルチャが日本語かどうか。日本語辞書（FriendlyNameMap）を引くかの分岐に使う。
        private static bool IsJapanese =>
            LocalizationService.Instance.CurrentCulture?.TwoLetterISOLanguageName == "ja";

        /// <summary>
        ///     クラスパス・インスタンス名・ツリーセグメント名のいずれを渡しても、末尾のクラス名相当を取り出して整形する。
        /// </summary>
        public static string Pretty(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            var s = raw;

            var slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);   // 最後の '/' 以降

            var dot = s.LastIndexOf('.');
            if (dot >= 0) s = s.Substring(dot + 1);        // 最後の '.' 以降（Build_X.Build_X_C -> Build_X_C）

            s = TrailingId().Replace(s, "");               // 末尾の UAID / 連番を除去（_C より先に）

            // ja カルチャのときはクラスセグメント（"Build_X_C" / "FGComponent" 等）を日本語辞書で引く。
            // 未収録なら以降の英語ヒューリスティックへフォールバックする。
            if (IsJapanese && FriendlyNameMap.Ja.TryGetValue(s, out var ja))
                return ja;

            if (s.EndsWith("_C", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2);

            foreach (var p in Prefixes)
            {
                if (s.StartsWith(p, StringComparison.Ordinal))
                {
                    s = s.Substring(p.Length);
                    break;
                }
            }

            // 内部接頭辞 "FG"（FGWorldSettings 等）を除去
            if (s.Length > 2 && s[0] == 'F' && s[1] == 'G' && char.IsUpper(s[2]))
                s = s.Substring(2);

            s = s.Replace('_', ' ');
            s = CamelSplit().Replace(s, " ");

            s = s.Trim();
            return s.Length == 0 ? raw : s;
        }
    }
}
