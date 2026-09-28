using OlhoNoChat.Chat;

namespace OlhoNoChat.Testes.Chat;

/// <summary>A vigia do chat Padrão e os limites da recuperação do navegador, com um relógio de mentira.</summary>
public sealed class VigiaERecuperacaoTestes
{
    private DateTime _agora = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private void Passar(double segundos) => _agora = _agora.AddSeconds(segundos);

    private VigiaDoChat NovaVigiaComAPaginaCarregada()
    {
        var vigia = new VigiaDoChat(() => _agora);
        vigia.ComecouACarregar();
        Passar(2);
        vigia.TerminouDeCarregar();
        return vigia;
    }

    private static SaudeDaPagina Aberta(double segundosSemDados) => SaudeDaPagina.Aberta(TimeSpan.FromSeconds(segundosSemDados));

    [Fact]
    public void Vigia_NaoPerguntaDuranteUmCarregamentoDeMenosDe60s()
    {
        var vigia = new VigiaDoChat(() => _agora);
        Assert.True(vigia.DevePerguntar());
        Assert.False(vigia.PaginaPronta);

        vigia.ComecouACarregar();
        Passar(59);
        Assert.False(vigia.DevePerguntar());
        Passar(1);
        Assert.True(vigia.DevePerguntar());

        vigia.TerminouDeCarregar();
        Assert.True(vigia.PaginaPronta);
        vigia.ComecouACarregar();
        Assert.False(vigia.PaginaPronta);
        vigia.NaoCarregou();
        Assert.True(vigia.DevePerguntar());
    }

    [Fact]
    public void Vigia_ConectadaRecebendo_NaoFazNada()
    {
        var vigia = NovaVigiaComAPaginaCarregada();

        Assert.Null(vigia.Avaliar(Aberta(5)));
        Assert.Null(vigia.Avaliar(Aberta(180))); // 3 min ainda não passa do limite
    }

    [Fact]
    public void Vigia_TwitchCaladaMaisDe3Min_Recarrega()
    {
        var vigia = NovaVigiaComAPaginaCarregada();

        Assert.NotNull(vigia.Avaliar(Aberta(181)));
    }

    [Fact]
    public void Vigia_Reconectando_NaoFazNada()
    {
        var vigia = NovaVigiaComAPaginaCarregada();
        Passar(120);

        Assert.Null(vigia.Avaliar(SaudeDaPagina.Reconectando));
    }

    [Fact]
    public void Vigia_SemScript_EsperaOs30sDoComeco()
    {
        var vigia = NovaVigiaComAPaginaCarregada();

        Passar(30);
        Assert.Null(vigia.Avaliar(SaudeDaPagina.SemScript));
        Passar(1);
        Assert.NotNull(vigia.Avaliar(SaudeDaPagina.SemScript));
    }

    [Fact]
    public void Vigia_ComErro_Recarrega()
    {
        var vigia = NovaVigiaComAPaginaCarregada();
        Passar(31);

        Assert.Contains("x is undefined", vigia.Avaliar(SaudeDaPagina.ComErro("error:x is undefined")));
    }

    [Fact]
    public void Vigia_NoMinimo60sEntreRecargas_E5MinDepoisDeTresSeguidas()
    {
        var vigia = NovaVigiaComAPaginaCarregada();
        Passar(31);

        Assert.NotNull(vigia.Avaliar(SaudeDaPagina.SemScript)); // 1
        Passar(59);
        Assert.Null(vigia.Avaliar(SaudeDaPagina.SemScript));
        Passar(1);
        Assert.NotNull(vigia.Avaliar(SaudeDaPagina.SemScript)); // 2
        Passar(60);
        Assert.NotNull(vigia.Avaliar(SaudeDaPagina.SemScript)); // 3

        Passar(60);
        Assert.Null(vigia.Avaliar(SaudeDaPagina.SemScript)); // depois de 3 seguidas: 5 min
        Passar(239);
        Assert.Null(vigia.Avaliar(SaudeDaPagina.SemScript));
        Passar(1);
        Assert.Contains("tentativa 4", vigia.Avaliar(SaudeDaPagina.SemScript));
    }

    [Fact]
    public void Vigia_ConexaoFuncionando_ZeraAsTentativas()
    {
        var vigia = NovaVigiaComAPaginaCarregada();
        Passar(31);
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(vigia.Avaliar(SaudeDaPagina.SemScript));
            Passar(60);
        }

        Assert.Null(vigia.Avaliar(Aberta(1))); // voltou
        Assert.Contains("tentativa 1", vigia.Avaliar(Aberta(200)));
    }

    [Fact]
    public void Recuperacao_TresVezesEm5Min_ParaONavegadorEParaAPaginaSeparados()
    {
        var recuperacao = new RecuperacaoDoNavegador(() => _agora);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(recuperacao.PodeRecriarONavegador());
            Passar(10);
        }
        Assert.False(recuperacao.PodeRecriarONavegador());

        // A página tem a contagem dela
        Assert.True(recuperacao.PodeRecarregarAPagina(out _));

        // A primeira sai da conta 5 min depois
        Passar(300 - 30 + 1);
        Assert.True(recuperacao.PodeRecriarONavegador());
    }

    [Fact]
    public void Recuperacao_PaginaPassouDoLimite_DizQuandoTentar()
    {
        var recuperacao = new RecuperacaoDoNavegador(() => _agora);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(recuperacao.PodeRecarregarAPagina(out TimeSpan zero));
            Assert.Equal(TimeSpan.Zero, zero);
            Passar(20);
        }

        Assert.False(recuperacao.PodeRecarregarAPagina(out TimeSpan tentarEm));
        Assert.Equal(TimeSpan.FromSeconds(300 - 60), tentarEm);
    }

    [Fact]
    public void Recuperacao_TravadaSoDepoisDe20sSeguidos()
    {
        var recuperacao = new RecuperacaoDoNavegador(() => _agora);

        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(10);
        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(10);
        Assert.True(recuperacao.TravadaHaMuitoTempo());

        // Recomeça a contar
        Passar(5);
        Assert.False(recuperacao.TravadaHaMuitoTempo());
    }

    [Fact]
    public void Recuperacao_AvisoDepoisDeMaisDe60s_EhOutraTravada()
    {
        var recuperacao = new RecuperacaoDoNavegador(() => _agora);

        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(61);
        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(19);
        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(1);
        Assert.True(recuperacao.TravadaHaMuitoTempo());
    }

    [Fact]
    public void Recuperacao_PaginaCarregou_EsqueceATravada()
    {
        var recuperacao = new RecuperacaoDoNavegador(() => _agora);

        Assert.False(recuperacao.TravadaHaMuitoTempo());
        Passar(15);
        recuperacao.PaginaCarregou();
        Passar(10);
        Assert.False(recuperacao.TravadaHaMuitoTempo());
    }
}
