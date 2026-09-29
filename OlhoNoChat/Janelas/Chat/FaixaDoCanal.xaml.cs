#nullable enable
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A faixa do canal na tela (a lógica fica em <see cref="LogicaFaixaDoCanal"/>): o botão da faixa fechada, a caixa do
/// nome, e o ponto da conexão, que pisca em amarelo enquanto conecta.
/// </summary>
public partial class FaixaDoCanal : UserControl
{
    private static readonly SolidColorBrush Amarelo = Pincel(0xF5, 0xC5, 0x18);
    private static readonly SolidColorBrush Verde = Pincel(0x3B, 0xD1, 0x6F);
    private static readonly SolidColorBrush Vermelho = Pincel(0xF0, 0x4A, 0x4A);

    private LogicaFaixaDoCanal? _logica;
    private bool _piscando;

    public FaixaDoCanal()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => MostrarPonto();
    }

    public LogicaFaixaDoCanal? Logica
    {
        get => _logica;
        set
        {
            if (_logica != null)
            {
                _logica.PropertyChanged -= LogicaMudou;
                _logica.PedirFoco -= Focar;
            }
            _logica = value;
            DataContext = value;
            if (value != null)
            {
                value.PropertyChanged += LogicaMudou;
                value.PedirFoco += Focar;
            }
            MostrarPonto();
        }
    }

    private static SolidColorBrush Pincel(byte r, byte g, byte b)
    {
        var pincel = new SolidColorBrush(Color.FromRgb(r, g, b));
        pincel.Freeze();
        return pincel;
    }

    private void LogicaMudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogicaFaixaDoCanal.Conexao) or nameof(LogicaFaixaDoCanal.EditorAberto))
            MostrarPonto();
    }

    private void MostrarPonto()
    {
        EstadoDaConexao estado = _logica?.Conexao ?? EstadoDaConexao.Nenhum;
        Brush? cor = estado switch
        {
            EstadoDaConexao.Conectando => Amarelo,
            EstadoDaConexao.Conectado => Verde,
            EstadoDaConexao.SemConexao => Vermelho,
            _ => null,
        };
        pontoDeConexao.Fill = cor;
        pontoDeConexao.Visibility = cor != null ? Visibility.Visible : Visibility.Collapsed;

        // Piscando só enquanto o ponto está na tela: uma animação redesenha a janela a cada quadro (também com a faixa
        // escondida por cima do jogo). 15 quadros por segundo bastam para o piscar lento.
        bool piscar = estado == EstadoDaConexao.Conectando && IsVisible && _logica?.EditorAberto == false;
        if (piscar == _piscando)
            return;

        _piscando = piscar;
        if (piscar)
        {
            var animacao = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(animacao, 15);
            pontoDeConexao.BeginAnimation(OpacityProperty, animacao);
        }
        else
        {
            pontoDeConexao.BeginAnimation(OpacityProperty, null);
        }
    }

    // Ao abrir, com o texto todo selecionado; depois de um erro, só o foco
    private void Focar(bool selecionarTudo)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            caixaCanal.Focus();
            Keyboard.Focus(caixaCanal);
            if (selecionarTudo)
                caixaCanal.SelectAll();
        });
    }

    private void TrocarCanal_Click(object sender, RoutedEventArgs e) => _logica?.AbrirEditor();

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _logica?.FecharEditor();

    private void Entrar_Click(object sender, RoutedEventArgs e) => _ = _logica?.ConfirmarAsync();

    private void SairDoCanal_Click(object sender, RoutedEventArgs e) => _logica?.SairDoCanal();

    private void CaixaCanal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = _logica?.ConfirmarAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _logica?.Esc();
        }
    }
}
