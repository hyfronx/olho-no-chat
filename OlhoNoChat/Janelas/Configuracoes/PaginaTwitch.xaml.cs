#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "Twitch" de Configurações (DataContext = <see cref="LogicaTwitch"/>).</summary>
public partial class PaginaTwitch : UserControl
{
    private LogicaTwitch? _logica;

    public PaginaTwitch()
    {
        InitializeComponent();
    }

    // Acompanha a conta e os resgates só enquanto a janela está aberta
    private void Pagina_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LogicaTwitch logica || logica == _logica)
            return;
        _logica = logica;
        logica.Erro += MostrarErro;
        logica.Ligar();
    }

    private void Pagina_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_logica == null)
            return;
        _logica.Erro -= MostrarErro;
        _logica.Desligar();
        _logica = null;
    }

    private void MostrarErro(string texto) =>
        MessageBox.Show(Window.GetWindow(this)!, texto, LogicaTwitch.TituloDosErros, MessageBoxButton.OK, MessageBoxImage.Warning);
}
