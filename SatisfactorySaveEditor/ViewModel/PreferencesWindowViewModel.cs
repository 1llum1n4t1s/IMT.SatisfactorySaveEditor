using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Util;

namespace SatisfactorySaveEditor.ViewModel
{
    public class PreferencesWindowViewModel : ObservableObject
    {
        private bool canApply;
        private bool autoUpdate;
        private bool autoBackup;
        private CultureInfo selectedCulture;
        //private int backupCount; //Future


        public bool AutoUpdate
        {
            get => autoUpdate;
            set
            {
                SetProperty(ref autoUpdate, value, nameof(AutoUpdate));
                SetProperty(ref canApply, true, nameof(CanApply));
            }
        }

        public bool AutoBackup
        {
            get => autoBackup;
            set
            {
                SetProperty(ref autoBackup, value, nameof(AutoBackup));
                SetProperty(ref canApply, true, nameof(CanApply));
            }
        }

        public IReadOnlyList<CultureInfo> AvailableCultures => LocalizationService.Instance.SupportedCultures;

        public CultureInfo SelectedCulture
        {
            get => selectedCulture;
            set
            {
                if (value == null) return;
                SetProperty(ref selectedCulture, value, nameof(SelectedCulture));
                // 言語切替は即時反映 (Apply を待たない)
                LocalizationService.Instance.CurrentCulture = value;
                SetProperty(ref canApply, true, nameof(CanApply));
            }
        }

        //Future
        /*public int BackupCount
        {
            get => backupCount;
            set
            {
                SetProperty(ref backupCount, value, nameof(BackupCount));
                SetProperty(ref canApply, true, nameof(CanApply));
            }
        }*/

        public bool CanApply => canApply;

        public RelayCommand<Window> AcceptCommand { get; }
        public RelayCommand ApplyCommand { get; }
        public RelayCommand<Window> CancelCommand { get; }

        public PreferencesWindowViewModel()
        {
            AcceptCommand = new RelayCommand<Window>(Accept);
            ApplyCommand = new RelayCommand(Apply);
            CancelCommand = new RelayCommand<Window>(Cancel);

            autoUpdate = Properties.Settings.Default.AutoUpdate;
            autoBackup = Properties.Settings.Default.AutoBackup;

            // 現在カルチャを SupportedCultures から同名で探して選択中にする
            var current = LocalizationService.Instance.CurrentCulture;
            selectedCulture = AvailableCultures.FirstOrDefault(c => c.TwoLetterISOLanguageName == current.TwoLetterISOLanguageName)
                ?? AvailableCultures[0];
        }

        private void Accept(Window window)
        {
            Apply();
            window.Close();
        }

        private void Apply()
        {
            Properties.Settings.Default.AutoUpdate = autoUpdate;
            Properties.Settings.Default.AutoBackup = autoBackup;
            Properties.Settings.Default.Culture = selectedCulture?.Name ?? string.Empty;

            Properties.Settings.Default.Save();
            SetProperty(ref canApply, false, nameof(CanApply));
        }

        private void Cancel(Window window)
        {
            window.Close();
        }
    }
}
