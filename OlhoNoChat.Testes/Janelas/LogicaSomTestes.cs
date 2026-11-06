using System.IO;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>
/// A aba Som sem a tela: o aviso de pasta sem sons (.wav ou .mp3) e "Usar os sons do app" desligado quando a pasta já é
/// a do app. As pastas são de mentira, numa pasta temporária.
/// </summary>
public sealed class LogicaSomTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));
    private readonly string _comSons;
    private readonly string _semSons;

    public LogicaSomTestes()
    {
        _comSons = Path.Combine(_pasta, "com-sons");
        _semSons = Path.Combine(_pasta, "sem-sons");
        Directory.CreateDirectory(_comSons);
        Directory.CreateDirectory(_semSons);
        File.WriteAllBytes(Path.Combine(_comSons, "alfa.wav"), []);
        File.WriteAllBytes(Path.Combine(_semSons, "leia-me.txt"), []);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // Sem tocar nada de verdade
    private static LogicaSom Nova() => new((_, _, _) => { });

    [Fact]
    public void PastaSemSons_Avisa()
    {
        var som = Nova();

        som.MudarPasta(_semSons);

        Assert.True(som.PastaSemSons);
        Assert.Equal(["Nenhum"], som.Sons.Select(s => s.Nome));
    }

    [Fact]
    public void TrocarParaUmaPastaComSons_TiraOAviso()
    {
        var som = Nova();
        som.MudarPasta(_semSons);

        som.MudarPasta(_comSons);

        Assert.False(som.PastaSemSons);
        Assert.Equal(["Nenhum", "Alfa"], som.Sons.Select(s => s.Nome));
    }

    [Fact]
    public void AoAbrir_PastaSalvaSemSonsJaAvisa()
    {
        var som = Nova();

        som.Carregar(new Opcoes { PastaDosSons = _semSons, SomDeMensagem = "alfa.wav" });

        Assert.True(som.PastaSemSons);
    }

    [Fact]
    public void UsarOsSonsDoApp_DesligadoQuandoAPastaJaEhADoApp()
    {
        var som = Nova();
        Assert.False(som.UsarPastaPadraoCommand.CanExecute(null));

        som.MudarPasta(_comSons);
        Assert.True(som.UsarPastaPadraoCommand.CanExecute(null));

        som.UsarPastaPadraoCommand.Execute(null);
        Assert.False(som.UsarPastaPadraoCommand.CanExecute(null));
        Assert.Equal(LogicaSom.TextoDaPastaPadrao, som.TextoDaPasta);
    }

    [Fact]
    public void UsarOsSonsDoApp_OBotaoFicaSabendoQuandoAPastaMuda()
    {
        var som = Nova();
        int avisos = 0;
        som.UsarPastaPadraoCommand.CanExecuteChanged += (_, _) => avisos++;

        som.MudarPasta(_comSons);

        Assert.NotEqual(0, avisos);
    }

    [Fact]
    public void AoAbrir_ComUmaPastaSalvaOBotaoFicaLigado()
    {
        var som = Nova();

        som.Carregar(new Opcoes { PastaDosSons = _comSons });

        Assert.True(som.UsarPastaPadraoCommand.CanExecute(null));
    }
}
