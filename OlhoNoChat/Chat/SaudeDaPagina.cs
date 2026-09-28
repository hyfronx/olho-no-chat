#nullable enable
namespace OlhoNoChat.Chat;

/// <summary>A conexão da página do Padrão com a Twitch, segundo <c>window.oncChat.health()</c>.</summary>
public sealed record SaudeDaPagina
{
    public enum Situacao
    {
        /// <summary>Conectada; <see cref="SemDados"/> diz há quanto tempo a Twitch não manda nada.</summary>
        Aberta,

        /// <summary>A página perdeu a conexão e está conectando de novo (ela cuida disso sozinha).</summary>
        Reconectando,

        /// <summary>O script da página não está rodando (ou ainda não começou).</summary>
        SemScript,

        /// <summary>O script da página deu erro ao responder.</summary>
        ComErro,
    }

    public Situacao Estado { get; private init; }
    public TimeSpan SemDados { get; private init; }
    public string Erro { get; private init; } = string.Empty;

    public static SaudeDaPagina Aberta(TimeSpan semDados) => new() { Estado = Situacao.Aberta, SemDados = semDados };
    public static readonly SaudeDaPagina Reconectando = new() { Estado = Situacao.Reconectando };
    public static readonly SaudeDaPagina SemScript = new() { Estado = Situacao.SemScript };
    public static SaudeDaPagina ComErro(string erro) => new() { Estado = Situacao.ComErro, Erro = erro };
}
