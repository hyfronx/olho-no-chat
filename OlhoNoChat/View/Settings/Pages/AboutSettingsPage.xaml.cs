using OlhoNoChat.Sistema;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

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
            AbrirNoWindows.Site(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
                AbrirNoWindows.Site(url);
        }
    }
}
