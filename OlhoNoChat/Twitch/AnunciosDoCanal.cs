using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using static OlhoNoChat.Twitch.EventSubDaTwitch;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Os anúncios do canal da conta conectada: quando vem o próximo (para o aviso de 5 minutos antes, como a Twitch faz),
/// quando um está rodando e o "Adiar" (5 minutos a mais, enquanto a Twitch der adiamentos). Só o dono do canal pode ver:
/// a Twitch não mostra os anúncios de um canal a moderadores.
/// </summary>
/// <remarks>
/// A agenda vem da Twitch a cada <see cref="IntervaloDaConsulta"/> enquanto está ligado; o começo de um anúncio chega na
/// hora pelo EventSub (<see cref="Comecou"/>). Os horários ficam aqui e a página do chat conta os segundos sozinha; a
/// janela usa <see cref="FaseEm"/> e <see cref="ProximaMudanca"/> para ligar o atalho de adiar só enquanto o aviso está
/// na tela. Tudo roda na thread de quem liga (a da tela).
/// </remarks>
public sealed class AnunciosDoCanal
{
    /// <summary>Quanto antes do anúncio o aviso aparece (o mesmo tempo do aviso da Twitch).</summary>
    public static readonly TimeSpan Antecedencia = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Depois da hora marcada, o aviso ainda fica um pouco ("começando"): o anúncio pode demorar a começar e o aviso
    /// do EventSub a chegar.
    /// </summary>
    public static readonly TimeSpan Folga = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan IntervaloDaConsulta = TimeSpan.FromSeconds(30);

    /// <summary>Quanto tempo o "adiado" e os outros avisos ficam no cartão (a página apaga sozinha).</summary>
    public const int SegundosDoAviso = 4;

    public enum Fase
    {
        Nenhuma,
        /// <summary>Faltam <see cref="Antecedencia"/> ou menos para o próximo (ou ele está começando).</summary>
        Chegando,
        Rodando,
    }

    public enum Situacao
    {
        /// <summary>Não foi pedido: opção desligada, sem conta, sem a permissão, ou o chat aberto não é o canal da conta.</summary>
        Desligado,
        /// <summary>A Twitch respondeu: o aviso aparece quando houver anúncio marcado.</summary>
        Funcionando,
        /// <summary>A Twitch recusou (<see cref="Problema"/> diz por quê).</summary>
        Recusado,
        SemConexao,
    }

    /// <summary>
    /// A agenda como a Twitch mandou. <paramref name="Proximo"/> é null sem anúncio marcado (ou com o canal offline);
    /// <paramref name="Segundos"/> é a duração do próximo.
    /// </summary>
    public sealed record Agenda(DateTimeOffset? Proximo, int Segundos, int Adiamentos, DateTimeOffset? NovoAdiamento,
        DateTimeOffset? Ultimo);

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger _log;
    private readonly Func<DateTimeOffset> _agora;
    private CancellationTokenSource? _parar;
    private bool _adiando;

    public AnunciosDoCanal(ApiDaTwitch api, ContaDaTwitch conta, ILogger log) : this(api, conta, log, () => DateTimeOffset.UtcNow) { }

    /// <summary>Os testes escolhem a hora.</summary>
    public AnunciosDoCanal(ApiDaTwitch api, ContaDaTwitch conta, ILogger log, Func<DateTimeOffset> agora)
    {
        _api = api;
        _conta = conta;
        _log = log;
        _agora = agora;
    }

    /// <summary>A agenda, o anúncio rodando ou o pode-adiar mudou; com um aviso para o cartão (null = nenhum).</summary>
    public event Action<string?>? Mudou;

    /// <summary><see cref="SituacaoAtual"/> mudou (para a aba Twitch).</summary>
    public event Action? SituacaoMudou;

    public Agenda? Atual { get; private set; }

    /// <summary>O anúncio rodando agora (ou o último que rodou): começo e fim.</summary>
    public (DateTimeOffset Inicio, DateTimeOffset Fim)? Rodando { get; private set; }

    public Situacao SituacaoAtual { get; private set; }

    /// <summary>Com <see cref="Situacao.Recusado"/>: o que a Twitch disse, para a aba Twitch.</summary>
    public string Problema { get; private set; } = string.Empty;

    public bool Ligado => _parar != null;

    /// <summary>Um pedido de adiar está esperando a Twitch.</summary>
    public bool Adiando => _adiando;

    /// <summary>Começa a perguntar a agenda (agora e a cada <see cref="IntervaloDaConsulta"/>). Já ligado, nada muda.</summary>
    public void Ligar()
    {
        if (_parar != null)
            return;
        var parar = new CancellationTokenSource();
        _parar = parar;
        _ = ConsultarSempreAsync(parar.Token);
    }

