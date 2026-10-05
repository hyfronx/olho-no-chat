using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;
using Velopack;

namespace OlhoNoChat.Inicio;

/// <summary>
/// A abertura do app, nesta ordem: o Velopack (instalação e atualização), as configurações (só lidas), a instância única
/// (uma cópia aberta depois manda os argumentos para a primeira e normalmente fecha), os serviços, a janela do chat, e,
/// com o app ocioso, a lista de ações do ícone na barra de tarefas. Os argumentos esperam a janela do chat ficar pronta.
/// </summary>
public static class Programa
{
    private const string TituloDoErro = "Olho no Chat - erro (clique aqui e aperte Ctrl+C para copiar)";

    private static ArquivoDeConfiguracoes? _arquivo;
    private static InstanciaUnica? _instanciaUnica;
    private static ServiceProvider? _servicos;

    [STAThread]
    public static void Main()
    {
        // Trata as chamadas do instalador (instalar, atualizar, desinstalar) e sai sozinho nesses casos
        VelopackApp.Build().Run();

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // Os estilos próprios valem para todas as janelas (e para o menu do ícone perto do relógio)
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/OlhoNoChat;component/Estilos/JanelaDoChat.xaml", UriKind.Relative),
        });
        app.Startup += async (_, e) => await AbrirAsync(app, e.Args);
        app.Exit += (_, _) => AoSair();
        app.Run();
    }

    private static async Task AbrirAsync(Application app, string[] argumentos)
    {
        try
        {
            // Só lê: uma cópia que vai fechar logo não grava nada
            _arquivo = ArquivoDeConfiguracoes.Abrir(InfoDoApp.PastaDeDados);

            _instanciaUnica = InstanciaUnica.TentarSerAPrimeira(app.Dispatcher);
            if (_instanciaUnica == null)
            {
                await InstanciaUnica.EnviarParaAPrimeiraAsync(argumentos);

                // Ação da barra de tarefas, ou "várias cópias" desligado: esta cópia fecha
                if (argumentos.Length > 0 || !_arquivo.Opcoes.PermitirVariasCopias)
                {
                    app.Shutdown();
                    return;
                }
            }

            // A cópia que continua aberta termina a conversão do arquivo antigo e passa a gravar
            _arquivo.ComecarAGravar();

            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErroInesperado(e.ExceptionObject);
            app.DispatcherUnhandledException += (_, e) =>
            {
                e.Handled = true;
                ErroInesperado(e.Exception);
            };

            _servicos = Servicos(_arquivo);
            var janela = _servicos.GetRequiredService<JanelaChat>();
            app.MainWindow = janela;
            if (_instanciaUnica != null)
                _instanciaUnica.PedidoRecebido += janela.Executar;

            janela.Executar(ArgumentosDoApp.Ler(argumentos));
            janela.Show();

            // Toda cópia completa monta a lista, para ela continuar certa depois que a primeira fechar. Não é preciso para
            // abrir, e uma falha aqui não pode parar o app.
            _ = app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try
                {
                    AcoesDaBarraDeTarefas.Montar(app);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"A lista de ações da barra de tarefas não foi montada: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            string texto = "Ocorreu um erro ao abrir o Olho no Chat. Dica: clique nesta mensagem e aperte Ctrl+C para copiar o texto inteiro.\n\n"
                           + ex.Message;
            if (ex.InnerException != null)
                texto += "\n\n" + ex.InnerException;
            if (ex.StackTrace != null)
                texto += "\n\n" + ex.StackTrace;
            MessageBox.Show(texto, TituloDoErro);
            app.Shutdown();
        }
    }

    // Um serviço de cada, criado quando a janela do chat pede
    private static ServiceProvider Servicos(ArquivoDeConfiguracoes arquivo)
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging(log => log.AddDebug());
        servicos.AddSingleton(arquivo);
        servicos.AddSingleton<ApiDaTwitch>();
        servicos.AddSingleton<IContaSalva>(new ContaSalvaNasConfiguracoes(arquivo));
        servicos.AddSingleton<ContaDaTwitch>();
        servicos.AddSingleton<AutorizacaoNoNavegador>();
        servicos.AddSingleton<EnvioDeMensagem>();
        // A última lista de emotes e as imagens ficam guardadas numa pasta dos dados do app
        string pastaDosEmotes = Path.Combine(InfoDoApp.PastaDeDados, "Emotes");
        servicos.AddSingleton(sp => new ListaDeEmotes(sp.GetRequiredService<ApiDaTwitch>(), sp.GetRequiredService<ContaDaTwitch>(),
            sp.GetRequiredService<ILogger<ListaDeEmotes>>(), pastaDosEmotes));
        servicos.AddSingleton(sp => new ImagensDeEmotes(pastaDosEmotes, sp.GetRequiredService<ILogger<ImagensDeEmotes>>()));
        servicos.AddSingleton<ResgatesDePontos>();
        servicos.AddSingleton(sp => new LeitorDoYouTube(sp.GetRequiredService<ILogger<LeitorDoYouTube>>()));
        servicos.AddSingleton(sp => new ProcuraDeAtualizacoes(sp.GetRequiredService<ILogger<ProcuraDeAtualizacoes>>(),
            () => arquivo.Opcoes, arquivo.Gravar));
        servicos.AddSingleton<IconeDaBandeja>();
        servicos.AddSingleton<JanelaChat>();
#if DEBUG
        SimuladorDoEventSub.RegistrarSeLigado(servicos); // resgates de mentira da Twitch CLI
#endif
        return servicos.BuildServiceProvider();
    }

    // Grava de novo ao sair, tira o ícone de perto do relógio e solta o canal da instância única
    private static void AoSair()
    {
        _arquivo?.Gravar();
        _servicos?.Dispose();
        _instanciaUnica?.Dispose();
    }

    private static void ErroInesperado(object erro)
    {
        MessageBox.Show("Ocorreu um erro inesperado. Dica: clique nesta mensagem e aperte Ctrl+C para copiar o texto inteiro.\n\n" + erro,
            TituloDoErro);
        Application.Current?.Shutdown();
    }
}
