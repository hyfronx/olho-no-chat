#nullable enable
using System.Windows;
using System.Windows.Controls;
using OlhoNoChat.Janelas.Filtros;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "Chat" de Configurações (DataContext = <see cref="LogicaChat"/>).</summary>
public partial class PaginaChat : UserControl
{
    public PaginaChat()
    {
        InitializeComponent();
    }

    // Filtros do chat abre sobre Configurações e salva sozinha
    private void CartaoFiltros_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogicaChat logica)
            new JanelaFiltros(logica.NovaLogicaDosFiltros()) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
