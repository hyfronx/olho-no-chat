using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A barra laranja da janela do chat: ocultar bordas, pausar, tamanho do texto e fundo (com os painéis e a rodinha), sempre no
/// topo, Chat Multiplataforma, escrever, Configurações e fechar. Arrastar a barra (fora dos botões) move a janela; clique duplo não maximiza.
/// Quem decide e aplica é a janela: a barra só avisa o que foi pedido e mostra o estado que a janela passa.
/// </summary>
public partial class BarraDeCima : UserControl
{
    // Depois de fechar o painel com um clique fora dele, esse mesmo clique (no botão do painel) não o abre de novo
    private static readonly TimeSpan ProtecaoDoClique = TimeSpan.FromMilliseconds(300);

    private bool _mostrando;
    private bool _acompanhaAJanela;
    private Popup? _painelFechado;
    private DateTime _fechouEm = DateTime.MinValue;

    // Os botões vão até uns 255 px, o selo ocupa uns 45 e o "×" os últimos 40
    private const double LarguraComSeloBeta = 345;

    public BarraDeCima()
    {
        InitializeComponent();

        // O selo BETA fica só quando sobra espaço entre a engrenagem e o "×" (nunca por cima de um botão)
        if (InfoDoApp.EhBeta)
            barra.SizeChanged += (_, _) => seloBeta.Visibility = barra.ActualWidth >= LarguraComSeloBeta ? Visibility.Visible : Visibility.Collapsed;

        ajusteTamanhoDoTexto.ValorEscolhido += valor => TamanhoDoTextoEscolhido?.Invoke(valor);
        ajusteTamanhoDoTexto.VoltarAoPadraoPedido += () => TamanhoDoTextoEscolhido?.Invoke(Opcoes.TamanhoDoTextoPadrao);
        ajusteFundo.ValorEscolhido += valor => FundoEscolhido?.Invoke(valor);
        ajusteFundo.VoltarAoPadraoPedido += () => FundoPadraoPedido?.Invoke();
        ajusteTamanhoDoTexto.FecharPedido += FecharPaineis;
        ajusteFundo.FecharPedido += FecharPaineis;
        ajusteTamanhoDoTexto.DicaDoPadrao = $"Voltar ao padrão ({Math.Round(Opcoes.TamanhoDoTextoPadrao * 100)}%)";
        ajusteFundo.DicaDoPadrao = $"Voltar ao padrão ({Math.Round(Opcoes.FundoPadrao / 2.55)}%)";

        // Um popup não acompanha a janela sozinho: arrastando a janela, o balão da novidade é posto de novo embaixo do botão
        Loaded += (_, _) =>
        {
            if (_acompanhaAJanela || Window.GetWindow(this) is not Window janela)
                return;
            _acompanhaAJanela = true;
            janela.LocationChanged += (_, _) =>
            {
                if (!balaoNovidade.IsOpen)
                    return;
                balaoNovidade.HorizontalOffset += 0.01;
                balaoNovidade.HorizontalOffset -= 0.01;
            };
        };

        // O Closed do painel só vem depois da animação de sumir: o clique que o fechou já teria aberto de novo
        var aberto = DependencyPropertyDescriptor.FromProperty(Popup.IsOpenProperty, typeof(Popup));
        foreach (Popup painel in new[] { painelTamanhoDoTexto, painelFundo })
        {
            aberto.AddValueChanged(painel, (_, _) =>
            {
                ((FrameworkElement)painel.PlacementTarget).Tag = painel.IsOpen ? "aberto" : null;
                if (!painel.IsOpen)
                {
                    _painelFechado = painel;
                    _fechouEm = DateTime.UtcNow;
                }
            });
        }
    }

    public event Action? OcultarBordasPedido;
    public event Action? ConfiguracoesPedido;
    public event Action? FecharPedido;

    /// <summary>O botão "Sempre no topo" foi clicado (ou trocado pela automação): o estado pedido.</summary>
    public event Action<bool>? SempreNoTopoPedido;

