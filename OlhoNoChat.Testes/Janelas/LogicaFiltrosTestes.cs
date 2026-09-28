using System.Windows.Media;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Filtros;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaFiltrosTestes
{
    private static readonly Color Amarelo = Color.FromArgb(150, 0xF5, 0xF5, 0x00);
    private static readonly Color Verde = Color.FromArgb(150, 0x00, 0xAD, 0x03);

    private int _gravacoes;

    private LogicaFiltros Nova(Opcoes opcoes) => new(opcoes, () => { _gravacoes++; return true; });

    private static Opcoes ComListas(string[] usuarios, string[] bloqueados) => new()
    {
        ListaDeUsuarios = [.. usuarios],
        UsuariosBloqueados = [.. bloqueados],
    };

    // ----- Adicionar -----

    [Theory]
    [InlineData("NovoUsuario", "NovoUsuario")]
    [InlineData("@NovoUsuario", "NovoUsuario")]
    [InlineData("twitch.tv/NovoUsuario", "NovoUsuario")]
    [InlineData("https://www.twitch.tv/NovoUsuario/videos", "NovoUsuario")]
    [InlineData("  twitch.tv/popout/NovoUsuario/chat  ", "NovoUsuario")]
    public void Adicionar_TiraONomeDoTextoGuardaAsMaiusculasELimpaACaixa(string digitado, string nome)
    {
        var logica = Nova(new Opcoes());
        logica.NovoUsuario = digitado;

        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.Equal([nome], logica.ListaDeUsuarios);
        Assert.Equal("", logica.NovoUsuario);
        Assert.Null(logica.ErroNovoUsuario);
        Assert.False(logica.ListaVazia);
    }

    [Fact]
    public void Adicionar_RepetidoSemImportarMaiusculasNaoEntraEExplica()
    {
        var logica = Nova(ComListas(["Fulano"], []));
        logica.NovoUsuario = "fulano";

        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.Equal(["Fulano"], logica.ListaDeUsuarios);
        Assert.Equal("fulano já está na lista.", logica.ErroNovoUsuario);
        Assert.Equal("fulano", logica.NovoUsuario); // o texto fica na caixa para corrigir
    }

    [Theory]
    [InlineData("nome errado!")]
    [InlineData("nome com espaço")]
    [InlineData("çedilha")]
    [InlineData("um_nome_com_mais_de_25_letras")]
    public void Adicionar_NomeInvalidoNaoEntraEMostraADica(string digitado)
    {
        var logica = Nova(new Opcoes());
        logica.NovoUsuario = digitado;

        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.Empty(logica.ListaDeUsuarios);
        Assert.Equal(NomesDaTwitch.DicaNomeInvalido, logica.ErroNovoUsuario);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    public void Adicionar_VazioPedeONome(string digitado)
    {
        var logica = Nova(new Opcoes());
        logica.NovoBloqueado = digitado;

        logica.BloquearCommand.Execute(null);

        Assert.Empty(logica.UsuariosBloqueados);
        Assert.Equal("Digite o nome do usuário.", logica.ErroNovoBloqueado);
    }

    [Fact]
    public void Adicionar_DeuCertoApagaOErroAnterior()
    {
        var logica = Nova(new Opcoes());
        logica.NovoUsuario = "nome errado!";
        logica.AdicionarUsuarioCommand.Execute(null);
        Assert.NotNull(logica.ErroNovoUsuario);

        logica.NovoUsuario = "nome_certo";
        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.Null(logica.ErroNovoUsuario);
    }

    [Fact]
    public void Adicionar_OMesmoNomePodeEstarNasDuasListas()
    {
        var logica = Nova(ComListas(["Fulano"], []));
        logica.NovoBloqueado = "Fulano";

        logica.BloquearCommand.Execute(null);

        Assert.Equal(["Fulano"], logica.ListaDeUsuarios);
        Assert.Equal(["Fulano"], logica.UsuariosBloqueados);
        Assert.Null(logica.ErroNovoBloqueado);
    }

    // ----- Remover (decisão 1) -----

    [Fact]
    public void Remover_OXDeBloqueiosTiraONomeSoDosBloqueios()
    {
        var logica = Nova(ComListas(["Fulano", "Beltrano"], ["Fulano"]));

        logica.DesbloquearCommand.Execute("Fulano");

        Assert.Equal(["Fulano", "Beltrano"], logica.ListaDeUsuarios);
        Assert.Empty(logica.UsuariosBloqueados);
        Assert.True(logica.BloqueadosVazia);
    }

    [Fact]
    public void Remover_OXDaListaDeUsuariosTiraONomeSoDaLista()
    {
        var logica = Nova(ComListas(["Fulano", "Beltrano"], ["Fulano"]));

        logica.TirarUsuarioCommand.Execute("Fulano");

        Assert.Equal(["Beltrano"], logica.ListaDeUsuarios);
        Assert.Equal(["Fulano"], logica.UsuariosBloqueados);
    }

    // ----- Modo da lista -----

    [Theory]
    [InlineData(false, false, ModoDaLista.Nada)]
    [InlineData(true, false, ModoDaLista.Destacar)]
    [InlineData(false, true, ModoDaLista.SoALista)]
    [InlineData(true, true, ModoDaLista.SoALista)] // as duas no arquivo: vale "Mostrar só"
    public void Modo_LidoDasOpcoes(bool destacar, bool soALista, ModoDaLista esperado)
    {
        var logica = Nova(new Opcoes { DestacarUsuarios = destacar, SoUsuariosDaLista = soALista });

        Assert.Equal(esperado, logica.Modo);
        Assert.Equal(esperado == ModoDaLista.Nada, logica.ModoNada);
        Assert.Equal(esperado == ModoDaLista.Destacar, logica.ModoDestacar);
        Assert.Equal(esperado == ModoDaLista.SoALista, logica.ModoSoALista);
    }

    [Fact]
    public void Modo_MudaRotulosLinhasECoresVisiveis()
    {
        var logica = Nova(new Opcoes());
        Assert.False(logica.ModeradoresEVipsAtivos);
        Assert.False(logica.CorDoDestaque.Visivel);
        Assert.True(logica.CorDosModeradores.Visivel);

        logica.ModoDestacar = true;
        Assert.True(logica.ModeradoresEVipsAtivos);
        Assert.True(logica.CorDoDestaque.Visivel);
        Assert.Equal("Destacar todos os moderadores", logica.RotuloModeradores);

        logica.ModoSoALista = true;
        Assert.False(logica.ModoDestacar);
        Assert.False(logica.CorDoDestaque.Visivel);
        Assert.False(logica.CorDosModeradores.Visivel);
        Assert.False(logica.CorDosVips.Visivel);
        Assert.Equal("Mostrar também todos os moderadores", logica.RotuloModeradores);
        Assert.Equal("Mostrar também todos os VIPs", logica.RotuloVips);
    }

    // ----- Cores -----

    [Fact]
    public void Cor_SalvaEntreAsProntasApareceComOSeuNome()
    {
        var logica = Nova(new Opcoes { CorDoDestaque = Amarelo, CorDosModeradores = Verde });

        Assert.Equal("Amarelo", logica.CorDoDestaque.Escolhida.Nome);
        Assert.Equal("Verde", logica.CorDosModeradores.Escolhida.Nome);
        Assert.False(logica.CorDoDestaque.MostrarCaixa);
    }

    [Fact]
    public void Cor_SalvaForaDasProntasApareceComoPersonalizadaComOCodigo()
    {
        // Amarelo com outra transparência também não é "Amarelo"
        var cor = new EscolhaDeCor(Color.FromArgb(200, 0xF5, 0xF5, 0x00));

        Assert.Equal(CoresDeDestaque.NomeDaPersonalizada, cor.Escolhida.Nome);
        Assert.Equal("#F5F500", cor.TextoPersonalizado);
        Assert.True(cor.MostrarCaixa);
        Assert.Equal(Color.FromArgb(200, 0xF5, 0xF5, 0x00), cor.Cor);
    }

    [Theory]
    [InlineData("#12AB34")]
    [InlineData("12ab34")]
    [InlineData("  #12AB34 ")]
    public void CorPersonalizada_ValidaFicaTranslucida(string digitado)
    {
        var cor = new EscolhaDeCor(Amarelo);
        cor.Escolhida = cor.Personalizada;
        cor.TextoPersonalizado = digitado;

        Assert.Equal(Color.FromArgb(150, 0x12, 0xAB, 0x34), cor.Salvar());
        Assert.False(cor.MostrarErro);
        Assert.Equal(Color.FromArgb(150, 0x12, 0xAB, 0x34), cor.Personalizada.Amostra);
    }

    [Fact]
    public void CorPersonalizada_AMesmaCorDigitadaDeNovoMantemATransparencia()
    {
        var salva = Color.FromArgb(200, 0x12, 0xAB, 0x34);
        var cor = new EscolhaDeCor(salva);

        cor.TextoPersonalizado = "12AB34";

        Assert.Equal(salva, cor.Salvar());
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("verde")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void CorPersonalizada_InvalidaMostraOErroEFicaACorSalvaAntes(string digitado)
    {
        var cor = new EscolhaDeCor(Amarelo);
        cor.Escolhida = cor.Personalizada;
        cor.TextoPersonalizado = digitado;

        Assert.Equal(Amarelo, cor.Salvar());
        Assert.True(cor.MostrarErro);
        Assert.Null(cor.Personalizada.Amostra); // só o contorno
    }

    [Fact]
    public void CorPersonalizada_ErroAoSairDaCaixaSoComAlgoDigitadoESomeQuandoFicaValida()
    {
        var cor = new EscolhaDeCor(Amarelo);
        cor.Escolhida = cor.Personalizada;

        cor.SaiuDaCaixa();
        Assert.False(cor.MostrarErro); // vazia: nada ao sair

        cor.TextoPersonalizado = "#12";
        cor.SaiuDaCaixa();
        Assert.True(cor.MostrarErro);

        cor.TextoPersonalizado = "#123456";
        Assert.False(cor.MostrarErro);
    }

    [Fact]
    public void CorPersonalizada_ListaEscondidaNaoMostraCaixaNemErro()
    {
        var logica = Nova(new Opcoes { CorDosModeradores = Color.FromArgb(150, 1, 2, 3) });
        logica.CorDosModeradores.TextoPersonalizado = "#12";
        logica.ModoSoALista = true;

        logica.Salvar();

        Assert.False(logica.CorDosModeradores.MostrarCaixa);
        Assert.False(logica.CorDosModeradores.MostrarErro);
        Assert.Equal(Color.FromArgb(150, 1, 2, 3), logica.CorDosModeradores.Cor);
    }

    // ----- Mudanças -----

    [Fact]
    public void Mudancas_NadaMudouAoAbrir()
    {
        var logica = Nova(new Opcoes { DestacarUsuarios = true, ListaDeUsuarios = ["a"], CorDosVips = Color.FromArgb(10, 1, 2, 3) });
        Assert.False(logica.TemMudancas);
    }

    [Fact]
    public void Mudancas_MudarEVoltarNaoConta()
    {
        var logica = Nova(ComListas(["a"], []));

        logica.ModoDestacar = true;
        logica.DestacarVips = true;
        logica.TirarUsuarioCommand.Execute("a");
        Assert.True(logica.TemMudancas);

        logica.ModoNada = true;
        logica.DestacarVips = false;
        logica.NovoUsuario = "a";
        logica.AdicionarUsuarioCommand.Execute(null);
        Assert.False(logica.TemMudancas);
    }

    [Fact]
    public void Mudancas_CadaParteConta()
    {
        void Muda(Action<LogicaFiltros> mudar)
        {
            var logica = Nova(new Opcoes());
            mudar(logica);
            Assert.True(logica.TemMudancas);
        }

        Muda(l => l.ModoSoALista = true);
        Muda(l => l.DestacarModeradores = true);
        Muda(l => l.DestacarVips = true);
        Muda(l => { l.NovoUsuario = "fulano"; l.AdicionarUsuarioCommand.Execute(null); });
        Muda(l => { l.NovoBloqueado = "fulano"; l.BloquearCommand.Execute(null); });
        Muda(l => l.CorDoDestaque.Escolhida = l.CorDoDestaque.Itens[2]);
        Muda(l => l.CorDosModeradores.Escolhida = l.CorDosModeradores.Itens[0]);
        Muda(l => { l.CorDosVips.Escolhida = l.CorDosVips.Personalizada; l.CorDosVips.TextoPersonalizado = "#010203"; });
    }

    [Fact]
    public void Mudancas_NomeDigitadoENaoAdicionadoNaoConta()
    {
        var logica = Nova(new Opcoes());
        logica.NovoUsuario = "fulano";
        logica.NovoBloqueado = "beltrano";

        Assert.False(logica.TemMudancas);
    }

    [Fact]
    public void Mudancas_CorPersonalizadaInvalidaNaoConta()
    {
        var logica = Nova(new Opcoes());
        logica.CorDosVips.Escolhida = logica.CorDosVips.Personalizada;
        logica.CorDosVips.TextoPersonalizado = "#12";

        Assert.False(logica.TemMudancas); // continua valendo a cor salva
    }

    // ----- Salvar -----

    [Fact]
    public void Salvar_GravaTudoUmaVezEAvisa()
    {
        var opcoes = new Opcoes();
        var logica = Nova(opcoes);
        int avisos = 0;
        logica.Salvou += () => avisos++;

        logica.ModoDestacar = true;
        logica.DestacarModeradores = true;
        logica.NovoUsuario = "Fulano";
        logica.AdicionarUsuarioCommand.Execute(null);
        logica.NovoBloqueado = "bot_chato";
        logica.BloquearCommand.Execute(null);
        logica.CorDoDestaque.Escolhida = logica.CorDoDestaque.Itens.First(i => i.Nome == "Azul");
        logica.Salvar();

        Assert.True(opcoes.DestacarUsuarios);
        Assert.False(opcoes.SoUsuariosDaLista);
        Assert.True(opcoes.DestacarModeradores);
        Assert.False(opcoes.DestacarVips);
        Assert.Equal(["Fulano"], opcoes.ListaDeUsuarios);
        Assert.Equal(["bot_chato"], opcoes.UsuariosBloqueados);
        Assert.Equal(Color.FromArgb(150, 0x1E, 0x90, 0xFF), opcoes.CorDoDestaque);
        Assert.Equal(1, _gravacoes);
        Assert.Equal(1, avisos);
        Assert.False(logica.TemMudancas);
    }

    [Fact]
    public void Salvar_SoAListaNuncaJuntoComDestacar()
    {
        var opcoes = new Opcoes { DestacarUsuarios = true, SoUsuariosDaLista = true };
        var logica = Nova(opcoes);

        logica.Salvar();

        Assert.False(opcoes.DestacarUsuarios);
        Assert.True(opcoes.SoUsuariosDaLista);
    }

    [Fact]
    public void Salvar_ComNomeDigitadoENaoAdicionadoAdicionaAntes()
    {
        var opcoes = new Opcoes();
        var logica = Nova(opcoes);
        logica.NovoUsuario = "twitch.tv/Fulano";
        logica.NovoBloqueado = "@bot_chato";

        logica.Salvar();

        Assert.Equal(["Fulano"], opcoes.ListaDeUsuarios);
        Assert.Equal(["bot_chato"], opcoes.UsuariosBloqueados);
        Assert.Equal("", logica.NovoUsuario);
        Assert.Equal("", logica.NovoBloqueado);
    }

    [Fact]
    public void Salvar_ComNomeInvalidoDigitadoMostraOErroESalvaORestoMesmoAssim()
    {
        var opcoes = new Opcoes();
        var logica = Nova(opcoes);
        logica.DestacarVips = true;
        logica.NovoUsuario = "nome errado!";

        logica.Salvar();

        Assert.Equal(NomesDaTwitch.DicaNomeInvalido, logica.ErroNovoUsuario);
        Assert.Empty(opcoes.ListaDeUsuarios);
        Assert.True(opcoes.DestacarVips);
        Assert.Equal(1, _gravacoes);
    }

    [Fact]
    public void Salvar_ComCorInvalidaGuardaACorSalvaAntes()
    {
        var opcoes = new Opcoes { CorDosVips = Color.FromArgb(150, 0xDB, 0x33, 0xB3) };
        var logica = Nova(opcoes);
        logica.CorDosVips.Escolhida = logica.CorDosVips.Personalizada;
        logica.CorDosVips.TextoPersonalizado = "#12";

        logica.Salvar();

        Assert.Equal(Color.FromArgb(150, 0xDB, 0x33, 0xB3), opcoes.CorDosVips);
        Assert.True(logica.CorDosVips.MostrarErro);
    }

    [Fact]
    public void Salvar_NadaVaiParaAsOpcoesAntes()
    {
        var opcoes = new Opcoes();
        var logica = Nova(opcoes);

        logica.ModoSoALista = true;
        logica.NovoUsuario = "Fulano";
        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.False(opcoes.SoUsuariosDaLista);
        Assert.Empty(opcoes.ListaDeUsuarios);
        Assert.Equal(0, _gravacoes);
    }
}
