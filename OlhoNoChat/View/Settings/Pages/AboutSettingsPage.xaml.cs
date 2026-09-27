using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using OlhoNoChat.Utils;

namespace OlhoNoChat.View.Settings
{
    /// <summary>
    /// "Sobre" tab: version, account security and links.
    /// </summary>
    public partial class AboutSettingsPage : UserControl
    {
        public AboutSettingsPage()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ShellHelper.OpenUrl(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
                ShellHelper.OpenUrl(url);
        }
    }
}
