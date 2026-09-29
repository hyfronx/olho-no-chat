using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// As imagens dos emotes da Twitch para a lista de emotes: baixadas algumas por vez e guardadas numa pasta, porque a
/// imagem de um emote nunca muda. Da segunda vez em diante elas vêm do disco, sem esperar a internet. Arquivos que não
/// são usados há <see cref="DiasSemUso"/> dias são apagados.
/// </summary>
public sealed partial class ImagensDeEmotes
{
    public const int DiasSemUso = 60;
    private const int BaixandoJuntos = 8;

    [GeneratedRegex(@"^[\w-]+$")]
    private static partial Regex IdValido();

    private readonly string _pasta;
    private readonly HttpClient _http;
    private readonly ILogger<ImagensDeEmotes> _log;
    private readonly SemaphoreSlim _vagas = new(BaixandoJuntos);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _emAndamento = new();
    private int _limpezaFeita;

    public ImagensDeEmotes(string pasta, ILogger<ImagensDeEmotes> log) : this(pasta, log, new SocketsHttpHandler()) { }

    /// <summary>Com outro <paramref name="transporte"/>, os testes respondem no lugar da Twitch.</summary>
    public ImagensDeEmotes(string pasta, ILogger<ImagensDeEmotes> log, HttpMessageHandler transporte)
    {
        _pasta = pasta;
        _log = log;
        _http = new HttpClient(transporte) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string Endereco(string id, bool animado, string escala) =>
        $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/{(animado ? "animated" : "static")}/dark/{escala}";

    public string Arquivo(string id, bool animado, string escala) =>
        Path.Combine(_pasta, $"{id}_{(animado ? "animado" : "parado")}_{escala}.{(animado ? "gif" : "png")}");

    /// <summary>Os bytes da imagem (do disco ou da Twitch); null se não deu para baixar.</summary>
    public Task<byte[]?> ObterAsync(string id, bool animado, string escala)
    {
        if (!IdValido().IsMatch(id) || !IdValido().IsMatch(escala.Replace('.', '_')))
            return Task.FromResult<byte[]?>(null);

        if (Interlocked.Exchange(ref _limpezaFeita, 1) == 0)
            _ = Task.Run(ApagarOsSemUso);

        string arquivo = Arquivo(id, animado, escala);
        // A mesma imagem pedida duas vezes ao mesmo tempo é baixada uma vez só
        return _emAndamento.GetOrAdd(arquivo, _ => ObterDeVerdadeAsync(id, animado, escala, arquivo));
    }

    private async Task<byte[]?> ObterDeVerdadeAsync(string id, bool animado, string escala, string arquivo)
    {
        try
        {
            byte[]? doDisco = await LerDoDiscoAsync(arquivo);
            if (doDisco != null)
                return doDisco;

            await _vagas.WaitAsync();
            try
            {
                using HttpResponseMessage resposta = await _http.GetAsync(Endereco(id, animado, escala));
                if (!resposta.IsSuccessStatusCode)
                {
                    _log.LogInformation("A imagem do emote {Id} não veio: {Codigo}", id, (int)resposta.StatusCode);
                    return null;
                }
                byte[] bytes = await resposta.Content.ReadAsByteArrayAsync();
                Gravar(arquivo, bytes);
                return bytes;
            }
            finally
            {
                _vagas.Release();
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Não deu para baixar a imagem do emote {Id}.", id);
            return null;
        }
        finally
        {
            _emAndamento.TryRemove(arquivo, out _);
        }
    }

    private async Task<byte[]?> LerDoDiscoAsync(string arquivo)
    {
        try
        {
            var info = new FileInfo(arquivo);
            if (!info.Exists || info.Length == 0)
                return null;
            byte[] bytes = await File.ReadAllBytesAsync(arquivo);
            // Usado de novo: a data marca o último uso (a data de acesso do Windows nem sempre é atualizada)
            if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1))
                info.LastWriteTimeUtc = DateTime.UtcNow;
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Grava com outro nome e troca no fim: um arquivo pela metade nunca fica com o nome certo
    private void Gravar(string arquivo, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_pasta);
            string temporario = $"{arquivo}.{Environment.ProcessId}.tmp";
            File.WriteAllBytes(temporario, bytes);
            File.Move(temporario, arquivo, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogInformation(ex, "Não deu para guardar a imagem do emote em {Arquivo}.", arquivo);
        }
    }

    private void ApagarOsSemUso()
    {
        try
        {
            if (!Directory.Exists(_pasta))
                return;
            DateTime limite = DateTime.UtcNow - TimeSpan.FromDays(DiasSemUso);
            foreach (string arquivo in Directory.EnumerateFiles(_pasta))
            {
                if (arquivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Um .tmp velho é uma gravação que não terminou
                if (File.GetLastWriteTimeUtc(arquivo) < limite
                    || (arquivo.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(arquivo) < DateTime.UtcNow.AddHours(-1)))
                    File.Delete(arquivo);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogInformation(ex, "Não deu para apagar as imagens de emotes sem uso.");
        }
    }
}
