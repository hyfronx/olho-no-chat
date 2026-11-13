using System.Net;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Twitch;
using static OlhoNoChat.Twitch.EventSubDaTwitch;

namespace OlhoNoChat.Testes.Twitch;

public class AnunciosDoCanalTestes
{
    // 2026-11-13 20:00:00 UTC
    private static readonly DateTimeOffset Agora = DateTimeOffset.FromUnixTimeSeconds(1794600000);

    private sealed class Cenario
    {
        public TwitchFalsa Twitch { get; } = new();
        public AnunciosDoCanal Anuncios { get; }
        public DateTimeOffset Hora { get; set; } = Agora;
        public List<string?> Avisos { get; } = [];

        public Cenario(long proximo, int adiamentos = 2, long ultimo = 0, int segundos = 90)
        {
            Twitch.Quando("GET", "channels/ads", HttpStatusCode.OK, Agenda(proximo, adiamentos, ultimo, segundos));
            Anuncios = new AnunciosDoCanal(new ApiDaTwitch(Twitch), Montar.Conta(Twitch, Montar.ContaConectada()), NullLogger.Instance,
                () => Hora);
            Anuncios.Mudou += aviso => Avisos.Add(aviso);
        }

        // Liga e espera a primeira consulta chegar
        public async Task LigarAsync()
        {
            Anuncios.Ligar();
            // A agenda chegou (Mudou) ou a Twitch recusou
            for (int i = 0; i < 200 && Avisos.Count == 0
                            && Anuncios.SituacaoAtual is AnunciosDoCanal.Situacao.Desligado or AnunciosDoCanal.Situacao.Funcionando; i++)
                await Task.Delay(10);
        }
    }

    private static string Agenda(long proximo, int adiamentos, long ultimo, int segundos) =>
        $$"""{"data":[{"next_ad_at":{{proximo}},"last_ad_at":{{ultimo}},"duration":{{segundos}},"preroll_free_time":0,"snooze_count":{{adiamentos}},"snooze_refresh_at":0}]}""";

    private static long Daqui(TimeSpan tempo) => (Agora + tempo).ToUnixTimeSeconds();

