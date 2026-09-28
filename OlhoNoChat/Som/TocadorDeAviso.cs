#nullable enable
using System.Diagnostics;
using NAudio.Wave;

namespace OlhoNoChat.Som;

/// <summary>
/// O aviso de mensagem nova do chat Padrão. Regras, nesta ordem: "Quando tocar"; nada se a saída de som já falhou
/// de vez; prepara a saída na primeira vez; nunca toca por cima de si mesmo (o pedido é ignorado); toca do começo.
/// </summary>
public sealed class TocadorDeAviso : IDisposable
{
    /// <summary>Uma saída de som do Windows; <see cref="Padrao"/> é a padrão do Windows.</summary>
    public sealed record Saida(int Id, string Nome)
    {
        // A lista da aba Som mostra o nome (também para a automação e os leitores de tela)
        public override string ToString() => Nome;
    }

    public const int Padrao = -1;
    public const string NomeDaPadraoGravado = "Default";

    private readonly RegraQuandoTocar _quandoTocar = new();
    private readonly Func<DateTime> _agora;

    private string? _arquivo;
    private float _volume = 1f;
    private int _idDaSaida = Padrao;
    private string _nomeDaSaida = NomeDaPadraoGravado;

    private AudioFileReader? _som;
    private WaveOutEvent? _saida;
    private bool _falhou;

    public TocadorDeAviso() : this(() => DateTime.UtcNow) { }

    public TocadorDeAviso(Func<DateTime> agora)
    {
        _agora = agora;
    }

    /// <summary>
    /// A saída escolhida não existe mais ou o Windows a recusou: o aviso passou para a padrão do Windows, e as
    /// configurações devem gravar isso.
    /// </summary>
    public event Action? SaidaVoltouParaAPadrao;

    /// <summary>"Padrão do Windows" e as saídas de som, pelo nome do aparelho.</summary>
    public static IReadOnlyList<Saida> ListarSaidas()
    {
        var saidas = new List<Saida> { new(Padrao, "Padrão do Windows") };
        for (int id = 0; id < WaveOut.DeviceCount; id++)
            saidas.Add(new Saida(id, WaveOut.GetCapabilities(id).ProductName));
        return saidas;
    }

    /// <summary>
    /// A saída gravada ainda é o mesmo aparelho (o número de cada aparelho muda quando outro é ligado ou tirado).
    /// </summary>
    public static bool SaidaAindaExiste(int id, string nomeGravado)
    {
        if (id == Padrao)
            return true;
        if (id < 0 || id >= WaveOut.DeviceCount)
            return false;
        string nome = WaveOut.GetCapabilities(id).ProductName;
        return nome.Length > 0 && (nomeGravado ?? string.Empty).StartsWith(nome, StringComparison.Ordinal);
    }

    /// <summary>
    /// Troca o som, o volume, a saída e o "Quando tocar" (ao abrir o app e a cada "Salvar"). A saída antiga é
    /// solta; a contagem do "Quando tocar" continua. Devolve o erro se o arquivo não pôde ser carregado.
    /// </summary>
    /// <param name="arquivo">Caminho do som; null = mudo.</param>
    public Exception? Configurar(string? arquivo, float volume, int idDaSaida, string nomeDaSaida, int segundosEntreSons)
    {
        Soltar();
        _falhou = false;
        _arquivo = arquivo;
        _volume = Math.Clamp(volume, 0f, 1f);
        _idDaSaida = idDaSaida;
        _nomeDaSaida = nomeDaSaida;
        _quandoTocar.Segundos = segundosEntreSons;

        if (arquivo == null)
            return null;
        try
        {
            _som = new AudioFileReader(arquivo) { Volume = _volume };
            return null;
        }
        catch (Exception ex)
        {
            _arquivo = null;
            return ex;
        }
    }

    /// <summary>A página do chat pediu o aviso de uma mensagem nova.</summary>
    public void Tocar()
    {
        DateTime agora = _agora();
        if (!_quandoTocar.Permite(agora) || _falhou || _som == null)
            return;
        if (_saida == null && !Preparar())
            return;
        if (_saida!.PlaybackState == PlaybackState.Playing)
            return;

        try
        {
            _som.Position = 0;
            _saida.Play();
            _quandoTocar.Tocou(agora);
        }
        catch (Exception ex)
        {
            // O aparelho sumiu no meio: prepara de novo no próximo aviso
            Debug.WriteLine($"O aviso não tocou: {ex.Message}");
            SoltarSaida();
        }
    }

    // A saída é preparada no primeiro aviso, não ao configurar
    private bool Preparar()
    {
        if (WaveOut.DeviceCount == 0)
            return false; // nenhuma saída de som no Windows agora

        if (!SaidaAindaExiste(_idDaSaida, _nomeDaSaida))
            VoltarParaAPadrao();

        if (TentarAbrir(_idDaSaida))
            return true;
        if (_idDaSaida != Padrao)
        {
            // O Windows recusou o aparelho escolhido: vai para o padrão na hora
            VoltarParaAPadrao();
            if (TentarAbrir(Padrao))
                return true;
        }
        _falhou = true; // nem a saída padrão abre: só tenta de novo ao configurar
        return false;
    }

    private bool TentarAbrir(int id)
    {
        var saida = new WaveOutEvent { DeviceNumber = id };
        try
        {
            saida.Init(_som);
            _saida = saida;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"A saída de som {id} não abriu: {ex.Message}");
            saida.Dispose();
            return false;
        }
    }

    private void VoltarParaAPadrao()
    {
        _idDaSaida = Padrao;
        _nomeDaSaida = NomeDaPadraoGravado;
        SaidaVoltouParaAPadrao?.Invoke();
    }

    /// <summary>
    /// A prévia da aba Som: o som, o volume e a saída que estão na tela, ainda sem salvar. Cada prévia é independente
    /// (podem tocar juntas) e erros são ignorados.
    /// </summary>
    public static void Previa(string? arquivo, float volume, int idDaSaida)
    {
        if (arquivo == null)
            return;
        AudioFileReader? som = null;
        WaveOutEvent? saida = null;
        try
        {
            som = new AudioFileReader(arquivo) { Volume = Math.Clamp(volume, 0f, 1f) };
            saida = new WaveOutEvent { DeviceNumber = idDaSaida >= 0 && idDaSaida < WaveOut.DeviceCount ? idDaSaida : Padrao };
            saida.Init(som);
            var (somDaPrevia, saidaDaPrevia) = (som, saida);
            saida.PlaybackStopped += (s, e) =>
            {
                saidaDaPrevia.Dispose();
                somDaPrevia.Dispose();
            };
            saida.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"A prévia não tocou: {ex.Message}");
            saida?.Dispose();
            som?.Dispose();
        }
    }

    private void SoltarSaida()
    {
        _saida?.Dispose();
        _saida = null;
    }

    private void Soltar()
    {
        SoltarSaida();
        _som?.Dispose();
        _som = null;
    }

    public void Dispose() => Soltar();
}
