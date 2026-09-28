#nullable enable
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Os emotes que a conta conectada pode usar no chat de um canal (do canal, das inscrições da conta e os globais),
/// agrupados por canal dono, para a lista da caixa de escrever. Guardada por 10 minutos.
/// </summary>
public sealed partial class ListaDeEmotes
{
    public sealed record Emote(string Id, string Nome);
    public sealed record Grupo(string Titulo, IReadOnlyList<Emote> Emotes);

    /// <param name="SoGlobais">O acesso não deixa ler os emotes da conta: só os globais foram listados.</param>
    public sealed record Lista(IReadOnlyList<Grupo> Grupos, bool SoGlobais);

    /// <summary>Um emote como a Twitch manda; <paramref name="Dono"/> é o id do canal ("" ou "0" nos globais).</summary>
    public sealed record EmoteDaTwitch(string Id, string Nome, string Dono);

    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(10);
    private const int MaximoDePaginas = 30;
    public const string TituloGlobais = "Globais da Twitch";

    // Os smileys antigos (":)", "<3", "R-)") ficam de fora: os emojis fazem isso melhor
    [GeneratedRegex(@"^\w+$")]
    private static partial Regex NomeDeEmote();
    private static readonly HashSet<string> SmileysAntigos = new(StringComparer.OrdinalIgnoreCase) { "O_o", "o_O", "O_O", "o_o" };

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger<ListaDeEmotes> _log;
    private readonly Func<DateTime> _agora;

    private (string Chave, DateTime Quando, Lista Lista)? _guardada;
    private (string Chave, Task<Lista> Busca)? _emAndamento;

    public ListaDeEmotes(ApiDaTwitch api, ContaDaTwitch conta, ILogger<ListaDeEmotes> log)
        : this(api, conta, log, () => DateTime.UtcNow) { }

    public ListaDeEmotes(ApiDaTwitch api, ContaDaTwitch conta, ILogger<ListaDeEmotes> log, Func<DateTime> agora)
    {
        _api = api;
        _conta = conta;
        _log = log;
        _agora = agora;
    }

    // A lista depende do canal, da conta e de a conta poder ler os próprios emotes
    private string Chave(string canal) => $"{canal.ToLowerInvariant()}|{_conta.Id}|{_conta.PodeLerEmotes}";

    /// <summary>A lista do canal já está guardada e vale (não precisa buscar).</summary>
    public bool EstaPronta(string canal) =>
        _guardada is { } g && g.Chave == Chave(canal) && _agora() - g.Quando < Validade;

    /// <summary>Esquece a lista guardada (outra conta, permissão nova).</summary>
    public void Descartar() => _guardada = null;

    /// <summary>
    /// A lista do canal: a guardada, a que já está sendo buscada, ou uma busca nova. Sem internet, a exceção passa.
    /// </summary>
    public Task<Lista> BuscarAsync(string canal)
    {
        string chave = Chave(canal);
        if (EstaPronta(canal))
            return Task.FromResult(_guardada!.Value.Lista);
        if (_emAndamento is { } busca && busca.Chave == chave)
            return busca.Busca;

        Task<Lista> nova = BuscarNaTwitchAsync(canal, chave);
        _emAndamento = (chave, nova);
        return nova;
    }

    private async Task<Lista> BuscarNaTwitchAsync(string canal, string chave)
    {
        try
        {
            Lista lista = await BuscarNaTwitchAsync(canal);
            _guardada = (chave, _agora(), lista);
            return lista;
        }
        finally
        {
            if (_emAndamento?.Chave == chave)
                _emAndamento = null;
        }
    }

