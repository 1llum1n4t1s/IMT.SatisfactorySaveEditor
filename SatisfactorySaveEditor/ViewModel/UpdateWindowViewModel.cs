using System;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.Util;

namespace SatisfactorySaveEditor.ViewModel
{
    public class UpdateWindowViewModel : ObservableObject
    {
        private readonly UpdateChecker.VersionInfo info;

        public RelayCommand<Window> OpenCommand { get; }
        public RelayCommand<Window> CloseCommand { get; }
        public RelayCommand<Window> DisableAutoCheckCommand { get; }

        public string Changelog => $"Satisfactory Save Editor {info.TagName}" + Environment.NewLine + info.Name + Environment.NewLine + Environment.NewLine + info.Changelog;

        public UpdateWindowViewModel(UpdateChecker.VersionInfo info)
        {
            this.info = info;

            OpenCommand = new RelayCommand<Window>(Open);
            CloseCommand = new RelayCommand<Window>(Close);
            DisableAutoCheckCommand = new RelayCommand<Window>(DisableAutoCheck);
        }

        private void Close(Window window)
        {
            window.Close();
        }

        private void DisableAutoCheck(Window window)
        {
            Properties.Settings.Default.AutoUpdate = false;
            MessageBox.Show(Resources.MsgAutoUpdateDisabled_Body, Resources.MsgEditorName_Caption, MessageBoxButton.OK);

            Properties.Settings.Default.Save();
            window.Close();
        }

        private void Open(Window window)
        {
            Process.Start(new ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true });
            window.Close();
        }
    }
}
