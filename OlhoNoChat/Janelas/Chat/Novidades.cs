namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// As novidades apresentadas uma vez só, num balão apontando para o botão delas na barra laranja: a pausa do chat (2.6.0),
/// aprovar no AutoMod (2.7.0), o overlay da live (2.9.0), o aviso de anúncios e moderar no chat (2.11.0). Fecha pelo "Entendi" (a pausa também pausando de outro jeito), e aí fica gravada em
/// <see cref="Configuracoes.Opcoes.NovidadesVistas"/>; fechada pelo app (bordas ocultas, Configurações, outra página),
/// volta quando der.
/// </summary>
/// <remarks>
/// Todas as que faltam, em sequência, na ordem em que nasceram (escolha do usuário): o "Entendi" de uma traz a próxima logo
/// depois. Quem pulou versões vê as que faltam; cada uma, vista uma vez, nunca mais aparece.
/// </remarks>
public static class Novidades
{
    /// <summary>O nome gravado da novidade da pausa.</summary>
    public const string Pausa = "pausar-chat";

    /// <summary>O nome gravado da novidade do AutoMod.</summary>
    public const string AutoMod = "aprovar-automod";

    /// <summary>O nome gravado da novidade do overlay da live.</summary>
    public const string Overlay = "overlay-da-live";

    /// <summary>O nome gravado da novidade do aviso de anúncios.</summary>
    public const string Anuncios = "aviso-de-anuncios";

    /// <summary>O nome gravado da novidade de moderar no chat.</summary>
    public const string Moderacao = "moderar-no-chat";

    /// <summary>Quanto tempo depois de dar para mostrar o balão aparece (a página acabou de carregar, as bordas voltaram).</summary>
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(1.5);

    /// <summary>Depois do "Entendi" de uma, a próxima vem logo, só com um respiro para o balão anterior sumir.</summary>
    public static readonly TimeSpan EsperaDaSeguinte = TimeSpan.FromSeconds(0.4);

    /// <summary>
    /// A novidade a mostrar agora, ou null. Só com o chat Padrão aberto e carregado (os dois recursos são dele), as bordas
    /// visíveis (os botões ficam na barra laranja) e a faixa do canal e as Configurações fechadas. A do AutoMod com a opção ligada (também sem conta conectada: o balão diz que precisa conectar) e, com a
    /// conta conectada, depois de conferir as permissões dela. A do overlay só com ele desligado (ligado, a pessoa já
    /// conhece). A dos anúncios e a de moderar como a do AutoMod (opção ligada; com conta, depois de conferir as permissões).
    /// </summary>
    public static string? Proxima(IReadOnlyCollection<string> vistas, bool autoModPronto, bool overlayLigado,
        bool chatPadraoCarregado, bool bordasVisiveis, bool faixaAberta, bool configuracoesAbertas, bool anunciosPronto = false,
        bool moderacaoPronta = false)
    {
        if (!chatPadraoCarregado || !bordasVisiveis || faixaAberta || configuracoesAbertas)
            return null;
        if (!vistas.Contains(Pausa))
            return Pausa;
        if (!vistas.Contains(AutoMod) && autoModPronto)
            return AutoMod;
        if (!vistas.Contains(Overlay) && !overlayLigado)
            return Overlay;
        if (!vistas.Contains(Anuncios) && anunciosPronto)
            return Anuncios;
        if (!vistas.Contains(Moderacao) && moderacaoPronta)
            return Moderacao;
        return null;
    }
}