    /// <summary>Para de perguntar e esquece tudo (o cartão some).</summary>
    public void Desligar()
    {
        _parar?.Cancel();
        _parar = null;
        bool tinha = Atual != null || Rodando != null;
        Atual = null;
        Rodando = null;
        DefinirSituacao(Situacao.Desligado, string.Empty);
        if (tinha)
            Mudou?.Invoke(null);
    }

    private async Task ConsultarSempreAsync(CancellationToken parar)
    {
        try
        {
            while (!parar.IsCancellationRequested)
            {
                await ConsultarAsync();
                await Task.Delay(IntervaloDaConsulta, parar);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Pergunta a agenda à Twitch agora.</summary>
    public async Task ConsultarAsync()
    {
        if (!_conta.EstaConectada)
            return;
        CancellationTokenSource? ligadoQuandoPediu = _parar;
        ApiDaTwitch.Resposta resposta;
        try
        {
            resposta = await _api.AnunciosAsync(_conta.Token, _conta.Id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Anúncios: não deu para falar com a Twitch.");
            if (_parar == ligadoQuandoPediu && _parar != null)
                DefinirSituacao(Situacao.SemConexao, string.Empty);
            return;
        }
        // Desligado (ou religado) enquanto esperava: a resposta não vale mais
        if (_parar != ligadoQuandoPediu || _parar == null)
            return;

        if (!resposta.Deu)
        {
            if (resposta.Codigo == HttpStatusCode.Unauthorized)
                _conta.AcessoRecusado();
            else
                _log.LogWarning("Anúncios: a Twitch respondeu {Codigo} {Corpo}", (int)resposta.Codigo, resposta.Corpo);
            DefinirSituacao(Situacao.Recusado, TextoDaRecusa(resposta));
            return;
        }

        DefinirSituacao(Situacao.Funcionando, string.Empty);
        Agenda? agenda = LerAgenda(resposta.Dados.FirstOrDefault());
        if (agenda == null)
            return;
        (DateTimeOffset, DateTimeOffset)? rodando = Rodando;
        // Sem o aviso do EventSub (a assinatura pode ter falhado), o último anúncio que ainda não acabou conta como rodando
        if (agenda.Ultimo is { } ultimo && agenda.Segundos > 0 && (Rodando == null || Math.Abs((Rodando.Value.Inicio - ultimo).TotalSeconds) > 30))
        {
            DateTimeOffset fim = ultimo.AddSeconds(agenda.Segundos);
            if (fim > _agora() && (Rodando == null || ultimo > Rodando.Value.Inicio))
                rodando = (ultimo, fim);
        }
        if (agenda == Atual && rodando == Rodando)
            return;
        Atual = agenda;
        Rodando = rodando;
        Mudou?.Invoke(null);
    }

    /// <summary>O EventSub avisou que um anúncio começou.</summary>
    public void Comecou(AnuncioComecou anuncio)
    {
        if (_parar == null)
            return;
        Rodando = (anuncio.Inicio, anuncio.Inicio.AddSeconds(anuncio.Segundos));
        Mudou?.Invoke(null);
    }

    /// <summary>
    /// "Adiar" (botão do cartão ou atalho): o próximo anúncio fica 5 minutos mais longe, se a Twitch ainda der
    /// adiamentos. Termina quando a Twitch respondeu.
    /// </summary>
    public async Task AdiarAsync()
    {
        if (_adiando || _parar == null || !_conta.EstaConectada || !PodeAdiar(_agora()))
            return;
        _adiando = true;
        Mudou?.Invoke(null);

        string aviso;
        try
        {
            ApiDaTwitch.Resposta resposta = await _api.AdiarAnuncioAsync(_conta.Token, _conta.Id);
            switch (resposta.Codigo)
            {
                case HttpStatusCode.OK:
                    JsonNode? dados = resposta.Dados.FirstOrDefault();
                    if (Atual != null && dados != null)
                    {
                        Atual = Atual with
                        {
                            Proximo = LerHorario(dados["next_ad_at"]) ?? Atual.Proximo?.AddMinutes(5),
                            Adiamentos = Math.Max(0, NumeroOuTexto(dados["snooze_count"])),
                            NovoAdiamento = LerHorario(dados["snooze_refresh_at"]),
                        };
                    }
                    aviso = "Adiado 5 minutos.";
                    break;
                case HttpStatusCode.TooManyRequests:
                    aviso = "A Twitch não deixou adiar agora: os adiamentos acabaram ou outro pedido está em andamento.";
                    break;
                case HttpStatusCode.BadRequest:
                    aviso = "Não há anúncio marcado para adiar (o canal precisa estar ao vivo).";
                    break;
                case HttpStatusCode.Unauthorized:
                    _conta.AcessoRecusado();
                    aviso = "A conexão com a Twitch expirou. Conecte a conta de novo em Configurações > Twitch.";
                    break;
                default:
                    _log.LogWarning("Anúncios: adiar respondeu {Codigo} {Corpo}", (int)resposta.Codigo, resposta.Corpo);
                    aviso = $"A Twitch não deixou adiar agora ({(int)resposta.Codigo}).";
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Anúncios: não deu para adiar.");
            aviso = "Sem conexão com a Twitch: não deu para adiar.";
        }
        finally
        {
            _adiando = false;
        }
        Mudou?.Invoke(aviso);
        // A agenda nova de verdade (o adiamento pode ter mudado mais que o próximo horário)
        _ = ConsultarAsync();
    }

    public Fase FaseEm(DateTimeOffset agora)
    {
        if (Rodando is { } rodando && agora >= rodando.Inicio && agora < rodando.Fim)
            return Fase.Rodando;
        // Um anúncio que começou dentro do aviso já foi esse próximo (a agenda só muda na consulta seguinte)
        if (Atual?.Proximo is { } proximo && proximo - agora <= Antecedencia && agora - proximo < Folga
            && !(Rodando is { } jaRodou && agora >= jaRodou.Inicio && jaRodou.Inicio >= proximo - Antecedencia))
            return Fase.Chegando;
        return Fase.Nenhuma;
    }

    /// <summary>O aviso de "chegando" está na tela e ainda há adiamentos.</summary>
    public bool PodeAdiar(DateTimeOffset agora) => FaseEm(agora) == Fase.Chegando && Atual is { Adiamentos: > 0 };

    /// <summary>Daqui a quanto a fase muda (o aviso aparece ou some); null se nada estiver marcado.</summary>
    public TimeSpan? ProximaMudanca(DateTimeOffset agora)
    {
        var horas = new List<DateTimeOffset>();
        if (Rodando is { } rodando)
            horas.AddRange([rodando.Inicio, rodando.Fim]);
        if (Atual?.Proximo is { } proximo)
            horas.AddRange([proximo - Antecedencia, proximo + Folga]);
        DateTimeOffset? primeira = horas.Where(h => h > agora).Order().Cast<DateTimeOffset?>().FirstOrDefault();
        return primeira - agora;
    }

    private void DefinirSituacao(Situacao situacao, string problema)
    {
        if (SituacaoAtual == situacao && Problema == problema)
            return;
        SituacaoAtual = situacao;
        Problema = problema;
        SituacaoMudou?.Invoke();
    }

    /// <summary>O primeiro item de "data" da consulta da agenda; null se não vier.</summary>
    public static Agenda? LerAgenda(JsonNode? dados) => dados == null
        ? null
        : new Agenda(LerHorario(dados["next_ad_at"]), Math.Max(0, NumeroOuTexto(dados["duration"])),
            Math.Max(0, NumeroOuTexto(dados["snooze_count"])), LerHorario(dados["snooze_refresh_at"]), LerHorario(dados["last_ad_at"]));

    /// <summary>
    /// Um horário da agenda: segundos Unix (como número ou texto, a documentação diz número) ou uma data RFC3339; 0 e
    /// vazio = nenhum.
    /// </summary>
    public static DateTimeOffset? LerHorario(JsonNode? valor)
    {
        long segundos = 0;
        switch (valor?.GetValueKind())
        {
            case JsonValueKind.Number:
                segundos = (long)valor.GetValue<double>();
                break;
            case JsonValueKind.String:
                string texto = (string?)valor ?? string.Empty;
                if (long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    segundos = n;
                else if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset data))
                    return data;
                break;
        }
        return segundos > 0 ? DateTimeOffset.FromUnixTimeSeconds(segundos) : null;
    }

    /// <summary>O aviso da aba Twitch quando a Twitch recusa mostrar os anúncios.</summary>
    public static string TextoDaRecusa(ApiDaTwitch.Resposta resposta) => resposta.Codigo switch
    {
        HttpStatusCode.Unauthorized => "A conexão com a Twitch expirou. Conecte a conta de novo, acima.",
        _ => $"A Twitch não deixou ver os anúncios do seu canal ({(int)resposta.Codigo}). Anúncios só existem em canais de afiliados e parceiros.",
    };
}
