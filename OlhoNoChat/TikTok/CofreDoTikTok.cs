using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.TikTok;

/// <summary>A conta do TikTok conectada (opção da aba TikTok): a sessão (o cookie "sessionid"), o @ e o id da conta.</summary>
public sealed record SessaoDoTikTok(string Sessao, string Login, string IdDoUsuario);

/// <summary>Como a conta está protegida no arquivo.</summary>
public enum ProtecaoDaSessao
{
    /// <summary>Windows (DPAPI) e o chip de segurança do computador (TPM).</summary>
    WindowsETpm = 1,

    /// <summary>Só o Windows (DPAPI): o computador não tem TPM, ou ele não respondeu.</summary>
    Windows = 2,
}

/// <summary>
/// Guarda a conta do TikTok no computador, em camadas:
/// <list type="number">
/// <item>os dados são cifrados com AES-256-GCM, com uma chave nova a cada vez;</item>
/// <item>essa chave é cifrada por uma chave RSA criada DENTRO do chip de segurança (TPM) do computador, que não sai de
/// lá (não dá para copiar nem exportar): o arquivo só abre neste computador;</item>
/// <item>tudo isso ainda passa pela proteção do Windows (DPAPI) do usuário: outro usuário do PC, ou o arquivo levado
/// para outro PC, não abre;</item>
/// <item>o arquivo fica numa pasta que só o usuário do Windows abre (<see cref="PastaProtegida"/>), é gravado de uma
/// vez (nunca fica pela metade) e, ao desconectar, é sobrescrito antes de apagar, junto com a chave do TPM.</item>
/// </list>
/// Sem TPM, ficam só as camadas do Windows (<see cref="ProtecaoDaSessao.Windows"/>).
/// </summary>
public sealed class CofreDoTikTok
{
    // Provedor de chaves do Windows que guarda as chaves no TPM
    private static readonly CngProvider ProvedorDoTpm = new("Microsoft Platform Crypto Provider");
    private static readonly byte[] Complemento = Encoding.UTF8.GetBytes("OlhoNoChat.SessaoDoTikTok.v1");
    private const byte Versao = 1;
    private const byte OaepSha256 = 1, OaepSha1 = 2;

    private readonly string _pasta;
    private readonly string _nomeDaChave;
    private readonly bool _usarTpm;

    /// <param name="pasta">Onde fica o arquivo (protegida por <see cref="PastaProtegida"/>).</param>
    /// <param name="nomeDaChave">O nome da chave no TPM (os testes usam outro).</param>
    /// <param name="usarTpm">false só nos testes da proteção sem TPM.</param>
    public CofreDoTikTok(string pasta, string nomeDaChave, bool usarTpm = true)
    {
        _pasta = pasta;
        _nomeDaChave = nomeDaChave;
        _usarTpm = usarTpm;
    }

    public string Caminho => Path.Combine(_pasta, "conta.bin");

