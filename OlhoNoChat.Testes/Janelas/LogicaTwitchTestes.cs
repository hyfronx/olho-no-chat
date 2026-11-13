using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Testes.Twitch;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>
/// A aba Twitch sem a tela: o aviso de conectar só sem conta, a dica da conta com o Chat Multiplataforma, a cor da
/// situação do AutoMod e "Fechar a caixa depois de enviar" só com atalho salvo.
/// </summary>
public sealed class LogicaTwitchTestes
{
    // A aba com a conta salva dada (sem conferir na Twitch: as permissões ficam como "não conferidas")
    private static LogicaTwitch Aba(IContaSalva salva)
    {
        ContaDaTwitch conta = Montar.Conta(new TwitchFalsa(), salva);
        return new LogicaTwitch(new AutorizacaoNoNavegador(NullLogger<AutorizacaoNoNavegador>.Instance), conta,
            new EventSubDaTwitch(new ApiDaTwitch(new TwitchFalsa()), conta, NullLogger<EventSubDaTwitch>.Instance));
    }

    // ----- Aviso de conectar -----

    [Fact]
    public void SemConta_MostraOAvisoDeConectar()
    {
        LogicaTwitch aba = Aba(new ContaSalvaNaMemoria());

        Assert.True(aba.MostrarConectar);
        Assert.True(aba.MostrarAvisoDeConectar);
    }

    [Fact]
    public void ContaConectada_EscondeOAvisoDeConectar()
    {
        LogicaTwitch aba = Aba(Montar.ContaConectada());

        Assert.True(aba.MostrarDesconectar);
        Assert.False(aba.MostrarConectar);
        Assert.False(aba.MostrarAvisoDeConectar);
    }

    // ----- Dica da conta com o Chat Multiplataforma -----

    [Fact]
    public void ContaConectada_NoMultiplataformaADicaNaoPrometeEscreverNemResgates()
    {
        LogicaTwitch aba = Aba(Montar.ContaConectada());
        string semMultiplataforma = aba.DicaDaConta;
        Assert.Contains("escrever no chat", semMultiplataforma);

        aba.MultiplataformaNaTela = true;

        Assert.Equal("Você pode moderar e aprovar as mensagens barradas pelo AutoMod, e ver os anúncios do seu canal.", aba.DicaDaConta);
        Assert.Equal("Conectado como Hyfronx", aba.TituloDaConta);

        aba.MultiplataformaNaTela = false;

        Assert.Equal(semMultiplataforma, aba.DicaDaConta);
    }

    [Fact]
    public void SemConta_NoMultiplataformaADicaSoFalaDoAutoMod()
    {
        LogicaTwitch aba = Aba(new ContaSalvaNaMemoria());
        Assert.Contains("escrever no chat", aba.DicaDaConta);

        aba.MultiplataformaNaTela = true;

        Assert.Equal("Para só ler o chat, não precisa conectar. Conecte para moderar, aprovar as mensagens barradas pelo AutoMod e ver os anúncios do seu canal.",
            aba.DicaDaConta);
    }

    // ----- Situação do AutoMod -----

    [Theory]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.Desligado, false)]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.Funcionando, false)]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.NaoModera, true)]
    [InlineData(EventSubDaTwitch.SituacaoDoAutoMod.Falhou, true)]
    public void SituacaoDoAutoMod_LaranjaSoNosProblemas(EventSubDaTwitch.SituacaoDoAutoMod situacao, bool problema)
    {
        Assert.Equal(problema, LogicaTwitch.SituacaoComProblema(situacao));
    }

    // ----- Fechar a caixa depois de enviar -----
}
