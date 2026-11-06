using System.Windows;
using System.Windows.Controls;
using OlhoNoChat.Janelas.LoginDoTikTok;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "TikTok" de Configurações (DataContext = <see cref="LogicaTikTok"/>).</summary>
public partial class PaginaTikTok : UserControl
{
    private LogicaTikTok? _logica;

    public PaginaTikTok()
    {
        InitializeComponent();
    }

    // Acompanha a conta só enquanto a janela está aberta; o login abre na frente das Configurações
    private void Pagina_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogicaTikTok logica || logica == _logica)
            return;
        _logica = logica;
        logica.AbrirLogin = () =>
        {
            var login = new JanelaLoginDoTikTok { Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            return login.ShowDialog() == true ? login.Conta : null;
        };
        logica.Ligar();
    }

    private void Pagina_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_logica == null)
            return;
        _logica.Desligar();
        _logica.AbrirLogin = null;
        _logica = null;
    }
}