    [Fact]
    public void LerAgenda_ExemploDaDocumentacao()
    {
        var agenda = AnunciosDoCanal.LerAgenda(JsonNode.Parse("""
            {"next_ad_at":1690931298,"last_ad_at":1690930398,"duration":60,"preroll_free_time":90,"snooze_count":1,"snooze_refresh_at":1690932198}
            """))!;

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1690931298), agenda.Proximo);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1690930398), agenda.Ultimo);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1690932198), agenda.NovoAdiamento);
        Assert.Equal(60, agenda.Segundos);
        Assert.Equal(1, agenda.Adiamentos);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("\"\"", null)]
    [InlineData("null", null)]
    [InlineData("1690931298", 1690931298L)]
    [InlineData("\"1690931298\"", 1690931298L)]
    [InlineData("\"2023-08-01T23:08:18+00:00\"", 1690931298L)]
    public void LerHorario_NumeroTextoOuData(string json, long? esperado)
    {
        DateTimeOffset? lido = AnunciosDoCanal.LerHorario(JsonNode.Parse(json));
        Assert.Equal(esperado, lido?.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Fases_AvisoCincoMinutosAntesEUmPoucoDepoisDaHora()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(7)));
        await c.LigarAsync();

        Assert.Equal(AnunciosDoCanal.Situacao.Funcionando, c.Anuncios.SituacaoAtual);
        Assert.Equal(AnunciosDoCanal.Fase.Nenhuma, c.Anuncios.FaseEm(Agora));
        Assert.Equal(TimeSpan.FromMinutes(2), c.Anuncios.ProximaMudanca(Agora)); // quando o aviso aparece
        Assert.Equal(AnunciosDoCanal.Fase.Chegando, c.Anuncios.FaseEm(Agora.AddMinutes(2)));
        Assert.True(c.Anuncios.PodeAdiar(Agora.AddMinutes(2)));
        Assert.False(c.Anuncios.PodeAdiar(Agora)); // fora do aviso, o atalho fica livre
        Assert.Equal(AnunciosDoCanal.Fase.Chegando, c.Anuncios.FaseEm(Agora.AddMinutes(7).AddSeconds(30))); // começando
        Assert.Equal(AnunciosDoCanal.Fase.Nenhuma, c.Anuncios.FaseEm(Agora.AddMinutes(8).AddSeconds(1)));
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task Comecou_PeloEventSubRodaPelaDuracao()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(1)));
        await c.LigarAsync();

        c.Anuncios.Comecou(new AnuncioComecou(Agora.AddMinutes(1), 90, Automatico: true));

        Assert.Equal(AnunciosDoCanal.Fase.Rodando, c.Anuncios.FaseEm(Agora.AddMinutes(2)));
        Assert.False(c.Anuncios.PodeAdiar(Agora.AddMinutes(2)));
        Assert.Equal(AnunciosDoCanal.Fase.Nenhuma, c.Anuncios.FaseEm(Agora.AddMinutes(2).AddSeconds(31)));
        // Logo depois, a hora marcada ainda está na folga, mas o anúncio já foi esse: o aviso não volta
        Assert.Equal(AnunciosDoCanal.Fase.Nenhuma, c.Anuncios.FaseEm(Agora.AddMinutes(1).AddSeconds(5).AddSeconds(90)));
        Assert.Equal(AnunciosDoCanal.Fase.Chegando, c.Anuncios.FaseEm(Agora.AddSeconds(30))); // antes de começar, o aviso fica
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task SemEventSub_OUltimoQueAindaNaoAcabouContaComoRodando()
    {
        var c = new Cenario(proximo: 0, ultimo: Daqui(TimeSpan.FromSeconds(-20)), segundos: 60);
        await c.LigarAsync();

        Assert.Equal(AnunciosDoCanal.Fase.Rodando, c.Anuncios.FaseEm(Agora));
        Assert.Equal(Agora.AddSeconds(40), c.Anuncios.Rodando!.Value.Fim);
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task Adiar_ChamaATwitchEMostraOHorarioNovo()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(3)));
        long novo = Daqui(TimeSpan.FromMinutes(8));
        c.Twitch.Quando("POST", "channels/ads/schedule/snooze", HttpStatusCode.OK,
            $$"""{"data":[{"snooze_count":1,"snooze_refresh_at":{{Daqui(TimeSpan.FromHours(1))}},"next_ad_at":{{novo}}}]}""");
        await c.LigarAsync();
        // A agenda que a consulta seguinte traz já é a nova
        c.Twitch.Quando("GET", "channels/ads", HttpStatusCode.OK, Agenda(novo, 1, 0, 90));

        await c.Anuncios.AdiarAsync();

        var pedido = Assert.Single(c.Twitch.PedidosPara("schedule/snooze"));
        Assert.Contains("broadcaster_id=121292674", pedido.Endereco);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(novo), c.Anuncios.Atual!.Proximo);
        Assert.Equal(1, c.Anuncios.Atual.Adiamentos);
        Assert.Contains("Adiado 5 minutos.", c.Avisos);
        Assert.Equal(AnunciosDoCanal.Fase.Nenhuma, c.Anuncios.FaseEm(Agora)); // o aviso sai até faltar 5 minutos de novo
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task Adiar_SemAdiamentosNemPedeATwitch()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(3)), adiamentos: 0);
        await c.LigarAsync();

        await c.Anuncios.AdiarAsync();

        Assert.Empty(c.Twitch.PedidosPara("schedule/snooze"));
        Assert.False(c.Anuncios.PodeAdiar(Agora));
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task Adiar_TwitchRecusaDizPorQue()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(3)));
        c.Twitch.Quando("POST", "channels/ads/schedule/snooze", HttpStatusCode.TooManyRequests);
        await c.LigarAsync();

        await c.Anuncios.AdiarAsync();

        Assert.Contains(c.Avisos, a => a != null && a.StartsWith("A Twitch não deixou adiar agora", StringComparison.Ordinal));
        Assert.False(c.Anuncios.Adiando);
        c.Anuncios.Desligar();
    }

    [Fact]
    public async Task Recusa_FicaNaSituacaoParaAAbaTwitch()
    {
        var c = new Cenario(0);
        c.Twitch.Quando("GET", "channels/ads", HttpStatusCode.Forbidden, """{"message":"x"}""");
        await c.LigarAsync();

        Assert.Equal(AnunciosDoCanal.Situacao.Recusado, c.Anuncios.SituacaoAtual);
        Assert.Contains("(403)", c.Anuncios.Problema);
        Assert.Null(c.Anuncios.Atual);
        c.Anuncios.Desligar();
        Assert.Equal(AnunciosDoCanal.Situacao.Desligado, c.Anuncios.SituacaoAtual);
    }

    [Fact]
    public async Task Desligar_EsqueceAAgenda()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(3)));
        await c.LigarAsync();

        c.Anuncios.Desligar();
        c.Anuncios.Comecou(new AnuncioComecou(Agora, 60, false)); // desligado, não vale

        Assert.Null(c.Anuncios.Atual);
        Assert.Null(c.Anuncios.Rodando);
        Assert.False(c.Anuncios.Ligado);
    }

    [Fact]
    public void EventSub_LeOComecoDoAnuncio_NumeroOuTexto()
    {
        foreach (string duracao in new[] { "60", "\"60\"" })
        {
            string json = """
                {"metadata":{"message_id":"m1","message_type":"notification"},
                 "payload":{"subscription":{"type":"channel.ad_break.begin","version":"1"},
                  "event":{"duration_seconds":DURACAO,"started_at":"2019-11-16T10:11:12.634234626Z","is_automatic":"false",
                   "broadcaster_user_id":"1337","broadcaster_user_login":"cool_user","broadcaster_user_name":"Cool_User",
                   "requester_user_id":"1337","requester_user_login":"cool_user","requester_user_name":"Cool_User"}}}
                """.Replace("DURACAO", duracao);
            var anuncio = EventSubDaTwitch.LerMensagem(json)!.Anuncio!;
            Assert.Equal(60, anuncio.Segundos);
            Assert.False(anuncio.Automatico);
            Assert.Equal(new DateTimeOffset(2019, 11, 16, 10, 11, 12, TimeSpan.Zero), anuncio.Inicio.AddTicks(-(anuncio.Inicio.Ticks % TimeSpan.TicksPerSecond)));
        }
    }

    [Fact]
    public async Task Pagina_RecebeOsHorariosEmMilissegundos()
    {
        var c = new Cenario(Daqui(TimeSpan.FromMinutes(3)));
        await c.LigarAsync();

        string script = ContratoComAPagina.MostrarAnuncios(c.Anuncios, new Atalho(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt), "Adiado 5 minutos.");
        var json = JsonNode.Parse(script[(script.IndexOf("ads(", StringComparison.Ordinal) + 4)..script.LastIndexOf(')')])!;

        Assert.Equal(Agora.AddMinutes(3).ToUnixTimeMilliseconds(), (long)json["nextAt"]!);
        Assert.Equal(300_000, (long)json["warnMs"]!);
        Assert.Equal(2, (int)json["snoozes"]!);
        Assert.Equal("Ctrl + Alt + Page Down", (string?)json["hotkey"]);
        Assert.Equal("Adiado 5 minutos.", (string?)json["notice"]);
        Assert.Contains("\"nextAt\":null", ContratoComAPagina.MostrarAnuncios(null, null, null));
        Assert.Equal(MensagemDaPagina.AdiarAnuncio, ContratoComAPagina.LerMensagem("onc:ads:snooze"));
        c.Anuncios.Desligar();
    }

    [Fact]
    public void Atalho_PadraoEConflitoComUmAntigo()
    {
        Assert.Equal(new Atalho(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt), new Opcoes().AtalhoAdiarAnuncio);

        var opcoes = new Opcoes { AtalhoBordas = new Atalho(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt) };
        opcoes.Corrigir();
        Assert.Null(opcoes.AtalhoAdiarAnuncio); // a combinação já era de outro: fica sem, a pessoa escolhe outra
    }

    [Fact]
    public void Novidade_DepoisDasOutras()
    {
        string[] vistas = [Novidades.Pausa, Novidades.AutoMod, Novidades.Overlay];
        Assert.Equal(Novidades.Anuncios, Novidades.Proxima(vistas, true, false, true, true, false, false, anunciosPronto: true));
        Assert.Null(Novidades.Proxima(vistas, true, false, true, true, false, false, anunciosPronto: false));
        Assert.Null(Novidades.Proxima([.. vistas, Novidades.Anuncios], true, false, true, true, false, false, anunciosPronto: true));
    }
}
