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
            //Robb, Goz3rr, and virusek20 have the login info for this bit.ly account if needed
            Process.Start(new ProcessStartInfo("http://bit.ly/SSE_Wrong_Download") { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        private void Close(Window obj)
        {
            Application.Current.Shutdown();
        }
    }
}
