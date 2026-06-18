using System;
using System.Globalization;
using System.Windows.Data;
using SatisfactorySaveEditor.Model;

namespace SatisfactorySaveEditor.Converter
{
    /// <summary>
    ///     ツリーノードのツールチップを生成する。葉（実オブジェクト）はクラスパスと
    ///     インスタンス名（＝セーブ内の正確な実体）を見せ、フォルダは生のセグメント名を見せる。
    ///     整形済みラベルだけでは区別できない個体を、ホバーで特定できるようにするのが目的。
    /// </summary>
    public class NodeTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is SaveObjectModel node)) return null;

            // 葉ノード（実 SaveObject を持つ）: クラスパス + インスタンス名
            if (node.Model != null)
            {
                var typePath = node.Model.TypePath;
                var instance = node.Model.InstanceName;
                if (string.IsNullOrEmpty(typePath)) return instance;
                if (string.IsNullOrEmpty(instance)) return typePath;
                return typePath + "\n" + instance;
            }

            // フォルダ／カテゴリノード: 生のセグメント名（クラスパスの一部）
            return node.Title;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
