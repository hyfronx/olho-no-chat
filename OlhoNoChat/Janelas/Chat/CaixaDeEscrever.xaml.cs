using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Sistema;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A caixa "Escrever no chat…" do app na tela (a lógica fica em <see cref="LogicaCaixaDeEscrever"/>): Enter envia, Esc
/// fecha a lista de emotes ou a caixa, "×" fecha; o botão de carinha abre a lista de emotes, que funciona igual com a
/// caixa aberta pelo botão ou pelo atalho, por cima do jogo.
/// </summary>
public partial class CaixaDeEscrever : UserControl
{
    // O clique que fechou a lista (fora dela) não a abre de novo
    private static readonly TimeSpan ProtecaoDoClique = TimeSpan.FromMilliseconds(300);

    private LogicaCaixaDeEscrever? _logica;
    private AutorizacaoNoNavegador? _autorizacao;
    private ContaDaTwitch? _conta;
    private DateTime _listaFechouEm = DateTime.MinValue;

    public CaixaDeEscrever()
    {
        InitializeComponent();
        listaDeEmotes.EmoteEscolhido += PorEmote;
        listaDeEmotes.FecharPedido += FecharEmotes;
        listaDeEmotes.EmojisPedido += () =>
        {
            FecharEmotes();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, JanelaDoWindows.AbrirPainelDeEmojis);
        };
        listaDeEmotes.ConectarDeNovoPedido += () => _ = ConectarDeNovoAsync();
    }

    /// <summary>"×" ou Esc: fechar a caixa (e devolver o foco ao jogo, se ela foi aberta pelo atalho).</summary>
    public event Action? FecharPedido;

    /// <summary>A mensagem foi enviada.</summary>
    public event Action? Enviada;

    public LogicaCaixaDeEscrever? Logica
    {
        get => _logica;
        set
        {
            _logica = value;
            DataContext = value;
        }
    }

    public void Ligar(ListaDeEmotes lista, ImagensDeEmotes imagens, ContaDaTwitch conta, AutorizacaoNoNavegador autorizacao, ILogger log)
    {
        _conta = conta;
        _autorizacao = autorizacao;
        listaDeEmotes.Ligar(lista, imagens, conta, log);
    }

    public bool EmotesAbertos => painelDeEmotes.IsOpen;

    /// <summary>O cursor na caixa de mensagem (se ela estiver na tela).</summary>
    public void Focar()
    {
        if (Visibility != Visibility.Visible)
            return;
        caixaMensagem.Focus();
        Keyboard.Focus(caixaMensagem);
        // A janela pode estar sendo ativada agora (atalho por cima do jogo): a ativação devolve o foco ao último elemento
        // da janela, então o cursor vai para a caixa de novo logo depois
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Visibility == Visibility.Visible && !caixaMensagem.IsKeyboardFocused)
                Keyboard.Focus(caixaMensagem);
        });
    }

    public void FecharEmotes()
    {
        painelDeEmotes.IsOpen = false;
        Focar();
    }

    /// <summary>Outra conta (ou desconectada): a lista guardada vai embora.</summary>
    public void DescartarEmotes() => listaDeEmotes.Descartar();

    // A lista começa a carregar quando a caixa ganha o foco, para estar pronta quando o botão for clicado
    private void Mensagem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_logica is { CaixaDoAppDisponivel: true } && !listaDeEmotes.Pronta(_logica.Canal))
            _ = listaDeEmotes.CarregarAsync(_logica.Canal);
    }

    private void Mensagem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = EnviarAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (painelDeEmotes.IsOpen)
                FecharEmotes(); // o Esc fecha primeiro a lista
            else
                FecharPedido?.Invoke();
        }
    }

    private void Enviar_Click(object sender, RoutedEventArgs e) => _ = EnviarAsync();

    private async Task EnviarAsync()
    {
        if (_logica != null && await _logica.EnviarAsync())
            Enviada?.Invoke();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => FecharPedido?.Invoke();

    // --- Emotes ----------------------------------------------------------------------------------------------

    private void Emotes_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Com a lista aberta, o botão a fecha
        if (painelDeEmotes.IsOpen)
        {
            e.Handled = true;
            FecharEmotes();
        }
    }

    private async void Emotes_Click(object sender, RoutedEventArgs e)
    {
        if (_logica == null || DateTime.UtcNow - _listaFechouEm < ProtecaoDoClique)
            return;

        listaDeEmotes.LimparBusca();
        painelDeEmotes.IsOpen = true;
        Focar();
        await listaDeEmotes.CarregarAsync(_logica.Canal);
    }

    private void PainelDeEmotes_Closed(object? sender, EventArgs e)
    {
        _listaFechouEm = DateTime.UtcNow;
        listaDeEmotes.AoFechar();
    }

    // O emote vai para o lugar do cursor, com espaços em volta; a lista continua aberta
    private void PorEmote(string nome)
    {
        if (_logica == null)
            return;

        string? texto = LogicaCaixaDeEscrever.ComEmote(caixaMensagem.Text, caixaMensagem.SelectionStart, caixaMensagem.SelectionLength,
            nome, out int cursor);
        if (texto == null)
        {
            _logica.Status = LogicaCaixaDeEscrever.AvisoTamanhoMaximo;
            return;
        }

        caixaMensagem.Text = texto;
        caixaMensagem.SelectionStart = cursor;
        caixaMensagem.SelectionLength = 0;
        Focar();
    }

    // "Conectar de novo" na lista: o mesmo da aba Twitch (o navegador pede a permissão nova)
    private async Task ConectarDeNovoAsync()
    {
        if (_logica == null || _autorizacao == null || _conta == null || _autorizacao.EstaEsperando)
            return;

        FecharEmotes();
        _logica.Status = "Termine no navegador que abriu: clique em \"Autorizar\" na página da Twitch.";

        AutorizacaoNoNavegador.Resultado resultado = await _autorizacao.ConectarAsync();
        switch (resultado.Fim)
        {
            case AutorizacaoNoNavegador.Fim.PortasOcupadas:
                _logica.Status = AutorizacaoNoNavegador.TextoPortasOcupadas;
                break;
            case AutorizacaoNoNavegador.Fim.Token:
                bool conectou = await _conta.ConectarAsync(resultado.Token);
                listaDeEmotes.Descartar(); // carrega de novo com a permissão nova
                _logica.Status = conectou ? string.Empty : "A Twitch não confirmou o acesso. Tente de novo na aba Twitch das Configurações.";
                break;
            default:
                _logica.Status = string.Empty;
                break;
        }
    }
}