    /// <summary>O botão do Chat Multiplataforma foi clicado: ligado (Twitch, YouTube, Kick e TikTok) ou só a Twitch.</summary>
    public event Action<bool>? MultiplataformaPedido;

    /// <summary>O botão Escrever foi clicado: a janela abre ou fecha a caixa e depois mostra o estado.</summary>
    public event Action? EscreverPedido;

    /// <summary>O botão de pausar foi clicado: a janela pausa ou continua o chat, e a página diz o estado novo.</summary>
    public event Action? PausarPedido;

    /// <summary>"Entendi" no balão da novidade (no aviso do overlay, "Resolver").</summary>
    public event Action? NovidadeEntendida;

    /// <summary>"Agora não" no aviso do overlay.</summary>
    public event Action? NovidadeRecusada;

    /// <summary>O balão de uma novidade está na tela.</summary>
    public bool NovidadeAberta => balaoNovidade.IsOpen;

    /// <summary>O tamanho do texto escolhido (0,5 a 2,5).</summary>
    public event Action<double>? TamanhoDoTextoEscolhido;

    /// <summary>O fundo escolhido, em porcentagem (0 a 100).</summary>
    public event Action<double>? FundoEscolhido;

    /// <summary>"Voltar ao padrão" do fundo: o valor gravado exato (165), não a porcentagem.</summary>
    public event Action? FundoPadraoPedido;

    // --- O que a janela mostra -------------------------------------------------------------------------------

    /// <summary>O tamanho do texto e o fundo em uso: nos painéis, nas dicas e para a automação.</summary>
    public void MostrarValores(double tamanhoDoTexto, byte fundo)
    {
        string texto = $"{Math.Round(tamanhoDoTexto * 100)}%";
        double porcentagem = Math.Round(fundo / 2.55);
        string escuro = $"{porcentagem}%";

        ajusteTamanhoDoTexto.Mostrar(tamanhoDoTexto, texto);
        // A porcentagem inteira, como aparece: a rodinha e as setas andam de 5 em 5 a partir dela
        ajusteFundo.Mostrar(porcentagem, escuro);
        AutomationProperties.SetHelpText(botaoTamanhoDoTexto, texto);
        AutomationProperties.SetHelpText(botaoFundo, escuro);
        dicaTamanhoDoTexto.Content = $"Tamanho do texto: {texto}\nA rodinha do mouse aqui também ajusta.";
        dicaFundo.Content = $"Fundo do chat: {escuro}\nA rodinha do mouse aqui também ajusta.";
    }

    public void MostrarSempreNoTopo(bool ligado, Atalho? atalho)
    {
        _mostrando = true;
        botaoSempreNoTopo.IsChecked = ligado;
        _mostrando = false;
        botaoSempreNoTopo.ToolTip = Atalho.NaDica(ligado ? "Desativar Sempre no topo" : "Ativar Sempre no topo (o chat fica na frente do jogo)",
            atalho);
    }

    public void MostrarMultiplataforma(bool ligado)
    {
        _mostrando = true;
        botaoMultiplataforma.IsChecked = ligado;
        _mostrando = false;
        botaoMultiplataforma.ToolTip = ligado
            ? "Desativar Chat Multiplataforma (Beta)"
            : "Ativar Chat Multiplataforma (Beta): Twitch, YouTube, Kick e TikTok juntos, só para ler";
    }

    /// <summary>O Escrever só existe nos tipos de chat com canal; aceso enquanto uma caixa está aberta.</summary>
    public void MostrarEscrever(bool existe, bool aceso, string dica)
    {
        botaoEscrever.Visibility = existe ? Visibility.Visible : Visibility.Collapsed;
        _mostrando = true;
        botaoEscrever.IsChecked = aceso;
        _mostrando = false;
        botaoEscrever.ToolTip = dica;
    }

