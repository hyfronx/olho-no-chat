#nullable enable
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

    // "Mudar atalho" começa a gravar na caixa da mesma linha; "Confirmar" para. O atalho só vale depois de "Salvar".
    private void MudarAtalho_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditorDeAtalho editor } botao)
            return;

        if (editor.Gravando)
        {
            editor.PararDeGravar();
            botao.Content = "Mudar atalho";
        }
        else
        {
            editor.ComecarAGravar();
            botao.Content = "Confirmar";
        }
    }
}
