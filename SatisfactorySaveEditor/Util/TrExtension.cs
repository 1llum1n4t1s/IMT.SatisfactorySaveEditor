using System;
using System.Windows.Markup;

namespace SatisfactorySaveEditor.Util
{
    public class TrExtension : System.Windows.Markup.MarkupExtension
    {
        [ConstructorArgument("key")]
        public string Key { get; set; }

        public TrExtension()
        {
        }

        public TrExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new System.Windows.Data.Binding("[" + Key + "]");
            binding.Source = LocalizationService.Instance;
            binding.Mode = System.Windows.Data.BindingMode.OneWay;
            return binding.ProvideValue(serviceProvider);
        }
    }
}
