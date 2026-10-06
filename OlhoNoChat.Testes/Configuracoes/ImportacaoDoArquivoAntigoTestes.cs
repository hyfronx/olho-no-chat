using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Configuracoes;

/// <summary>
/// O arquivo das versões até a 1.5 vira as opções novas sem perder nada: cada chave antiga vai para a opção certa,
/// com as migrações que o app antigo fazia ao abrir.
/// </summary>
public sealed class ImportacaoDoArquivoAntigoTestes
{
    private static ImportacaoDoArquivoAntigo.Resultado Converter(JsonObject valor, string? janela = null) =>
        ImportacaoDoArquivoAntigo.Converter(ArquivoAntigoFalso.Opcoes(valor), janela);

    [Fact]
    public void TodasAsChavesDiferentesDoPadrao_ViramAsOpcoesComOsMesmosValores()
    {
        var resultado = Converter(ArquivoAntigoFalso.TodasAsChavesDiferentesDoPadrao(),
                                  ArquivoAntigoFalso.Janela(527, 250, 437, 450, 2));
        var o = resultado.Opcoes;

        Assert.False(resultado.AntesDa1017);
        Assert.False(resultado.OpcoesIlegiveis);
        Assert.Equal("canalteste", o.Canal);
        Assert.True(o.ApagarMensagensAntigas);
        Assert.Equal("45", o.SegundosParaApagar);
        Assert.False(o.EsconderBots);
        Assert.True(o.EsconderGifs);
        Assert.True(o.EsconderOutrosCanais);
        Assert.Equal("coins.wav", o.SomDeMensagem);
        Assert.Equal(30, o.SegundosEntreSons);
        Assert.Equal(Opcoes.TemaNenhum, o.Tema);
        // Tipo "Chat oficial": não dá para saber de qual dos dois era o CSS, então vai para os dois
        Assert.Equal("body { color: red; }", o.CssDoTemaNenhum);
        Assert.Equal("body { color: red; }", o.CssDoEnderecoPersonalizado);
        Assert.Equal(".chat-line__message { color: blue; }", o.CssDoChatOficial);
        Assert.False(o.AparenciaPadraoNoChatOficial);
        Assert.Equal(1, o.TipoDeChat);
        Assert.Equal("https://exemplo.invalid/chat", o.EnderecoPersonalizado);
        Assert.Equal(1.35, o.TamanhoDoTexto);
        Assert.Equal(90, o.Fundo);
        Assert.True(o.EsconderBordasAoAbrir);
        Assert.True(o.EsconderIconeDaBarraDeTarefas);
        Assert.True(o.DestacarUsuarios);
        Assert.True(o.SoUsuariosDaLista);
        Assert.True(o.DestacarModeradores);
        Assert.True(o.DestacarVips);
        Assert.Equal(["Amigo1", "amiga2"], o.ListaDeUsuarios);
        Assert.Equal(["chato"], o.UsuariosBloqueados);
        Assert.True(o.MostrarResgates);
        Assert.Equal("121292674", o.Conta.Id);
        Assert.Equal("tokenfalso123", o.Conta.Token);
        Assert.Equal("hyfronx", o.Conta.Login);
        Assert.Equal("Hyfronx", o.Conta.NomeDeExibicao);
        Assert.True(o.CaixaDaTwitch);
        Assert.True(o.FecharCaixaDepoisDeEnviar);
        Assert.False(o.BetterTtv);
        Assert.False(o.Emotes7tv);
        Assert.False(o.MenuDeEmotesDoBetterTtv);
        Assert.False(o.FrankerFaceZ);
        Assert.False(o.ProcurarAtualizacoes);
        Assert.Equal("#FFF3A6", o.CorDoTexto);
        Assert.Equal("soft", o.ContornoDasLetras);
        Assert.Equal("Verdana", o.Fonte);
        Assert.True(o.MostrarHorario);
        Assert.Equal(Color.FromArgb(0x80, 0x11, 0x22, 0x33), o.CorDoDestaque);
        Assert.Equal(Color.FromArgb(0x90, 0x44, 0x55, 0x66), o.CorDosModeradores);
        Assert.Equal(Color.FromArgb(0xA0, 0x77, 0x88, 0x99), o.CorDosVips);
        Assert.Equal(0.35f, o.Volume);
        Assert.Equal("Fones (Audeze Maxwell)", o.NomeDaSaidaDeSom);
        Assert.Equal(2, o.SaidaDeSom);
        Assert.Equal(@"C:\Sons", o.PastaDosSons);
        Assert.Equal(new Atalho(Key.F10, ModifierKeys.Control | ModifierKeys.Shift), o.AtalhoBordas);
        Assert.Equal(new Atalho(Key.A, ModifierKeys.Alt), o.AtalhoModoRolagem);
        Assert.False(o.SempreNoTopo);
        Assert.Null(o.AtalhoSempreNoTopo); // apagado pela pessoa: continua sem atalho
        Assert.Equal(new Atalho(Key.F11, ModifierKeys.Control), o.AtalhoEscrever);
        Assert.True(o.PermitirVariasCopias);
        Assert.Equal(new PosicaoDaJanela(527, 250, 437, 450, WindowState.Maximized), o.Janela);
    }

