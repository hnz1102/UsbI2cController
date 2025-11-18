using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace UsbI2cController.Views
{
    public partial class EditOperationDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _title = "";
        private string _message = "";
        private string _inputHint = "";
        private string _commentHint = "";
        private string _inputText = "";
        private string _commentText = "";
        private bool _showDataInput = true;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public string InputHint
        {
            get => _inputHint;
            set { _inputHint = value; OnPropertyChanged(); }
        }

        public string CommentHint
        {
            get => _commentHint;
            set { _commentHint = value; OnPropertyChanged(); }
        }

        public string InputText
        {
            get => _inputText;
            set { _inputText = value; OnPropertyChanged(); }
        }

        public string CommentText
        {
            get => _commentText;
            set { _commentText = value; OnPropertyChanged(); }
        }

        public bool ShowDataInput
        {
            get => _showDataInput;
            set { _showDataInput = value; OnPropertyChanged(); }
        }

        public EditOperationDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
