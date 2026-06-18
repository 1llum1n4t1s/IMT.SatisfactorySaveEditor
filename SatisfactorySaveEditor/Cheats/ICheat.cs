using SatisfactorySaveEditor.Model;

using SatisfactorySaveParser;

namespace SatisfactorySaveEditor.Cheats
{
    public interface ICheat
    {
        /// <summary>
        ///     チートメニュー項目の表示名を解決するための resx キー（例 "CheatNoCost"）。
        ///     実際の表示文字列は <see cref="Converter.CheatNameConverter"/> 経由でローカライズされ、
        ///     言語切替にもランタイムで追従する。
        /// </summary>
        string NameKey { get; }

        /// <summary>
        ///     Activate the cheat
        /// </summary>
        /// <param name="rootItem">SaveObjectModel to apply the cheat on</param>
        /// <param name="saveGame"></param>
        /// <returns>true if succesfull and the SaveObjectModel was mutated, false on failure</returns>
        bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame);
    }
}