    /// <summary>
    /// O botão de pausar só existe no chat Padrão. Pausado, fica aceso com o ▶ (continuar); ao vivo e enquanto o chat
    /// rola até a última mensagem, mostra o ⏸. O estado também vai para a automação (HelpText).
    /// </summary>
    public void MostrarPausa(bool existe, bool pausado, bool continuando, Atalho? atalho)
    {
        botaoPausar.Visibility = existe ? Visibility.Visible : Visibility.Collapsed;
        _mostrando = true;
        botaoPausar.IsChecked = pausado;
        _mostrando = false;
        iconePausar.Text = pausado ? "" : "";
        AutomationProperties.SetHelpText(botaoPausar, pausado ? "pausado" : continuando ? "continuando" : "ao vivo");
        botaoPausar.ToolTip = Atalho.NaDica(pausado
                ? "Continuar: o chat rola sozinho até a última mensagem"
                : continuando
                    ? "Pausar de novo (o chat está rolando até a última mensagem)"
                    : "Pausar o chat\nAs mensagens continuam chegando; ao continuar, ele rola sozinho até a última.",
            atalho);
    }

    /// <summary>O balão da novidade da pausa, apontando para o botão, com o atalho (sem atalho, a linha dele some).</summary>
    public void MostrarNovidadeDaPausa(Atalho? atalho) =>
        MostrarNovidade(botaoPausar, "Novo: pausar o chat",
            "Momento tenso na partida? Pause o chat: as mensagens continuam chegando e esperam você. Ao continuar, elas " +
            "aparecem uma por uma, no ritmo de leitura (a velocidade muda em Configurações > Chat).",
            [(atalho, string.Empty, "#BDBDBD")]);

    /// <summary>
    /// O balão da novidade do AutoMod, apontando para a engrenagem (a conta fica na aba Twitch). Curto: como aprovar o
    /// próprio aviso do chat mostra quando acontecer (escolha do usuário). O fim diz o que falta para usar.
    /// </summary>
    public void MostrarNovidadeDoAutoMod(FaltaNaConta falta) =>
        MostrarNovidade(botaoConfiguracoes, "Novo: aprovar mensagens do AutoMod",
            "Se você é dono ou moderador do canal, as mensagens que o AutoMod segurar aparecem no chat para você aprovar ou recusar." +
            falta switch
            {
                FaltaNaConta.Conectar => " Precisa conectar a sua conta da Twitch em Configurações > Twitch.",
                FaltaNaConta.ConectarDeNovo => " Para usar, clique em \"Conectar de novo\" em Configurações > Twitch.",
                _ => string.Empty,
            },
            []);

    /// <summary>O balão da novidade do overlay da live, apontando para a engrenagem (ele é ligado na aba Overlay).</summary>
    public void MostrarNovidadeDoOverlay() =>
        MostrarNovidade(botaoConfiguracoes, "Novo: o chat na sua live",
            "Agora dá para colocar o Olho no Chat como overlay no OBS, com opções próprias para ele ficar do melhor jeito " +
            "na tela da live. Para ligar, vá em Configurações > Overlay.",
            []);

    /// <summary>
    /// O overlay da live não abriu (o endereço dele está ocupado): o balão na engrenagem, com "Resolver" (abre a aba
    /// Overlay) e "Agora não". Outro Olho no Chat na porta: o texto diz para fechar o outro.
    /// </summary>
    public void MostrarAvisoDoOverlay(bool outroOlhoNoChat) =>
        MostrarNovidade(botaoConfiguracoes, "O overlay da live não abriu",
            outroOlhoNoChat
                ? "Outro Olho no Chat aberto já está usando o endereço do overlay. Feche o outro, ou veja como usar outro endereço."
                : "Outro programa está usando o endereço do overlay. O app acha outro endereço para você em um clique.",
            [], principal: "Resolver", secundario: "Agora não");