    private async Task<Lista> BuscarNaTwitchAsync(string canal)
    {
        string token = _conta.Token;
        string idDaConta = _conta.Id;
        if (!_conta.EstaConectada)
            return new Lista([], SoGlobais: true);

        string? idDoCanal = canal.Length == 0 ? null : await _api.IdDoCanalAsync(token, canal);
        var encontrados = new List<EmoteDaTwitch>();
        bool soGlobais = !_conta.PodeLerEmotes;

        if (!soGlobais)
        {
            string? cursor = null;
            for (int pagina = 0; pagina < MaximoDePaginas; pagina++)
            {
                ApiDaTwitch.Resposta resposta = await _api.EmotesDaContaAsync(token, idDaConta, idDoCanal, cursor);
                if (resposta.Codigo == HttpStatusCode.Unauthorized)
                {
                    // Permissão faltando (ou acesso expirado): confere o acesso e mostra os globais
                    _log.LogInformation("A Twitch recusou a lista de emotes da conta: {Corpo}", resposta.Corpo);
                    _conta.AcessoRecusado();
                    soGlobais = true;
                    encontrados.Clear();
                    break;
                }
                GarantirQueDeu(resposta);

                encontrados.AddRange(resposta.Dados
                    .Where(e => (string?)e?["emote_type"] != "smilies")
                    .Select(e => new EmoteDaTwitch((string?)e?["id"] ?? "", (string?)e?["name"] ?? "", (string?)e?["owner_id"] ?? "")));

                cursor = (string?)resposta.Json?["pagination"]?["cursor"];
                if (string.IsNullOrEmpty(cursor))
                    break;
            }
        }

        if (soGlobais)
        {
            ApiDaTwitch.Resposta resposta = await _api.EmotesGlobaisAsync(token);
            GarantirQueDeu(resposta);
            // A Twitch lista os mais novos primeiro; os clássicos (Kappa...) são os mais usados
            encontrados.AddRange(resposta.Dados.Reverse()
                .Select(e => new EmoteDaTwitch((string?)e?["id"] ?? "", (string?)e?["name"] ?? "", "0")));
        }

        var donos = encontrados.Select(e => e.Dono).Where(d => !EhGlobal(d)).Distinct().ToList();
        return new Lista(Agrupar(encontrados, idDoCanal, canal, await NomesDosCanaisAsync(donos, token)), soGlobais);
    }

    /// <summary>
    /// Filtra e agrupa: um grupo por canal dono (o do chat primeiro, "{nome} (este canal)", os outros pelo nome) e os
    /// globais no fim. Só nomes que são uma palavra; nome repetido fica o primeiro.
    /// </summary>
    public static IReadOnlyList<Grupo> Agrupar(IEnumerable<EmoteDaTwitch> emotes, string? idDoCanal, string loginDoCanal,
        IReadOnlyDictionary<string, string> nomesDosDonos)
    {
        var usaveis = emotes.Where(e => e.Id.Length > 0 && NomeDeEmote().IsMatch(e.Nome) && !SmileysAntigos.Contains(e.Nome))
                            .DistinctBy(e => e.Nome)
                            .ToList();

        var grupos = usaveis.Where(e => !EhGlobal(e.Dono))
            .GroupBy(e => e.Dono)
            .OrderBy(g => g.Key == idDoCanal ? 0 : 1)
            .ThenBy(g => nomesDosDonos.GetValueOrDefault(g.Key, g.Key), StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                bool esteCanal = g.Key == idDoCanal;
                string nome = nomesDosDonos.GetValueOrDefault(g.Key, esteCanal ? loginDoCanal : "Outro canal");
                return new Grupo(esteCanal ? $"{nome} (este canal)" : nome, g.Select(e => new Emote(e.Id, e.Nome)).ToList());
            })
            .ToList();

        var globais = usaveis.Where(e => EhGlobal(e.Dono)).Select(e => new Emote(e.Id, e.Nome)).ToList();
        if (globais.Count > 0)
            grupos.Add(new Grupo(TituloGlobais, globais));
        return grupos;
    }

    private static bool EhGlobal(string dono) => dono.Length == 0 || dono == "0" || dono == "twitch";

    // Nomes dos canais donos, em lotes de 100; os que faltarem ficam de fora
    private async Task<Dictionary<string, string>> NomesDosCanaisAsync(IReadOnlyList<string> ids, string token)
    {
        var nomes = new Dictionary<string, string>();
        foreach (string[] lote in ids.Chunk(100))
        {
            try
            {
                ApiDaTwitch.Resposta resposta = await _api.UsuariosAsync(token, string.Join("&", lote.Select(id => "id=" + Uri.EscapeDataString(id))));
                GarantirQueDeu(resposta);
                foreach (JsonNode? usuario in resposta.Dados)
                {
                    string? id = (string?)usuario?["id"];
                    string? nome = (string?)usuario?["display_name"];
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(nome))
                        nomes[id] = nome;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Não deu para buscar os nomes dos canais dos emotes.");
            }
        }
        return nomes;
    }

    private static void GarantirQueDeu(ApiDaTwitch.Resposta resposta)
    {
        if (!resposta.Deu)
            throw new System.Net.Http.HttpRequestException($"A Twitch respondeu {(int)resposta.Codigo}.", null, resposta.Codigo);
    }
}
