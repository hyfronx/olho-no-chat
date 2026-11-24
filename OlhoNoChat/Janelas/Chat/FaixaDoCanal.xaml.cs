using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OlhoNoChat.Controles;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;
using OlhoNoChat.YouTube;

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
    private static readonly SolidColorBrush Cinza = Pincel(0x8A, 0x8A, 0x8A);

    private LogicaFaixaDoCanal? _logica;
    private bool _piscando;
    // A troca entre os viewers e os nomes dos canais, só com a faixa fechada na tela e algum número
    private readonly DispatcherTimer _troca = new();

    private const double LarguraComTrocarCanalEscrito = 280;

    // Abaixo disso, o "Aplicar" ao lado da caixa do nome deixaria pouco lugar para digitar
    private const double LarguraDoEditorNumaLinha = 320;

    // A faixa aberta numa janela estreita: as caixas ocupam a largura toda e o "Aplicar" (esticado) e a seta de fechar
    // descem para uma linha própria, embaixo das caixas e em cima da dica. Larga: os dois ao lado da primeira caixa.
    private void ArrumarEditor()
    {
        bool estreita = ActualWidth < LarguraDoEditorNumaLinha;
        foreach (TextBox caixa in new[] { caixaCanal, caixaYouTube, caixaKick, caixaTikTok })
            Grid.SetColumnSpan(caixa, estreita ? 3 : 1);
        Grid.SetRow(botaoEntrar, estreita ? 4 : 0);
        Grid.SetColumn(botaoEntrar, estreita ? 1 : 2);
        botaoEntrar.Margin = estreita ? new Thickness(0, 6, 0, 0) : new Thickness(6, 0, 0, 0);
        Grid.SetRow(botaoCancelarCanal, estreita ? 4 : 0);
        botaoCancelarCanal.Margin = estreita ? new Thickness(4, 6, 0, 0) : new Thickness(4, 0, 0, 0);
    }

    public FaixaDoCanal()
    {
        InitializeComponent();
        // Numa janela estreita o "Trocar canal" escrito tomaria o lugar do nome do canal: fica só o ícone
        SizeChanged += (_, _) =>
        {
            textoTrocarCanal.Visibility = ActualWidth >= LarguraComTrocarCanalEscrito ? Visibility.Visible : Visibility.Collapsed;
            ArrumarEditor();
        };
        _troca.Tick += (_, _) =>
        {
            if (_logica != null)
                _troca.Interval = _logica.Alternar();
        };
        IsVisibleChanged += (_, _) =>
        {
            MostrarPonto();
            AtualizarTroca();
        };
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
                _logica.PedirFocoNoYouTube -= FocarNoYouTube;
                _logica.PedirFocoNaKick -= FocarNaKick;
                _logica.PedirFocoNoTikTok -= FocarNoTikTok;
            }
            _logica = value;
            DataContext = value;
            if (value != null)
            {
                value.PropertyChanged += LogicaMudou;
                value.PedirFoco += Focar;
                value.PedirFocoNoYouTube += FocarNoYouTube;
                value.PedirFocoNaKick += FocarNaKick;
                value.PedirFocoNoTikTok += FocarNoTikTok;
            }
            MostrarPonto();
            AtualizarTroca();
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
        if (e.PropertyName is nameof(LogicaFaixaDoCanal.Conexao) or nameof(LogicaFaixaDoCanal.EditorAberto)
            or nameof(LogicaFaixaDoCanal.ConexaoDoYouTube) or nameof(LogicaFaixaDoCanal.ConexaoDaKick)
            or nameof(LogicaFaixaDoCanal.ConexaoDoTikTok))
            MostrarPonto();
        if (e.PropertyName is nameof(LogicaFaixaDoCanal.TemViewersNaFaixa) or nameof(LogicaFaixaDoCanal.EditorAberto))
            AtualizarTroca();
    }

    // Começa pelos viewers; sem número, sem a faixa na tela ou com ela aberta, para e volta aos nomes
    private void AtualizarTroca()
    {
        bool trocar = _logica is { TemViewersNaFaixa: true, EditorAberto: false } && IsVisible;
        if (trocar == _troca.IsEnabled)
            return;
        if (trocar)
        {
            _troca.Interval = LogicaFaixaDoCanal.TempoDosViewers;
            _troca.Start();
        }
        else
        {
            _troca.Stop();
            if (_logica != null)
                _logica.MostrandoNomes = false;
        }
    }

    private void MostrarPonto()
    {
        // O do YouTube não pisca: amarelo procurando, verde lendo, cinza esperando a live, vermelho com problema
        pontoDoYouTube.Fill = (_logica?.ConexaoDoYouTube ?? EstadoDoYouTube.Desligado) switch
        {
            EstadoDoYouTube.Procurando => Amarelo,
            EstadoDoYouTube.Conectado => Verde,
            EstadoDoYouTube.CanalNaoExiste or EstadoDoYouTube.SemConexao => Vermelho,
            _ => Cinza,
        };
        // O da Kick também (sem esperar a live: o chat da Kick funciona com o canal fora do ar)
        pontoDaKick.Fill = (_logica?.ConexaoDaKick ?? EstadoDaKick.Desligado) switch
        {
            EstadoDaKick.Conectando => Amarelo,
            EstadoDaKick.Conectado => Verde,
            EstadoDaKick.CanalNaoExiste or EstadoDaKick.SemConexao => Vermelho,
            _ => Cinza,
        };
        // O do TikTok, como o do YouTube
        pontoDoTikTok.Fill = (_logica?.ConexaoDoTikTok ?? EstadoDoTikTok.Desligado) switch
        {
            EstadoDoTikTok.Procurando => Amarelo,
            EstadoDoTikTok.Conectado => Verde,
            EstadoDoTikTok.CanalNaoExiste or EstadoDoTikTok.SemConexao => Vermelho,
            _ => Cinza,
        };

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

    private void FocarNoYouTube() => FocarESelecionar(caixaYouTube);

    private void FocarNaKick() => FocarESelecionar(caixaKick);

    private void FocarNoTikTok() => FocarESelecionar(caixaTikTok);

    private void FocarESelecionar(TextBox caixa)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            caixa.Focus();
            Keyboard.Focus(caixa);
            caixa.SelectAll();
        });
    }

    private void TrocarCanal_Click(object sender, RoutedEventArgs e)
    {
        Faixa_MouseLeave(sender, null); // a faixa fechada some sem avisar que o mouse saiu
        _logica?.AbrirEditor();
    }

    // Com o mouse em cima da faixa, os nomes cortados deslizam para mostrar o resto
    private void Faixa_MouseEnter(object sender, MouseEventArgs e)
    {
        foreach (TextBlock nome in Nomes)
            NomeQueDesliza.Comecar(nome);
    }

    private void Faixa_MouseLeave(object sender, MouseEventArgs? e)
    {
        foreach (TextBlock nome in Nomes)
            NomeQueDesliza.Parar(nome);
    }

    private TextBlock[] Nomes => [textoCanal, textoYouTube, textoKick, textoTikTok];

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _logica?.FecharEditor();

    private void Entrar_Click(object sender, RoutedEventArgs e) => _ = _logica?.ConfirmarAsync();

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
