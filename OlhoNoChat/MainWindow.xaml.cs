using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace OlhoNoChat;

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Threading;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Inicio;
using OlhoNoChat.Sistema;
using OlhoNoChat.Som;
using OlhoNoChat.Twitch;
using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// The chat window: the chat page (WebView2) over the game, with its borders, quick settings in the title bar and tray menu.
/// The other parts are in the MainWindow.*.cs files.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly ContaDaTwitch _conta;
    private readonly AutorizacaoNoNavegador _autorizacao;
    private readonly EnvioDeMensagem _envio;
    private readonly ListaDeEmotes _listaDeEmotes;
    private readonly ResgatesDePontos _resgates;
    private readonly AtalhosGlobais _atalhos;
    private readonly ProcuraDeAtualizacoes _atualizacoes;

    // Configurações aberta (só uma por vez: pedir de novo traz a aberta para a frente)
    private JanelaConfiguracoes _janelaDeConfiguracoes;

    // O chat dentro da janela (o WebView2 e as páginas)
    private readonly NavegadorDoChat _navegador;
    private bool hasWebView2Runtime = false;

    private DispatcherTimer _timerCheckWebView2Install;

    private readonly Thickness noBorderThickness = new Thickness(0);

    private bool _hiddenBorders = false;
    private WindowDisplayMode CurrentDisplayMode = WindowDisplayMode.Setup;

    private readonly TocadorDeAviso _aviso = new();

    public MainWindow(ILogger<MainWindow> logger, ContaDaTwitch conta,
        AutorizacaoNoNavegador autorizacao, EnvioDeMensagem envio, ListaDeEmotes listaDeEmotes, ResgatesDePontos resgates,
        ProcuraDeAtualizacoes atualizacoes)
    {
        InitializeComponent();
        SetupQuickPanels();

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _conta = conta;
        _autorizacao = autorizacao;
        _envio = envio;
        _listaDeEmotes = listaDeEmotes;
        _resgates = resgates;
        _resgates.Resgatado += MostrarResgate;
        _aviso.SaidaVoltouParaAPadrao += GravarSaidaDeSomPadrao;
        _atalhos = new AtalhosGlobais(logger);
        _atualizacoes = atualizacoes;
        // "Atualizar agora": o Velopack fecha o app sem passar pelo Closing, então a posição vai para as opções antes
        _atualizacoes.AntesDeReiniciar = () => App.Opcoes.Janela = PosicaoDaJanela.De(this);
        _atualizacoes.ProcuraAutomaticaDesligada += () => _janelaDeConfiguracoes?.Logica.ProcuraAutomaticaDesligada();
        this.Closed += (s, e) =>
        {
            _atalhos.Dispose();
            _resgates.Desligar();
            _aviso.Dispose();
        };

        // Where the chat window was when it closed (nothing on the first start: the size of the XAML)
        App.Opcoes.Janela?.AplicarEm(this);
        this.Closing += (s, e) =>
        {
            App.Opcoes.Janela = PosicaoDaJanela.De(this);
            App.ArquivoDeConfiguracoes.Gravar();
        };

        SetupOrReplaceHotkeys();

        _navegador = new NavegadorDoChat(logger, mainWindowGrid, linha: 2, () => App.Opcoes, () => !_hiddenBorders);
        LigarNavegador();
        InitializeWebViewAsync();
        StartKeepOnTopGuard();
        StartDialogAttention();
        StartResizeCorner();
        StartChatInput();
        mainWindowGrid.SizeChanged += (s, e) => UpdateContentClip();
    }

    // Os resgates são do canal da conta: só aparecem quando o chat aberto é o desse canal (e só o Padrão os mostra)
    private void MostrarResgate(ResgatesDePontos.Resgate resgate)
    {
        if (!string.Equals(ChatChannel, _conta.Login, StringComparison.OrdinalIgnoreCase))
            return;

        PushNewChatMessageDispatcherInvoke(resgate.Texto, resgate.Nome, "#a1b3c4");
        if (!string.IsNullOrEmpty(resgate.TextoDigitado))
            PushNewChatMessageDispatcherInvoke(resgate.TextoDigitado, resgate.Nome, "#a1b3c4");
    }

    // O que a janela faz quando o chat carrega, recebe um aviso da página ou perde o navegador interno
    private void LigarNavegador()
    {
        // O controle fica a alguns pixels das bordas de redimensionar (e o canto de baixo é recortado dele)
        _navegador.ControleCriado += controle =>
        {
            controle.SizeChanged += (s, e) => UpdateResizeCorner();
            UpdateChatResizeEdge();
        };

        _navegador.ComecouACarregar += () =>
        {
            if (_composingInTwitchBox)
                EndCompose(returnFocus: false); // a caixa da Twitch vai embora com a página dela
            OnChatPageLoading();
        };

        _navegador.Carregou += sucesso =>
        {
            OnChatPageLoaded(sucesso);
            if (!sucesso)
                return;

            UpdateQuickValues(); // o tamanho do texto salvo pode ter sido ajustado aos limites
            UpdateChatScrollMode();
            UpdateChatLinks();
            TryShowBordersHint();
            TryShowWriteHint();
            TryShowSettingsNotice(); // por último: o aviso mais importante fica na tela

            // Resgates de pontos do canal (depois de verificar o acesso salvo à Twitch)
            _ = StartRedemptionsAsync();
        };

        _navegador.Mensagem += OnChatPageMessage;

        _navegador.FalhouDeVez += texto =>
        {
            _atalhos.Ligados = false; // o chat antigo não pode mais ser usado
            MessageBox.Show(texto, "Falha na recuperação", MessageBoxButton.OK, MessageBoxImage.Stop);
            ExitApplication();
        };
    }

    // Os quatro atalhos globais, registrados de novo a cada salvamento (um atalho trocado deixa de valer na hora)
    private void SetupOrReplaceHotkeys()
    {
        var settings = App.Opcoes;
        _atalhos.Definir("MostrarEsconderBordas", settings.AtalhoBordas, ToggleBorderVisibility);
        _atalhos.Definir("ModoRolagem", settings.AtalhoModoRolagem, ToggleInteractable);
        _atalhos.Definir("SempreNoTopo", settings.AtalhoSempreNoTopo, ToggleAlwaysOnTop);
        _atalhos.Definir("EscreverNoChat", settings.AtalhoEscrever, OnHotKeyWriteMessage);

        UpdateHotkeyTooltips();
    }

    private void CheckWebView2Timer_Tick(object sender, EventArgs e)
    {
        if (NavegadorDoChat.EstaInstalado())
            InitializeWebViewAsync();
    }

    private void ShowWebViewInstallUI()
    {
        hasWebView2Runtime = false;
        PlaceholderOverlay.Visibility = Visibility.Visible;
    }

    // Microsoft's small WebView2 installer, which downloads the rest itself
    private const string WebView2InstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var installButton = sender as Button;
        installButton.IsEnabled = false; // Disable button to prevent multiple clicks
        installButton.Content = "Baixando...";

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "OlhoNoChat");
            Directory.CreateDirectory(folder);
            string installerPath = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");

            // The whole download (about 2 MB) is read within the timeout, so a stalled connection can't leave it stuck
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                byte[] installer = await http.GetByteArrayAsync(WebView2InstallerUrl);
                await File.WriteAllBytesAsync(installerPath, installer);
            }

            // Run the installer and wait for it to finish.
            installButton.Content = "Instalando...";
            using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            if (process != null)
                await process.WaitForExitAsync();

            installButton.Content = "Instalação concluída";
            PlaceHolderOverlayText.Text = "WebView2 instalado com sucesso! O app vai recarregar sozinho.";

            if (_timerCheckWebView2Install == null)
            {
                _timerCheckWebView2Install = new DispatcherTimer();
                _timerCheckWebView2Install.Interval = TimeSpan.FromSeconds(2.5);
                _timerCheckWebView2Install.Tick += CheckWebView2Timer_Tick;
                _timerCheckWebView2Install.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível baixar ou instalar o WebView2. Confira sua internet e tente de novo.\n\n{ex.Message}", "Falha na instalação");
            installButton.IsEnabled = true;
            installButton.Content = "Baixar e instalar";
        }
    }

    private async void InitializeWebViewAsync()
    {
        if (!NavegadorDoChat.EstaInstalado())
        {
            ShowWebViewInstallUI();
            return;
        }

        hasWebView2Runtime = true;
        if (_timerCheckWebView2Install != null)
        {
            _timerCheckWebView2Install.Stop();
            _timerCheckWebView2Install.Tick -= CheckWebView2Timer_Tick;
            _timerCheckWebView2Install = null;
        }

        // Make sure the placeholder overlay is hidden
        PlaceholderOverlay.Visibility = Visibility.Collapsed;

        await _navegador.CriarAsync();
        SetupBrowser();
    }

    // Argumentos desta abertura, ou pedidos de uma cópia aberta depois (ver InstanciaUnica)
    public void ExecutarComandos(IReadOnlyList<ComandoDoApp> comandos)
    {
        foreach (ComandoDoApp comando in comandos)
        {
            switch (comando)
            {
                case ComandoDoApp.MostrarJanela:
                    // Com "Sempre no topo" só vem para a frente, sem tirar o foco do jogo
                    if (AlwaysOnTop)
                        ReassertTopMost();
                    else
                        ActivateChatWindow();
                    break;
                case ComandoDoApp.AlternarBordas:
                    ToggleBorderVisibility();
                    break;
                case ComandoDoApp.AbrirConfiguracoes:
                    ShowSettingsWindow();
                    break;
                case ComandoDoApp.RestaurarPosicao:
                    ResetWindowAndOfferSettingsFolder();
                    break;
            }
        }
    }

    // "Modo rolagem" (hotkey and tray menu)
    private void ToggleInteractable()
    {
        if (!hasWebView2Runtime) return;
        SetInteractable(CurrentDisplayMode != WindowDisplayMode.Setup);
    }

    private void SetInteractable(bool interactable)
    {
        ApplyInteractable(interactable);

        // Not over Configurações after "Salvar" (it stays open)
        if (interactable && !_settingsDialogOpen)
            ActivateChatWindow();

        UpdateChatScrollMode();
        ReassertTopMostBurst();
    }

    // The window takes clicks (setup mode) or lets them through to the game (overlay)
    private void ApplyInteractable(bool interactable)
    {
        if (_navegador.Controle != null)
            _navegador.Controle.Focusable = interactable;

        CurrentDisplayMode = interactable ? WindowDisplayMode.Setup : WindowDisplayMode.Overlay;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (interactable)
            JanelaDoWindows.TornarClicavel(hwnd);
        else
            JanelaDoWindows.DeixarCliqueAtravessar(hwnd);

        ApplyBackgroundOpacity();

        // The title bar is shown/hidden only with the borders (drawBorders/hideBorders);
        // with hidden borders, a thin colored frame shows that the chat is taking clicks.
        if (_hiddenBorders)
        {
            SetWindowFrame(interactable ? AccentBrush : Brushes.Transparent, interactable ? scrollModeBorderThickness : noBorderThickness);
        }
    }

    private readonly Thickness scrollModeBorderThickness = new Thickness(2);

    // Lets the "Padrão" chat page scroll while the window takes clicks, and shows the "modo rolagem" hint
    // only when the borders are hidden (otherwise the window is simply in setup mode).
    private void UpdateChatScrollMode()
    {
        if (_navegador.Pagina is not ChatPadrao || _navegador.Controle == null)
            return;

        bool enabled = _navegador.Controle.Focusable;
        Atalho hotkey = App.Opcoes.AtalhoModoRolagem;
        string hotkeyText = Atalho.Existe(hotkey) ? hotkey.ToString() : string.Empty;
        _navegador.Executar(ContratoComAPagina.ModoRolagem(enabled, enabled && _hiddenBorders, hotkeyText));
    }

    // The "Padrão" chat shows its links as clickable only while the borders are visible
    private void UpdateChatLinks()
    {
        if (_navegador.Pagina is ChatPadrao)
            _navegador.Executar(ContratoComAPagina.LinksClicaveis(!_hiddenBorders));
    }

    // With the borders shown, the side and bottom edges of the window resize it: the chat page (its own
    // window, which takes the mouse) stays a few pixels away from them, over its dark background
    private static readonly Thickness ResizeEdgeMargin = new Thickness(6, 0, 6, 6);

    private void UpdateChatResizeEdge()
    {
        _navegador.Controle?.SetValue(MarginProperty, _hiddenBorders ? this.noBorderThickness : ResizeEdgeMargin);
        UpdateResizeCorner();
    }

    private void drawBorders()
    {
        this.ShowInTaskbar = true;

        this.AppTitleBar.Visibility = Visibility.Visible;
        SetChatRowSpan(1);
        SetWindowFrame(Brushes.Transparent, this.noBorderThickness);
        this.ResizeMode = ResizeMode.CanResize;

        _hiddenBorders = false;
        UpdateChatResizeEdge();
        ApplyInteractable(true); // com as bordas visíveis o chat sempre aceita clique (rolar, links)

        // The title bar and the toolbar take clicks also when clicking the chat is off
        JanelaDoWindows.TornarClicavel(new WindowInteropHelper(this).Handle);

        UpdateChatInput();
        UpdateChannelBar();
        UpdateChatLinks();
        UpdateChatScrollMode();
        HideBordersHint();

        ActivateChatWindow();
        ReassertTopMostBurst();
    }

    private void hideBorders()
    {
        if (_composeFromButton)
            EndCompose(returnFocus: false);

        if (App.Opcoes.EsconderIconeDaBarraDeTarefas)
            this.ShowInTaskbar = false;

        // Prevent interaction with the browser
        ApplyInteractable(false);

        this.AppTitleBar.Visibility = Visibility.Collapsed;
        CloseQuickPanels();
        // The chat and its dark background cover the message box row too
        // (unless the message box is open with the hotkey, see MainWindow.ChatInput.cs)
        SetChatRowSpan(_composing ? 1 : 2);
        SetWindowFrame(Brushes.Transparent, this.noBorderThickness);
        this.ResizeMode = System.Windows.ResizeMode.NoResize;

        _hiddenBorders = true;
        _chatBoxOpen = false; // the box opened with "Escrever" starts closed again when the borders come back
        UpdateChatResizeEdge();
        UpdateChatInput();
        UpdateChannelBar();
        UpdateChatLinks();
        UpdateChatScrollMode();
        RequestBordersHint();

        ActivateChatWindow();
        ReassertTopMostBurst();
    }

    private void ToggleBorderVisibility()
    {
        if (!hasWebView2Runtime) return;

        if (_hiddenBorders)
            drawBorders();
        else
            hideBorders();
    }

    private void ResetWindowState()
    {
        drawBorders();
        this.WindowState = WindowState.Normal;
        this.Left = 10;
        this.Top = 10;
        this.Height = 500;
        this.Width = 320;
    }

    private void ExitApplication()
    {
        App.IsShuttingDown = true;
        Application.Current.Shutdown();
    }

    private void MenuItem_ToggleBorderVisible(object sender, RoutedEventArgs e)
    {
        ToggleBorderVisibility();
    }

    private void MenuItem_CheckForUpdates(object sender, RoutedEventArgs e)
    {
        _ = _atualizacoes.ProcurarAsync(manual: true, dono: null);
    }

    private void btnHide_Click(object sender, RoutedEventArgs e)
    {
        if (!hasWebView2Runtime) return;
        hideBorders();
    }

    // The text size gives letters of the same size in the "Padrão" and "Chat oficial da Twitch"
    // chats: the official one uses the font and size of the "Padrão" (CssDoChat)
    private void SetZoomFactor(double zoom)
    {
        App.Opcoes.TamanhoDoTexto = _navegador.AplicarZoom(zoom);
        UpdateQuickValues();
    }

    private void OpenSettingsFolder()
    {
        AbrirNoWindows.Pasta(App.ArquivoDeConfiguracoes.Pasta);
    }

    // The new-message sound of the "Padrão" chat, the only chat type that plays one. It follows the saved chat
    // type (the welcome page of the "Padrão" rings as soon as a channel is entered on the channel strip).
    // Also after every save: the sound, its volume or the output device may have changed.
    private void UpdateChatSound()
    {
        var settings = App.Opcoes;
        string file = settings.TipoDeChat == (int)TipoDeChat.Padrao
            ? SonsDisponiveis.Caminho(settings.PastaDosSons, settings.SomDeMensagem)
            : null;

        Exception erro = _aviso.Configurar(file, settings.Volume, settings.SaidaDeSom, settings.NomeDaSaidaDeSom ?? string.Empty,
            settings.SegundosEntreSons);
        if (erro != null)
        {
            MessageBox.Show($"Não foi possível carregar o arquivo de som: {file}\n\n{erro.Message}",
                "Erro ao carregar som", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // A saída de som gravada não existe mais (ou o Windows a recusou): o aviso já passou para a padrão do Windows
    private void GravarSaidaDeSomPadrao()
    {
        App.Opcoes.SaidaDeSom = TocadorDeAviso.Padrao;
        App.Opcoes.NomeDaSaidaDeSom = TocadorDeAviso.NomeDaPadraoGravado;
        App.ArquivoDeConfiguracoes.Gravar();
    }

    private void ShowSettingsWindow()
    {
        if (!hasWebView2Runtime)
        {
            MessageBox.Show(
                "Para usar o Olho no Chat, baixe e instale o WebView2 da Microsoft.\nDepois de instalar, abra o app de novo.",
                "WebView2 necessário",
                MessageBoxButton.OK, MessageBoxImage.Error);
            AbrirNoWindows.Site(WebView2InstallerUrl);
            return;
        }

        // Só uma janela de Configurações: pedir de novo (menu perto do relógio, /settings) traz a aberta para a frente
        if (_janelaDeConfiguracoes != null)
        {
            _janelaDeConfiguracoes.Activate();
            return;
        }

        // Atalhos devolvidos ao Windows enquanto Configurações está aberta: a combinação pode ser gravada na caixa
        // de atalho sem disparar a ação
        _atalhos.Ligados = false;

        var logica = new LogicaConfiguracoes(App.ArquivoDeConfiguracoes, new LogicaTwitch(_autorizacao, _conta, _resgates));
        var settingsWindow = new JanelaConfiguracoes(logica, this);
        _janelaDeConfiguracoes = settingsWindow;

        logica.ProcurarAtualizacoesPedido += () => _ = _atualizacoes.ProcurarAsync(manual: true, dono: settingsWindow);

        // The chat type saved before, for the notice about writing when a chat with a channel is chosen
        int chatTypeSaved = App.Opcoes.TipoDeChat;

        // "Salvar" keeps the window open, so every save is applied right away
        logica.Salvou += () =>
        {
            int chatTypeBefore = chatTypeSaved;
            chatTypeSaved = App.Opcoes.TipoDeChat;

            if (!IsChannelChatType(chatTypeBefore) && IsChannelChatType(chatTypeSaved))
                RequestWriteHint();

            _ = ApplySavedSettingsAsync();
        };

        _settingsDialogOpen = true;
        try
        {
            settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsDialogOpen = false;
            _janelaDeConfiguracoes = null;
        }
        TryShowWriteHint();

        // Changes that were not saved never reach App.Opcoes, so there is nothing to undo
        _atalhos.Ligados = true;
    }

    // Applies the settings just saved in the Settings (or Chat Filters) window
    private async Task ApplySavedSettingsAsync()
    {
        // Loading the chat page again clears the messages on screen, so it only happens when a
        // setting needs it (channel, theme, chat type...). The rest is applied to the open page.
        await _navegador.AplicarOpcoesSalvasAsync();
        UpdateChatSound();

        // Channel point redemptions and the "Escrever no chat…" box (options of the Twitch tab)
        if (App.Opcoes.MostrarResgates)
            _ = StartRedemptionsAsync();
        else
            _resgates.Desligar();
        UpdateChatInput();
        UpdateChannelBar();

        // The taskbar button hides only with the borders (see hideBorders)
        this.ShowInTaskbar = !_hiddenBorders || !App.Opcoes.EsconderIconeDaBarraDeTarefas;

        // Text size, background and "Sempre no topo" of the title bar (changed here only by "Restaurar tudo para o padrão")
        SetZoomFactor(App.Opcoes.TamanhoDoTexto);
        ApplyBackgroundOpacity();
        ApplyAlwaysOnTop();

        if (!this._hiddenBorders)
        {
            if (_navegador.Controle != null)
                _navegador.Controle.Focusable = true;
            SetInteractable(true);
        }

        SetupOrReplaceHotkeys();
    }

    // The gear opens Configurações (left or right click)
    private void btnSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Sistema.MolduraDaJanela.Aplicar(this); // own close button, no gray line around

        // Every time the app opens (it used to be once a day)
        if (App.Opcoes.ProcurarAtualizacoes)
        {
            _ = _atualizacoes.ProcurarAsync(manual: false, dono: null);
        }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetupBrowser()
    {
        // The background is applied with the borders (see ApplyInteractable)
        if (App.Opcoes.EsconderBordasAoAbrir)
            hideBorders();
        else
            drawBorders();

        UpdateChatSound();
        _navegador.AbrirDasOpcoes();
    }

    private void MenuItem_SettingsClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    private void MenuItem_Exit(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void MenuItem_ResetWindowClick(object sender, RoutedEventArgs e)
    {
        ResetWindowAndOfferSettingsFolder();
    }

    // Tray menu and jump list "Restaurar posição da janela"
    private void ResetWindowAndOfferSettingsFolder()
    {
        ResetWindowState();

        if (MessageBox.Show("Abrir a pasta de configurações?", "Pasta de configurações", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes)
        {
            OpenSettingsFolder();
        }
    }

    // The saved "Fundo" (0-255) on the dark background of the chat
    private void ApplyBackgroundOpacity()
    {
        double opacity = App.Opcoes.Fundo / 255.0;

        // A 0% background still takes clicks in setup mode
        if (opacity <= 0 && this.CurrentDisplayMode == WindowDisplayMode.Setup)
            opacity = 0.01;

        this.overlay.Opacity = opacity;
        UpdateQuickValues();
    }

    private void SetOpacityLevel(int level)
    {
        if (!hasWebView2Runtime) return;

        App.Opcoes.Fundo = (byte)Math.Clamp(level, 0, 255);
        ApplyBackgroundOpacity();
        SchedulePersist();
    }

    // Called from the EventSub thread: the chat and the page are only touched on the UI thread
    private void PushNewChatMessageDispatcherInvoke(string message, string nick, string color)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Only the "Padrão" page shows them, as action lines (like /me)
            if (_navegador.Pagina is ChatPadrao)
                _navegador.Executar(ContratoComAPagina.AdicionarAcao(nick ?? string.Empty, color, message));
        });
    }

    // "Sempre no topo" of the tray menu
    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        ToggleAlwaysOnTop();
    }

    // "Sempre no topo" of the title bar (also switched by UI Automation, which raises no Click)
    private void btnQuickTop_Toggled(object sender, RoutedEventArgs e)
    {
        if (btnQuickTop.IsChecked != AlwaysOnTop)
            ToggleAlwaysOnTop();
    }

    private void MenuItem_ToggleInteractable(object sender, RoutedEventArgs e)
    {
        ToggleInteractable();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        if (!App.IsShuttingDown)
            ExitApplication();
    }
}