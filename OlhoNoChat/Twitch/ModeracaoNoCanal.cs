using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Moderar o chat do canal aberto pelo app: apagar uma mensagem, suspender (timeout) ou banir quem mandou. Só quando a
/// conta conectada é dona do canal ou moderadora dele (a Twitch diz quais canais a conta modera). A página do Padrão mostra
/// os botões em cima da mensagem (com as bordas visíveis) e manda o pedido; aqui ele vai para a Twitch e volta um aviso
/// curto. A mensagem some do chat sozinha, quando a Twitch avisa no próprio chat (CLEARMSG e CLEARCHAT).
/// </summary>
public sealed class ModeracaoNoCanal
{
    public enum Papel
    {
        /// <summary>Sem conta, sem a permissão, opção desligada ou a conta não modera o canal aberto.</summary>
        Nenhum,
        Dono,
        Moderador,
    }

    /// <summary>
    /// O que a página pediu: <paramref name="Acao"/> "delete" (precisa do <paramref name="IdDaMensagem"/>), "timeout"
    /// (com <paramref name="Segundos"/>) ou "ban". <paramref name="Nome"/> é só para o aviso.
    /// </summary>
    public sealed record Pedido(string Acao, string? IdDaMensagem, string IdDoUsuario, string Nome, int? Segundos);

    /// <summary>A maior suspensão que a Twitch aceita: 14 dias.</summary>
    public const int MaiorSuspensao = 1_209_600;

    // Páginas de 100 canais moderados: mais que isso, desiste (a conta modera canais demais para procurar)
    private const int PaginasNoMaximo = 20;

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger _log;
    // Cada troca de canal ganha um número: uma resposta de uma troca antiga não vale
    private int _versao;
    // (conta, canal) → modera, durante a execução: salvar ou recarregar a página não pergunta de novo
    private readonly Dictionary<(string Conta, string Canal), bool> _jaConferidos = [];

    public ModeracaoNoCanal(ApiDaTwitch api, ContaDaTwitch conta, ILogger log)
    {
        _api = api;
        _conta = conta;
        _log = log;
    }

    /// <summary><see cref="PapelNoCanal"/> mudou.</summary>
    public event Action? Mudou;

    public Papel PapelNoCanal { get; private set; }

    /// <summary>O id do canal aberto (null com <see cref="Papel.Nenhum"/>).</summary>
    public string? IdDoCanal { get; private set; }

    public bool PodeModerar => PapelNoCanal != Papel.Nenhum;

    /// <summary>
    /// O canal aberto (o login; null = nenhum, ou moderar desligado). Confere se a conta é dona ou moderadora dele.
    /// Termina quando souber.
    /// </summary>
    public async Task TrocarDeCanalAsync(string? canal)
    {
        int versao = ++_versao;
        if (canal == null || !_conta.EstaConectada || !_conta.PodeModerarOChat)
        {
            Definir(Papel.Nenhum, null);
            return;
        }
        if (string.Equals(canal, _conta.Login, StringComparison.OrdinalIgnoreCase))
        {
            Definir(Papel.Dono, _conta.Id);
            return;
        }

        string? idDoCanal;
        bool modera;
        try
        {
            idDoCanal = await _api.IdDoCanalAsync(_conta.Token, canal);
            modera = idDoCanal != null && await ModeraAsync(idDoCanal);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Moderação: não deu para saber se a conta modera o canal.");
            idDoCanal = null;
            modera = false;
        }
        if (versao != _versao)
            return;
        Definir(modera ? Papel.Moderador : Papel.Nenhum, modera ? idDoCanal : null);
    }

    // Procura o canal nas páginas de canais moderados da conta (uma resposta de erro vira exceção)
    private async Task<bool> ModeraAsync(string idDoCanal)
    {
        var chave = (_conta.Id, idDoCanal);
        if (_jaConferidos.TryGetValue(chave, out bool guardado))
            return guardado;

        string? cursor = null;
        for (int pagina = 0; pagina < PaginasNoMaximo; pagina++)
        {
            ApiDaTwitch.Resposta resposta = await _api.CanaisModeradosAsync(_conta.Token, _conta.Id, cursor);
            if (!resposta.Deu)
            {
                if (resposta.Codigo == HttpStatusCode.Unauthorized)
                    _conta.AcessoRecusado();
                throw new HttpRequestException($"A Twitch respondeu {(int)resposta.Codigo} à lista de canais moderados.");
            }
            if (resposta.Dados.Any(c => (string?)c?["broadcaster_id"] == idDoCanal))
                return _jaConferidos[chave] = true;
            cursor = (string?)resposta.Json?["pagination"]?["cursor"];
            if (string.IsNullOrEmpty(cursor))
                break;
        }
        return _jaConferidos[chave] = false;
    }

    private void Definir(Papel papel, string? idDoCanal)
    {
        if (PapelNoCanal == papel && IdDoCanal == idDoCanal)
            return;
        PapelNoCanal = papel;
        IdDoCanal = idDoCanal;
        Mudou?.Invoke();
    }