    // Se aparecer uma opção nova no modelo, este teste lembra de ligar ela a uma chave do arquivo antigo
    [Fact]
    public void TodasAsChavesDiferentesDoPadrao_NenhumaOpcaoFicaComOPadrao()
    {
        var convertidas = Converter(ArquivoAntigoFalso.TodasAsChavesDiferentesDoPadrao(),
                                    ArquivoAntigoFalso.Janela(527, 250, 437, 450, 0)).Opcoes;
        var padrao = new Opcoes();

        var iguais = typeof(Opcoes).GetProperties()
            .Where(p => p.Name != nameof(Opcoes.Versao))
            // Nasceram depois do arquivo antigo: ficam com o padrão
            .Where(p => p.Name is not (nameof(Opcoes.EsconderTituloDoChat) or nameof(Opcoes.EsconderPlacarDoTopo) or nameof(Opcoes.EsconderDestaques)
                or nameof(Opcoes.ChatMultiplataforma) or nameof(Opcoes.CanalDoYouTube) or nameof(Opcoes.MostrarHistoricoDoYouTube)
                or nameof(Opcoes.CanalDaKick)))
            .Where(p => JsonSerializer.Serialize(p.GetValue(convertidas), ArquivoDeConfiguracoes.Formato)
                        == JsonSerializer.Serialize(p.GetValue(padrao), ArquivoDeConfiguracoes.Formato))
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(iguais);

        var contaIgual = typeof(ContaGravada).GetProperties()
            .Where(p => Equals(p.GetValue(convertidas.Conta), p.GetValue(padrao.Conta)))
            .Select(p => p.Name);
        Assert.Empty(contaIgual);
    }

