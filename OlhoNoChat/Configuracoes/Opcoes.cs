using System.Windows.Input;
using System.Windows.Media;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Som;
using Tipos = OlhoNoChat.Chat.TipoDeChat;

namespace OlhoNoChat.Configuracoes;

/// <summary>
/// Todas as opções do app, com os valores da primeira instalação. É o que fica em Configuracoes.json, na ordem
/// das abas onde cada opção aparece.
/// </summary>
/// <remarks>
/// Os números (tipo de chat, tema, saída de som) e os textos especiais ("Default", "None", "theme", "none") são os
/// mesmos do arquivo antigo, para a conversão só copiar os valores.
/// </remarks>
public sealed class Opcoes
{
    /// <summary>Formato do arquivo: muda só se um dia o arquivo precisar ser convertido de novo.</summary>
    public const int VersaoAtual = 1;

    public const int TemaNenhum = 0;
    public const int TemaPadrao = 1;
    public const string SomPadrao = "job-done.wav";
    // Texto em 80% e fundo escuro em 65% (o fundo vai de 0 a 255: 165 aparece como 65%)
    public const double TamanhoDoTextoPadrao = 0.8;
    public const byte FundoPadrao = 165;

    public int Versao { get; set; } = VersaoAtual;

    // Chat (o canal é trocado na faixa acima do chat)
    public string Canal { get; set; } = string.Empty;
    /// <summary>0 = Padrão, 1 = Chat oficial da Twitch, 2 = Endereço personalizado (<see cref="Tipos"/>).</summary>
    public int TipoDeChat { get; set; } = (int)Tipos.Padrao;
    public string EnderecoPersonalizado { get; set; } = string.Empty;
    public bool ApagarMensagensAntigas { get; set; } = false;
    /// <summary>Do jeito que foi digitado na aba Chat.</summary>
    public string SegundosParaApagar { get; set; } = "120";
    public bool EsconderBots { get; set; } = true;
    public bool EsconderGifs { get; set; } = false;
    public bool EsconderOutrosCanais { get; set; } = false;
    // Extensões de emotes do "Chat oficial da Twitch"
    public bool BetterTtv { get; set; } = true;
    public bool Emotes7tv { get; set; } = true;
    public bool MenuDeEmotesDoBetterTtv { get; set; } = true;
    public bool FrankerFaceZ { get; set; } = true;
    // Partes da página do "Chat oficial da Twitch" que podem sumir
    /// <summary>O título "Chat da live" em cima do chat.</summary>
    public bool EsconderTituloDoChat { get; set; } = true;
    /// <summary>A faixa que passa no topo (placar de presentes, bits, clipes e o botão de Cheer).</summary>
    public bool EsconderPlacarDoTopo { get; set; } = true;
    /// <summary>Os destaques por cima das mensagens (enquetes, palpites, hype train, mensagem fixada, drops).</summary>
    public bool EsconderDestaques { get; set; } = false;

    // Aparência
    public int Tema { get; set; } = TemaPadrao;
    public string CssDoTemaNenhum { get; set; } = string.Empty;
    public string CssDoEnderecoPersonalizado { get; set; } = string.Empty;
    public bool AparenciaPadraoNoChatOficial { get; set; } = true;
    public string CssDoChatOficial { get; set; } = string.Empty;
    /// <summary>"" = a cor do tema; ou "#RRGGBB".</summary>
    public string CorDoTexto { get; set; } = string.Empty;
    /// <summary>"theme" (contorno preto), "soft" (sombra suave) ou "none".</summary>
    public string ContornoDasLetras { get; set; } = "none";
    /// <summary>"theme", "Segoe UI", "Arial" ou "Verdana".</summary>
    public string Fonte { get; set; } = "theme";
    public bool MostrarHorario { get; set; } = false;

    // Barra laranja (gravados 0,5 s depois da última mudança) e o alfinete
    public double TamanhoDoTexto { get; set; } = TamanhoDoTextoPadrao;
    public byte Fundo { get; set; } = FundoPadrao;
    public bool SempreNoTopo { get; set; } = true;

    // Som
    /// <summary>Nome do arquivo na pasta de sons, ou <see cref="SonsDisponiveis.Nenhum"/>.</summary>
    public string SomDeMensagem { get; set; } = SomPadrao;
    /// <summary>0 = em toda mensagem nova; senão no máximo uma vez a cada tantos segundos.</summary>
    public int SegundosEntreSons { get; set; } = 0;
    public float Volume { get; set; } = 1.0f;
    /// <summary><see cref="TocadorDeAviso.Padrao"/> (-1) = a saída padrão do Windows.</summary>
    public int SaidaDeSom { get; set; } = TocadorDeAviso.Padrao;
    public string NomeDaSaidaDeSom { get; set; } = string.Empty;
    public string PastaDosSons { get; set; } = SonsDisponiveis.PastaPadrao;

