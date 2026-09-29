using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>Página "Usuários" da janela Filtros do chat (DataContext = <see cref="LogicaFiltros"/>).</summary>
public partial class PaginaUsuarios : UserControl
{
    public PaginaUsuarios()
    {
        InitializeComponent();
    }

    // O foco sempre volta para a caixa do nome
    private void BotaoAdicionar_Click(object sender, RoutedEventArgs e) => caixaNovoUsuario.Focus();

    // Saiu da caixa da cor personalizada com algo que não é uma cor: mostra o erro
    private void CaixaDeCor_LostFocus(object sender, RoutedEventArgs e) =>
        ((sender as FrameworkElement)?.DataContext as EscolhaDeCor)?.SaiuDaCaixa();
}
