using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace OlhoNoChat.Overlay;

/// <summary>
/// O arquivo que a Fonte de navegador do OBS abre ("Arquivo local"), em vez do endereço do overlay. Com o endereço, o OBS
/// aberto antes do app cai na página de erro do navegador e nunca mais tenta. O arquivo sempre abre: fica transparente,
/// espera o app abrir e só então troca para o overlay (<c>http://localhost:&lt;porta&gt;/overlay</c>).
/// </summary>
/// <remarks>
/// O OBS abre arquivo local com a origem <c>http://absolute</c>, que o navegador dele trata como site público: nada dela
/// chega ao localhost (fetch, imagem, e no OBS 33 nem um iframe, como o carregador do qobuz-obs fazia; testado em
/// 2026-11-11: nenhum pedido chegou ao app). Só a troca da página inteira passa. Então o app dá sinal de vida por um arquivo
/// ao lado (<see cref="NomeDoSinal"/>, a hora em ms, reescrita a cada <see cref="IntervaloDoSinal"/> enquanto o overlay
/// funciona), que a página lê como arquivo da mesma origem; com o sinal recente, ela troca para o overlay.
/// O instalador cria o arquivo; o app o confere ao abrir e a cada Salvar (a porta pode mudar); a desinstalação apaga. Fica
/// protegido contra exclusão sem querer (uma regra que nega, até a desinstalação tirar): nem o arquivo nem a pasta podem ser
/// apagados, nem mandados para a Lixeira (o Explorer diz "Acesso negado"). Renomear a pasta o Windows deixa (quem decide é
/// a pasta de cima); o app cria de novo ao abrir.
/// </remarks>
public static class ArquivoDoObs
{
    public const string NomeDoArquivo = "overlay.html";

    /// <summary>O sinal de vida do app: a hora (ms desde 1970, UTC) da última vez que o overlay estava funcionando.</summary>
    public const string NomeDoSinal = "app-aberto.txt";

    public static readonly TimeSpan IntervaloDoSinal = TimeSpan.FromSeconds(3);

    /// <summary>Um sinal mais velho que isto é de um app que fechou (ou travou): a página continua esperando.</summary>
    public const int SinalValeMs = 10000;

