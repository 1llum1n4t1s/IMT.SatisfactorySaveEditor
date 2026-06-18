using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SourceCodeMessage.ViewModel
{
    public class MainViewModel : ObservableObject
    {
        public RelayCommand<Window> OpenReleasesCommand { get; }
        public RelayCommand<Window> CloseCommand { get; }

        public MainViewModel()
        {
            OpenReleasesCommand = new RelayCommand<Window>(OpenReleases);
            CloseCommand = new RelayCommand<Window>(Close);
        }

        private void OpenReleases(Window obj)
        {
            // 平文 HTTP + 短縮 URL（bit.ly）を避け、公式リポジトリの Releases ページへ https 直リンクする。
            Process.Start(new ProcessStartInfo("https://github.com/Goz3rr/SatisfactorySaveEditor/releases") { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        private void Close(Window obj)
        {
            Application.Current.Shutdown();
        }
    }
}
