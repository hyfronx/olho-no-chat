using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Escrever no chat de um canal ("Padrão" e "Chat oficial da Twitch") sem a tela. Duas caixas, uma de cada vez (aba Twitch):
/// a caixa "Escrever no chat…" do app, embaixo do chat, que envia pela conta conectada; ou, só no chat oficial, a caixa da
/// própria Twitch dentro da página. O botão Escrever abre a caixa com as bordas visíveis e ela fica aberta; o atalho abre
/// por cima do jogo, e clicar no jogo fecha. Aqui ficam as regras (quando dá, o que o botão e o atalho fazem, os textos) e
/// o envio; a janela faz o resto (foco, clique atravessa, o lugar do chat).
/// </summary>
public sealed partial class LogicaCaixaDeEscrever : ObservableObject
{
    /// <summary>O tamanho máximo de uma mensagem da Twitch.</summary>
    public const int TamanhoMaximo = 500;

    public enum AcaoDoBotao { Fechar, AbrirCaixaDaTwitch, AvisarQueNaoDa, AbrirCaixaDoApp }

    public enum AcaoDoAtalho { Fechar, AvisarQueNaoDa, Abrir }

    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _contaConectada;
    private readonly Func<bool> _contaPodeEnviar;
    private readonly Func<string> _nomeDaConta;
    private readonly Func<string, string, Task<EnvioDeMensagem.Resultado>> _enviar;

    /// <param name="enviar">Envia (canal, texto) pela conta conectada.</param>
    public LogicaCaixaDeEscrever(Func<Opcoes> opcoes, Func<bool> contaConectada, Func<bool> contaPodeEnviar, Func<string> nomeDaConta,
        Func<string, string, Task<EnvioDeMensagem.Resultado>> enviar)
    {
        _opcoes = opcoes;
        _contaConectada = contaConectada;
        _contaPodeEnviar = contaPodeEnviar;
        _nomeDaConta = nomeDaConta;
        _enviar = enviar;
    }

    // --- Estado ----------------------------------------------------------------------------------------------

    /// <summary>A caixa do app foi aberta pelo botão Escrever (fica aberta até fechar ou ocultar as bordas).</summary>
    public bool AbertaPeloBotao { get; private set; }

    /// <summary>
    /// Uma escrita começou pelo atalho (ou a caixa da Twitch pelo botão): a janela do chat está com o foco e, com as
    /// bordas ocultas, recebe cliques.
    /// </summary>
    public bool PeloAtalho { get; private set; }

    /// <summary>A escrita é na caixa da própria Twitch.</summary>
    public bool NaCaixaDaTwitch { get; private set; }

    /// <summary>A caixa da Twitch foi aberta pelo botão Escrever: fica aberta ao clicar em outra janela.</summary>
    public bool DaTwitchPeloBotao { get; private set; }

    /// <summary>A janela que estava em foco quando o atalho foi apertado (o jogo), para devolver o foco.</summary>
    public IntPtr DevolverFocoPara { get; private set; }

    /// <summary>Alguma caixa está aberta (a do botão só com as bordas visíveis).</summary>
    public bool Aberta(bool bordasVisiveis) => PeloAtalho || (AbertaPeloBotao && bordasVisiveis);

    /// <summary>A caixa do app aparece embaixo do chat.</summary>
    public bool CaixaDoAppNaTela(bool bordasVisiveis) => CaixaDoAppDisponivel && Aberta(bordasVisiveis);

    // --- Regras ----------------------------------------------------------------------------------------------

    private TipoDeChat Tipo => TiposDeChat.Ler(_opcoes().TipoDeChat);

    public bool TipoTemCanal => TiposDeChat.UsaCanal(Tipo);

    /// <summary>O canal do chat na tela, para onde as mensagens vão ("" nos tipos sem canal).</summary>
    public string Canal => TipoTemCanal ? PaginaDoChat.CanalSalvo(_opcoes()) : string.Empty;

    /// <summary>A caixa da própria Twitch foi escolhida (só existe no chat oficial).</summary>
    public bool UsaCaixaDaTwitch => Tipo == TipoDeChat.ChatOficial && _opcoes().CaixaDaTwitch;

    /// <summary>A caixa do app pode ser usada: conta conectada que pode escrever e um canal.</summary>
    public bool CaixaDoAppDisponivel => !UsaCaixaDaTwitch && _contaConectada() && _contaPodeEnviar() && Canal.Length > 0;

