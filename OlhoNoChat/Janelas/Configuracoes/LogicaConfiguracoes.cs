#nullable enable
using System.ComponentModel;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A janela Configurações sem a tela: as seis abas, o tipo de chat escolhido (que as abas Chat, Aparência e Som seguem
/// antes de salvar), "Salvar" (grava sem fechar), "Restaurar tudo para o padrão" e a conferência de mudanças. Nada vai
/// para as opções antes de <see cref="Salvar"/>; conectar e desconectar a conta são na hora (aba Twitch).
/// </summary>
public sealed class LogicaConfiguracoes
{
    private readonly ArquivoDeConfiguracoes _arquivo;
    private Dictionary<string, string> _estadoSalvo;

    public LogicaConfiguracoes(ArquivoDeConfiguracoes arquivo, LogicaTwitch twitch, Action<string?, float, int>? tocarPrevia = null)
    {
        _arquivo = arquivo;
        Chat = new LogicaChat(() => _arquivo.Opcoes, _arquivo.Gravar);
        Aparencia = new LogicaAparencia();
        Som = new LogicaSom(tocarPrevia);
        Geral = new LogicaGeral();
        Twitch = twitch;
        Sobre = new LogicaSobre();

        Chat.PropertyChanged += ChatMudou;
        Chat.FiltrosSalvos += () => Salvou?.Invoke();
        Geral.ProcurarAtualizacoesPedido += () => ProcurarAtualizacoesPedido?.Invoke();

        Carregar();
        _estadoSalvo = Estado();
    }

    /// <summary>Depois de gravar (Salvar, Restaurar ou o Salvar da janela Filtros do chat): o chat aplica na hora.</summary>
    public event Action? Salvou;

    /// <summary>"Procurar agora" da aba Geral.</summary>
    public event Action? ProcurarAtualizacoesPedido;

    public LogicaChat Chat { get; }
    public LogicaAparencia Aparencia { get; }
    public LogicaSom Som { get; }
    public LogicaGeral Geral { get; }
    public LogicaTwitch Twitch { get; }
    public LogicaSobre Sobre { get; }

    private IEnumerable<LogicaDaPagina> Paginas => [Chat, Aparencia, Som, Geral, Twitch];

    // Trocar o tipo na lista muda na hora o que as abas mostram
    private void ChatMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogicaChat.TipoDeChat))
            MostrarTipo();
    }

    private void MostrarTipo()
    {
        foreach (LogicaDaPagina pagina in Paginas)
            pagina.TipoNaTela = Chat.Tipo;
    }

    // O tipo primeiro: ele decide o que as outras abas mostram
    private void Carregar()
    {
        Opcoes opcoes = _arquivo.Opcoes;
        Chat.Carregar(opcoes);
        MostrarTipo();
        foreach (LogicaDaPagina pagina in Paginas.Skip(1))
            pagina.Carregar(opcoes);
    }

    private Dictionary<string, string> Estado()
    {
        var estado = new Dictionary<string, string>();
        foreach (LogicaDaPagina pagina in Paginas)
            pagina.Estado(estado);
        return estado;
    }

    /// <summary>O que está na tela é diferente do que foi salvo (mudar e voltar não conta; a aba Sobre não entra).</summary>
    public bool TemMudancas
    {
        get
        {
            Dictionary<string, string> agora = Estado();
            return agora.Count != _estadoSalvo.Count
                   || agora.Any(item => !_estadoSalvo.TryGetValue(item.Key, out string? salvo) || salvo != item.Value);
        }
    }

    /// <summary>Grava tudo o que está na tela (só as opções do tipo de chat escolhido) e avisa o chat.</summary>
    public void Salvar()
    {
        Opcoes opcoes = _arquivo.Opcoes;
        opcoes.TipoDeChat = (int)Chat.Tipo;
        foreach (LogicaDaPagina pagina in Paginas)
            pagina.Gravar(opcoes);
        _arquivo.Gravar();

        Twitch.Salvou(opcoes);
        _estadoSalvo = Estado();
        Salvou?.Invoke();
    }

    /// <summary>
    /// "Restaurar tudo para o padrão" (já confirmado): as opções voltam às da primeira instalação (menos canal, conta,
    /// listas dos filtros e posição da janela), todas as abas mostram os valores novos e o chat aplica.
    /// </summary>
    public void Restaurar()
    {
        _arquivo.RestaurarPadrao();
        Carregar();
        _estadoSalvo = Estado();
        Salvou?.Invoke();
    }

    /// <summary>
    /// "Não procurar atualizações automaticamente" (janela Nova versão) já gravou a opção desligada: o interruptor da
    /// aba Geral acompanha, sem contar como mudança; as outras mudanças da tela continuam sem salvar.
    /// </summary>
    public void ProcuraAutomaticaDesligada()
    {
        Geral.ProcurarAtualizacoes = false;
        _estadoSalvo[LogicaGeral.ChaveProcurarAtualizacoes] = false.ToString();
    }
}
