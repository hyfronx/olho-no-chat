using System.IO;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Testes.Overlay;

public sealed class ArquivoDoObsTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OncArquivoDoObs-" + Guid.NewGuid().ToString("N"));

    private string Arquivo => Path.Combine(_pasta, ArquivoDoObs.NomeDoArquivo);

    // A proteção vale também para os testes: sem tirar, a pasta temporária ficaria para sempre
    public void Dispose() => ArquivoDoObs.Apagar(_pasta);

    [Fact]
    public void Garantir_CriaOArquivoQueEsperaOAppNaPorta()
    {
        ArquivoDoObs.Garantir(8987, _pasta);

        string conteudo = File.ReadAllText(Arquivo);
        Assert.Contains("location.replace('http://localhost:8987/overlay')", conteudo);
        // Lê o sinal como arquivo da mesma pasta: o OBS bloqueia qualquer pedido do arquivo ao localhost
        Assert.Contains($"fetch('{ArquivoDoObs.NomeDoSinal}'", conteudo);
        Assert.Contains($"< {ArquivoDoObs.SinalValeMs})", conteudo);
        Assert.DoesNotContain("iframe", conteudo);
    }

    [Fact]
    public void Sinal_AHoraEmMsEZeroAoParar()
    {
        ArquivoDoObs.Garantir(8987, _pasta);
        var agora = new DateTimeOffset(2026, 11, 11, 17, 0, 0, TimeSpan.Zero);
        string sinal = Path.Combine(_pasta, ArquivoDoObs.NomeDoSinal);

        ArquivoDoObs.DarSinalDeVida(_pasta, agora);
        Assert.Equal(agora.ToUnixTimeMilliseconds().ToString(), File.ReadAllText(sinal));

        ArquivoDoObs.TirarSinalDeVida(_pasta);
        Assert.Equal("0", File.ReadAllText(sinal));
        Assert.True(ArquivoDoObs.Protegido(_pasta));
    }

    [Fact]
    public void Sinal_SemAPastaNaoCriaNada()
    {
        ArquivoDoObs.DarSinalDeVida(_pasta, DateTimeOffset.UtcNow);
        ArquivoDoObs.TirarSinalDeVida(_pasta);

        Assert.False(Directory.Exists(_pasta));
    }

    [Fact]
    public void Apagar_LevaOSinalJunto()
    {
        ArquivoDoObs.Garantir(8987, _pasta);
        ArquivoDoObs.DarSinalDeVida(_pasta, DateTimeOffset.UtcNow);

        ArquivoDoObs.Apagar(_pasta);

        Assert.False(Directory.Exists(_pasta));
    }

    [Fact]
    public void Garantir_OutraPortaReescreveOMesmoArquivo()
    {
        ArquivoDoObs.Garantir(8987, _pasta);
        ArquivoDoObs.Garantir(18987, _pasta);

        Assert.Contains("'http://localhost:18987/overlay'", File.ReadAllText(Arquivo));
        Assert.Single(Directory.GetFiles(_pasta));
        Assert.True(ArquivoDoObs.Protegido(_pasta));
    }

    [Fact]
    public void Protegido_NaoDaParaApagarNemRenomear()
    {
        ArquivoDoObs.Garantir(8987, _pasta);

        Assert.True(ArquivoDoObs.Protegido(_pasta));
        Assert.Throws<UnauthorizedAccessException>(() => File.Delete(Arquivo));
        Assert.Throws<UnauthorizedAccessException>(() => File.Move(Arquivo, Arquivo + ".velho"));
        Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(_pasta, recursive: true));
        Assert.True(File.Exists(Arquivo));
        // Renomear a pasta o Windows deixa (quem decide é a pasta de cima); o app cria de novo ao abrir
    }

    [Fact]
    public void Apagar_TiraAProtecaoEApagaTudo()
    {
        ArquivoDoObs.Garantir(8987, _pasta);

        ArquivoDoObs.Apagar(_pasta);

        Assert.False(Directory.Exists(_pasta));
        ArquivoDoObs.Apagar(_pasta); // sem a pasta, não falha
    }

    [Fact]
    public void Garantir_ArquivoMexidoVoltaAoCerto()
    {
        ArquivoDoObs.Garantir(8987, _pasta);
        File.WriteAllText(Arquivo, "estragado");

        ArquivoDoObs.Garantir(8987, _pasta);

        Assert.Equal(ArquivoDoObs.Conteudo(8987), File.ReadAllText(Arquivo));
    }

    [Fact]
    public void Caminho_ForaDaPastaDaInstalacao()
    {
        Assert.EndsWith(Path.Combine(InfoDoApp.NomeDaPastaDeDados + "-OBS", "overlay.html"), ArquivoDoObs.Caminho);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ArquivoDoObs.Caminho);
    }
}
