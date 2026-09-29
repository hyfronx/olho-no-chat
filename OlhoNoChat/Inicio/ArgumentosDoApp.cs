namespace OlhoNoChat.Inicio;

/// <summary>O que uma abertura do .exe pede ao app (ações da barra de tarefas ou só abrir de novo).</summary>
public enum ComandoDoApp
{
    /// <summary>Abrir o app de novo sem argumento: mostrar a janela do chat.</summary>
    MostrarJanela,
    /// <summary>/toggleborders</summary>
    AlternarBordas,
    /// <summary>/settings</summary>
    AbrirConfiguracoes,
    /// <summary>/resetwindow</summary>
    RestaurarPosicao,
}

public static class ArgumentosDoApp
{
    public const string AlternarBordas = "/toggleborders";
    public const string AbrirConfiguracoes = "/settings";
    public const string RestaurarPosicao = "/resetwindow";

    /// <summary>
    /// Os comandos dos argumentos, na ordem em que vieram (sem diferenciar maiúsculas). Argumentos desconhecidos
    /// são ignorados.
    /// </summary>
    public static IReadOnlyList<ComandoDoApp> Ler(IEnumerable<string> argumentos)
    {
        var comandos = new List<ComandoDoApp>();
        foreach (string argumento in argumentos)
        {
            switch (argumento.Trim().ToLowerInvariant())
            {
                case AlternarBordas: comandos.Add(ComandoDoApp.AlternarBordas); break;
                case AbrirConfiguracoes: comandos.Add(ComandoDoApp.AbrirConfiguracoes); break;
                case RestaurarPosicao: comandos.Add(ComandoDoApp.RestaurarPosicao); break;
            }
        }
        return comandos;
    }
}