    /// <summary>
    /// Faz o pedido na Twitch e devolve o aviso para o chat (o que aconteceu, ou por que não deu) e se deu certo.
    /// </summary>
    public async Task<(string Aviso, bool Deu)> ExecutarAsync(Pedido pedido)
    {
        if (!PodeModerar || IdDoCanal is not string canal || !_conta.EstaConectada)
            return ("Só dá para moderar com a conta dona ou moderadora deste canal conectada.", false);

        bool apagar = pedido.Acao == "delete";
        ApiDaTwitch.Resposta resposta;
        try
        {
            resposta = apagar
                ? await _api.ApagarMensagemAsync(_conta.Token, canal, _conta.Id, pedido.IdDaMensagem ?? string.Empty)
                : await _api.BanirAsync(_conta.Token, canal, _conta.Id, pedido.IdDoUsuario,
                    pedido.Acao == "timeout" ? pedido.Segundos : null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Moderação: não deu para falar com a Twitch.");
            return ("Sem conexão com a Twitch: não deu para " + (apagar ? "apagar." : pedido.Acao == "timeout" ? "suspender." : "banir."), false);
        }
        return (TextoDaResposta(pedido, resposta, () =>
        {
            _conta.AcessoRecusado();
            Definir(Papel.Nenhum, null);
        }, () =>
        {
            // A Twitch diz que a conta não modera mais: os botões saem
            _jaConferidos.Remove((_conta.Id, canal));
            Definir(Papel.Nenhum, null);
        }), resposta.Deu);
    }

    /// <summary>O aviso para cada resposta da Twitch.</summary>
    public static string TextoDaResposta(Pedido pedido, ApiDaTwitch.Resposta resposta, Action? acessoRecusado = null,
        Action? naoModera = null)
    {
        string nome = pedido.Nome;
        bool apagar = pedido.Acao == "delete";
        if (resposta.Deu)
        {
            return pedido.Acao switch
            {
                "delete" => $"Mensagem de {nome} apagada.",
                "timeout" => $"{nome} suspenso por {Duracao(pedido.Segundos ?? 0)}.",
                _ => $"{nome} banido do chat.",
            };
        }
        switch (resposta.Codigo)
        {
            case HttpStatusCode.Unauthorized:
                acessoRecusado?.Invoke();
                return "A conexão com a Twitch expirou. Conecte a conta de novo em Configurações > Twitch.";
            case HttpStatusCode.Forbidden:
                naoModera?.Invoke();
                return "A sua conta não é mais moderadora deste canal.";
            case HttpStatusCode.NotFound when apagar:
                return "Essa mensagem já tinha sido apagada.";
            case HttpStatusCode.BadRequest when apagar:
                return "A Twitch não deixou apagar: mensagens do dono, de moderadores ou com mais de 6 horas não podem ser apagadas.";
            case HttpStatusCode.BadRequest when MensagemDaTwitch(resposta).Contains("already banned", StringComparison.OrdinalIgnoreCase):
                return $"{nome} já está banido.";
            case HttpStatusCode.BadRequest:
                return $"A Twitch não deixou {(pedido.Acao == "timeout" ? "suspender" : "banir")} {nome}: o dono do canal e os moderadores não podem ser.";
            case HttpStatusCode.Conflict:
            case HttpStatusCode.TooManyRequests:
                return "A Twitch pediu para esperar um pouco. Tente de novo.";
            default:
                return $"A Twitch não aceitou agora ({(int)resposta.Codigo}). Tente de novo.";
        }
    }

    private static string MensagemDaTwitch(ApiDaTwitch.Resposta resposta)
    {
        try { return (string?)resposta.Json?["message"] ?? string.Empty; }
        catch (InvalidOperationException) { return string.Empty; }
    }

    /// <summary>"1 min", "10 min", "1 hora", "1 dia", "1 semana" (ou "90 s", "3 horas"...).</summary>
    public static string Duracao(int segundos) => segundos switch
    {
        < 60 => $"{segundos} s",
        < 3600 when segundos % 60 == 0 => $"{segundos / 60} min",
        < 86400 when segundos % 3600 == 0 => segundos == 3600 ? "1 hora" : $"{segundos / 3600} horas",
        604800 => "1 semana",
        1209600 => "2 semanas",
        _ when segundos % 86400 == 0 => segundos == 86400 ? "1 dia" : $"{segundos / 86400} dias",
        _ => $"{segundos / 60} min",
    };

    /// <summary>
    /// O pedido que a página mandou (<c>onc:mod:{"a":"timeout","msg":"...","uid":"...","name":"...","s":600}</c>); null se
    /// não for um, ou faltar o que a ação precisa.
    /// </summary>
    public static Pedido? LerPedido(string? mensagem)
    {
        const string prefixo = "onc:mod:";
        if (mensagem?.StartsWith(prefixo, StringComparison.Ordinal) != true)
            return null;
        try
        {
            JsonNode? json = JsonNode.Parse(mensagem[prefixo.Length..]);
            string acao = (string?)json?["a"] ?? string.Empty;
            string? idDaMensagem = (string?)json?["msg"];
            string idDoUsuario = (string?)json?["uid"] ?? string.Empty;
            string nome = (string?)json?["name"] ?? string.Empty;
            int? segundos = json?["s"]?.GetValueKind() == JsonValueKind.Number ? (int)json["s"]!.GetValue<double>() : null;
            return acao switch
            {
                "delete" when !string.IsNullOrEmpty(idDaMensagem) => new Pedido(acao, idDaMensagem, idDoUsuario, nome, null),
                "timeout" when idDoUsuario.Length > 0 && segundos is >= 1 and <= MaiorSuspensao =>
                    new Pedido(acao, idDaMensagem, idDoUsuario, nome, segundos),
                "ban" when idDoUsuario.Length > 0 => new Pedido(acao, idDaMensagem, idDoUsuario, nome, null),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
