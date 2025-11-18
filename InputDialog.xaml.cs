using System.Windows;

namespace UsbI2cController
{
    public partial class InputDialog : Window
    {
        public new string Title { get; set; }
        public string Message { get; set; }
        public string InputText { get; set; }

        public InputDialog(string title, string message, string defaultValue)
        {
            InitializeComponent();
            Title = title;
            Message = message;
            InputText = defaultValue;
            DataContext = this;
            
            Loaded += (s, e) => InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
