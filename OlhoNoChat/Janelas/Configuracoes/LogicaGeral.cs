using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Geral: a janela do chat, os atalhos de teclado, as atualizações e as opções avançadas (com "Restaurar tudo
/// para o padrão", que a janela confirma antes).
/// </summary>
public sealed partial class LogicaGeral : LogicaDaPagina
{
    public const string ChaveProcurarAtualizacoes = "Geral.ProcurarAtualizacoes";

    /// <summary>"Procurar agora".</summary>
    public event Action? ProcurarAtualizacoesPedido;

    /// <summary>"Restaurar" (a janela pergunta antes).</summary>
    public event Action? RestaurarPedido;

    [ObservableProperty]
    private bool _esconderBordasAoAbrir;

    [ObservableProperty]
    private bool _esconderIconeDaBarraDeTarefas;

    [ObservableProperty]
    private bool _procurarAtualizacoes;

    [ObservableProperty]
    private bool _permitirVariasCopias;

    // null = sem atalho
    [ObservableProperty]
    private Atalho? _atalhoBordas;

    [ObservableProperty]
    private Atalho? _atalhoModoRolagem;

    [ObservableProperty]
    private Atalho? _atalhoSempreNoTopo;

    [ObservableProperty]
    private Atalho? _atalhoEscrever;

    /// <summary>A versão instalada, ou "de desenvolvimento".</summary>
    public string Versao => InfoDoApp.Versao;

    [RelayCommand]
    private void ProcurarAgora() => ProcurarAtualizacoesPedido?.Invoke();

    [RelayCommand]
    private void Restaurar() => RestaurarPedido?.Invoke();

    public override void Carregar(Opcoes opcoes)
    {
        EsconderBordasAoAbrir = opcoes.EsconderBordasAoAbrir;
        EsconderIconeDaBarraDeTarefas = opcoes.EsconderIconeDaBarraDeTarefas;
        ProcurarAtualizacoes = opcoes.ProcurarAtualizacoes;
        PermitirVariasCopias = opcoes.PermitirVariasCopias;
        AtalhoBordas = opcoes.AtalhoBordas;
        AtalhoModoRolagem = opcoes.AtalhoModoRolagem;
        AtalhoSempreNoTopo = opcoes.AtalhoSempreNoTopo;
        AtalhoEscrever = opcoes.AtalhoEscrever;
    }

    public override void Gravar(Opcoes opcoes)
    {
        opcoes.EsconderBordasAoAbrir = EsconderBordasAoAbrir;
        opcoes.EsconderIconeDaBarraDeTarefas = EsconderIconeDaBarraDeTarefas;
        opcoes.ProcurarAtualizacoes = ProcurarAtualizacoes;
        opcoes.PermitirVariasCopias = PermitirVariasCopias;
        // Um atalho apagado fica sem valor no arquivo
        opcoes.AtalhoBordas = SoSeExiste(AtalhoBordas);
        opcoes.AtalhoModoRolagem = SoSeExiste(AtalhoModoRolagem);
        opcoes.AtalhoSempreNoTopo = SoSeExiste(AtalhoSempreNoTopo);
        opcoes.AtalhoEscrever = SoSeExiste(AtalhoEscrever);
    }

    private static Atalho? SoSeExiste(Atalho? atalho) => Atalho.Existe(atalho) ? atalho : null;

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Geral.EsconderBordasAoAbrir"] = EsconderBordasAoAbrir.ToString();
        estado["Geral.EsconderIconeDaBarraDeTarefas"] = EsconderIconeDaBarraDeTarefas.ToString();
        estado[ChaveProcurarAtualizacoes] = ProcurarAtualizacoes.ToString();
        estado["Geral.PermitirVariasCopias"] = PermitirVariasCopias.ToString();
        estado["Geral.AtalhoBordas"] = Atalho.TextoOuNenhum(AtalhoBordas);
        estado["Geral.AtalhoModoRolagem"] = Atalho.TextoOuNenhum(AtalhoModoRolagem);
        estado["Geral.AtalhoSempreNoTopo"] = Atalho.TextoOuNenhum(AtalhoSempreNoTopo);
        estado["Geral.AtalhoEscrever"] = Atalho.TextoOuNenhum(AtalhoEscrever);
    }
}
