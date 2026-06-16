using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SatisfactorySaveEditor.Util;

public sealed partial class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    [ObservableProperty]
    private CultureInfo currentCulture = CultureInfo.CurrentUICulture;

    partial void OnCurrentCultureChanged(CultureInfo value)
    {
        System.Threading.Thread.CurrentThread.CurrentUICulture = value;
        System.Threading.Thread.CurrentThread.CurrentCulture = value;
        SatisfactorySaveEditor.Properties.Resources.Culture = value;
        OnPropertyChanged("Item[]");
        OnPropertyChanged(string.Empty);
    }

    public string this[string key] =>
        SatisfactorySaveEditor.Properties.Resources.ResourceManager.GetString(key, CurrentCulture) ?? "!" + key + "!";

    public IReadOnlyList<CultureInfo> SupportedCultures { get; } = new[]
    {
        new CultureInfo("en"),
        new CultureInfo("ja"),
    };
}
