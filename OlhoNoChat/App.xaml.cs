using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using OlhoNoChat.Inicio;
using OlhoNoChat.Twitch;
using OlhoNoChat.View.Settings;
using Velopack;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace OlhoNoChat
{
    /// <summary>
    /// Startup: settings, one running copy (IPC), dependency injection, the chat window and the jump list.
    /// </summary>
    public partial class App : Application
    {
        public static readonly AppSettings Settings = new AppSettings();
        public static bool IsShuttingDown { get; set; } = false;

        private InstanciaUnica _instanciaUnica;

        public App()
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // Velopack needs to be able to bootstrap your application and handle updates
            VelopackApp.Build().Run();

            var application = new App();
            application.InitializeComponent(); // loads App.xaml resources
            application.Run(); // Triggers the OnStartup event
        }

        protected override async void OnStartup(StartupEventArgs e) {
            try {
                base.OnStartup(e);

                Settings.Init();

                // Só uma cópia principal: uma cópia aberta depois manda os argumentos para a primeira
                _instanciaUnica = InstanciaUnica.TentarSerAPrimeira(Dispatcher);
                if (_instanciaUnica == null)
                {
                    await InstanciaUnica.EnviarParaAPrimeiraAsync(e.Args);

                    // Ação da barra de tarefas, ou "várias cópias" desligado: esta cópia fecha
                    if (e.Args.Length > 0 || !Settings.GeneralSettings.AllowMultipleInstances)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }
                else
                {
                    _instanciaUnica.PedidoRecebido += comandos =>
                    {
                        if (Application.Current.MainWindow is MainWindow janela)
                            janela.ExecutarComandos(comandos);
                    };
                }

                // Hook the global unhandled exception handler
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // Dependency injection (the provider stays alive through MainWindow)
                var services = new ServiceCollection();
                services.AddLogging(logging => logging.AddDebug());
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ApiDaTwitch>();
                services.AddSingleton<IContaSalva, ContaSalvaNasConfiguracoes>();
                services.AddSingleton<ContaDaTwitch>();
                services.AddSingleton<AutorizacaoNoNavegador>();
                services.AddSingleton<EnvioDeMensagem>();
                services.AddSingleton<ListaDeEmotes>();
                services.AddSingleton<ResgatesDePontos>();
#if DEBUG
                SimuladorDoEventSub.RegistrarSeLigado(services); // resgates de mentira da Twitch CLI
#endif

                // Settings pages
                services.AddTransient<ConnectionSettingsPage>();
                services.AddTransient<ChatSettingsPage>();
                services.AddTransient<AppearanceSettingsPage>();
                services.AddTransient<SoundSettingsPage>();
                services.AddTransient<GeneralSettingsPage>();
                services.AddTransient<AboutSettingsPage>();

                // Main settings window
                services.AddTransient<SettingsWindow>();

                // Create and show the main window
                var mainWindow = services.BuildServiceProvider().GetRequiredService<MainWindow>();

                // Let the main window process its own startup arguments
                mainWindow.ExecutarComandos(ArgumentosDoApp.Ler(e.Args));
                mainWindow.Show();

                // Every full instance sets up the jump list, so it stays right even after the first instance
                // closes. After the window shows: it is not needed to start, and a failure must not stop the app.
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        AcoesDaBarraDeTarefas.Montar(this);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not set up the jump list: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex) {
                var msg = "Ocorreu um erro ao abrir o Olho no Chat. Dica: clique nesta mensagem e aperte Ctrl+C para copiar o texto inteiro.\n\n" + ex.Message;
                if (ex.InnerException != null)
                {
                    msg += "\n\n" + ex.InnerException.ToString();
                }
                if (ex.StackTrace != null)
                {
                    msg += "\n\n" + ex.StackTrace;
                }

                MessageBox.Show(msg,
                    "Olho no Chat - erro (clique aqui e aperte Ctrl+C para copiar)");
                Application.Current.Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _instanciaUnica?.Dispose();
            base.OnExit(e);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Ocorreu um erro inesperado. Dica: clique nesta mensagem e aperte Ctrl+C para copiar o texto inteiro.\n\n" + e.ExceptionObject.ToString(),
                "Olho no Chat - erro (clique aqui e aperte Ctrl+C para copiar)");
            Application.Current.Shutdown();
        }
    }
}
