#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>Página "Bloqueios" da janela Filtros do chat (DataContext = <see cref="LogicaFiltros"/>).</summary>
public partial class PaginaBloqueios : UserControl
{
    public PaginaBloqueios()
    {
        InitializeComponent();
    }

    // O foco sempre volta para a caixa do nome
    private void BotaoBloquear_Click(object sender, RoutedEventArgs e) => caixaNovoBloqueado.Focus();
}