    /// <summary>A caixa da Twitch pode ser usada: a página do chat oficial está aberta (não a de boas-vindas).</summary>
    public bool CaixaDaTwitchDisponivel(bool paginaDoChatOficialAberta) =>
        UsaCaixaDaTwitch && Canal.Length > 0 && paginaDoChatOficialAberta;

    /// <summary>O que o botão Escrever faz agora (ele só existe com as bordas visíveis).</summary>
    public AcaoDoBotao AoClicarNoBotao(bool paginaDoChatOficialAberta)
    {
        if (Aberta(bordasVisiveis: true))
            return AcaoDoBotao.Fechar;
        if (CaixaDaTwitchDisponivel(paginaDoChatOficialAberta))
            return AcaoDoBotao.AbrirCaixaDaTwitch;
        return CaixaDoAppDisponivel ? AcaoDoBotao.AbrirCaixaDoApp : AcaoDoBotao.AvisarQueNaoDa;
    }

    /// <summary>
    /// O que o atalho faz agora: apertado de novo fecha; a caixa aberta pelo botão com a janela do chat ativa também fecha
    /// (com o jogo na frente, ele só dá o foco a ela).
    /// </summary>
    public AcaoDoAtalho AoApertarOAtalho(bool bordasVisiveis, bool janelaAtiva, bool paginaDoChatOficialAberta)
    {
        if (PeloAtalho || (AbertaPeloBotao && bordasVisiveis && janelaAtiva))
            return AcaoDoAtalho.Fechar;
        if (!CaixaDoAppDisponivel && !CaixaDaTwitchDisponivel(paginaDoChatOficialAberta))
            return AcaoDoAtalho.AvisarQueNaoDa;
        return AcaoDoAtalho.Abrir;
    }

    /// <summary>A caixa aberta pelo atalho fecha sozinha depois de enviar (opção da aba Twitch) e o jogo volta.</summary>
    public bool FechaDepoisDeEnviar => PeloAtalho && _opcoes().FecharCaixaDepoisDeEnviar;

    // --- Mudanças --------------------------------------------------------------------------------------------

    public void AbrirPeloBotao() => AbertaPeloBotao = true;

    /// <summary>Começa uma escrita pelo atalho, ou a da caixa da Twitch pelo botão.</summary>
    public void Comecar(bool naCaixaDaTwitch, bool daTwitchPeloBotao, IntPtr devolverFocoPara)
    {
        PeloAtalho = true;
        NaCaixaDaTwitch = naCaixaDaTwitch;
        DaTwitchPeloBotao = daTwitchPeloBotao;
        DevolverFocoPara = devolverFocoPara;
        AvisarDicas();
    }

    /// <summary>Termina a escrita começada por <see cref="Comecar"/>. Devolve a janela para dar o foco (ou zero).</summary>
    public IntPtr Terminar()
    {
        IntPtr devolver = DevolverFocoPara;
        PeloAtalho = false;
        NaCaixaDaTwitch = false;
        DaTwitchPeloBotao = false;
        DevolverFocoPara = IntPtr.Zero;
        AvisarDicas();
        return devolver;
    }

    /// <summary>Fechar a caixa ("×", Esc, o botão ou o atalho): a do botão também fecha.</summary>
    public void FecharOBotao()
    {
        AbertaPeloBotao = false;
        Status = string.Empty;
    }

    /// <summary>Ocultar as bordas fecha a caixa aberta pelo botão (e ela não volta com as bordas).</summary>
    public void BordasOcultas() => AbertaPeloBotao = false;

    /// <summary>A conta, o canal ou o tipo mudou: sem a caixa do app disponível, a aberta pelo botão fecha.</summary>
    public void Atualizar()
    {
        if (!CaixaDoAppDisponivel)
            AbertaPeloBotao = false;
        AvisarDicas();
    }

    // --- Textos ----------------------------------------------------------------------------------------------

    private Atalho? AtalhoEscrever => _opcoes().AtalhoEscrever;

    public string DicaDaCaixa => $"Vai para o chat de {Canal} como {_nomeDaConta()}";

    public string DicaDoFechar => Atalho.NaDica(PeloAtalho ? "Fechar a caixa e voltar para o jogo (Esc)." : "Fechar a caixa (Esc).",
        AtalhoEscrever);

