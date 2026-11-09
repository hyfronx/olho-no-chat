using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaFaixaDoCanalTestes
{
    private readonly Opcoes _opcoes = new() { Canal = "hyfronx" };
    private readonly List<string> _trocas = [];
    private readonly List<string> _trocasDoYouTube = [];
    private readonly List<string> _trocasDaKick = [];
    private readonly List<string> _trocasDoTikTok = [];
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
            (canal, youTube, kick, tikTok) =>
            {
                _trocas.Add(canal);
                _trocasDoYouTube.Add(youTube);
                _trocasDaKick.Add(kick);
                _trocasDoTikTok.Add(tikTok);
                _opcoes.Canal = canal;
                _opcoes.CanalDoYouTube = youTube;
                _opcoes.CanalDaKick = kick;
                _opcoes.CanalDoTikTok = tikTok;
            });
        logica.PedirFoco += selecionar => _focos.Add(selecionar);
        return logica;
    }

    [Fact]
    public void ComCanal_FechadaComONome()
    {
        var logica = Nova();

        Assert.False(logica.EditorAberto);
        Assert.Equal("hyfronx", logica.CanalSalvo);
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
    [InlineData("nome inválido!","Use o nome como aparece no endereço do canal (twitch.tv/nome): só letras, números e _.")]
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
        var logica = new LogicaFaixaDoCanal(() => _opcoes, () => true, _ => resposta.Task, (canal, _, _, _) => _trocas.Add(canal));
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
    public void FaixaSumiu_OEditorFecha()
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Atualizar(faixaNaTela: false);

        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public void Conexao_Ponto()
    {
        var logica = Nova();

        logica.PaginaComecouACarregar(paginaDeCanal: true);
        Assert.Equal(EstadoDaConexao.Conectando, logica.Conexao);

        logica.PaginaAvisou(EstadoDaConexao.Conectado);
        Assert.Equal(EstadoDaConexao.Conectado, logica.Conexao);

        logica.PaginaAvisou(EstadoDaConexao.SemConexao);
        Assert.Equal(EstadoDaConexao.SemConexao, logica.Conexao);

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

        Assert.False(logica.Multiplataforma);
        Assert.False(logica.KickNaFaixa);
        Assert.False(logica.YouTubeNaFaixa);
        Assert.Equal(LogicaFaixaDoCanal.DicaPadrao, logica.Dica);
    }

    [Fact]
    public void ComMultiplataforma_MostraOYouTube()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "https://www.youtube.com/@Hyfronx/live";
        var logica = Nova();

        Assert.True(logica.YouTubeNaFaixa);
        Assert.Equal("@Hyfronx", logica.YouTubeSalvo);
        Assert.Equal(LogicaFaixaDoCanal.DicaMultiplataforma, logica.Dica);
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
    // --- Chat Multiplataforma: o canal da Kick na mesma faixa ---------------------------------------------

    [Fact]
    public void ComMultiplataforma_MostraAKick()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@Hyfronx";
        _opcoes.CanalDaKick = "gaules";
        var logica = Nova();

        Assert.True(logica.KickNaFaixa);
        Assert.True(logica.OutrosCanaisNaFaixa);
        Assert.Equal("gaules", logica.KickSalvo);
    }

    [Fact]
    public void SoAKick_SemYouTube()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDaKick = "gaules";
        var logica = Nova();

        Assert.False(logica.YouTubeNaFaixa);
        Assert.True(logica.OutrosCanaisNaFaixa);
    }

    [Fact]
    public void SemMultiplataforma_AKickSalvaNaoAparece()
    {
        _opcoes.CanalDaKick = "gaules";
        var logica = Nova();

        Assert.False(logica.KickNaFaixa);
        Assert.False(logica.OutrosCanaisNaFaixa);
    }

    [Fact]
    public async Task SoAKickTrocada_TrocaComOsMesmosOutros()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@hyfronx";
        _conectada = true;
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDaKick = "https://kick.com/Gaules/videos";

        await logica.ConfirmarAsync();

        Assert.Empty(_perguntados); // a Twitch não muda: não pergunta
        Assert.Equal(["hyfronx"], _trocas);
        Assert.Equal(["@hyfronx"], _trocasDoYouTube);
        Assert.Equal(["gaules"], _trocasDaKick);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public async Task KickInvalida_MostraOErroEPedeOFocoNela()
    {
        _opcoes.ChatMultiplataforma = true;
        var logica = Nova();
        int focos = 0;
        logica.PedirFocoNaKick += () => focos++;
        logica.AbrirEditor();
        logica.TextoDaKick = "não é um canal!";

        await logica.ConfirmarAsync();

        Assert.Equal(CanalDaKick.DicaInvalido, logica.Dica);
        Assert.True(logica.DicaEhErro);
        Assert.Equal(1, focos);
        Assert.Empty(_trocas);
    }

    [Fact]
    public async Task KickVazia_SemKick()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDaKick = "gaules";
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDaKick = "  ";

        await logica.ConfirmarAsync();

        Assert.Equal([""], _trocasDaKick);
        Assert.False(logica.KickNaFaixa);
    }
    // --- Chat Multiplataforma: o canal do TikTok na mesma faixa -------------------------------------------

    [Fact]
    public void ComMultiplataforma_MostraOTikTokSemOArroba()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoTikTok = "fulano.live";
        var logica = Nova();

        Assert.True(logica.TikTokNaFaixa);
        Assert.True(logica.OutrosCanaisNaFaixa);
        Assert.True(logica.VariosCanaisNaFaixa); // com a Twitch
        Assert.Equal("fulano.live", logica.TikTokSalvo);
    }

    [Fact]
    public void SemMultiplataforma_OTikTokSalvoNaoAparece()
    {
        _opcoes.CanalDoTikTok = "fulano";
        var logica = Nova();

        Assert.False(logica.TikTokNaFaixa);
        Assert.False(logica.OutrosCanaisNaFaixa);
    }

    [Fact]
    public void CaixaDoTikTok_MostraOSalvoComArroba()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoTikTok = "fulano";
        var logica = Nova();
        logica.AbrirEditor();

        Assert.Equal("@fulano", logica.TextoDoTikTok);
    }

    [Fact]
    public async Task SoOTikTokTrocado_PeloLinkDaLive()
    {
        _opcoes.ChatMultiplataforma = true;
        _conectada = true;
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoTikTok = "https://www.tiktok.com/@Fulano_1/live?lang=pt-BR";

        await logica.ConfirmarAsync();

        Assert.Empty(_perguntados); // a Twitch não muda: não pergunta
        Assert.Equal(["hyfronx"], _trocas);
        Assert.Equal(["fulano_1"], _trocasDoTikTok);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public async Task OMesmoTikTokComArroba_NaoTroca()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoTikTok = "fulano";
        var logica = Nova();
        logica.AbrirEditor();
        logica.TextoDoTikTok = "@Fulano";

        await logica.ConfirmarAsync();

        Assert.Empty(_trocas);
        Assert.False(logica.EditorAberto);
    }

    [Fact]
    public async Task TikTokInvalido_MostraOErroEPedeOFocoNele()
    {
        _opcoes.ChatMultiplataforma = true;
        var logica = Nova();
        int focos = 0;
        logica.PedirFocoNoTikTok += () => focos++;
        logica.AbrirEditor();
        logica.TextoDoTikTok = "vm.tiktok.com/ZMabc123";

        await logica.ConfirmarAsync();

        Assert.Equal(CanalDoTikTok.DicaInvalido, logica.Dica);
        Assert.True(logica.DicaEhErro);
        Assert.Equal(1, focos);
        Assert.Empty(_trocas);
    }

    [Fact]
    public async Task Multiplataforma_SemTwitch_ConectaSoOTikTok()
    {
        _opcoes.Canal = string.Empty;
        _opcoes.ChatMultiplataforma = true;
        var logica = Nova();
        logica.TextoDoTikTok = "fulano";

        await logica.ConfirmarAsync();

        Assert.Equal([""], _trocas);
        Assert.Equal(["fulano"], _trocasDoTikTok);
        Assert.True(logica.TemCanal);
        Assert.False(logica.VariosCanaisNaFaixa);
    }

    // --- Chat Multiplataforma: a Twitch é opcional ----------------------------------------------------------

    [Fact]
    public async Task Multiplataforma_SemTwitch_ConectaSoAKick()
    {
        _opcoes.Canal = string.Empty;
        _opcoes.ChatMultiplataforma = true;
        _conectada = true;
        var logica = Nova();
        Assert.True(logica.EditorAberto);
        logica.TextoDaKick = "gaules";

        await logica.ConfirmarAsync();

        Assert.Empty(_perguntados); // sem Twitch, nada a perguntar
        Assert.Equal([""], _trocas);
        Assert.Equal(["gaules"], _trocasDaKick);
        Assert.True(logica.TemCanal);
        Assert.False(logica.EditorAberto);
        Assert.False(logica.TwitchNaFaixa);
        Assert.False(logica.VariosCanaisNaFaixa); // só a Kick: o "Trocar canal" aparece inteiro
    }

    // Apagar todos e aplicar sai dos canais (pedido do usuário, 2026-11-06): nada fica conectado
    [Fact]
    public async Task Multiplataforma_ApagarTodos_SaiDeTodosOsCanais()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDoYouTube = "@seucanal";
        _opcoes.CanalDaKick = "seucanal";
        _opcoes.CanalDoTikTok = "seucanal";
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = "";
        logica.TextoDoYouTube = "";
        logica.TextoDaKick = "";
        logica.TextoDoTikTok = " ";

        await logica.ConfirmarAsync();

        Assert.Equal([""], _trocas);
        Assert.Equal([""], _trocasDoYouTube);
        Assert.Equal([""], _trocasDaKick);
        Assert.Equal([""], _trocasDoTikTok);
        Assert.False(logica.DicaEhErro);
        Assert.False(logica.TemCanal);
        Assert.True(logica.EditorAberto); // sem canal, a faixa fica aberta para entrar de novo
        Assert.Empty(_perguntados);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   @ ")]
    public async Task SoTwitch_ApagarOCanal_SaiDele(string digitado)
    {
        var logica = Nova();
        logica.AbrirEditor();
        logica.Texto = digitado;

        await logica.ConfirmarAsync();

        Assert.Equal([""], _trocas);
        Assert.False(logica.DicaEhErro);
        Assert.False(logica.TemCanal);
    }

    [Fact]
    public async Task SemCanalNenhum_AplicarVazio_NaoFazNada()
    {
        _opcoes.Canal = string.Empty;
        var logica = Nova();

        await logica.ConfirmarAsync();

        Assert.Empty(_trocas);
        Assert.False(logica.DicaEhErro);
        Assert.True(logica.EditorAberto);
    }

    [Fact]
    public void ExemploDaCaixaDaTwitch_DizQueEhOpcionalNoMultiplataforma()
    {
        var logica = Nova();
        Assert.Equal("Nome do canal da Twitch", logica.ExemploDaTwitch);

        _opcoes.ChatMultiplataforma = true;
        Assert.Equal("Canal da Twitch (nome ou link)", Nova().ExemploDaTwitch);
    }

    [Fact]
    public void VariosCanais_TrocarCanalSoComOIcone()
    {
        _opcoes.ChatMultiplataforma = true;
        _opcoes.CanalDaKick = "gaules";

        Assert.True(Nova().VariosCanaisNaFaixa);
    }
}
