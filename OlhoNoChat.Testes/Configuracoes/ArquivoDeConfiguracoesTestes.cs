using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Configuracoes;

/// <summary>
/// O arquivo Configuracoes.json numa pasta de mentira: primeira instalação, conversão dos arquivos antigos (que só
/// saem da pasta depois de o novo estar gravado), segunda cópia que só lê, arquivo ilegível, gravação segura e
/// "Restaurar tudo para o padrão".
/// </summary>
public sealed class ArquivoDeConfiguracoesTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));

    public ArquivoDeConfiguracoesTestes() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string NaPasta(params string[] partes) => Path.Combine([_pasta, .. partes]);

    private string Novo => NaPasta(ArquivoDeConfiguracoes.NomeDoArquivo);

    private string Antigos(string nome) => NaPasta(ArquivoDeConfiguracoes.PastaDosAntigos, nome);

    private void GravarAntigos(JsonObject? opcoes, string? janela = null)
    {
        if (opcoes != null)
            File.WriteAllText(NaPasta("AppSettings.json"), ArquivoAntigoFalso.Opcoes(opcoes));
        if (janela != null)
            File.WriteAllText(NaPasta("MainWindow_State.json"), janela);
    }

    private ArquivoDeConfiguracoes AbrirComoCopiaPrincipal()
    {
        var arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);
        arquivo.ComecarAGravar();
        return arquivo;
    }

    private List<string> ArquivosDaPasta() =>
        Directory.GetFiles(_pasta, "*", SearchOption.AllDirectories).Select(a => Path.GetRelativePath(_pasta, a)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void PrimeiraInstalacao_PadroesSemAvisoESemArquivoAteGravar()
    {
        var arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);

        Assert.Equal(ArquivoDeConfiguracoes.ParaJson(new Opcoes()), ArquivoDeConfiguracoes.ParaJson(arquivo.Opcoes));
        Assert.Null(arquivo.Aviso);
        arquivo.ComecarAGravar();
        Assert.Empty(ArquivosDaPasta());

        Assert.True(arquivo.Gravar());
        Assert.Equal([ArquivoDeConfiguracoes.NomeDoArquivo], ArquivosDaPasta());
    }

    [Fact]
    public void PrimeiraInstalacao_ValoresDaPrimeiraInstalacao()
    {
        var o = ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes;

        Assert.Equal(0.8, o.TamanhoDoTexto);
        Assert.Equal(165, o.Fundo);
        Assert.Equal(Opcoes.TemaPadrao, o.Tema);
        Assert.Equal("job-done.wav", o.SomDeMensagem);
        Assert.Equal(1.0f, o.Volume);
        Assert.Equal("none", o.ContornoDasLetras);
        Assert.Equal(new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt), o.AtalhoBordas);
        Assert.Null(o.Janela);
    }

    [Fact]
    public void Conversao_GravaONovoEMoveOsAntigosSemMudarNada()
    {
        GravarAntigos(ArquivoAntigoFalso.TodasAsChavesDiferentesDoPadrao(), ArquivoAntigoFalso.Janela(527, 250, 437, 450, 0));
        byte[] opcoesAntes = File.ReadAllBytes(NaPasta("AppSettings.json"));
        byte[] janelaAntes = File.ReadAllBytes(NaPasta("MainWindow_State.json"));

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.Equal([ArquivoDeConfiguracoes.NomeDoArquivo,
                      Path.Combine(ArquivoDeConfiguracoes.PastaDosAntigos, "AppSettings.json"),
                      Path.Combine(ArquivoDeConfiguracoes.PastaDosAntigos, "MainWindow_State.json")], ArquivosDaPasta());
        Assert.Equal(opcoesAntes, File.ReadAllBytes(Antigos("AppSettings.json")));
        Assert.Equal(janelaAntes, File.ReadAllBytes(Antigos("MainWindow_State.json")));
        Assert.Null(arquivo.Aviso);

        // Aberto de novo, o arquivo novo tem os mesmos valores que a conversão deu
        var reaberto = ArquivoDeConfiguracoes.Abrir(_pasta);
        Assert.Equal(SemToken(arquivo.Opcoes), SemToken(reaberto.Opcoes));
        Assert.Equal("tokenfalso123", reaberto.Opcoes.Conta.Token);
        Assert.Equal("canalteste", reaberto.Opcoes.Canal);
        Assert.Equal(new PosicaoDaJanela(527, 250, 437, 450, WindowState.Normal), reaberto.Opcoes.Janela);
    }

    private static string SemToken(Opcoes opcoes)
    {
        var json = JsonNode.Parse(ArquivoDeConfiguracoes.ParaJson(opcoes))!;
        json["Conta"]!.AsObject().Remove("TokenProtegido");
        return json.ToJsonString();
    }

    [Fact]
    public void Conversao_SoAJanela()
    {
        GravarAntigos(null, ArquivoAntigoFalso.Janela(100, 200, 360, 530, 0));

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.Equal(new PosicaoDaJanela(100, 200, 360, 530, WindowState.Normal), arquivo.Opcoes.Janela);
        Assert.True(File.Exists(Antigos("MainWindow_State.json")));
        Assert.True(File.Exists(Novo));
    }

    [Fact]
    public void SegundaCopia_SoLeENaoMexeEmNada()
    {
        GravarAntigos(ArquivoAntigoFalso.Minimo(("Username", "canal")), ArquivoAntigoFalso.Janela(1, 2, 300, 400, 0));
        var antes = ArquivosDaPasta();

        var arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);
        arquivo.Opcoes.Canal = "outro";
        Assert.False(arquivo.Gravar());
        arquivo.GravarDaquiAPouco();
        arquivo.RestaurarPadrao();
        Thread.Sleep(700);

        Assert.Equal("canal", ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes.Canal);
        Assert.Equal(antes, ArquivosDaPasta());
        Assert.False(arquivo.PodeGravar);
    }

    [Fact]
    public void ConversaoQueNaoConsegueGravar_AntigosFicamETentaDeNovoDepois()
    {
        GravarAntigos(ArquivoAntigoFalso.Minimo(("Username", "canal")));
        // Uma pasta com o nome do arquivo novo: o arquivo não pode ser gravado
        Directory.CreateDirectory(Novo);

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.Equal("canal", arquivo.Opcoes.Canal); // abriu com o antigo
        Assert.True(File.Exists(NaPasta("AppSettings.json")));
        Assert.False(Directory.Exists(NaPasta(ArquivoDeConfiguracoes.PastaDosAntigos)));
        Assert.Empty(Directory.GetFiles(_pasta, "*.tmp"));

        // Na próxima abertura (o problema passou) a conversão é feita
        Directory.Delete(Novo);
        AbrirComoCopiaPrincipal();
        Assert.True(File.Exists(Novo));
        Assert.True(File.Exists(Antigos("AppSettings.json")));
    }

    [Fact]
    public void NovoValidoComAntigosQueSobraram_NovoGanhaEOsAntigosSaoGuardados()
    {
        var primeira = ArquivoDeConfiguracoes.Abrir(_pasta);
        primeira.ComecarAGravar();
        primeira.Opcoes.Canal = "donovo";
        primeira.Gravar();
        GravarAntigos(ArquivoAntigoFalso.Minimo(("Username", "doantigo")));

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.Equal("donovo", arquivo.Opcoes.Canal);
        Assert.False(File.Exists(NaPasta("AppSettings.json")));
        Assert.True(File.Exists(Antigos("AppSettings.json")));
    }

    [Fact]
    public void AntigosJaGuardadosAntes_NadaESobrescrito()
    {
        Directory.CreateDirectory(NaPasta(ArquivoDeConfiguracoes.PastaDosAntigos));
        File.WriteAllText(Antigos("AppSettings.json"), "o de antes");
        GravarAntigos(ArquivoAntigoFalso.Minimo(("Username", "canal")));

        AbrirComoCopiaPrincipal();

        Assert.Equal("o de antes", File.ReadAllText(Antigos("AppSettings.json")));
        Assert.Contains("canal", File.ReadAllText(Antigos("AppSettings (2).json")));
    }

    [Fact]
    public void AntesDa1017_TodosOsJsonSaoGuardadosESoOCanalFica()
    {
        File.WriteAllText(NaPasta("AppSettings.json"), ArquivoAntigoFalso.Opcoes(new JsonObject { ["Username"] = "Amigo", ["ZoomLevel"] = 2.0 }));
        File.WriteAllText(NaPasta("MainWindow_State.json"), ArquivoAntigoFalso.Janela(1, 2, 300, 400, 0));
        File.WriteAllText(NaPasta("CustomWindow_1.json"), "[]");

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.Equal("amigo", arquivo.Opcoes.Canal);
        Assert.Equal(0.8, arquivo.Opcoes.TamanhoDoTexto);
        Assert.Null(arquivo.Opcoes.Janela);
        Assert.Equal([ArquivoDeConfiguracoes.NomeDoArquivo,
                      Path.Combine(ArquivoDeConfiguracoes.PastaDosAntigos, "AppSettings.json"),
                      Path.Combine(ArquivoDeConfiguracoes.PastaDosAntigos, "CustomWindow_1.json"),
                      Path.Combine(ArquivoDeConfiguracoes.PastaDosAntigos, "MainWindow_State.json")], ArquivosDaPasta());
    }

    [Fact]
    public void NovoIlegivel_GuardaUmaCopiaAbreComOsPadroesEAvisa()
    {
        File.WriteAllText(Novo, "{ \"Canal\": \"x\", estragado");

        var arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);
        Assert.NotNull(arquivo.Aviso);
        Assert.Equal("", arquivo.Opcoes.Canal);
        Assert.False(Directory.Exists(NaPasta(ArquivoDeConfiguracoes.PastaComProblema))); // só lendo, nada muda

        arquivo.ComecarAGravar();

        string copia = Directory.GetFiles(NaPasta(ArquivoDeConfiguracoes.PastaComProblema)).Single();
        Assert.Equal("{ \"Canal\": \"x\", estragado", File.ReadAllText(copia));
        Assert.Contains(copia, arquivo.Aviso);
        Assert.Equal(ArquivoDeConfiguracoes.ParaJson(new Opcoes()).Length, File.ReadAllText(Novo).Length);
        Assert.Null(ArquivoDeConfiguracoes.Abrir(_pasta).Aviso);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("{\"TipoDeChat\": \"um\"}")]
    [InlineData("{\"CorDoDestaque\": \"azulado\"}")]
    public void NovoIlegivel_VariosJeitos(string texto)
    {
        File.WriteAllText(Novo, texto);

        Assert.NotNull(ArquivoDeConfiguracoes.Abrir(_pasta).Aviso);
    }

    [Fact]
    public void AntigoIlegivel_ConverteComOsPadroesEAvisaOndeFicou()
    {
        File.WriteAllText(NaPasta("AppSettings.json"), "{{{ não é json");

        var arquivo = AbrirComoCopiaPrincipal();

        Assert.NotNull(arquivo.Aviso);
        Assert.Contains(Antigos("AppSettings.json"), arquivo.Aviso);
        Assert.Equal("{{{ não é json", File.ReadAllText(Antigos("AppSettings.json")));
        Assert.True(File.Exists(Novo));
    }

    [Fact]
    public void GravarELerDeNovo_DaOMesmo()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        var o = arquivo.Opcoes;
        o.Canal = "canal_1";
        o.CssDoTemaNenhum = "#chat_box { color: \"ação\"; } /* < > & */";
        o.TamanhoDoTexto = 1.37;
        o.Fundo = 7;
        o.Volume = 0.73f;
        o.CorDosVips = Color.FromArgb(1, 2, 3, 4);
        o.AtalhoBordas = null;
        o.AtalhoEscrever = new Atalho(Key.OemPlus, ModifierKeys.Windows | ModifierKeys.Alt);
        o.ListaDeUsuarios = ["a", "b"];
        o.Conta.Id = "1"; o.Conta.Login = "l"; o.Conta.NomeDeExibicao = "L"; o.Conta.Token = "segredo";
        o.Janela = new PosicaoDaJanela(-1919.5, 20, 400.25, 600, WindowState.Normal);
        arquivo.Gravar();

        var lidas = ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes;

        Assert.Equal(SemToken(o), SemToken(lidas));
        Assert.Equal("segredo", lidas.Conta.Token);
        Assert.Null(lidas.AtalhoBordas);
        Assert.Equal(new Atalho(Key.OemPlus, ModifierKeys.Windows | ModifierKeys.Alt), lidas.AtalhoEscrever);
        Assert.Equal(0.73f, lidas.Volume);
    }

    [Fact]
    public void Formato_NomesEmPortuguesAtalhoECorLegiveis()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        arquivo.Opcoes.AtalhoModoRolagem = null;
        arquivo.Gravar();

        var json = JsonNode.Parse(File.ReadAllText(Novo))!.AsObject();

        Assert.Equal(Opcoes.VersaoAtual, (int)json["Versao"]!);
        Assert.Equal(98, (int)json["AtalhoBordas"]!["Tecla"]!);
        Assert.Equal(3, (int)json["AtalhoBordas"]!["Modificadores"]!);
        Assert.True(json.ContainsKey("AtalhoModoRolagem"));
        Assert.Null(json["AtalhoModoRolagem"]);
        Assert.Equal("#96F5F500", (string)json["CorDoDestaque"]!);
        Assert.False(File.ReadAllText(Novo).StartsWith('﻿'));
    }

    [Fact]
    public void Token_NoArquivoFicaProtegido()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        arquivo.Opcoes.Conta.Id = "121292674";
        arquivo.Opcoes.Conta.Token = "tokenfalso123";
        arquivo.Gravar();

        string texto = File.ReadAllText(Novo);
        string protegido = (string)JsonNode.Parse(texto)!["Conta"]!["TokenProtegido"]!;

        Assert.DoesNotContain("tokenfalso123", texto);
        Assert.NotEmpty(protegido);
        Assert.Equal("tokenfalso123", ProtecaoDoToken.Abrir(protegido));
    }

    [Fact]
    public void Token_QueNaoAbre_ContaFicaDesconectada()
    {
        // Como uma pasta copiada de outro computador: o token protegido lá não abre aqui
        File.WriteAllText(Novo, """
            { "Canal": "c", "Conta": { "Id": "1", "Login": "l", "NomeDeExibicao": "L", "TokenProtegido": "AQAAANCMnd8BFdERjHoAwE" } }
            """);

        var arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);

        Assert.Null(arquivo.Aviso);
        Assert.Equal("c", arquivo.Opcoes.Canal);
        Assert.Equal("", arquivo.Opcoes.Conta.Token);
        Assert.Equal("", arquivo.Opcoes.Conta.Login);
        Assert.Equal("", arquivo.Opcoes.Conta.Id);
    }

    [Fact]
    public void Gravar_NaoDeixaArquivoTemporarioESubstituiOAnterior()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        arquivo.Opcoes.Canal = "um";
        arquivo.Gravar();
        arquivo.Opcoes.Canal = "dois";
        arquivo.Gravar();

        Assert.Equal([ArquivoDeConfiguracoes.NomeDoArquivo], ArquivosDaPasta());
        Assert.Equal("dois", ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes.Canal);
    }

    [Fact]
    public void ChavesDesconhecidasEFaltando_NoArquivoNovo()
    {
        File.WriteAllText(Novo, """{ "canal": "minusculo", "OpcaoDoFuturo": 1, "Tema": 5, "TamanhoDoTexto": 0 }""");

        var o = ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes;

        Assert.Equal("minusculo", o.Canal);
        Assert.Equal(Opcoes.TemaPadrao, o.Tema);
        Assert.Equal(0.8, o.TamanhoDoTexto);
        Assert.True(o.EsconderBots);
    }

    [Fact]
    public void RestaurarPadrao_MantemCanalContaListasEJanelaEGuardaUmaCopia()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        var o = arquivo.Opcoes;
        o.Canal = "canal";
        o.Conta.Id = "1"; o.Conta.Token = "t"; o.Conta.Login = "l"; o.Conta.NomeDeExibicao = "L";
        o.ListaDeUsuarios = ["amigo"];
        o.UsuariosBloqueados = ["chato"];
        o.Janela = new PosicaoDaJanela(1, 2, 300, 400, WindowState.Normal);
        o.TamanhoDoTexto = 2;
        o.SempreNoTopo = false;
        o.AtalhoBordas = null;
        o.CorDoDestaque = Colors.Red;
        arquivo.Gravar();
        string antes = File.ReadAllText(Novo);

        arquivo.RestaurarPadrao();

        var r = arquivo.Opcoes;
        Assert.Equal("canal", r.Canal);
        Assert.Equal("t", r.Conta.Token);
        Assert.Equal("L", r.Conta.NomeDeExibicao);
        Assert.Equal(["amigo"], r.ListaDeUsuarios);
        Assert.Equal(["chato"], r.UsuariosBloqueados);
        Assert.Equal(new PosicaoDaJanela(1, 2, 300, 400, WindowState.Normal), r.Janela);
        Assert.Equal(0.8, r.TamanhoDoTexto);
        Assert.True(r.SempreNoTopo);
        Assert.Equal(new Opcoes().AtalhoBordas, r.AtalhoBordas);
        Assert.Equal(new Opcoes().CorDoDestaque, r.CorDoDestaque);
        Assert.Equal(antes, File.ReadAllText(NaPasta(ArquivoDeConfiguracoes.PastaAntesDeRestaurar, ArquivoDeConfiguracoes.NomeDoArquivo)));
        Assert.Equal(0.8, ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes.TamanhoDoTexto);
    }

    [Fact]
    public void GravarDaquiAPouco_GravaUmaVezDepoisDaUltimaMudanca()
    {
        var arquivo = AbrirComoCopiaPrincipal();
        for (int i = 1; i <= 5; i++)
        {
            arquivo.Opcoes.Fundo = (byte)i;
            arquivo.GravarDaquiAPouco();
            Thread.Sleep(50);
        }
        Assert.False(File.Exists(Novo));

        Thread.Sleep(800);

        Assert.Equal(5, ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes.Fundo);
    }
}
