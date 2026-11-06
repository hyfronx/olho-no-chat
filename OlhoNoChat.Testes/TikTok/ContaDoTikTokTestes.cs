using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Sistema;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>Cada teste com a sua pasta do cofre, apagada no fim.</summary>
public abstract class ComPastaDoCofre : IDisposable
{
    protected readonly string Pasta = Path.Combine(Path.GetTempPath(), "OncCofreTeste-" + Guid.NewGuid().ToString("N"));
    protected static readonly SessaoDoTikTok Exemplo = new("abc123sessaofalsa0000000000000000", "fulano", "7329124766361797638");
    private static readonly CngProvider Tpm = new("Microsoft Platform Crypto Provider");

    protected static string NomeDeChave() => "OlhoNoChat.Teste." + Guid.NewGuid().ToString("N");

    protected static bool ChaveExiste(string nome)
    {
        try
        {
            return CngKey.Exists(nome, Tpm);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Pasta, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

/// <summary>O cofre da sessão do TikTok: camadas de proteção, arquivo que não abre mexido, apagar e as permissões da pasta.</summary>
public class CofreDoTikTokTestes : ComPastaDoCofre
{
    [Fact]
    public void GuardaEAbre_ComOTpm()
    {
        var cofre = new CofreDoTikTok(Pasta, NomeDeChave());
        try
        {
            ProtecaoDaSessao guardou = cofre.Guardar(Exemplo);
            Assert.Equal(Exemplo, cofre.Abrir(out ProtecaoDaSessao abriu));
            Assert.Equal(guardou, abriu);
        }
        finally
        {
            cofre.Apagar();
        }
    }

    [Fact]
    public void GuardaEAbre_SemTpm()
    {
        var cofre = new CofreDoTikTok(Pasta, "x", usarTpm: false);
        Assert.Equal(ProtecaoDaSessao.Windows, cofre.Guardar(Exemplo));
        Assert.Equal(Exemplo, cofre.Abrir());
    }

    [Fact]
    public void OArquivo_NaoTemASessaoAsClaras()
    {
        var cofre = new CofreDoTikTok(Pasta, "x", usarTpm: false);
        cofre.Guardar(Exemplo);
        string bruto = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(cofre.Caminho));
        Assert.DoesNotContain("abc123", bruto);
        Assert.DoesNotContain("fulano", bruto);
    }

    [Fact]
    public void ArquivoMexido_NaoAbre()
    {
        var cofre = new CofreDoTikTok(Pasta, "x", usarTpm: false);
        cofre.Guardar(Exemplo);
        byte[] bytes = File.ReadAllBytes(cofre.Caminho);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(cofre.Caminho, bytes);
        Assert.Null(cofre.Abrir());
    }

    [Fact]
    public void SemAChaveDoTpm_NaoAbre()
    {
        string nome = NomeDeChave();
        var cofre = new CofreDoTikTok(Pasta, nome);
        if (cofre.Guardar(Exemplo) != ProtecaoDaSessao.WindowsETpm)
            return; // computador sem TPM: nada a conferir aqui
        CngKey.Open(nome, new CngProvider("Microsoft Platform Crypto Provider")).Delete();
        Assert.Null(cofre.Abrir());
        cofre.Apagar();
    }

    [Fact]
    public void Apagar_TiraOArquivoEAChave()
    {
        string nome = NomeDeChave();
        var cofre = new CofreDoTikTok(Pasta, nome);
        cofre.Guardar(Exemplo);
        cofre.Apagar();
        Assert.False(File.Exists(cofre.Caminho));
        Assert.Null(cofre.Abrir());
        Assert.False(ChaveExiste(nome));
    }

    [Fact]
    public void APasta_ESoDoUsuario()
    {
        new CofreDoTikTok(Pasta, "x", usarTpm: false).Guardar(Exemplo);
        string usuario = WindowsIdentity.GetCurrent().User!.Value;
        Assert.False(PastaProtegida.Herda(Pasta));
        Assert.Equal([usuario], PastaProtegida.QuemEntra(Pasta));
        var regras = new FileInfo(Path.Combine(Pasta, "conta.bin")).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>()
            .Select(r => r.IdentityReference.Value).Distinct().ToList();
        Assert.Equal([usuario], regras);
    }
}

/// <summary>A conta do TikTok: guardar, desconectar e a sessão recusada pelo TikTok.</summary>
public class ContaDoTikTokTestes : ComPastaDoCofre
{
    private ContaDoTikTok Nova() => new(NullLogger.Instance, new CofreDoTikTok(Pasta, "x", usarTpm: false));

    [Fact]
    public void Guardar_ValeNaHora_EVoltaAoAbrirDeNovo()
    {
        ContaDoTikTok conta = Nova();
        int mudancas = 0;
        conta.Mudou += () => mudancas++;
        conta.Guardar(Exemplo);

        Assert.Equal("abc123sessaofalsa0000000000000000", conta.Sessao);
        Assert.Equal(1, mudancas);
        Assert.Equal(Exemplo, Nova().Conta);
    }

    [Fact]
    public void Desconectar_ApagaASessao()
    {
        ContaDoTikTok conta = Nova();
        conta.Guardar(Exemplo);
        conta.Desconectar();

        Assert.Null(conta.Sessao);
        Assert.False(conta.FoiRecusada);
        Assert.Null(Nova().Conta);
    }

    [Fact]
    public void SessaoRecusada_ApagaEMarca_ConectarDeNovoTiraAMarca()
    {
        ContaDoTikTok conta = Nova();
        conta.Guardar(Exemplo);
        conta.SessaoRecusada();

        Assert.Null(conta.Sessao);
        Assert.True(conta.FoiRecusada);
        Assert.Null(Nova().Conta);

        conta.Guardar(Exemplo);
        Assert.False(conta.FoiRecusada);
    }

    [Fact]
    public void SemConta_DesconectarNaoAvisa()
    {
        ContaDoTikTok conta = Nova();
        int mudancas = 0;
        conta.Mudou += () => mudancas++;
        conta.Desconectar();
        conta.SessaoRecusada();
        Assert.Equal(0, mudancas);
    }
}

/// <summary>Onde a janela de login do TikTok pode ir.</summary>
public class EnderecosDoLoginTestes
{
    [Theory]
    [InlineData("https://www.tiktok.com/login", true)]
    [InlineData("https://tiktok.com/login/qrcode", true)]
    [InlineData("https://accounts.google.com/o/oauth2/auth?x=1", true)]
    [InlineData("https://www.facebook.com/dialog/oauth", true)]
    [InlineData("https://appleid.apple.com/auth/authorize", true)]
    [InlineData("https://accounts.youtube.com/accounts/SetSID", true)]
    [InlineData("https://accounts.google.com.br/accounts/SetSID", true)]
    [InlineData("https://accounts.google.co.uk/accounts/SetSID", true)]
    [InlineData("https://accounts.google.de/accounts/SetSID", true)]
    [InlineData("https://www.google.com/accounts/", true)]
    [InlineData("https://idmsa.apple.com/appleauth", true)]
    [InlineData("https://m.facebook.com/login", true)]
    [InlineData("https://accounts.google.abc.xy/", false)]
    [InlineData("https://accounts.google.golpe.com/", false)]
    [InlineData("https://accounts.googl.com/", false)]
    [InlineData("https://www.youtube.com/watch", false)]
    [InlineData("https://usuario@www.tiktok.com/login", false)]
    [InlineData("http://www.tiktok.com/login", false)]
    [InlineData("https://www.tiktok.com:8443/login", false)]
    [InlineData("https://tiktok.com.golpe.com/login", false)]
    [InlineData("https://golpetiktok.com/login", false)]
    [InlineData("https://www.tiktok.com@golpe.com/", false)]
    [InlineData("https://exemplo.com/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void SoOTikTokEOsLoginsDele(string endereco, bool permitido) =>
        Assert.Equal(permitido, EnderecosDoLogin.Permitido(endereco));
}
