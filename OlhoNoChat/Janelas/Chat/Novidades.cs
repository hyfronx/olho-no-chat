namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// As novidades apresentadas uma vez só, num balão apontando para o botão delas na barra laranja. Hoje, a pausa do chat
/// (2.6.0). Fecha pelo "Entendi", pelo "Testar agora" (que pausa o chat) ou pausando de outro jeito, e aí fica gravada em
/// <see cref="Configuracoes.Opcoes.NovidadesVistas"/>; fechada pelo app (bordas ocultas, Configurações, outra página),
/// volta quando der.
/// </summary>
public static class Novidades
{
    /// <summary>O nome gravado da novidade da pausa.</summary>
    public const string Pausa = "pausar-chat";

    /// <summary>Quanto tempo depois de dar para mostrar o balão aparece (a página acabou de carregar, as bordas voltaram).</summary>
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// O balão da pausa pode aparecer: ainda não visto, com o chat Padrão aberto e carregado (o botão só existe nele), as
    /// bordas visíveis (o botão fica na barra laranja), a faixa do canal fechada e as Configurações fechadas.
    /// </summary>
    public static bool PodeMostrarAPausa(IReadOnlyCollection<string> vistas, bool chatPadraoCarregado, bool bordasVisiveis,
        bool faixaAberta, bool configuracoesAbertas) =>
        !vistas.Contains(Pausa) && chatPadraoCarregado && bordasVisiveis && !faixaAberta && !configuracoesAbertas;
}
