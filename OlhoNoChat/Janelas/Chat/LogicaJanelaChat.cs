#nullable enable
using OlhoNoChat.Chat;
using OlhoNoChat.Inicio;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// O estado da janela do chat e o que sai dele: bordas visíveis ou ocultas, modo rolagem, clique atravessa, o fundo
/// escuro, a moldura laranja, as linhas que o chat ocupa, a faixa do canal, o canto de redimensionar. A janela só
/// aplica o que esta classe diz.
/// </summary>
public sealed class LogicaJanelaChat
{
    /// <summary>Com as bordas, a página fica esta distância das bordas da esquerda, direita e de baixo (para redimensionar).</summary>
    public const double MargemDasBordas = 6;

    private List<ComandoDoApp>? _comandosEsperando = [];

    /// <summary>A barra laranja aparece; a janela pode ser movida e redimensionada.</summary>
    public bool BordasVisiveis { get; private set; } = true;

    /// <summary>Com as bordas ocultas, a janela recebe cliques e a rodinha rola o chat.</summary>
    public bool ModoRolagem { get; private set; }

    /// <summary>Uma caixa de escrever está aberta pelo atalho (ou a da Twitch): a janela recebe cliques e teclas.</summary>
    public bool Escrevendo { get; set; }

    /// <summary>A caixa aberta é a da própria Twitch, dentro da página (a página precisa receber o foco).</summary>
    public bool EscrevendoNaCaixaDaTwitch { get; set; }

    /// <summary>A caixa "Escrever no chat…" do app está na tela, embaixo do chat.</summary>
    public bool CaixaDoAppNaTela { get; set; }

    /// <summary>Recebe cliques; senão os cliques atravessam para o jogo.</summary>
    public bool Clicavel => BordasVisiveis || ModoRolagem || Escrevendo;

    /// <summary>A página do chat pode receber o foco (rolar, links, a caixa da Twitch).</summary>
    public bool PaginaRecebeFoco => BordasVisiveis || ModoRolagem || EscrevendoNaCaixaDaTwitch;

    /// <summary>A linha laranja de 2 px em volta: só no modo rolagem com as bordas ocultas.</summary>
    public bool MolduraLaranja => ModoRolagem && !BordasVisiveis;

    /// <summary>
    /// Quantas linhas da grade o chat e o fundo escuro ocupam a partir da linha do chat: a caixa do app, quando está
    /// na tela, fica na linha de baixo (o navegador é desenhado por cima de tudo o que estiver no lugar dele).
    /// </summary>
    public int LinhasDoChat => CaixaDoAppNaTela ? 1 : 2;

    /// <summary>A margem da página nas bordas da esquerda, direita e de baixo.</summary>
    public double MargemDoChat => BordasVisiveis ? MargemDasBordas : 0;

    /// <summary>O canto de baixo à direita fica livre da página e mostra os pontinhos.</summary>
    public bool CantoLivre => BordasVisiveis && !CaixaDoAppNaTela;

    /// <summary>Os links do Padrão só abrem com as bordas visíveis.</summary>
    public bool LinksClicaveis => BordasVisiveis;

    /// <summary>A rolagem da página do Padrão: liberada quando a janela aceita cliques; o aviso só no modo rolagem.</summary>
    public (bool Ligado, bool Aviso) RolagemDaPagina => (BordasVisiveis || ModoRolagem, MolduraLaranja);

    /// <summary>O fundo escuro: <paramref name="fundo"/> de 0 a 255. Em 0% com a janela clicável vira 1%, para receber cliques.</summary>
    public double OpacidadeDoFundo(byte fundo)
    {
        double opacidade = fundo / 255.0;
        return opacidade <= 0 && (BordasVisiveis || ModoRolagem) ? 0.01 : opacidade;
    }

    /// <summary>O botão na barra de tarefas: sempre com as bordas; com elas ocultas, conforme a opção.</summary>
    public bool NaBarraDeTarefas(bool esconderIcone) => BordasVisiveis || !esconderIcone;

    /// <summary>A faixa do canal: com as bordas visíveis e um tipo de chat de canal.</summary>
    public bool FaixaDoCanalNaTela(TipoDeChat tipo) => BordasVisiveis && TiposDeChat.UsaCanal(tipo);

    public void MostrarBordas()
    {
        BordasVisiveis = true;
        ModoRolagem = false;
    }

    /// <summary>Ocultar as bordas sempre sai do modo rolagem.</summary>
    public void OcultarBordas()
    {
        BordasVisiveis = false;
        ModoRolagem = false;
    }

    /// <summary>
    /// Entra ou sai do modo rolagem. Com as bordas visíveis não faz nada: a janela já aceita cliques (decisão 5).
    /// Devolve se mudou.
    /// </summary>
    public bool AlternarModoRolagem()
    {
        if (BordasVisiveis)
            return false;
        ModoRolagem = !ModoRolagem;
        return true;
    }

    /// <summary>Clique no aviso do modo rolagem. Devolve se mudou.</summary>
    public bool SairDoModoRolagem()
    {
        if (!ModoRolagem)
            return false;
        ModoRolagem = false;
        return true;
    }

    // --- Comandos (argumentos e pedidos de outra cópia) ------------------------------------------------------

    /// <summary>A janela já tem o estado de abertura (bordas conforme a opção) e os comandos são feitos na hora.</summary>
    public bool Pronta => _comandosEsperando == null;

    /// <summary>
    /// Os comandos a fazer agora. Antes de a janela ficar pronta eles esperam (decisão 9): "/settings" abre com o chat já na
    /// tela e "/toggleborders" inverte o estado de abertura.
    /// </summary>
    public IReadOnlyList<ComandoDoApp> Receber(IReadOnlyList<ComandoDoApp> comandos)
    {
        if (_comandosEsperando == null)
            return comandos;
        _comandosEsperando.AddRange(comandos);
        return [];
    }

    /// <summary>A janela ficou pronta: devolve os comandos que estavam esperando, na ordem em que chegaram.</summary>
    public IReadOnlyList<ComandoDoApp> FicouPronta()
    {
        IReadOnlyList<ComandoDoApp> esperando = _comandosEsperando ?? [];
        _comandosEsperando = null;
        return esperando;
    }
}