    /// <summary>
    /// O balão da novidade dos anúncios, apontando para a engrenagem (a opção fica na aba Twitch). Curto, como o do
    /// AutoMod; o fim diz o que falta para usar.
    /// </summary>
    public void MostrarNovidadeDosAnuncios(FaltaNaConta falta) =>
        MostrarNovidade(botaoConfiguracoes, "Novo: aviso de anúncios",
            "Se o seu canal tem anúncios, o chat avisa 5 minutos antes de cada um, com um botão para adiar, e mostra quando ele está rodando." +
            falta switch
            {
                FaltaNaConta.Conectar => " Precisa conectar a sua conta da Twitch em Configurações > Twitch.",
                FaltaNaConta.ConectarDeNovo => " Para usar, clique em \"Conectar de novo\" em Configurações > Twitch.",
                _ => string.Empty,
            },
            []);

    /// <summary>O balão da novidade de moderar no chat, apontando para a engrenagem (a opção fica na aba Twitch).</summary>
    public void MostrarNovidadeDaModeracao(FaltaNaConta falta) =>
        MostrarNovidade(botaoConfiguracoes, "Novo: moderar no chat",
            "Se você é dono ou moderador do canal, passe o mouse numa mensagem (com as bordas visíveis) para apagar, suspender ou banir." +
            falta switch
            {
                FaltaNaConta.Conectar => " Precisa conectar a sua conta da Twitch em Configurações > Twitch.",
                FaltaNaConta.ConectarDeNovo => " Para usar, clique em \"Conectar de novo\" em Configurações > Twitch.",
                _ => string.Empty,
            },
            []);

    /// <summary>O que falta na conta para o AutoMod, os anúncios ou moderar funcionarem, para o fim do balão.</summary>
    public enum FaltaNaConta
    {
        Nada,
        Conectar,
        /// <summary>A conta foi conectada antes da permissão nova (2.7.0 no AutoMod, 2.11.0 nos anúncios).</summary>
        ConectarDeNovo,
    }

    // Os atalhos que não existem não aparecem (sem nenhum, a linha some). "No jogo:" só na primeira linha; as teclas ficam
    // uma embaixo da outra
    private void MostrarNovidade(FrameworkElement botao, string titulo, string texto,
        (Atalho? Atalho, string OQueFaz, string Cor)[] atalhos, string principal = "Entendi", string? secundario = null)
    {
        tituloDaNovidade.Text = titulo;
        textoDaNovidade.Text = texto;
        botaoEntendiNovidade.Content = principal;
        botaoAgoraNaoNovidade.Content = secundario ?? string.Empty;
        botaoAgoraNaoNovidade.Visibility = secundario == null ? Visibility.Collapsed : Visibility.Visible;

        Grid linhas = linhaAtalhoDaNovidade;
        linhas.Children.Clear();
        linhas.RowDefinitions.Clear();
        var existem = atalhos.Where(a => Atalho.Existe(a.Atalho)).ToList();
        if (existem.Count > 0)
            linhas.Children.Add(TextoDaLinha("No jogo:", "#BDBDBD", new Thickness(0)));
        for (int i = 0; i < existem.Count; i++)
        {
            var (atalho, oQueFaz, cor) = existem[i];
            linhas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tecla = new Border
            {
                Margin = new Thickness(6, i > 0 ? 4 : 0, 0, 0),
                Padding = new Thickness(7, 1, 7, 2),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = TextoDaLinha(atalho!.ToString(), "#FFFFFF", new Thickness(0)),
            };
            Grid.SetRow(tecla, i);
            Grid.SetColumn(tecla, 1);
            linhas.Children.Add(tecla);
            if (oQueFaz.Length > 0)
            {
                TextBlock faz = TextoDaLinha(oQueFaz, cor, new Thickness(6, i > 0 ? 4 : 0, 0, 0));
                Grid.SetRow(faz, i);
                Grid.SetColumn(faz, 2);
                linhas.Children.Add(faz);
            }
        }
        linhas.Visibility = existem.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Na pausa (esquerda da barra) o balão sai do botão para a direita; na engrenagem (direita), para a esquerda
        bool naDireita = botao == botaoConfiguracoes;
        balaoNovidade.PlacementTarget = botao;
        balaoNovidade.HorizontalOffset = naDireita ? botao.ActualWidth - 290 : 0;
        pontaDaNovidade.HorizontalAlignment = naDireita ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        balaoNovidade.IsOpen = true;
    }