    /// <summary>Guarda a conta (troca a que havia). Devolve como ficou protegida.</summary>
    public ProtecaoDaSessao Guardar(SessaoDoTikTok conta)
    {
        PastaProtegida.Garantir(_pasta);
        byte[] dados = JsonSerializer.SerializeToUtf8Bytes(conta);
        byte[] chave = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] marca = new byte[16];
        byte[] cifrado = new byte[dados.Length];
        byte[]? pacote = null;
        try
        {
            using (var aes = new AesGcm(chave, marca.Length))
                aes.Encrypt(nonce, dados, cifrado, marca);

            (ProtecaoDaSessao protecao, byte preenchimento, byte[] chaveGuardada) = Embrulhar(chave);
            using var escrita = new MemoryStream();
            using (var w = new BinaryWriter(escrita, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Versao);
                w.Write((byte)protecao);
                w.Write(preenchimento);
                w.Write(chaveGuardada.Length);
                w.Write(chaveGuardada);
                w.Write(nonce);
                w.Write(marca);
                w.Write(cifrado.Length);
                w.Write(cifrado);
            }
            pacote = escrita.ToArray();
            byte[] protegido = ProtectedData.Protect(pacote, Complemento, DataProtectionScope.CurrentUser);
            GravarDeUmaVez(protegido);
            return protecao;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dados);
            CryptographicOperations.ZeroMemory(chave);
            if (pacote != null)
                CryptographicOperations.ZeroMemory(pacote);
        }
    }

    /// <summary>A conta guardada, ou null (nenhuma, ou o arquivo não abre aqui: outro PC, outro usuário, estragado).</summary>
    public SessaoDoTikTok? Abrir() => Abrir(out _);

    public SessaoDoTikTok? Abrir(out ProtecaoDaSessao protecao)
    {
        protecao = ProtecaoDaSessao.Windows;
        if (!File.Exists(Caminho))
            return null;
        byte[]? pacote = null;
        byte[]? chave = null;
        byte[]? dados = null;
        try
        {
            pacote = ProtectedData.Unprotect(File.ReadAllBytes(Caminho), Complemento, DataProtectionScope.CurrentUser);
            using var r = new BinaryReader(new MemoryStream(pacote));
            if (r.ReadByte() != Versao)
                return null;
            protecao = (ProtecaoDaSessao)r.ReadByte();
            byte preenchimento = r.ReadByte();
            byte[] chaveGuardada = r.ReadBytes(r.ReadInt32());
            byte[] nonce = r.ReadBytes(12);
            byte[] marca = r.ReadBytes(16);
            byte[] cifrado = r.ReadBytes(r.ReadInt32());

            chave = Desembrulhar(protecao, preenchimento, chaveGuardada);
            dados = new byte[cifrado.Length];
            using (var aes = new AesGcm(chave, marca.Length))
                aes.Decrypt(nonce, cifrado, marca, dados);
            return JsonSerializer.Deserialize<SessaoDoTikTok>(dados);
        }
        catch (Exception e) when (e is CryptographicException or IOException or EndOfStreamException or JsonException
                                      or ArgumentException or InvalidCastException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("A conta guardada não abriu neste computador (" + e.GetType().Name + "): será preciso conectar de novo.");
            return null;
        }
        finally
        {
            if (pacote != null) CryptographicOperations.ZeroMemory(pacote);
            if (chave != null) CryptographicOperations.ZeroMemory(chave);
            if (dados != null) CryptographicOperations.ZeroMemory(dados);
        }
    }

    /// <summary>Apaga a conta: o arquivo (sobrescrito antes) e a chave do TPM.</summary>
    public void Apagar()
    {
        try
        {
            if (File.Exists(Caminho))
            {
                long tamanho = new FileInfo(Caminho).Length;
                using (var f = new FileStream(Caminho, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    f.Write(RandomNumberGenerator.GetBytes((int)tamanho));
                    f.Flush(flushToDisk: true);
                }
                File.Delete(Caminho);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Não deu para apagar o arquivo da conta: " + e.Message);
        }
        try
        {
            if (CngKey.Exists(_nomeDaChave, ProvedorDoTpm))
                CngKey.Open(_nomeDaChave, ProvedorDoTpm).Delete();
        }
        catch (CryptographicException)
        {
            // Sem TPM: não há chave
        }
    }

    // Grava num arquivo ao lado e troca no fim: nunca fica um arquivo pela metade
    private void GravarDeUmaVez(byte[] conteudo)
    {
        string temporario = Caminho + ".novo";
        using (var f = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(conteudo);
            f.Flush(flushToDisk: true);
        }
        File.Move(temporario, Caminho, overwrite: true);
    }

    private (ProtecaoDaSessao, byte, byte[]) Embrulhar(byte[] chave)
    {
        if (_usarTpm)
        {
            try
            {
                using RSA rsa = ChaveDoTpm();
                try
                {
                    return (ProtecaoDaSessao.WindowsETpm, OaepSha256, rsa.Encrypt(chave, RSAEncryptionPadding.OaepSHA256));
                }
                catch (CryptographicException)
                {
                    // Alguns TPMs só aceitam OAEP com SHA-1 (continua seguro para cifrar uma chave)
                    return (ProtecaoDaSessao.WindowsETpm, OaepSha1, rsa.Encrypt(chave, RSAEncryptionPadding.OaepSHA1));
                }
            }
            catch (CryptographicException e)
            {
                System.Diagnostics.Debug.WriteLine("O chip de segurança (TPM) não respondeu; a conta fica só com a proteção do Windows: " + e.Message);
            }
        }
        // Sem TPM a chave vai junto, dentro da proteção do Windows
        return (ProtecaoDaSessao.Windows, 0, (byte[])chave.Clone());
    }

    private byte[] Desembrulhar(ProtecaoDaSessao protecao, byte preenchimento, byte[] chaveGuardada)
    {
        if (protecao == ProtecaoDaSessao.Windows)
            return chaveGuardada;
        if (protecao != ProtecaoDaSessao.WindowsETpm)
            throw new CryptographicException("Proteção desconhecida.");
        using RSA rsa = new RSACng(CngKey.Open(_nomeDaChave, ProvedorDoTpm));
        return rsa.Decrypt(chaveGuardada, preenchimento == OaepSha1 ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.OaepSHA256);
    }

    // A chave RSA no TPM: criada uma vez, nunca exportável
    private RSA ChaveDoTpm()
    {
        if (CngKey.Exists(_nomeDaChave, ProvedorDoTpm))
            return new RSACng(CngKey.Open(_nomeDaChave, ProvedorDoTpm));
        var parametros = new CngKeyCreationParameters
        {
            Provider = ProvedorDoTpm,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Decryption,
            KeyCreationOptions = CngKeyCreationOptions.None,
        };
        parametros.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
        return new RSACng(CngKey.Create(CngAlgorithm.Rsa, _nomeDaChave, parametros));
    }
}
