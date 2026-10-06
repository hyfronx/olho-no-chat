using OlhoNoChat.Kick;

namespace OlhoNoChat.Testes.Kick;

public class CanalDaKickTestes
{
    [Theory]
    [InlineData("gaules", "gaules")]
    [InlineData("  Gaules ", "gaules")]
    [InlineData("@Gaules", "gaules")]
    [InlineData("kick.com/gaules", "gaules")]
    [InlineData("https://kick.com/Gaules", "gaules")]
    [InlineData("https://www.kick.com/gaules/videos?sort=date", "gaules")]
    [InlineData("https://kick.com/nome-com-traco/", "nome-com-traco")]
    [InlineData("marCelo_mga", "marcelo-mga")]
    public void Le_ONomeDoEndereco(string digitado, string esperado)
    {
        Assert.Equal(esperado, CanalDaKick.Ler(digitado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("nome com espaço")]
    [InlineData("não!")]
    [InlineData("https://kick.com/")]
    [InlineData("um_nome_grande_demais_para_a_kick")]
    public void NaoEntende_Nulo(string? digitado)
    {
        Assert.Null(CanalDaKick.Ler(digitado));
    }
}