    private static TextBlock TextoDaLinha(string texto, string cor, Thickness margem) => new()
    {
        Text = texto,
        FontSize = 12,
        Foreground = (Brush)new BrushConverter().ConvertFromString(cor)!,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margem,
    };

    public void FecharNovidade() => balaoNovidade.IsOpen = false;

    public void MostrarAtalhoDasBordas(Atalho? atalho) =>
        botaoOcultarBordas.ToolTip = Atalho.NaDica(
            "Ocultar bordas\nPara mostrar de novo: o atalho ou o ícone do Olho no Chat perto do relógio.", atalho);

    public void FecharPaineis()
    {
        painelTamanhoDoTexto.IsOpen = false;
        painelFundo.IsOpen = false;
    }

    // --- Cliques ---------------------------------------------------------------------------------------------

    private void Barra_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Só no laranja livre: os botões tratam o próprio clique
        if (e.OriginalSource == barra && e.ButtonState == MouseButtonState.Pressed)
            Window.GetWindow(this)?.DragMove();
    }

    private void OcultarBordas_Click(object sender, RoutedEventArgs e) => OcultarBordasPedido?.Invoke();

    private void Configuracoes_Click(object sender, RoutedEventArgs e) => ConfiguracoesPedido?.Invoke();

    private void Fechar_Click(object sender, RoutedEventArgs e) => FecharPedido?.Invoke();

    private void SempreNoTopo_Mudou(object sender, RoutedEventArgs e)
    {
        if (!_mostrando)
            SempreNoTopoPedido?.Invoke(botaoSempreNoTopo.IsChecked == true);
    }

    private void Multiplataforma_Mudou(object sender, RoutedEventArgs e)
    {
        if (!_mostrando)
            MultiplataformaPedido?.Invoke(botaoMultiplataforma.IsChecked == true);
    }

    private void Escrever_Mudou(object sender, RoutedEventArgs e)
    {
        if (!_mostrando)
            EscreverPedido?.Invoke();
    }

    private void Pausar_Mudou(object sender, RoutedEventArgs e)
    {
        if (!_mostrando)
            PausarPedido?.Invoke();
    }

    private void EntendiNovidade_Click(object sender, RoutedEventArgs e) => NovidadeEntendida?.Invoke();

    private void AgoraNaoNovidade_Click(object sender, RoutedEventArgs e) => NovidadeRecusada?.Invoke();

    // --- Painéis ---------------------------------------------------------------------------------------------

    private Popup PainelDe(object botao) => botao == botaoTamanhoDoTexto ? painelTamanhoDoTexto : painelFundo;

    private PainelDeAjuste AjusteDe(object botao) => botao == botaoTamanhoDoTexto ? ajusteTamanhoDoTexto : ajusteFundo;

    // Com o painel aberto, o botão fecha o painel
    private void BotaoDePainel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Popup painel = PainelDe(sender);
        if (painel.IsOpen)
        {
            e.Handled = true;
            painel.IsOpen = false;
        }
    }

    private void BotaoDePainel_Click(object sender, RoutedEventArgs e)
    {
        Popup painel = PainelDe(sender);
        if (painel == _painelFechado && DateTime.UtcNow - _fechouEm < ProtecaoDoClique)
            return;

        painel.IsOpen = true;
        AjusteDe(sender).FocarNoDeslizante();
    }

    // A rodinha em cima do botão muda o valor sem abrir o painel, e a dica mostra o valor novo
    private void BotaoDePainel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        AjusteDe(sender).Girar(e.Delta);
        if (((FrameworkElement)sender).ToolTip is ToolTip dica)
            dica.IsOpen = true;
    }

    private void BotaoDePainel_MouseLeave(object sender, MouseEventArgs e)
    {
        dicaTamanhoDoTexto.IsOpen = false;
        dicaFundo.IsOpen = false;
    }
}
