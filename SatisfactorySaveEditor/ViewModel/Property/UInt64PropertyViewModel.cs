using SatisfactorySaveParser.PropertyTypes;

namespace SatisfactorySaveEditor.ViewModel.Property
{
    public class UInt64PropertyViewModel : SerializedPropertyViewModel
    {
        private readonly UInt64Property model;

        private ulong value;

        public ulong Value
        {
            get => value;
            set { SetProperty(ref this.value, value, nameof(Value)); }
        }

        public override string ShortName => "UInt64";

        public UInt64PropertyViewModel(UInt64Property uintProperty) : base(uintProperty)
        {
            model = uintProperty;

            value = model.Value;
        }

        public override void ApplyChanges()
        {
            model.Value = value;
        }
    }
}
