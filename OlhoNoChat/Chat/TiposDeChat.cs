#nullable enable
namespace OlhoNoChat.Chat;

/// <summary>Regras comuns aos tipos de chat.</summary>
public static class TiposDeChat
{
    /// <summary>O tipo gravado no arquivo; um número que não existe mais (o 3 era o jCyan) vira o Padrão.</summary>
    public static TipoDeChat Ler(int numero) =>
        Enum.IsDefined(typeof(TipoDeChat), numero) ? (TipoDeChat)numero : TipoDeChat.Padrao;

    /// <summary>O Padrão e o chat oficial mostram o chat de um canal (trocado na faixa acima do chat).</summary>
    public static bool UsaCanal(TipoDeChat tipo) => tipo is TipoDeChat.Padrao or TipoDeChat.ChatOficial;
}
