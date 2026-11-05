using System.Net;
using Microsoft.Extensions.Logging;
using static OlhoNoChat.Twitch.EventSubDaTwitch;

namespace OlhoNoChat.Twitch;

/// <summary>
/// As mensagens que o AutoMod segurou no canal aberto, numa fila por ordem de chegada: o chat mostra só a mais antiga
/// ("1 de 3") e aprovar ou recusar vale para ela; depois aparece a próxima. Quem decide fora do app (outro moderador, o
/// site da Twitch) ou o tempo que acaba também tira a mensagem da fila.
/// </summary>
/// <remarks>
/// Aprovar e recusar tiram a mensagem da fila na hora, sem esperar a Twitch (o atalho apertado duas vezes vale para a
/// primeira e depois para a segunda); se a Twitch não fizer, ela volta para o começo. Os atalhos só ficam registrados
/// enquanto há mensagem esperando: sem nada na fila, a combinação continua livre para os outros programas.
/// </remarks>
public sealed class AutoModDoCanal
{
    /// <summary>Mais que isso, as mais antigas saem (já devem ter expirado na Twitch).</summary>
    public const int Maximo = 100;

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger _log;
    private readonly List<MensagemRetida> _fila = [];

    public AutoModDoCanal(ApiDaTwitch api, ContaDaTwitch conta, ILogger log)
    {
        _api = api;
        _conta = conta;
        _log = log;
    }

    /// <summary>
    /// A fila mudou: a página mostra a primeira (ou esconde o cartão) e, se houver, o aviso de como foi a última decisão.
    /// </summary>
    public event Action<string?>? Mudou;

    /// <summary>A fila passou a ter mensagens (true) ou ficou vazia (false): registra ou solta os atalhos.</summary>
    public event Action<bool>? TemMensagensMudou;

    /// <summary>O id do canal da fila (null = AutoMod desligado ou a conta não modera o canal aberto).</summary>
    public string? Canal { get; private set; }

    public IReadOnlyList<MensagemRetida> Mensagens => _fila;

    public MensagemRetida? Primeira => _fila.Count > 0 ? _fila[0] : null;

    /// <summary>Outro canal (ou nenhum): as mensagens do anterior saem da fila.</summary>
    public void TrocarDeCanal(string? idDoCanal)
    {
        if (idDoCanal == Canal)
            return;
        Canal = idDoCanal;
        if (_fila.Count > 0)
            Mudar(() => _fila.Clear(), aviso: null);
    }

    public void Receber(MensagemRetida mensagem)
    {
        if (Canal == null || _fila.Any(m => m.Id == mensagem.Id))
            return;
        Mudar(() =>
        {
            _fila.Add(mensagem);
            if (_fila.Count > Maximo)
                _fila.RemoveAt(0);
        }, aviso: null);
    }

    /// <summary>Aprovada, recusada ou expirada fora do app (ou a resposta do próprio app, que já tirou da fila).</summary>
    public void Decidida(Decisao decisao)
    {
        int lugar = _fila.FindIndex(m => m.Id == decisao.Id);
        if (lugar >= 0)
            Mudar(() => _fila.RemoveAt(lugar), aviso: null);
    }

    /// <summary>
    /// Aprova ou recusa a mensagem com esse id, ou a primeira da fila (null: os atalhos). Termina quando a Twitch
    /// respondeu.
    /// </summary>
    public async Task DecidirAsync(string? id, bool aprovar)
    {
        MensagemRetida? mensagem = id == null ? Primeira : _fila.Find(m => m.Id == id);
        if (mensagem == null || !_conta.EstaConectada)
            return;

        Mudar(() => _fila.Remove(mensagem), (aprovar ? "Aprovada: " : "Recusada: ") + mensagem.Nome);

        ApiDaTwitch.Resposta resposta;
        try
        {
            resposta = await _api.DecidirNoAutoModAsync(_conta.Token, _conta.Id, mensagem.Id, aprovar);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AutoMod: não deu para falar com a Twitch.");
            Voltar(mensagem, "Sem conexão com a Twitch: a mensagem continua na fila. Tente de novo.");
            return;
        }

        switch (resposta.Codigo)
        {
            case HttpStatusCode.NoContent or HttpStatusCode.OK:
                break;
            case HttpStatusCode.NotFound:
                Mudar(() => { }, "Essa mensagem já tinha saído da fila (o tempo acabou ou outro moderador decidiu).");
                break;
            case HttpStatusCode.Forbidden:
                Mudar(() => _fila.Clear(), "A sua conta não é mais moderadora deste canal.");
                break;
            case HttpStatusCode.Unauthorized:
                _conta.AcessoRecusado();
                Voltar(mensagem, "A conexão com a Twitch expirou. Conecte a conta de novo em Configurações > Twitch.");
                break;
            default:
                _log.LogWarning("AutoMod: a Twitch respondeu {Codigo} {Corpo}", (int)resposta.Codigo, resposta.Corpo);
                Voltar(mensagem, $"A Twitch não aceitou agora ({(int)resposta.Codigo}): a mensagem continua na fila. Tente de novo.");
                break;
        }
    }

    // A Twitch não fez: a mensagem volta para o começo (se nada a tirou da fila enquanto isso)
    private void Voltar(MensagemRetida mensagem, string aviso) => Mudar(() =>
    {
        if (Canal != null && !_fila.Any(m => m.Id == mensagem.Id))
            _fila.Insert(0, mensagem);
    }, aviso);

    private void Mudar(Action mudanca, string? aviso)
    {
        bool tinha = _fila.Count > 0;
        mudanca();
        Mudou?.Invoke(aviso);
        if (tinha != _fila.Count > 0)
            TemMensagensMudou?.Invoke(_fila.Count > 0);
    }

    /// <summary>Por que o AutoMod segurou, para o cartão: "termo bloqueado", "palavrão"... ("" se não souber).</summary>
    public static string Motivo(MensagemRetida mensagem) => mensagem.Motivo switch
    {
        "blocked_term" => "termo bloqueado",
        "blocked_link" => "link bloqueado",
        _ => (mensagem.Categoria ?? string.Empty).ToLowerInvariant() switch
        {
            "swearing" or "profanity" => "palavrão",
            "aggression" or "aggressive" => "agressividade",
            "bullying" => "bullying",
            "disability" or "ableism" => "deficiência",
            "sexuality_sex_or_gender" or "identity" => "sexualidade ou gênero",
            "misogyny" => "misoginia",
            "race_ethnicity_or_religion" or "racism" => "raça, etnia ou religião",
            "sex_based_terms" or "sexual" or "sexwords" => "termos sexuais",
            _ => string.Empty,
        },
    };
}
