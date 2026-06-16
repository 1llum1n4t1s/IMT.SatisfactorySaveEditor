using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using SatisfactorySaveEditor.Model;

namespace SatisfactorySaveEditor.ViewModel
{
    public class FillViewModel : ObservableObject
    {
        private ResourceType selectedItem;
        public ResourceType SelectedItem
        {
            get => selectedItem;
            set
            {
                SetProperty(ref selectedItem, value, nameof(SelectedItem));
                OnPropertyChanged(nameof(CanConfirm));
            }
        }

        public bool IsConfirmed { get; set; }

        public List<ResourceType> ItemTypes => ResourceTypes.RESOURCES;

        public RelayCommand<Window> OkCommand => new RelayCommand<Window>(Confirmed);

        public RelayCommand<Window> CancelCommand => new RelayCommand<Window>(Cancelled);

        private void Confirmed(Window window)
        {
            IsConfirmed = true;
            window?.Close();
        }

        private void Cancelled(Window window)
        {
            window?.Close();
        }
        public bool CanConfirm
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedItem.ItemPath)) return false;
                return SelectedItem.Quantity > 0;
            }
        }
    }
}