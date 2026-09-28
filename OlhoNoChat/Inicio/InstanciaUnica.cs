#nullable enable
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;

namespace OlhoNoChat.Inicio;

/// <summary>
/// Só uma cópia "principal" do app por vez. A primeira cópia abre um canal local com nome fixo; uma cópia aberta
/// depois manda por ele os argumentos dela (ou o pedido de mostrar a janela) e, normalmente, fecha.
/// </summary>
/// <remarks>
/// O nome do canal e o formato das mensagens são os mesmos desde a 1.0: argumentos separados por "|||", ou
/// "::SHOW_WINDOW_COMMAND::" sem argumentos. Assim uma cópia nova sempre conversa com uma já aberta.
/// </remarks>
public sealed class InstanciaUnica : IDisposable
{
#if DEBUG
    private const string NomeDoCanal = "OlhoNoChat_Pipe_7A6C5D4B_Dev"; // a versão de desenvolvimento roda ao lado da instalada
#else
    private const string NomeDoCanal = "OlhoNoChat_Pipe_7A6C5D4B";
#endif
    public const string PedidoMostrarJanela = "::SHOW_WINDOW_COMMAND::";
    public const string Separador = "|||";
    private static readonly TimeSpan EsperaParaConectar = TimeSpan.FromMilliseconds(200);

    private readonly NamedPipeServerStream _canal;
    private readonly Dispatcher _telas;
    private readonly CancellationTokenSource _parar = new();

    /// <summary>Uma cópia aberta depois pediu algo. Chega na thread da tela.</summary>
    public event Action<IReadOnlyList<ComandoDoApp>>? PedidoRecebido;

    private InstanciaUnica(NamedPipeServerStream canal, Dispatcher telas)
    {
        _canal = canal;
        _telas = telas;
        _ = OuvirAsync();
    }

    /// <summary>
    /// Abre o canal se esta for a primeira cópia. Devolve null se outra cópia já estiver aberta.
    /// </summary>
    public static InstanciaUnica? TentarSerAPrimeira(Dispatcher telas)
    {
        try
        {
            var canal = new NamedPipeServerStream(NomeDoCanal, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            return new InstanciaUnica(canal, telas);
        }
        catch (IOException)
        {
            return null; // o nome já está em uso: outra cópia é a primeira
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Manda os argumentos para a cópia que já está aberta. Se ela não responder a tempo, desiste em silêncio.
    /// </summary>
    public static async Task EnviarParaAPrimeiraAsync(IReadOnlyList<string> argumentos)
    {
        try
        {
            using var canal = new NamedPipeClientStream(".", NomeDoCanal, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            await canal.ConnectAsync((int)EsperaParaConectar.TotalMilliseconds);
            byte[] mensagem = Encoding.UTF8.GetBytes(MontarMensagem(argumentos));
            await canal.WriteAsync(mensagem);
            await canal.FlushAsync();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string MontarMensagem(IReadOnlyList<string> argumentos) =>
        argumentos.Count == 0 ? PedidoMostrarJanela : string.Join(Separador, argumentos);

    public static IReadOnlyList<ComandoDoApp> LerMensagem(string mensagem) =>
        mensagem == PedidoMostrarJanela
            ? new[] { ComandoDoApp.MostrarJanela }
            : ArgumentosDoApp.Ler(mensagem.Split(Separador));

    private async Task OuvirAsync()
    {
        while (!_parar.IsCancellationRequested)
        {
            string mensagem;
            try
            {
                await _canal.WaitForConnectionAsync(_parar.Token);
                using (var leitor = new StreamReader(_canal, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
                    mensagem = await leitor.ReadToEndAsync(_parar.Token);
                _canal.Disconnect();
            }
            catch (Exception) when (_parar.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A outra cópia desistiu no meio: espera a próxima (com uma pausa, para um canal quebrado não
                // virar um laço sem fim)
                if (_canal.IsConnected)
                    _canal.Disconnect();
                await Task.Delay(100);
                continue;
            }

            if (mensagem.Length == 0)
                continue;
            IReadOnlyList<ComandoDoApp> comandos = LerMensagem(mensagem);
            _ = _telas.BeginInvoke(() => PedidoRecebido?.Invoke(comandos));
        }
    }

    public void Dispose()
    {
        _parar.Cancel();
        _canal.Dispose();
    }
}
