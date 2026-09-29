#nullable enable
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// "Sempre no topo" da janela do chat. Ligado, o chat fica na frente também dos jogos que se põem "sempre na frente" ao
/// ganhar o foco (o Hunt, por exemplo: vence a última janela ativada): sempre que outro programa ganha o foco, o chat
/// volta para a frente numa rajada (os jogos às vezes se levantam um pouco depois), e a cada segundo, enquanto a janela
/// em foco de outro programa for "sempre na frente". Trazer para a frente nunca tira o foco do jogo. Desligado, a janela
/// é comum e nada é vigiado.
/// </summary>
internal sealed class SempreNaFrente : IDisposable
{
    /// <summary>Quando a rajada traz o chat de volta, em ms depois de começar.</summary>
    public static readonly IReadOnlyList<int> AtrasosDaRajada = [0, 150, 500, 1200, 2500];

    private readonly Window _janela;
    private readonly Func<bool> _podeTrazer;
    private readonly DispatcherTimer _aCadaSegundo = new() { Interval = TimeSpan.FromSeconds(1) };
    private VigiaDeFoco? _vigia;
    private bool _ligado;

    /// <param name="podeTrazer">Não traz com as Configurações abertas (cobriria a janela) nem com o app fechando.</param>
    public SempreNaFrente(Window janela, Func<bool> podeTrazer)
    {
        _janela = janela;
        _podeTrazer = podeTrazer;
        _aCadaSegundo.Tick += (_, _) =>
        {
            if (JanelaDoWindows.FocoEmOutroProgramaSempreNaFrente())
                Trazer();
        };
    }

    public bool Ligado
    {
        get => _ligado;
        set
        {
            _ligado = value;
            _janela.Topmost = value;
            if (value)
            {
                _vigia ??= CriarVigia();
                _aCadaSegundo.Start();
                Trazer();
            }
            else
            {
                // Uma janela comum: nada para vigiar
                _aCadaSegundo.Stop();
                _vigia?.Dispose();
                _vigia = null;
            }
        }
    }

    private VigiaDeFoco CriarVigia()
    {
        var vigia = new VigiaDeFoco();
        vigia.OutroProgramaGanhouOFoco += Rajada;
        return vigia;
    }

    /// <summary>Traz o chat para a frente agora e mais algumas vezes logo depois (outra janela pode voltar por cima).</summary>
    public async void Rajada()
    {
        if (!_ligado)
            return;

        int passou = 0;
        foreach (int atraso in AtrasosDaRajada)
        {
            await Task.Delay(atraso - passou);
            passou = atraso;
            Trazer();
        }
    }

    /// <summary>Põe o chat na frente sem ativá-lo (o foco continua no jogo).</summary>
    public void Trazer()
    {
        if (!_ligado || !_podeTrazer() || !_janela.IsVisible)
            return;

        IntPtr janela = new WindowInteropHelper(_janela).Handle;
        if (janela != IntPtr.Zero)
            JanelaDoWindows.ColocarNaFrente(janela);
    }

    /// <summary>
    /// Dá o foco à janela do chat (minimizada, ela volta). Só ativar não passa na frente de um jogo "sempre na frente":
    /// a janela deixa de ser "sempre no topo" por um instante.
    /// </summary>
    public void Ativar()
    {
        if (_janela.WindowState == WindowState.Minimized)
            _janela.WindowState = WindowState.Normal;
        _janela.Topmost = false;
        _janela.Activate();
        _janela.Topmost = _ligado;
    }

    public void Dispose()
    {
        _aCadaSegundo.Stop();
        _vigia?.Dispose();
        _vigia = null;
    }
}
