using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A barra laranja da janela do chat: ocultar bordas, tamanho do texto e fundo (com os painéis e a rodinha), sempre no
/// topo, escrever, Configurações e fechar. Arrastar a barra (fora dos botões) move a janela; clique duplo não maximiza.
/// Quem decide e aplica é a janela: a barra só avisa o que foi pedido e mostra o estado que a janela passa.
/// </summary>
public partial class BarraDeCima : UserControl
{
    // Depois de fechar o painel com um clique fora dele, esse mesmo clique (no botão do painel) não o abre de novo
    private static readonly TimeSpan ProtecaoDoClique = TimeSpan.FromMilliseconds(300);

    private bool _mostrando;
    private Popup? _painelFechado;
    private DateTime _fechouEm = DateTime.MinValue;

    public BarraDeCima()
    {
        InitializeComponent();

        ajusteTamanhoDoTexto.ValorEscolhido += valor => TamanhoDoTextoEscolhido?.Invoke(valor);
        ajusteTamanhoDoTexto.VoltarAoPadraoPedido += () => TamanhoDoTextoEscolhido?.Invoke(Opcoes.TamanhoDoTextoPadrao);
        ajusteFundo.ValorEscolhido += valor => FundoEscolhido?.Invoke(valor);
        ajusteFundo.VoltarAoPadraoPedido += () => FundoPadraoPedido?.Invoke();
        ajusteTamanhoDoTexto.FecharPedido += FecharPaineis;
        ajusteFundo.FecharPedido += FecharPaineis;
        ajusteTamanhoDoTexto.DicaDoPadrao = $"Voltar ao padrão ({Math.Round(Opcoes.TamanhoDoTextoPadrao * 100)}%)";
        ajusteFundo.DicaDoPadrao = $"Voltar ao padrão ({Math.Round(Opcoes.FundoPadrao / 2.55)}%)";

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

    /// <summary>O botão Escrever foi clicado: a janela abre ou fecha a caixa e depois mostra o estado.</summary>
    public event Action? EscreverPedido;

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
        dicaTamanhoDoTexto.Content = $"Tamanho do texto: {texto}\nClique para ajustar, ou gire a rodinha do mouse aqui.";
        dicaFundo.Content = $"Fundo do chat: {escuro}\nClique para ajustar, ou gire a rodinha do mouse aqui.";
    }

    public void MostrarSempreNoTopo(bool ligado, Atalho? atalho)
    {
        _mostrando = true;
        botaoSempreNoTopo.IsChecked = ligado;
        _mostrando = false;
        botaoSempreNoTopo.ToolTip = Atalho.NaDica(ligado
            ? "Sempre no topo: ligado. O chat fica na frente do jogo e das outras janelas. Clique para desligar."
            : "Sempre no topo: desligado. O chat é uma janela comum, que fica atrás de outra quando você clica nela. Clique para ligar.",
            atalho);
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

    public void MostrarAtalhoDasBordas(Atalho? atalho) =>
        botaoOcultarBordas.ToolTip = Atalho.NaDica(
            "Ocultar bordas: deixa só o chat por cima do jogo. Para mostrar as bordas de novo, use o atalho ou o ícone do Olho no Chat perto do relógio.",
            atalho);

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

    private void Escrever_Mudou(object sender, RoutedEventArgs e)
    {
        if (!_mostrando)
            EscreverPedido?.Invoke();
    }

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
