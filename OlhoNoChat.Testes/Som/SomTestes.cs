using System.IO;
using OlhoNoChat.Som;

namespace OlhoNoChat.Testes.Som;

public class SomTestes
{
    [Theory]
    [InlineData("job-done.wav", "Job done")]
    [InlineData("you-wouldnt-believe.wav", "You wouldnt believe")]
    [InlineData("meu_som_legal.mp3", "Meu som legal")]
    [InlineData("Alerta.wav", "Alerta")]
    [InlineData("x.wav", "X")]
    public void NomeMostrado_SemExtensaoComEspacosEMaiusculaNoComeco(string arquivo, string nome)
    {
        Assert.Equal(nome, SonsDisponiveis.NomeMostrado(arquivo));
    }

    [Fact]
    public void Listar_PrimeiroOsWavDepoisOsMp3()
    {
        string pasta = Directory.CreateTempSubdirectory("onc-sons").FullName;
        try
        {
            foreach (string arquivo in new[] { "b.mp3", "a.wav", "leia.txt", "c-som.wav", "d.mp3" })
                File.WriteAllText(Path.Combine(pasta, arquivo), "");

            var sons = SonsDisponiveis.Listar(pasta);

            Assert.Equal(["a.wav", "c-som.wav", "b.mp3", "d.mp3"], sons.Select(s => s.Arquivo));
            Assert.Equal("C som", sons[1].Nome);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Pasta_PadraoVaziaOuQueSumiuUsaOsSonsDoApp()
    {
        string doApp = SonsDisponiveis.PastaDosSonsDoApp;
        Assert.Equal(doApp, SonsDisponiveis.ResolverPasta("Default"));
        Assert.Equal(doApp, SonsDisponiveis.ResolverPasta(""));
        Assert.Equal(doApp, SonsDisponiveis.ResolverPasta(null));
        Assert.Equal(doApp, SonsDisponiveis.ResolverPasta(@"C:\pasta\que\nao\existe\onc"));
        string temp = Path.GetTempPath();
        Assert.Equal(temp, SonsDisponiveis.ResolverPasta(temp));
    }

    [Fact]
    public void Caminho_NenhumOuArquivoQueNaoExisteFicaMudo()
    {
        Assert.Null(SonsDisponiveis.Caminho("Default", "None"));
        Assert.Null(SonsDisponiveis.Caminho("Default", "none"));
        Assert.Null(SonsDisponiveis.Caminho("Default", ""));
        Assert.Null(SonsDisponiveis.Caminho("Default", "nao-existe.wav"));
        // Os sons do app vão para a pasta de saída da compilação
        Assert.Equal(Path.Combine(SonsDisponiveis.PastaDosSonsDoApp, "job-done.wav"), SonsDisponiveis.Caminho("Default", "job-done.wav"));
    }

    [Fact]
    public void SonsDoApp_OsDozeComOsNomesDaLista()
    {
        var nomes = SonsDisponiveis.Listar(SonsDisponiveis.PastaDosSonsDoApp).Select(s => s.Nome).Order().ToList();
        Assert.Equal(["Base", "Coins", "Intuition", "Job done", "Knob", "Pull out", "Shoot em", "Suppressed", "Unsure",
                      "Wind up", "You wouldnt believe", "Your turn"], nomes);
    }

    private static readonly DateTime Inicio = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void QuandoTocar_ZeroTocaSempre()
    {
        var regra = new RegraQuandoTocar { Segundos = 0 };
        regra.Tocou(Inicio);
        Assert.True(regra.Permite(Inicio));
    }

    [Fact]
    public void QuandoTocar_PrimeiraSempreTocaEDepoisContaDoUltimoSomTocado()
    {
        var regra = new RegraQuandoTocar { Segundos = 30 };
        Assert.True(regra.Permite(Inicio)); // a primeira
        regra.Tocou(Inicio);

        Assert.False(regra.Permite(Inicio.AddSeconds(10)));  // ignorado: não reinicia a contagem
        Assert.False(regra.Permite(Inicio.AddSeconds(29.9)));
        Assert.True(regra.Permite(Inicio.AddSeconds(30)));

        regra.Tocou(Inicio.AddSeconds(45));
        Assert.False(regra.Permite(Inicio.AddSeconds(60)));
        Assert.True(regra.Permite(Inicio.AddSeconds(75)));
    }

    [Fact]
    public void QuandoTocar_ContagemContinuaAoTrocarOTempo()
    {
        var regra = new RegraQuandoTocar { Segundos = 10 };
        regra.Tocou(Inicio);
        regra.Segundos = 60; // salvou outra opção
        Assert.False(regra.Permite(Inicio.AddSeconds(30)));
    }

    [Fact]
    public void Tocador_SemArquivoFicaMudoSemErro()
    {
        using var tocador = new TocadorDeAviso();
        Assert.Null(tocador.Configurar(null, 1f, TocadorDeAviso.Padrao, "Default", 0));
        tocador.Tocar(); // nada acontece
    }

    [Fact]
    public void Tocador_ArquivoQueNaoAbreDevolveOErro()
    {
        string arquivo = Path.Combine(Path.GetTempPath(), $"onc-quebrado-{Guid.NewGuid():N}.wav");
        File.WriteAllText(arquivo, "isto não é um wav");
        try
        {
            using var tocador = new TocadorDeAviso();
            Assert.NotNull(tocador.Configurar(arquivo, 1f, TocadorDeAviso.Padrao, "Default", 0));
            tocador.Tocar();
        }
        finally
        {
            File.Delete(arquivo);
        }
    }

    [Fact]
    public void Saidas_ComecaPeloPadraoDoWindows()
    {
        var saidas = TocadorDeAviso.ListarSaidas();
        Assert.Equal(new TocadorDeAviso.Saida(-1, "Padrão do Windows"), saidas[0]);
        Assert.True(TocadorDeAviso.SaidaAindaExiste(-1, "Default"));
        Assert.False(TocadorDeAviso.SaidaAindaExiste(999, "Fone"));
        if (saidas.Count > 1)
        {
            Assert.True(TocadorDeAviso.SaidaAindaExiste(saidas[1].Id, saidas[1].Nome));
            Assert.False(TocadorDeAviso.SaidaAindaExiste(saidas[1].Id, "Outro aparelho"));
        }
    }

    // A licença dos sons (CC BY 4.0) exige crédito: um som novo na pasta assets precisa de uma linha no CREDITOS.txt
    [Fact]
    public void Creditos_TodoSomQueVemComOAppTemCreditoELink()
    {
        string pasta = AppContext.BaseDirectory;
        string[] linhas = File.ReadAllLines(Path.Combine(pasta, "CREDITOS.txt"));
        string[] sons = Directory.GetFiles(Path.Combine(pasta, "assets"), "*.wav").Select(Path.GetFileName).OfType<string>().ToArray();

        Assert.NotEmpty(sons);
        foreach (string som in sons)
            Assert.Contains(linhas, l => l.TrimStart().StartsWith(som + " ") && l.Contains("https://notificationsounds.com/"));
    }
}