    /// <summary>
    /// %LocalAppData%\OlhoNoChat-OBS: fora da pasta da instalação (o Velopack troca as pastas dela nas atualizações), ao lado
    /// da pasta do cofre.
    /// </summary>
    public static string PastaPadrao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InfoDoApp.NomeDaPastaDeDados + "-OBS");

    /// <summary>O caminho que a pessoa cola no OBS.</summary>
    public static string Caminho => Path.Combine(PastaPadrao, NomeDoArquivo);

    /// <summary>O conteúdo do arquivo para o overlay nesta porta.</summary>
    public static string Conteudo(int porta) => $$"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <title>Overlay do Olho no Chat</title>
        <!--
          A Fonte de navegador do OBS abre este arquivo ("Arquivo local" marcado). Ele espera o Olho no Chat abrir e então
          mostra o overlay. Assim o overlay aparece mesmo se o OBS abrir antes do app.
          O Olho no Chat cria e atualiza este arquivo sozinho, e o apaga quando é desinstalado.
        -->
        <style>html, body { margin: 0; background: transparent; overflow: hidden; }</style>
        </head>
        <body>
        <script>
          // O app, aberto com o overlay ligado, reescreve {{NomeDoSinal}} (aqui do lado) com a hora a cada poucos segundos.
          // O navegador do OBS não deixa este arquivo falar com o app, mas deixa ler um arquivo da mesma pasta.
          function esperar() {
            fetch('{{NomeDoSinal}}', { cache: 'no-store' })
              .then(function (r) { return r.ok ? r.text() : ''; })
              .then(function (texto) {
                var hora = parseInt(texto, 10);
                if (hora && Math.abs(Date.now() - hora) < {{SinalValeMs}})
                  location.replace('http://localhost:{{porta}}/overlay');
                else
                  setTimeout(esperar, 2000);
              })
              .catch(function () { setTimeout(esperar, 2000); });
          }
          esperar();
        </script>
        </body>
        </html>
        """;

    /// <summary>O app está com o overlay funcionando agora: renova o sinal de vida (sem falhar).</summary>
    public static void DarSinalDeVida() => SemFalhar(() => DarSinalDeVida(PastaPadrao, DateTimeOffset.UtcNow));

    /// <summary>O overlay parou (desligado, o app fechando): o sinal deixa de valer na hora.</summary>
    public static void TirarSinalDeVida() => SemFalhar(() => TirarSinalDeVida(PastaPadrao));

    public static void DarSinalDeVida(string pasta, DateTimeOffset agora)
    {
        if (Directory.Exists(pasta))
            File.WriteAllText(Path.Combine(pasta, NomeDoSinal), agora.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void TirarSinalDeVida(string pasta)
    {
        string sinal = Path.Combine(pasta, NomeDoSinal);
        if (File.Exists(sinal))
            File.WriteAllText(sinal, "0");
    }

    /// <summary>Cria o arquivo (ou corrige a porta dele) e protege; sem falhar (um erro aqui não pode parar o app).</summary>
    public static void Garantir(int porta) => SemFalhar(() => Garantir(porta, PastaPadrao));

    /// <summary>Instalação e atualização: cria se faltar, com a porta padrão (o app corrige a porta ao abrir).</summary>
    public static void CriarSeFaltar() => SemFalhar(() =>
    {
        if (!File.Exists(Caminho))
            Garantir(ServidorDoOverlay.PortaPadrao, PastaPadrao);
    });

    /// <summary>Desinstalação: tira a proteção e apaga o arquivo e a pasta.</summary>
    public static void Apagar() => SemFalhar(() => Apagar(PastaPadrao));

    /// <summary>Os testes usam outra pasta.</summary>
    public static void Garantir(int porta, string pasta)
    {
        string arquivo = Path.Combine(pasta, NomeDoArquivo);
        Directory.CreateDirectory(pasta);
        string conteudo = Conteudo(porta);
        // Só escreve se mudou. Escrever por cima (sem apagar antes) passa pela proteção
        if (!File.Exists(arquivo) || File.ReadAllText(arquivo, Encoding.UTF8) != conteudo)
            File.WriteAllText(arquivo, conteudo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Proteger(pasta, arquivo);
    }

    public static void Apagar(string pasta)
    {
        if (!Directory.Exists(pasta))
            return;
        string arquivo = Path.Combine(pasta, NomeDoArquivo);
        TirarAProtecao(pasta, arquivo);
        Directory.Delete(pasta, recursive: true);
    }

    /// <summary>O arquivo e a pasta estão protegidos contra exclusão.</summary>
    public static bool Protegido(string pasta)
    {
        string arquivo = Path.Combine(pasta, NomeDoArquivo);
        return File.Exists(arquivo) && TemARegra(new DirectoryInfo(pasta).GetAccessControl(), RegraDaPasta())
                                    && TemARegra(new FileInfo(arquivo).GetAccessControl(), RegraDoArquivo());
    }

    // --- A proteção --------------------------------------------------------------------------------------------
    // Apagar um arquivo pede a permissão de apagar dele OU a de apagar o que está dentro da pasta dele: as duas são negadas
    // ao usuário (uma regra que nega vence as que permitem). Com o arquivo preso, a pasta (que não fica vazia) também não
    // sai; e ela mesma nega ser apagada. Escrever no arquivo continua permitido (o app atualiza a porta).

    private static SecurityIdentifier Usuario => WindowsIdentity.GetCurrent().User!;

    private static FileSystemAccessRule RegraDaPasta() =>
        new(Usuario, FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);

    private static FileSystemAccessRule RegraDoArquivo() => new(Usuario, FileSystemRights.Delete, AccessControlType.Deny);

    private static void Proteger(string pasta, string arquivo)
    {
        var infoDaPasta = new DirectoryInfo(pasta);
        DirectorySecurity daPasta = infoDaPasta.GetAccessControl();
        if (!TemARegra(daPasta, RegraDaPasta()))
        {
            daPasta.AddAccessRule(RegraDaPasta());
            infoDaPasta.SetAccessControl(daPasta);
        }
        var infoDoArquivo = new FileInfo(arquivo);
        FileSecurity doArquivo = infoDoArquivo.GetAccessControl();
        if (!TemARegra(doArquivo, RegraDoArquivo()))
        {
            doArquivo.AddAccessRule(RegraDoArquivo());
            infoDoArquivo.SetAccessControl(doArquivo);
        }
    }

    private static void TirarAProtecao(string pasta, string arquivo)
    {
        // Como dono, o usuário sempre pode mudar as permissões, mesmo com a regra que nega
        var infoDaPasta = new DirectoryInfo(pasta);
        DirectorySecurity daPasta = infoDaPasta.GetAccessControl();
        daPasta.RemoveAccessRuleAll(RegraDaPasta());
        infoDaPasta.SetAccessControl(daPasta);
        if (File.Exists(arquivo))
        {
            var infoDoArquivo = new FileInfo(arquivo);
            FileSecurity doArquivo = infoDoArquivo.GetAccessControl();
            doArquivo.RemoveAccessRuleAll(RegraDoArquivo());
            infoDoArquivo.SetAccessControl(doArquivo);
        }
    }

    private static bool TemARegra(FileSystemSecurity seguranca, FileSystemAccessRule procurada) =>
        seguranca.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(r => r.AccessControlType == AccessControlType.Deny && r.IdentityReference == procurada.IdentityReference
                      && (r.FileSystemRights & procurada.FileSystemRights) == procurada.FileSystemRights);

    private static void SemFalhar(Action acao)
    {
        try
        {
            acao();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arquivo do OBS: {ex.Message}");
        }
    }
}
