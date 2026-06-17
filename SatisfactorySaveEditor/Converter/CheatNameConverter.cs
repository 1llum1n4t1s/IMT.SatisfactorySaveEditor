using System;
using System.Globalization;
using System.Windows.Data;

using SatisfactorySaveEditor.Util;

namespace SatisfactorySaveEditor.Converter
{
    /// <summary>
    ///     チートメニュー項目の表示名を resx キー（<see cref="Cheats.ICheat.NameKey"/>）から
    ///     ローカライズ解決する。第2バインディング（<see cref="LocalizationService.CurrentCulture"/>）は
    ///     言語切替時に MultiBinding を再評価させるためのトリガで、値自体は使わない。
    /// </summary>
    public class CheatNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var key = values != null && values.Length > 0 ? values[0] as string : null;
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // MenuItem はアクセスキー指定に "_" を使うため、表示名中の "_" を "__" にエスケープする
            // （既存メニュー（MenuTextConverter）と同じ慣習）。
            return LocalizationService.Instance[key].Replace("_", "__");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