    [Fact]
    public void ArquivoDoUsuario_ComOsValoresDeVerdade()
    {
        // O arquivo de uma 1.3.4 de verdade (o do desenvolvedor, sem o token), com os tipos gravados pelo Jot
        string texto = """
            [
              {
                "Type": "OlhoNoChat.GeneralSettings, OlhoNoChat, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "Name": "GeneralSettings",
                "Value": {
                  "Username": "zarvohk", "FadeChat": false, "FadeTime": "120", "BlockBotActivity": true, "HideGifs": false,
                  "HideOtherChannels": false, "ChatNotificationSound": "job-done.wav", "ChatSoundQuietSeconds": 0, "ThemeIndex": 1,
                  "CustomCSS": "body { background-color: rgba(0, 0, 0, 0); margin: 0px auto; overflow: hidden; }",
                  "TwitchPopoutCSS": "", "UseDefaultTwitchPopoutCSS": true, "ChatType": 0, "CustomURL": "", "ZoomLevel": 0.8,
                  "OpacityLevel": 125, "AutoHideBorders": false, "HideTaskbarIcon": true, "AllowInteraction": true,
                  "HighlightUsersChat": false, "AllowedUsersOnlyChat": false, "FilterAllowAllMods": false, "FilterAllowAllVIPs": false,
                  "AllowedUsersList": [], "BlockedUsersList": [], "RedemptionsEnabled": false, "ChannelID": "121292674",
                  "OAuthToken": "", "TwitchLogin": "hyfronx", "TwitchDisplayName": "Hyfronx", "UseTwitchChatBox": false,
                  "CloseChatBoxAfterSend": false, "BetterTtv": true, "BetterTtv_7tv": true, "BetterTtv_AdvEmoteMenu": true,
                  "FrankerFaceZ": true, "CheckForUpdates": true, "ChatMessageColor": "", "ChatTextOutline": "theme",
                  "ChatFontFamily": "theme", "ShowMessageTime": false, "ChatHighlightColor": "#96F5F500",
                  "ChatHighlightModsColor": "#9600AD03", "ChatHighlightVIPsColor": "#96DB33B3", "OutputVolume": 0.5,
                  "DeviceName": "Default", "DeviceID": -1, "SoundClipsFolder": "Default",
                  "ToggleBordersHotkey": { "Key": 98, "Modifiers": 3 }, "ToggleInteractableHotkey": { "Key": 96, "Modifiers": 3 },
                  "AlwaysOnTop": true, "BringToTopHotkey": { "Key": 97, "Modifiers": 3 },
                  "WriteMessageHotkey": { "Key": 100, "Modifiers": 3 }, "AllowMultipleInstances": false
                }
              }
            ]
            """;
        var o = ImportacaoDoArquivoAntigo.Converter(texto, ArquivoAntigoFalso.Janela(527, 250, 437, 450, 0)).Opcoes;

        Assert.Equal("zarvohk", o.Canal);
        Assert.Equal(125, o.Fundo);
        Assert.Equal(0.8, o.TamanhoDoTexto);
        Assert.True(o.EsconderIconeDaBarraDeTarefas);
        Assert.Equal("theme", o.ContornoDasLetras);
        Assert.Equal(0.5f, o.Volume);
        Assert.Equal("Default", o.NomeDaSaidaDeSom);
        // Tipo "Padrão" com o tema "Padrão": não se sabe de qual era o CSS, fica nos dois
        Assert.Equal("body { background-color: rgba(0, 0, 0, 0); margin: 0px auto; overflow: hidden; }", o.CssDoTemaNenhum);
        Assert.Equal(o.CssDoTemaNenhum, o.CssDoEnderecoPersonalizado);
        // Sem token não há conta (o app antigo também não a considerava conectada)
        Assert.Equal("", o.Conta.Login);
        Assert.Equal(new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt), o.AtalhoBordas);
        Assert.Equal(new PosicaoDaJanela(527, 250, 437, 450, WindowState.Normal), o.Janela);
    }

    [Fact]
    public void ChavesFaltando_FicamComOPadrao()
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("Username", "canal"), ("FadeChat", true))).Opcoes;

        Assert.Equal("canal", o.Canal);
        Assert.True(o.ApagarMensagensAntigas);
        string semOsDois(Opcoes x)
        {
            x.Canal = ""; x.ApagarMensagensAntigas = false;
            return ArquivoDeConfiguracoes.ParaJson(x);
        }
        Assert.Equal(semOsDois(new Opcoes()), semOsDois(o));
        Assert.Null(o.Janela);
    }

    [Fact]
    public void ChavesDesconhecidas_SaoIgnoradas()
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("Username", "canal"), ("VersionTracker", "1.0.1"),
            ("CustomWindows", new JsonArray(new JsonObject { ["x"] = 1 })), ("jChatURL", "https://x.invalid"),
            ("EnableTrayIcon", true))).Opcoes;

        Assert.Equal("canal", o.Canal);
        Assert.Equal(new Opcoes().EsconderBots, o.EsconderBots);
    }

    [Fact]
    public void ValorQueNaoDaParaLer_FicaComOPadraoDaquelaChave()
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("Username", "canal"), ("OpacityLevel", 999), ("ZoomLevel", "grande"),
            ("ChatType", 1.5), ("ChatHighlightColor", "não é cor"), ("ToggleBordersHotkey", "F9"),
            ("AllowedUsersList", null), ("FadeChat", "sim"))).Opcoes;

        var padrao = new Opcoes();
        Assert.Equal("canal", o.Canal);
        Assert.Equal(padrao.Fundo, o.Fundo);
        Assert.Equal(padrao.TamanhoDoTexto, o.TamanhoDoTexto);
        Assert.Equal(padrao.TipoDeChat, o.TipoDeChat);
        Assert.Equal(padrao.CorDoDestaque, o.CorDoDestaque);
        Assert.Equal(padrao.AtalhoBordas, o.AtalhoBordas);
        Assert.Empty(o.ListaDeUsuarios);
        Assert.False(o.ApagarMensagensAntigas);
    }

    [Fact]
    public void TiposTrocados_SaoLidosComoOAppAntigoLia()
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("FadeTime", 90), ("OpacityLevel", "120"), ("ZoomLevel", "1.5"),
            ("FadeChat", "true"), ("ChatType", 2.0))).Opcoes;

        Assert.Equal("90", o.SegundosParaApagar);
        Assert.Equal(120, o.Fundo);
        Assert.Equal(1.5, o.TamanhoDoTexto);
        Assert.True(o.ApagarMensagensAntigas);
        Assert.Equal(2, o.TipoDeChat);
    }

    [Theory]
    [InlineData("https://www.twitch.tv/AmigoTeste", "amigoteste")]
    [InlineData("@OutroCanal", "outrocanal")]
    [InlineData("nome com espaço", "")]
    public void AntesDa1017_SoOCanalFica(string canalSalvo, string esperado)
    {
        // Sem "WriteMessageHotkey": um app muito diferente (ex.: a 1.0.1). Tudo o mais começa como na primeira instalação.
        var valor = new JsonObject
        {
            ["Username"] = canalSalvo, ["ZoomLevel"] = 1.5, ["OpacityLevel"] = 30, ["ChatType"] = 2,
            ["CustomURL"] = "http://exemplo.invalid", ["AutoHideBorders"] = true, ["ThemeIndex"] = 4,
        };

        var resultado = Converter(valor, ArquivoAntigoFalso.Janela(10, 10, 300, 300, 0));

        Assert.True(resultado.AntesDa1017);
        Assert.Equal(esperado, resultado.Opcoes.Canal);
        var comOCanalDoPadrao = resultado.Opcoes;
        comOCanalDoPadrao.Canal = "";
        Assert.Equal(ArquivoDeConfiguracoes.ParaJson(new Opcoes()), ArquivoDeConfiguracoes.ParaJson(comOCanalDoPadrao));
    }

    [Fact]
    public void AntesDa1017_ComOTipoDoAppOriginal()
    {
        string texto = ArquivoAntigoFalso.Opcoes(new JsonObject { ["Username"] = "amigo" })
            .Replace("OlhoNoChat.GeneralSettings, OlhoNoChat", "TransparentTwitchChatWPF.GeneralSettings, TransparentTwitchChatWPF");

        var resultado = ImportacaoDoArquivoAntigo.Converter(texto, null);

        Assert.True(resultado.AntesDa1017);
        Assert.Equal("amigo", resultado.Opcoes.Canal);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(7, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void Migracao_TemasQueNaoExistemMaisViramOPadrao(int salvo, int esperado)
    {
        Assert.Equal(esperado, Converter(ArquivoAntigoFalso.Minimo(("ThemeIndex", salvo))).Opcoes.Tema);
    }

    [Theory]
    [InlineData(3, 0)] // jCyan
    [InlineData(9, 0)]
    [InlineData(2, 2)]
    public void Migracao_TipoDeChatQueNaoExisteMaisViraOPadrao(int salvo, int esperado)
    {
        Assert.Equal(esperado, Converter(ArquivoAntigoFalso.Minimo(("ChatType", salvo))).Opcoes.TipoDeChat);
    }

    [Theory]
    [InlineData("Default", "Alert 2 (Low).wav", "job-done.wav")]
    [InlineData("Default", "Alert 1.wav", "job-done.wav")]
    [InlineData(@"C:\MeusSons", "Alert 2 (Low).wav", "Alert 2 (Low).wav")]
    [InlineData("Default", "coins.wav", "coins.wav")]
    [InlineData("Default", "None", "None")]
    public void Migracao_SonsAlertDoAppViramOPadraoNovo(string pasta, string som, string esperado)
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("SoundClipsFolder", pasta), ("ChatNotificationSound", som))).Opcoes;
        Assert.Equal(esperado, o.SomDeMensagem);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Migracao_TamanhoDoTextoZeradoVolta80(double salvo)
    {
        Assert.Equal(0.8, Converter(ArquivoAntigoFalso.Minimo(("ZoomLevel", salvo))).Opcoes.TamanhoDoTexto);
    }

    [Theory]
    [InlineData(2, 1, "css", "", "css")] // Endereço personalizado: só a opção do endereço
    [InlineData(0, 0, "css", "css", "")] // Padrão com tema "Nenhum": só a do tema
    [InlineData(0, 1, "css", "css", "css")] // os outros casos: as duas
    [InlineData(1, 0, "css", "css", "css")]
    [InlineData(3, 0, "css", "css", "css")]
    public void CssUnico_VaiParaAOpcaoCertaPeloTipoSalvo(int tipo, int tema, string css, string doTema, string doEndereco)
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("ChatType", tipo), ("ThemeIndex", tema), ("CustomCSS", css))).Opcoes;

        Assert.Equal(doTema, o.CssDoTemaNenhum);
        Assert.Equal(doEndereco, o.CssDoEnderecoPersonalizado);
    }

    [Fact]
    public void AtalhoSemTecla_ContinuaSemAtalho()
    {
        var o = Converter(ArquivoAntigoFalso.Minimo(("ToggleInteractableHotkey", ArquivoAntigoFalso.Atalho(0, 0)))).Opcoes;

        Assert.False(Atalho.Existe(o.AtalhoModoRolagem));
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("")]
    [InlineData("{\"Username\": \"canal\"}")]
    public void ArquivoIlegivel_FicamOsPadroes(string texto)
    {
        var resultado = ImportacaoDoArquivoAntigo.Converter(texto, ArquivoAntigoFalso.Janela(1, 2, 300, 400, 0));

        Assert.True(resultado.OpcoesIlegiveis);
        Assert.Equal("", resultado.Opcoes.Canal);
        Assert.Equal(new PosicaoDaJanela(1, 2, 300, 400, WindowState.Normal), resultado.Opcoes.Janela); // a janela era outro arquivo
    }

    [Fact]
    public void JanelaIlegivel_FicaSemPosicao()
    {
        var resultado = ImportacaoDoArquivoAntigo.Converter(ArquivoAntigoFalso.Opcoes(ArquivoAntigoFalso.Minimo(("Username", "c"))), "[{");

        Assert.False(resultado.OpcoesIlegiveis);
        Assert.Equal("c", resultado.Opcoes.Canal);
        Assert.Null(resultado.Opcoes.Janela);
    }

    [Fact]
    public void SoOArquivoDaJanela_OpcoesDoPadraoEAPosicao()
    {
        var resultado = ImportacaoDoArquivoAntigo.Converter(null, ArquivoAntigoFalso.Janela(-1900, 100, 360, 530, 1));

        Assert.False(resultado.OpcoesIlegiveis);
        Assert.Equal(new PosicaoDaJanela(-1900, 100, 360, 530, WindowState.Minimized), resultado.Opcoes.Janela);
    }
}
