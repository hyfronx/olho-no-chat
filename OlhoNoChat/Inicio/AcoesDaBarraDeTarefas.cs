#nullable enable
using System.Windows;
using System.Windows.Shell;

namespace OlhoNoChat.Inicio;

/// <summary>
/// As ações do botão direito no ícone do app na barra de tarefas. Cada uma abre o .exe com um argumento, que
/// chega à cópia já aberta (ver <see cref="InstanciaUnica"/>).
/// </summary>
public static class AcoesDaBarraDeTarefas
{
    private const string Categoria = "Ações";

    public static void Montar(Application app)
    {
        var lista = new JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };
        lista.JumpItems.Add(Acao("Mostrar/esconder bordas", ArgumentosDoApp.AlternarBordas));
        lista.JumpItems.Add(Acao("Configurações", ArgumentosDoApp.AbrirConfiguracoes));
        lista.JumpItems.Add(Acao("Restaurar posição da janela", ArgumentosDoApp.RestaurarPosicao));
        JumpList.SetJumpList(app, lista);
    }

    private static JumpTask Acao(string titulo, string argumento) =>
        new() { Title = titulo, Arguments = argumento, CustomCategory = Categoria };
}