    public string DicaDoBotaoEscrever(bool bordasVisiveis) => Atalho.NaDica(Aberta(bordasVisiveis)
        ? "Fechar a caixa de escrever no chat."
        : "Escrever no chat: abre a caixa embaixo do chat.", AtalhoEscrever);

    /// <summary>Por que não dá para escrever, na ordem em que a pessoa precisa resolver.</summary>
    public string TextoQueNaoDa =>
        !TipoTemCanal ? "Para escrever no chat, escolha o tipo de chat \"Padrão\" ou \"Chat oficial da Twitch\" em Configurações > Chat."
        : Canal.Length == 0 ? "Para escrever no chat, escolha o canal na faixa de cima do chat."
        : !_contaConectada() ? "Para escrever no chat, conecte sua conta da Twitch em Configurações > Twitch."
        : "Para escrever no chat, conecte sua conta de novo em Configurações > Twitch: a Twitch precisa dar a permissão de escrever.";

    /// <summary>
    /// O aviso de que dá para escrever (depois de trocar para um chat de canal nas Configurações), ou null quando não há
    /// canal salvo.
    /// </summary>
    public string? TextoQueDa(bool bordasVisiveis)
    {
        if (!TipoTemCanal || Canal.Length == 0)
            return null;

        string noJogo = Atalho.Existe(AtalhoEscrever)
            ? $"No jogo, aperte {AtalhoEscrever} para abrir ou fechar a caixa de escrever."
            : "Para abrir a caixa no jogo, escolha um atalho em Configurações > Geral.";
        string botao = bordasVisiveis ? "Clique no balão de conversa, na barra laranja, para abrir a caixa. " : string.Empty;
        return _contaConectada() || UsaCaixaDaTwitch
            ? "Neste chat você pode escrever. " + botao + noJogo
            : "Neste chat você pode escrever depois de conectar sua conta em Configurações > Twitch. " + noJogo;
    }

    private void AvisarDicas()
    {
        OnPropertyChanged(nameof(DicaDaCaixa));
        OnPropertyChanged(nameof(DicaDoFechar));
    }

    // --- Mensagem e envio ------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _texto = string.Empty;

    /// <summary>O erro do último envio (ou outro aviso da caixa); vazio = escondido.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeEnviar))]
    private bool _enviando;

    public bool PodeEnviar => !Enviando;

    // Digitar apaga o aviso
    partial void OnTextoChanged(string value) => Status = string.Empty;

    /// <summary>
    /// Envia o texto sem os espaços das pontas (vazio não faz nada; enquanto envia, outro pedido é ignorado). Deu certo: a
    /// caixa esvazia e devolve true. Deu errado: o texto fica e o erro aparece.
    /// </summary>
    public async Task<bool> EnviarAsync()
    {
        string texto = Texto.Trim();
        if (texto.Length == 0 || Enviando)
            return false;

        EnvioDeMensagem.Resultado resultado;
        Enviando = true;
        try
        {
            resultado = await _enviar(Canal, texto);
        }
        finally
        {
            Enviando = false;
        }

        if (resultado.Situacao == EnvioDeMensagem.Situacao.Enviada)
        {
            Texto = string.Empty;
            return true;
        }

        Status = resultado.Texto;
        return false;
    }

    /// <summary>
    /// A mensagem com o emote no lugar do cursor (trocando a seleção), com um espaço antes e depois quando falta (a Twitch
    /// precisa deles). Null se passaria do tamanho máximo; <paramref name="cursor"/> fica logo depois do emote.
    /// </summary>
    public static string? ComEmote(string texto, int inicio, int tamanhoDaSelecao, string nome, out int cursor)
    {
        string antes = texto[..inicio];
        string depois = texto[(inicio + tamanhoDaSelecao)..];
        string inserido = (antes.Length > 0 && !char.IsWhiteSpace(antes[^1]) ? " " : string.Empty)
                          + nome
                          + (depois.Length > 0 && char.IsWhiteSpace(depois[0]) ? string.Empty : " ");

        cursor = inicio + inserido.Length;
        if (antes.Length + inserido.Length + depois.Length > TamanhoMaximo)
            return null;
        return antes + inserido + depois;
    }

    public const string AvisoTamanhoMaximo = "A mensagem já está no tamanho máximo da Twitch (500 letras).";
}
