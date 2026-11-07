using System.Text.Json.Serialization;
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
    // O som de uma instalação nova ("Intuition" desde a 2.6.1; antes "Job done"). Quem já tem o app continua com o som
    // gravado no arquivo, porque todas as opções são gravadas
    public const string SomPadrao = "intuition.wav";
    /// <summary>O som que entrou no lugar dos "Alert …" antigos (até a 1.2.0), na conversão do arquivo antigo.</summary>
    public const string SomNoLugarDosAntigos = "job-done.wav";
    // Texto em 80% e fundo escuro em 65% (o fundo vai de 0 a 255: 165 aparece como 65%)
    public const double TamanhoDoTextoPadrao = 0.8;
    public const byte FundoPadrao = 165;
    // A velocidade fixa de continuar a pausa, em mensagens de uma linha por segundo (aba Chat)
    public const double LinhasPorSegundoMinimo = 0.5;
    public const double LinhasPorSegundoMaximo = 6;
    public const double LinhasPorSegundoPadrao = 1.5;
    // Os limites das opções do overlay (aba Overlay)
    public const int SegundosParaSumirNoOverlayMinimo = 5;
    public const int SegundosParaSumirNoOverlayMaximo = 300;
    public const int SegundosParaSumirNoOverlayPadrao = 30;
    public const int SegundosDeAtrasoNoOverlayMaximo = 30;
    public const int MensagensNoOverlayMinimo = 1;
    public const int MensagensNoOverlayMaximo = 50;
    public const int MensagensNoOverlayPadrao = 12;
    public const int TamanhoDoTextoNoOverlayMinimo = 10;
    public const int TamanhoDoTextoNoOverlayMaximo = 60;
    public const int TamanhoDoTextoNoOverlayPadrao = 22;
    public const int CaracteresNoOverlayMinimo = 20;
    public const int CaracteresNoOverlayMaximo = 500;
    public const int CaracteresNoOverlayPadrao = 200;
    public const int EmotesNoOverlayMinimo = 1;
    public const int EmotesNoOverlayMaximo = 30;
    public const int EmotesNoOverlayPadrao = 8;
    public const int VelocidadeDaFaixaMinima = 30;
    public const int VelocidadeDaFaixaMaxima = 400;
    public const int VelocidadeDaFaixaPadrao = 120;
    public const int InclinacaoNoOverlayMinima = 5;
    public const int InclinacaoNoOverlayMaxima = 60;
    public const int InclinacaoNoOverlayPadrao = 30;
    public const int ConeDoFunilMinimo = 1;
    public const int ConeDoFunilMaximo = 10;
    public const int ConeDoFunilPadrao = 5;

    public int Versao { get; set; } = VersaoAtual;

    // Chat (o canal é trocado na faixa acima do chat)
    public string Canal { get; set; } = string.Empty;
    /// <summary>0 = Padrão, 1 = Chat oficial da Twitch, 2 = Endereço personalizado (<see cref="Tipos"/>).</summary>
    public int TipoDeChat { get; set; } = (int)Tipos.Padrao;
    /// <summary>
    /// "Chat Multiplataforma": o chat de uma live do YouTube, o de um canal da Kick e o de uma live do TikTok junto com o da Twitch, só no Padrão. Ligado, os recursos só
    /// da Twitch ficam desligados (escrever, emotes da conta, resgates, outros tipos de chat).
    /// </summary>
    public bool ChatMultiplataforma { get; set; } = false;
    /// <summary>"@nome", "UC…" ou "youtu.be/id" (<see cref="YouTube.CanalDoYouTube"/>); trocado na faixa do canal.</summary>
    public string CanalDoYouTube { get; set; } = string.Empty;
    /// <summary>Ao entrar no chat de uma live, mostrar também as últimas mensagens que o YouTube e o TikTok mandam (as de antes).</summary>
    public bool MostrarHistoricoDoYouTube { get; set; } = false;
    /// <summary>O nome do canal da Kick (<see cref="Kick.CanalDaKick"/>); trocado na faixa do canal.</summary>
    public string CanalDaKick { get; set; } = string.Empty;
    /// <summary>O nome do canal do TikTok, sem o @ (<see cref="TikTok.CanalDoTikTok"/>); trocado na faixa do canal.</summary>
    public string CanalDoTikTok { get; set; } = string.Empty;
    public string EnderecoPersonalizado { get; set; } = string.Empty;
    public bool ApagarMensagensAntigas { get; set; } = false;
    /// <summary>Os segundos em texto: a aba Chat grava um número inteiro (deslizante de 10 s a 10 min) e mostra o padrão quando o texto salvo não vale.</summary>
    public string SegundosParaApagar { get; set; } = "120";
    /// <summary>
    /// Pausar o chat (só no Padrão): ao continuar, ele rola sozinho até a última mensagem no ritmo de leitura, pela
    /// quantidade de texto que vai aparecendo; desligado, na velocidade fixa <see cref="LinhasPorSegundoAoContinuar"/>.
    /// </summary>
    public bool VelocidadeAutomaticaAoContinuar { get; set; } = true;
    public double LinhasPorSegundoAoContinuar { get; set; } = LinhasPorSegundoPadrao;
    public bool EsconderBots { get; set; } = true;
    /// <summary>
    /// "Prefixos dos comandos": a mensagem que começa com um deles é um comando. Valem para o "Esconder bots e comandos"
    /// do chat e para o "Esconder comandos" do overlay (uma lista para os dois, escolha do usuário em 2026-11-06).
    /// </summary>
    public List<string> PrefixosDosComandos { get; set; } = ["!"];
    public bool EsconderGifs { get; set; } = false;
    /// <summary>O selo "Primeira vez" na primeira mensagem de alguém no canal (a Twitch avisa; só no Padrão).</summary>
    public bool MostrarPrimeiraVez { get; set; } = true;
    public bool EsconderOutrosCanais { get; set; } = false;
    // Os avisos no chat Padrão, cada plataforma e cada tipo com o seu interruptor (pedido do usuário em 2026-10-23)
    /// <summary>Inscrições, presentes, raids, comunicados, sequências de visualizações... da Twitch.</summary>
    public bool MostrarAvisosDaTwitch { get; set; } = true;
    /// <summary>Os de membro do YouTube (Chat Multiplataforma).</summary>
    public bool MostrarAvisosDoYouTube { get; set; } = true;
    /// <summary>Os presentes do TikTok, um combo por linha (Chat Multiplataforma).</summary>
    public bool MostrarPresentesDoTikTok { get; set; } = true;
    /// <summary>Quem seguiu o criador no TikTok (Chat Multiplataforma).</summary>
    public bool MostrarSeguidoresDoTikTok { get; set; } = true;
    /// <summary>Quem compartilhou a live no TikTok (Chat Multiplataforma; desligado: em live grande são muitos).</summary>
    public bool MostrarCompartilhamentosDoTikTok { get; set; } = false;
    /// <summary>Quem virou Superfã, a assinatura do TikTok (Chat Multiplataforma).</summary>
    public bool MostrarSuperfasDoTikTok { get; set; } = true;
    /// <summary>
    /// Até a 2.4.x, "Esconder avisos" valia para a Twitch e o YouTube juntos. Só lido, para <see cref="Corrigir"/> passar
    /// para <see cref="MostrarAvisosDaTwitch"/> e <see cref="MostrarAvisosDoYouTube"/>; não volta para o arquivo.
    /// </summary>
    [JsonPropertyName("EsconderAvisos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EsconderAvisosAntigo { get; set; }
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
    public Atalho? AtalhoPausar { get; set; } = new(Key.F6, ModifierKeys.Control | ModifierKeys.Alt);
    // AutoMod: registrados só enquanto há mensagem esperando. Não F1 a F3 nem F12 (atalhos comuns de gravação e de
    // captura de tela de jogo) nem F4 (soltar o Ctrl antes vira Alt + F4, que fecha o jogo); Home e End ficam juntos
    public Atalho? AtalhoAprovarAutoMod { get; set; } = new(Key.Home, ModifierKeys.Control | ModifierKeys.Alt);
    public Atalho? AtalhoRecusarAutoMod { get; set; } = new(Key.End, ModifierKeys.Control | ModifierKeys.Alt);
    // Overlay da live: registrado só com o overlay ligado
    public Atalho? AtalhoLimparOverlay { get; set; } = new(Key.F5, ModifierKeys.Control | ModifierKeys.Alt);

    // Overlay (a página para a Fonte de navegador do OBS, ver Overlay/ServidorDoOverlay.cs)
    public bool OverlayLigado { get; set; } = false;
    /// <summary>A porta do endereço do overlay: fixa, muda só pelo "Encontrar outro endereço" ou à mão (aba Overlay).</summary>
    public int PortaDoOverlay { get; set; } = Overlay.ServidorDoOverlay.PortaPadrao;
    public bool OverlaySome { get; set; } = true;
    public int SegundosParaSumirNoOverlay { get; set; } = SegundosParaSumirNoOverlayPadrao;
    /// <summary>Quanto tempo a mensagem espera antes de ir ao overlay (0 = na hora): dá tempo de apagar uma ruim.</summary>
    public int SegundosDeAtrasoNoOverlay { get; set; } = 0;
    /// <summary>
    /// "Quantas mensagens: As que couberem" (aba Overlay > Mensagens; ligado por padrão, pedido do usuário em 2026-11-06): ficam quantas mensagens couberem na
    /// fonte do OBS, e a mais antiga sai por cima; o <see cref="MensagensNoOverlay"/> só vale com ele desligado.
    /// </summary>
    public bool PreencherAreaNoOverlay { get; set; } = true;
    public int MensagensNoOverlay { get; set; } = MensagensNoOverlayPadrao;
    /// <summary>Em pixels da Fonte de navegador.</summary>
    public int TamanhoDoTextoNoOverlay { get; set; } = TamanhoDoTextoNoOverlayPadrao;
    public string FonteDoOverlay { get; set; } = "Segoe UI";
    public bool NegritoNoOverlay { get; set; } = true;
    /// <summary>"theme" (contorno preto), "soft" (sombra suave) ou "none", como na aba Aparência.</summary>
    public string ContornoNoOverlay { get; set; } = "theme";
    /// <summary>"none" (sem fundo), "card" (cada mensagem num cartão escuro) ou "bubble" (balão, o nome em cima).</summary>
    public string FundoNoOverlay { get; set; } = "none";
    /// <summary>A opacidade do cartão, em porcentagem.</summary>
    public int OpacidadeDoFundoNoOverlay { get; set; } = 55;
    /// <summary>"bottom" (as novas embaixo) ou "top" (as novas em cima).</summary>
    public string DirecaoDoOverlay { get; set; } = "bottom";
    /// <summary>"left" ou "right".</summary>
    public string AlinhamentoDoOverlay { get; set; } = "left";
    /// <summary>
    /// "Saída suave" (aba Overlay > Visual, pedido do usuário em 2026-11-06; ligada por padrão): na borda por onde as
    /// mensagens antigas saem, elas somem num degradê em vez de ficarem cortadas.
    /// </summary>
    public bool SaidaSuaveNoOverlay { get; set; } = true;
    /// <summary>
    /// "Perspectiva" (aba Overlay > Visual, pedido do usuário em 2026-11-06): "none" (desligada, o padrão), "left" (o lado
    /// esquerdo mais para dentro da tela) ou "right" (o direito).
    /// </summary>
    public string PerspectivaNoOverlay { get; set; } = "none";
    /// <summary>
    /// O formato da perspectiva: "funnel" (funil, o padrão: as mensagens ficam no meio da fonte do OBS e se fecham por cima
    /// e por baixo, o que o usuário queria desde o começo) ou "tilt" (inclinada: ficam embaixo e sobem em direção ao lado
    /// de dentro).
    /// </summary>
    public string FormatoDaPerspectivaNoOverlay { get; set; } = "funnel";
    /// <summary>
    /// "Cone" do funil (pedido do usuário em 2026-11-07, com desenho): o quanto as linhas de cima e de baixo se fecham para o
    /// lado de dentro, de 1 (quase retas) a 10 (bem fechadas). Muda a profundidade da perspectiva, não o giro.
    /// </summary>
    public int ConeDoFunilNoOverlay { get; set; } = ConeDoFunilPadrao;
    /// <summary>O quanto as mensagens giram com a perspectiva, em graus.</summary>
    public int InclinacaoNoOverlay { get; set; } = InclinacaoNoOverlayPadrao;
    /// <summary>Como as mensagens entram e saem (<see cref="Overlay.ContratoDoOverlay.Animacoes"/>; "none" = sem animação).</summary>
    public string AnimacaoDoOverlay { get; set; } = "pop"; // "Pular", escolha do usuário (2026-11-06)
    public bool SelosNoOverlay { get; set; } = true;
    public bool HorarioNoOverlay { get; set; } = false;
    /// <summary>O ícone da plataforma (só com o Chat Multiplataforma).</summary>
    public bool PlataformaNoOverlay { get; set; } = true;
    /// <summary>O selo "Primeira vez" também na live.</summary>
    public bool PrimeiraVezNoOverlay { get; set; } = true;
    /// <summary>"list" (uma embaixo da outra) ou "ticker" (uma faixa horizontal, passando da direita para a esquerda).</summary>
    public string DisposicaoDoOverlay { get; set; } = "list";
    /// <summary>A velocidade da faixa horizontal, em pixels por segundo.</summary>
    public int VelocidadeDaFaixaNoOverlay { get; set; } = VelocidadeDaFaixaPadrao;
    /// <summary>Os prefixos são os do chat (<see cref="PrefixosDosComandos"/>).</summary>
    public bool EsconderComandosNoOverlay { get; set; } = true;
    public bool EsconderBotsNoOverlay { get; set; } = true;
    /// <summary>Viram **** na live (um * no fim pega o começo da palavra).</summary>
    public List<string> PalavrasProibidasNoOverlay { get; set; } = [];
    /// <summary>"replace" ([link]), "show" ou "hide" (a mensagem com link não vai para a live).</summary>
    public string LinksNoOverlay { get; set; } = "replace";
    public bool LimitarTamanhoNoOverlay { get; set; } = false;
    public int CaracteresNoOverlay { get; set; } = CaracteresNoOverlayPadrao;
    public bool LimitarEmotesNoOverlay { get; set; } = false;
    public int EmotesNoOverlay { get; set; } = EmotesNoOverlayPadrao;
    // Os avisos que vão para a live (só os que o chat também mostra)
    public bool InscricoesNoOverlay { get; set; } = true;
    public bool RaidsNoOverlay { get; set; } = true;
    public bool OutrosAvisosDaTwitchNoOverlay { get; set; } = true;
    public bool ResgatesNoOverlay { get; set; } = true;
    public bool SuperChatsNoOverlay { get; set; } = true;
    public bool MembrosDoYouTubeNoOverlay { get; set; } = true;
    public bool PresentesDoTikTokNoOverlay { get; set; } = true;
    public bool OutrosAvisosDoTikTokNoOverlay { get; set; } = true;

    // Twitch
    public bool MostrarResgates { get; set; } = false;
    /// <summary>As mensagens que o AutoMod segurou aparecem no Padrão para aprovar ou recusar (só nos canais que a conta modera).</summary>
    public bool AprovarAutoMod { get; set; } = true;
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

    /// <summary>As novidades já apresentadas (o balão que aparece uma vez só, ver <see cref="Janelas.Chat.Novidades"/>).</summary>
    public List<string> NovidadesVistas { get; set; } = [];

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
        // O Chat Multiplataforma só existe no Padrão
        if (ChatMultiplataforma)
            TipoDeChat = (int)Tipos.Padrao;
        if (TamanhoDoTexto <= 0 || double.IsNaN(TamanhoDoTexto))
            TamanhoDoTexto = TamanhoDoTextoPadrao;
        if (float.IsNaN(Volume))
            Volume = padrao.Volume;
        // A pausa chegou na 2.6.0 com Ctrl + Alt + F6: se essa combinação já era de outro atalho, ela não funcionaria
        // (o outro fica com ela), então a pausa fica sem atalho e a pessoa escolhe outro na aba Geral
        if (Atalho.Existe(AtalhoPausar)
            && new[] { AtalhoBordas, AtalhoModoRolagem, AtalhoSempreNoTopo, AtalhoEscrever }.Contains(AtalhoPausar))
            AtalhoPausar = null;
        // Os do AutoMod chegaram na 2.7.0 do mesmo jeito
        var anteriores = new[] { AtalhoBordas, AtalhoModoRolagem, AtalhoSempreNoTopo, AtalhoEscrever, AtalhoPausar };
        if (Atalho.Existe(AtalhoAprovarAutoMod) && anteriores.Contains(AtalhoAprovarAutoMod))
            AtalhoAprovarAutoMod = null;
        if (Atalho.Existe(AtalhoRecusarAutoMod) && anteriores.Append(AtalhoAprovarAutoMod).Contains(AtalhoRecusarAutoMod))
            AtalhoRecusarAutoMod = null;
        // O de limpar o overlay chegou na 2.9.0 do mesmo jeito
        if (Atalho.Existe(AtalhoLimparOverlay)
            && anteriores.Append(AtalhoAprovarAutoMod).Append(AtalhoRecusarAutoMod).Contains(AtalhoLimparOverlay))
            AtalhoLimparOverlay = null;
        LinhasPorSegundoAoContinuar = double.IsNaN(LinhasPorSegundoAoContinuar)
            ? LinhasPorSegundoPadrao
            : Math.Clamp(LinhasPorSegundoAoContinuar, LinhasPorSegundoMinimo, LinhasPorSegundoMaximo);
        CorrigirOverlay(padrao);
        // Uma lista vazia não esconderia comando nenhum, e a caixa da aba Chat nunca grava uma vazia
        PrefixosDosComandos = LerPrefixos(string.Join(' ', PrefixosDosComandos?.Where(p => p != null) ?? []));
        if (PrefixosDosComandos.Count == 0)
            PrefixosDosComandos = padrao.PrefixosDosComandos;

        if (EsconderAvisosAntigo == true)
        {
            MostrarAvisosDaTwitch = false;
            MostrarAvisosDoYouTube = false;
        }
        EsconderAvisosAntigo = null;

        Canal ??= padrao.Canal;
        CanalDoYouTube ??= padrao.CanalDoYouTube;
        CanalDaKick ??= padrao.CanalDaKick;
        CanalDoTikTok ??= padrao.CanalDoTikTok;
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
        NovidadesVistas ??= [];
        NovidadesVistas.RemoveAll(novidade => novidade == null);
        ListaDeUsuarios.RemoveAll(nome => nome == null);
        UsuariosBloqueados.RemoveAll(nome => nome == null);

        Conta ??= new ContaGravada();
        Conta.Corrigir();
        Versao = VersaoAtual;
    }

    // Os números fora dos limites voltam para dentro, e um texto que o overlay não conhece volta para o padrão
    private void CorrigirOverlay(Opcoes padrao)
    {
        if (!Overlay.ServidorDoOverlay.PortaValida(PortaDoOverlay))
            PortaDoOverlay = padrao.PortaDoOverlay;
        SegundosParaSumirNoOverlay = Math.Clamp(SegundosParaSumirNoOverlay, SegundosParaSumirNoOverlayMinimo, SegundosParaSumirNoOverlayMaximo);
        SegundosDeAtrasoNoOverlay = Math.Clamp(SegundosDeAtrasoNoOverlay, 0, SegundosDeAtrasoNoOverlayMaximo);
        MensagensNoOverlay = Math.Clamp(MensagensNoOverlay, MensagensNoOverlayMinimo, MensagensNoOverlayMaximo);
        TamanhoDoTextoNoOverlay = Math.Clamp(TamanhoDoTextoNoOverlay, TamanhoDoTextoNoOverlayMinimo, TamanhoDoTextoNoOverlayMaximo);
        OpacidadeDoFundoNoOverlay = Math.Clamp(OpacidadeDoFundoNoOverlay, 0, 100);
        CaracteresNoOverlay = Math.Clamp(CaracteresNoOverlay, CaracteresNoOverlayMinimo, CaracteresNoOverlayMaximo);
        EmotesNoOverlay = Math.Clamp(EmotesNoOverlay, EmotesNoOverlayMinimo, EmotesNoOverlayMaximo);

        FonteDoOverlay = UmDe(FonteDoOverlay, Overlay.ContratoDoOverlay.Fontes, padrao.FonteDoOverlay);
        ContornoNoOverlay = UmDe(ContornoNoOverlay, ["theme", "soft", "none"], padrao.ContornoNoOverlay);
        FundoNoOverlay = UmDe(FundoNoOverlay, ["none", "card", "bubble"], padrao.FundoNoOverlay);
        DisposicaoDoOverlay = UmDe(DisposicaoDoOverlay, ["list", "ticker"], padrao.DisposicaoDoOverlay);
        VelocidadeDaFaixaNoOverlay = Math.Clamp(VelocidadeDaFaixaNoOverlay, VelocidadeDaFaixaMinima, VelocidadeDaFaixaMaxima);
        DirecaoDoOverlay = UmDe(DirecaoDoOverlay, ["bottom", "top"], padrao.DirecaoDoOverlay);
        AlinhamentoDoOverlay = UmDe(AlinhamentoDoOverlay, ["left", "right"], padrao.AlinhamentoDoOverlay);
        PerspectivaNoOverlay = UmDe(PerspectivaNoOverlay, ["none", "left", "right"], padrao.PerspectivaNoOverlay);
        FormatoDaPerspectivaNoOverlay = UmDe(FormatoDaPerspectivaNoOverlay, ["funnel", "tilt"], padrao.FormatoDaPerspectivaNoOverlay);
        InclinacaoNoOverlay = Math.Clamp(InclinacaoNoOverlay, InclinacaoNoOverlayMinima, InclinacaoNoOverlayMaxima);
        ConeDoFunilNoOverlay = Math.Clamp(ConeDoFunilNoOverlay, ConeDoFunilMinimo, ConeDoFunilMaximo);
        AnimacaoDoOverlay = UmDe(AnimacaoDoOverlay, Overlay.ContratoDoOverlay.Animacoes, padrao.AnimacaoDoOverlay);
        LinksNoOverlay = UmDe(LinksNoOverlay, ["replace", "show", "hide"], padrao.LinksNoOverlay);
        PalavrasProibidasNoOverlay ??= [];
        PalavrasProibidasNoOverlay.RemoveAll(string.IsNullOrWhiteSpace);
    }

    /// <summary>
    /// Os prefixos de um texto, como "! $" ou "!, $": separados por espaço, vírgula ou ponto e vírgula, sem repetir.
    /// </summary>
    public static List<string> LerPrefixos(string? texto) =>
        (texto ?? string.Empty).Split([' ', '\t', '\r', '\n', ',', ';'],StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Os prefixos como a caixa da aba Chat mostra: "! $".</summary>
    public static string TextoDosPrefixos(IEnumerable<string> prefixos) => string.Join(' ', prefixos);

    private static string UmDe(string? valor, IEnumerable<string> conhecidos, string padrao) =>
        valor != null && conhecidos.Contains(valor) ? valor : padrao;

    /// <summary>
    /// "Restaurar tudo para o padrão": as opções da primeira instalação, menos o que não é opção: os canais, a conta da
    /// Twitch, as listas de nomes dos filtros, a posição da janela, as novidades já vistas, as palavras proibidas do
    /// overlay e a porta dele (o endereço colado no OBS não pode mudar).
    /// </summary>
    public Opcoes RestauradasParaOPadrao() => new()
    {
        Canal = Canal,
        CanalDoYouTube = CanalDoYouTube,
        CanalDaKick = CanalDaKick,
        CanalDoTikTok = CanalDoTikTok,
        Conta = Conta,
        ListaDeUsuarios = ListaDeUsuarios,
        UsuariosBloqueados = UsuariosBloqueados,
        Janela = Janela,
        NovidadesVistas = NovidadesVistas,
        PalavrasProibidasNoOverlay = PalavrasProibidasNoOverlay,
        PortaDoOverlay = PortaDoOverlay,
    };
}
