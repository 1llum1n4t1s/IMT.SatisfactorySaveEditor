using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SatisfactorySaveEditor.Properties;

//TODO: Make pressing enter in the text box trigger Ok()
namespace SatisfactorySaveEditor.ViewModel
{
    public class StringPromptViewModel : ObservableObject
    {
        public RelayCommand<Window> OkCommand { get; }
        public RelayCommand<Window> CancelCommand { get; }

        private string windowTitle = Resources.PromptString_Title;
        public string WindowTitle
        {
            get => windowTitle;
            set { SetProperty(ref windowTitle, value, nameof(WindowTitle)); }
        }

        private string promptMessage = Resources.PromptString_Caption;
        public string PromptMessage
        {
            get => promptMessage;
            set { SetProperty(ref promptMessage, value, nameof(PromptMessage)); }
        }

        private string valueChosen;
        public string ValueChosen
        {
            get => valueChosen;
            set { SetProperty(ref valueChosen, value, nameof(ValueChosen)); }
        }

        private string oldValueMessage = Resources.PromptString_Detail;
        public string OldValueMessage
        {
            get => oldValueMessage;
            set { SetProperty(ref oldValueMessage, value, nameof(OldValueMessage)); }
        }

        public StringPromptViewModel()
        {
            OkCommand = new RelayCommand<Window>(Ok);
            CancelCommand = new RelayCommand<Window>(Cancel);
        }

        private void Cancel(Window obj)
        {
            ValueChosen = "cancel";
            obj.Close();
        }

        private void Ok(Window obj)
        {
            obj.Close();
        }
    }
}
