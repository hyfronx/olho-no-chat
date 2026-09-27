using System.Windows;

namespace OlhoNoChat.View
{
    /// <summary>
    /// Asks whether to install a new version now.
    /// </summary>
    public partial class UpdateDialog : Window
    {
        // Public property to access the checkbox state from outside
        public bool ShouldDisableUpdates => DisableUpdateCheckBox.IsChecked == true;

        public UpdateDialog(string currentVersion, string newVersion)
        {
            InitializeComponent();
            CurrentVersionText.Text = currentVersion;
            NewVersionText.Text = newVersion;
        }

        // "Depois" is the cancel button: closing with it, Esc or the X gives DialogResult false
        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Helpers.AppWindowFrame.Apply(this); // own close button, no gray line around
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}