namespace OlhoNoChat.TikTok;

/// <summary>
/// Onde a janela de login do TikTok pode ir: só HTTPS na porta normal, só o TikTok e os endereços que o "Continuar com
/// Google/Facebook/Apple" do TikTok usa no meio do login (o Google, por exemplo, passa por accounts.youtube.com e pelo
/// accounts.google do país para gravar o login). Qualquer outro endereço é barrado.
/// </summary>
public static class EnderecosDoLogin
{
    // O endereço e todos os de baixo dele (tiktok.com vale para www.tiktok.com)
    private static readonly string[] Dominios =
    [
        "tiktok.com",
        "google.com", "accounts.youtube.com",
        "facebook.com",
        "apple.com",
    ];

    public static bool Permitido(string endereco)
    {
        if (!Uri.TryCreate(endereco, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0)
            return false;
        string host = uri.IdnHost.ToLowerInvariant();
        return Dominios.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal)) || ContasDoGoogleDoPais(host);
    }

    /// <summary>Endereços internos do navegador que aparecem durante o login (página em branco).</summary>
    public static bool Interno(string endereco) => endereco is "about:blank";

    // accounts.google.com.br, accounts.google.co.uk...: o Google grava o login também no domínio do país
    private static bool ContasDoGoogleDoPais(string host)
    {
        if (!host.StartsWith("accounts.google.", StringComparison.Ordinal))
            return false;
        // Só o formato dos domínios do Google: "de" ou "com.br" / "co.uk" (o país sempre com duas letras)
        string[] pais = host["accounts.google.".Length..].Split('.');
        bool duasLetras(string p) => p.Length == 2 && p.All(char.IsAsciiLetterLower);
        return pais switch
        {
            [var cc] => duasLetras(cc),
            ["com" or "co", var cc] => duasLetras(cc),
            _ => false,
        };
    }
}
