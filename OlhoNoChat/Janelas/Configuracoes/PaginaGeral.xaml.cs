using System.Windows;
using System.Windows.Controls;
using OlhoNoChat.Atalhos;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "Geral" de Configurações (DataContext = <see cref="LogicaGeral"/>).</summary>
public partial class PaginaGeral : UserControl
{
    public PaginaGeral()
    {
        InitializeComponent();
    }

    // "Mudar atalho"/"Confirmar": ver EditorDeAtalho.AlternarPeloBotao
    private void MudarAtalho_Click(object sender, RoutedEventArgs e) => EditorDeAtalho.AlternarPeloBotao(sender);
}
