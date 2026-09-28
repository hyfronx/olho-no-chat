#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>
/// A janela Filtros do chat: páginas Usuários e Bloqueios. A lógica (e o que é salvo) fica em <see cref="LogicaFiltros"/>.
/// </summary>
public partial class JanelaFiltros : JanelaComMoldura
{
    public JanelaFiltros(LogicaFiltros logica)
    {
        Logica = logica;
        DataContext = logica;
        InitializeComponent();
    }

    public LogicaFiltros Logica { get; }

    protected override bool TemMudancas => Logica.TemMudancas;

    protected override void Salvar() => Logica.Salvar();

    protected override string TextoDaPerguntaAoFechar => "Você mudou os filtros e ainda não salvou.";

    private void ListaDePaginas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Chamado também enquanto a janela é montada, antes das páginas existirem
        if (paginaUsuarios == null || paginaBloqueios == null)
            return;

        bool usuarios = listaDePaginas.SelectedIndex == 0;
        paginaUsuarios.Visibility = usuarios ? Visibility.Visible : Visibility.Collapsed;
        paginaBloqueios.Visibility = usuarios ? Visibility.Collapsed : Visibility.Visible;
    }
}
