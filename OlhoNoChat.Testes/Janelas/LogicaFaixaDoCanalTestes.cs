using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaFaixaDoCanalTestes
{
    private readonly Opcoes _opcoes = new() { Canal = "hyfronx" };
    private readonly List<string> _trocas = [];
    private readonly List<string> _trocasDoYouTube = [];
    private readonly List<string> _perguntados = [];
    private bool _conectada;
    private bool? _existe = true;
    private readonly List<bool> _focos = [];

    private LogicaFaixaDoCanal Nova()
    {
        var logica = new LogicaFaixaDoCanal(() => _opcoes, () => _conectada,
            nome =>
            {
                _perguntados.Add(nome);
                return Task.FromResult(_existe);
            },
            (canal, youTube) =>
            {
                _trocas.Add(canal);
                _trocasDoYouTube.Add(youTube);
                _opcoes.Canal = canal;
                _opcoes.CanalDoYouTube = youTube;
            });
        logica.PedirFoco += selecionar => _focos.Add(selecionar);
        return logica;
    }

    [Fact]
    public void ComCanal_FechadaComONomeEODaTroca()
    {
        var logica = Nova();

        Assert.False(logica.EditorAberto);
        Assert.Equal("hyfronx", logica.CanalSalvo);
        Assert.Equal("Clique para trocar de canal.", logica.DicaDaFaixa);
    }

    [Fact]
    public void SemCanal_SempreAbertaComEntrarNoChat()
    {
        _opcoes.Canal = string.Empty;
        var logica = Nova();

        Assert.True(logica.EditorAberto);
        Assert.False(logica.TemCanal);
        logica.Esc();
        Assert.True(logica.EditorAberto); // sem canal, o Esc só volta a dica
    }

    [Fact]
    public void CanalGravadoComoLink_MostraSoONome()
    {
        _opcoes.Canal = "https://www.twitch.tv/Hyfronx/videos";
        Assert.Equal("Hyfronx", Nova().CanalSalvo);
    }

    [Fact]
    public void AbrirEditor_ComOCanalSelecionadoEOFoco()
    {
        var logica = Nova();
        logica.AbrirEditor();

        Assert.True(logica.EditorAberto);
        Assert.Equal("hyfronx", logica.Texto);
        Assert.Equal([true], _focos);

        logica.Esc();
        Assert.False(logica.EditorAberto);
    }

    [Theory]
    [InlineData("", "Digite o nome do canal.")]
    [InlineData("   @ ", "Digite o nome do canal.")]
    [InlineData("nome inválido!", "Use o nome como aparece no endereço do canal (twitch.tv/nome): só letras, números e _.")]
    public async Task NomeRuim_MostraOErroENaoTroca(string digitado, string erro)
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = digitado;

        await logica.ConfirmarAsync();

        Assert.Equal(erro, logica.Dica);
        Assert.True(logica.DicaEhErro);
        Assert.Empty(_trocas);
        Assert.Equal([true, false], _focos); // o foco volta para a caixa
    }

    [Fact]
    public async Task MesmoCanal_SoFechaSemRecarregar()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "@HyFronx";

        await logica.ConfirmarAsync();

        Assert.Empty(_trocas);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public async Task SemConta_TrocaSemPerguntarATwitch_EmMinusculas()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "twitch.tv/popout/OutroCanal/chat?popout=";

        await logica.ConfirmarAsync();

        Assert.Empty(_perguntados);
        Assert.Equal(["outrocanal"], _trocas);
        Assert.False(logica.EditorAberto);
        Assert.Equal("outrocanal", logica.Texto);
    }

    [Fact]
    public async Task ComConta_CanalQueNaoExiste_Avisa()
    {
        _conectada = true;
        _existe = false;
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "naoexiste123";

        await logica.ConfirmarAsync();

        Assert.Equal(["naoexiste123"], _perguntados);
        Assert.Empty(_trocas);
        Assert.Equal("Não achei o canal \"naoexiste123\" na Twitch. Confira o nome.", logica.Dica);
        Assert.False(logica.Procurando);
    }

    [Fact]
    public async Task ComConta_SemComoVerificar_SegueEmFrente()
    {
        _conectada = true;
        _existe = null;
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "outro";

        await logica.ConfirmarAsync();

        Assert.Equal(["outro"], _trocas);
    }

    [Fact]
    public async Task EnquantoProcura_MostraProcurandoEIgnoraOutraConfirmacao()
    {
        _conectada = true;
        var resposta = new TaskCompletionSource<bool?>();
        var logica = new LogicaFaixaDoCanal(() => _opcoes, () => true, _ => resposta.Task, (canal, _) => _trocas.Add(canal));
        logica.AbrirEditor();
        logica.Texto = "outro";

        Task primeira = logica.ConfirmarAsync();
        Assert.True(logica.Procurando);
        Assert.False(logica.PodeConfirmar);
        Assert.Equal("Procurando o canal…", logica.Dica);
        Assert.False(logica.DicaEhErro);
        await logica.ConfirmarAsync(); // ignorada

        resposta.SetResult(true);
        await primeira;
        Assert.Equal(["outro"], _trocas);
    }

    [Fact]
    public void Digitar_VoltaADicaNormal()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "x y";
        _ = logica.ConfirmarAsync();
        Assert.True(logica.DicaEhErro);

        logica.Texto = "xy";
        Assert.Equal(LogicaFaixaDoCanal.DicaPadrao, logica.Dica);
        Assert.False(logica.DicaEhErro);
    }

    [Fact]
    public void SairDoCanal_GravaVazioEAFaixaAbre()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.SairDoCanal();

        Assert.Equal([""], _trocas);
        Assert.True(logica.EditorAberto);
        Assert.False(logica.TemCanal);
    }

    [Fact]
    public void FaixaSumiu_OEditorFecha()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Atualizar(faixaNaTela: false);

        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public void Conexao_PontoEDica()
    {
        var logica = Nova();

        logica.PaginaComecouACarregar(paginaDeCanal: true);
        Assert.Equal(EstadoDaConexao.Conectando, logica.Conexao);
        Assert.Equal("Conectando ao chat de hyfronx…\nClique para trocar de canal.", logica.DicaDaFaixa);

        logica.PaginaAvisou(EstadoDaConexao.Conectado);
        Assert.Equal("Conectado ao chat de hyfronx.\nClique para trocar de canal.", logica.DicaDaFaixa);

        logica.PaginaAvisou(EstadoDaConexao.SemConexao);
        Assert.Equal("Sem conexão com o chat. Tentando de novo…\nClique para trocar de canal.", logica.DicaDaFaixa);

        logica.PaginaComecouACarregar(paginaDeCanal: false);
        Assert.Equal(EstadoDaConexao.Nenhum, logica.Conexao);
    }

    [Fact]
    public void Conexao_ChatOficialConectaAoCarregar_EFalhaFicaSemConexao()
    {
        var logica = Nova();

        logica.PaginaComecouACarregar(paginaDeCanal: true);
        logica.PaginaCarregou(sucesso: true, chatOficial: false);
        Assert.Equal(EstadoDaConexao.Conectando, logica.Conexao); // o Padrão avisa pela página

        logica.PaginaCarregou(sucesso: true, chatOficial: true);
        Assert.Equal(EstadoDaConexao.Conectado, logica.Conexao);

        logica.PaginaCarregou(sucesso: false, chatOficial: true);
        Assert.Equal(EstadoDaConexao.SemConexao, logica.Conexao);

        // Página sem canal que falha: continua sem ponto
        logica.PaginaComecouACarregar(paginaDeCanal: false);
        logica.PaginaCarregou(sucesso: false, chatOficial: false);
        Assert.Equal(EstadoDaConexao.Nenhum, logica.Conexao);
    }

    // --- Chat Multiplataforma: o canal do YouTube na mesma faixa ------------------------------------------

    [Fact]
    public void SemMultiplataforma_SemYouTubeNaFaixa()
    {
        _opcoes.CanalDoYouTube = "@hyfronx";
        var logica = Nova();

        Assert.False(logica.ComYouTube);
        Assert.False(logica.YouTubeNaFaixa);
        Assert.Equal(LogicaFaixaDoCanal.DicaPadrao, logica.Dica);
    }

    [Fact]
    public void ComMultiplataforma_MostraOYouTubeEADicaDosDois()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "https://www.youtube.com/@Hyfronx/live";
        var logica = Nova();

        Assert.True(logica.YouTubeNaFaixa);
        Assert.Equal("@Hyfronx", logica.YouTubeSalvo);
        Assert.Equal(LogicaFaixaDoCanal.DicaComYouTube, logica.Dica);

        logica.ConexaoDoYouTube = EstadoDoYouTube.EsperandoALive;
        Assert.Equal("YouTube: @Hyfronx não está ao vivo. Esperando a live começar…\nClique para trocar de canal.", logica.DicaDaFaixa);

        logica.PaginaAvisou(EstadoDaConexao.Conectado);
        logica.ConexaoDoYouTube = EstadoDoYouTube.Conectado;
        Assert.Equal("Twitch: Conectado ao chat de hyfronx.\nYouTube: lendo o chat da live de @Hyfronx.\nClique para trocar de canal.",
            logica.DicaDaFaixa);
    }

    [Fact]
    public async Task SoOYouTubeTrocado_TrocaComOMesmoCanalDaTwitch()
    {
        _opcoes.ChatMultiplataforma = true;
        _conectada = true;
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoYouTube = "youtube.com/@OutroCanal";

        await logica.ConfirmarAsync();

        Assert.Empty(_perguntados); // a Twitch não muda: não pergunta
        Assert.Equal(["hyfronx"], _trocas);
        Assert.Equal(["@OutroCanal"], _trocasDoYouTube);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public async Task YouTubeInvalido_MostraOErroENaoTroca()
    {
        _opcoes.ChatMultiplataforma = true;
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoYouTube = "não é um canal!";

        await logica.ConfirmarAsync();

        Assert.Equal(CanalDoYouTube.DicaInvalido, logica.Dica);
        Assert.True(logica.DicaEhErro);
        Assert.Empty(_trocas);
    }

    [Fact]
    public async Task YouTubeVazio_SoATwitch()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@hyfronx";
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoYouTube = "  ";

        await logica.ConfirmarAsync();

        Assert.Equal([""], _trocasDoYouTube);
        Assert.False(logica.YouTubeNaFaixa);
    }

    [Fact]
    public async Task MesmosCanais_SoFecha()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@hyfronx";
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoYouTube = "https://www.youtube.com/@hyfronx";

        await logica.ConfirmarAsync();

        Assert.Empty(_trocas);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public void SairDoCanal_ComMultiplataforma_TiraOsDois()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@hyfronx";
        var logica = Nova();
        logica.SairDoCanal();

        Assert.Equal([""], _trocas);
        Assert.Equal([""], _trocasDoYouTube);
    }
}
