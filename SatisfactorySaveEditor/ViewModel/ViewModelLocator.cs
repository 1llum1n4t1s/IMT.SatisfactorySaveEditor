using System;
using Microsoft.Extensions.DependencyInjection;

namespace SatisfactorySaveEditor.ViewModel
{
    public class ViewModelLocator
    {
        private static readonly IServiceProvider services = BuildServiceProvider();

        private static IServiceProvider BuildServiceProvider()
        {
            var collection = new ServiceCollection();

            // メインウィンドウは単一インスタンス
            collection.AddSingleton<MainViewModel>();

            // これらのウィンドウは永続データを持たないため、参照のたびに新しいインスタンスを生成する
            collection.AddTransient<AddViewModel>();
            collection.AddTransient<CheatInventoryViewModel>();
            collection.AddTransient<StringPromptViewModel>();
            collection.AddTransient<PreferencesWindowViewModel>();
            collection.AddTransient<FillViewModel>();
            collection.AddTransient<UnlockResearchWindowViewModel>();

            return collection.BuildServiceProvider();
        }

        public MainViewModel MainViewModel => services.GetRequiredService<MainViewModel>();

        public AddViewModel AddViewModel => services.GetRequiredService<AddViewModel>();
        public CheatInventoryViewModel CheatInventoryViewModel => services.GetRequiredService<CheatInventoryViewModel>();
        public StringPromptViewModel StringPromptViewModel => services.GetRequiredService<StringPromptViewModel>();
        public PreferencesWindowViewModel PreferencesWindowViewModel => services.GetRequiredService<PreferencesWindowViewModel>();
        public FillViewModel FillViewModel => services.GetRequiredService<FillViewModel>();
        public UnlockResearchWindowViewModel UnlockResearchWindowViewModel => services.GetRequiredService<UnlockResearchWindowViewModel>();
    }
}
