using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Um painel de ajuste da barra laranja (tamanho do texto ou fundo). O valor muda na hora pelo controle deslizante, pelas
/// setas do teclado, pela rodinha e pelo "voltar ao padrão"; quem usa o painel aplica e mostra o valor que ficou.
/// </summary>
public partial class PainelDeAjuste : UserControl
{
    private bool _mostrando;
    private string _nome = string.Empty;

    public PainelDeAjuste()
    {
        InitializeComponent();
    }

    /// <summary>A pessoa escolheu um valor (deslizante, teclado ou rodinha).</summary>
    public event Action<double>? ValorEscolhido;

    public event Action? VoltarAoPadraoPedido;

    /// <summary>Esc ou Enter dentro do painel.</summary>
    public event Action? FecharPedido;

    /// <summary>O final dos nomes dos controles para a automação ("deslizante" + Nome...).</summary>
    public string Nome
    {
        get => _nome;
        set
        {
            _nome = value;
            AutomationProperties.SetAutomationId(deslizante, "deslizante" + value);
            AutomationProperties.SetAutomationId(valor, "valor" + value);
            AutomationProperties.SetAutomationId(botaoPadrao, "botaoPadrao" + value);
        }
    }

    public string Titulo
    {
        get => titulo.Text;
        set
        {
            titulo.Text = value;
            AutomationProperties.SetName(deslizante, value);
        }
    }

    public object? Icone { get => icone.Content; set => icone.Content = value; }

    public double Minimo { get => deslizante.Minimum; set => deslizante.Minimum = value; }

    public double Maximo { get => deslizante.Maximum; set => deslizante.Maximum = value; }

    /// <summary>Os passos do deslizante (arrastar e clicar no trilho).</summary>
    public double Passo { get => deslizante.TickFrequency; set => deslizante.TickFrequency = value; }

    /// <summary>Os passos das setas do teclado.</summary>
    public double PassoDoTeclado
    {
        get => deslizante.SmallChange;
        set
        {
            deslizante.SmallChange = value;
            deslizante.LargeChange = value;
        }
    }

    public double PassoDaRodinha { get => deslizante.PassoDaRodinha; set => deslizante.PassoDaRodinha = value; }

    public string LegendaMenor { get => legendaMenor.Text; set => legendaMenor.Text = value; }

    public string LegendaMaior { get => legendaMaior.Text; set => legendaMaior.Text = value; }

    public string DicaDoPadrao { get => botaoPadrao.ToolTip as string ?? string.Empty; set => botaoPadrao.ToolTip = value; }

    /// <summary>O valor em uso, sem avisar de volta (veio de fora ou foi ajustado aos limites).</summary>
    public void Mostrar(double numero, string texto)
    {
        _mostrando = true;
        deslizante.Value = numero;
        _mostrando = false;
        valor.Text = texto;
    }

    /// <summary>Um giro da rodinha em cima do botão do painel (sem abrir o painel).</summary>
    public void Girar(int delta) => deslizante.Girar(delta);

    /// <summary>Ao abrir: as setas do teclado já mudam o valor.</summary>
    public void FocarNoDeslizante() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(deslizante));

    private void Deslizante_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_mostrando)
            ValorEscolhido?.Invoke(e.NewValue);
    }

    private void BotaoPadrao_Click(object sender, RoutedEventArgs e) => VoltarAoPadraoPedido?.Invoke();

    private void Painel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            FecharPedido?.Invoke();
        }
    }
}