    // Geral
    public bool EsconderBordasAoAbrir { get; set; } = false;
    public bool EsconderIconeDaBarraDeTarefas { get; set; } = false;
    public bool ProcurarAtualizacoes { get; set; } = true;
    public bool PermitirVariasCopias { get; set; } = false;
    // Atalhos: null = sem atalho (apagado pela pessoa)
    public Atalho? AtalhoBordas { get; set; } = new(Key.F9, ModifierKeys.Control | ModifierKeys.Alt);
    public Atalho? AtalhoModoRolagem { get; set; } = new(Key.F7, ModifierKeys.Control | ModifierKeys.Alt);
    public Atalho? AtalhoSempreNoTopo { get; set; } = new(Key.F8, ModifierKeys.Control | ModifierKeys.Alt);
    // F10 não: Alt + F10 é um atalho comum de gravação de jogo
    public Atalho? AtalhoEscrever { get; set; } = new(Key.F11, ModifierKeys.Control | ModifierKeys.Alt);

    // Twitch
    public bool MostrarResgates { get; set; } = false;
    /// <summary>false = a caixa "Escrever no chat…" do app; true = a caixa da própria Twitch (só no chat oficial).</summary>
    public bool CaixaDaTwitch { get; set; } = false;
    public bool FecharCaixaDepoisDeEnviar { get; set; } = false;
    public ContaGravada Conta { get; set; } = new();

    // Filtros do chat
    public bool DestacarUsuarios { get; set; } = false;
    public bool SoUsuariosDaLista { get; set; } = false;
    public bool DestacarModeradores { get; set; } = false;
    public bool DestacarVips { get; set; } = false;
    public List<string> ListaDeUsuarios { get; set; } = [];
    public List<string> UsuariosBloqueados { get; set; } = [];
    public Color CorDoDestaque { get; set; } = Color.FromArgb(150, 245, 245, 0); // amarelo
    public Color CorDosModeradores { get; set; } = Color.FromArgb(150, 0, 173, 3); // verde
    public Color CorDosVips { get; set; } = Color.FromArgb(150, 219, 51, 179); // rosa

    /// <summary>Onde a janela do chat estava quando fechou; null = nunca fechou (primeira abertura).</summary>
    public PosicaoDaJanela? Janela { get; set; }

    /// <summary>
    /// Valores que o app não sabe usar (arquivo editado à mão, ou valores de versões antigas) voltam para um que ele
    /// sabe: tema e tipo de chat que não existem mais, tamanho do texto zerado, listas e textos vazios (null).
    /// </summary>
    public void Corrigir()
    {
        var padrao = new Opcoes();

        // Só "Nenhum" (0) e "Padrão" (1) existem: os outros temas foram escondidos na 1.0.16 e depois removidos
        if (Tema != TemaNenhum && Tema != TemaPadrao)
            Tema = TemaPadrao;
        // O 3 era o jCyan, removido
        TipoDeChat = (int)TiposDeChat.Ler(TipoDeChat);
        if (TamanhoDoTexto <= 0 || double.IsNaN(TamanhoDoTexto))
            TamanhoDoTexto = TamanhoDoTextoPadrao;
        if (float.IsNaN(Volume))
            Volume = padrao.Volume;

        Canal ??= padrao.Canal;
        EnderecoPersonalizado ??= padrao.EnderecoPersonalizado;
        SegundosParaApagar ??= padrao.SegundosParaApagar;
        CssDoTemaNenhum ??= padrao.CssDoTemaNenhum;
        CssDoEnderecoPersonalizado ??= padrao.CssDoEnderecoPersonalizado;
        CssDoChatOficial ??= padrao.CssDoChatOficial;
        CorDoTexto ??= padrao.CorDoTexto;
        ContornoDasLetras ??= padrao.ContornoDasLetras;
        Fonte ??= padrao.Fonte;
        SomDeMensagem ??= padrao.SomDeMensagem;
        NomeDaSaidaDeSom ??= padrao.NomeDaSaidaDeSom;
        PastaDosSons ??= padrao.PastaDosSons;
        ListaDeUsuarios ??= [];
        UsuariosBloqueados ??= [];
        ListaDeUsuarios.RemoveAll(nome => nome == null);
        UsuariosBloqueados.RemoveAll(nome => nome == null);

        Conta ??= new ContaGravada();
        Conta.Corrigir();
        Versao = VersaoAtual;
    }

    /// <summary>
    /// "Restaurar tudo para o padrão": as opções da primeira instalação, menos o que não é opção: o canal, a conta da
    /// Twitch, as listas de nomes dos filtros e a posição da janela.
    /// </summary>
    public Opcoes RestauradasParaOPadrao() => new()
    {
        Canal = Canal,
        Conta = Conta,
        ListaDeUsuarios = ListaDeUsuarios,
        UsuariosBloqueados = UsuariosBloqueados,
        Janela = Janela,
    };
}
