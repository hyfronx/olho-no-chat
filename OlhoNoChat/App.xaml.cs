using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
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

                // Try to become the IPC server. If it fails, another instance is already running.
                if (!IpcManager.StartServer())
                {
                    // We are another instance. Always send arguments to the first instance.
                    await IpcManager.SendArgumentsToFirstInstance(e.Args);

                    // If it was a jump list action OR single-instance mode is on, we are done. Exit now.
                    if (e.Args.Length > 0 || !Settings.GeneralSettings.AllowMultipleInstances)
                    {
                        // Immediately shut down this new instance.
                        Application.Current.Shutdown();
                        return;
                    }
        
                    // If we get here, it means:
                    // 1. We are another instance.
                    // 2. It was NOT a jump list action.
                    // 3. Multi-instance IS allowed.
                    // Therefore, we can proceed to launch a new full instance.
                }

                IpcManager.ArgumentsReceived += ProcessCommandLineArgsFromSecondInstance;

                // Hook the global unhandled exception handler
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // Dependency injection (the provider stays alive through MainWindow)
                var services = new ServiceCollection();
                services.AddLogging(logging => logging.AddDebug());
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ITwitchAuthService, TwitchAuthService>();
                services.AddSingleton<TwitchAccount>();

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
                mainWindow.ProcessCommandLineArgs(e.Args);
                mainWindow.Show();

                // Every full instance sets up the jump list, so it stays right even after the first instance
                // closes. After the window shows: it is not needed to start, and a failure must not stop the app.
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        CreateJumpList();
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

        private void CreateJumpList()
        {
            JumpList jumplist = new JumpList();

            jumplist.JumpItems.Add(new JumpTask
            {
                Title = "Mostrar/esconder bordas",
                CustomCategory = "Ações",
                Arguments = "/toggleborders"
            });

            jumplist.JumpItems.Add(new JumpTask
            {
                Title = "Configurações",
                CustomCategory = "Ações",
                Arguments = "/settings"
            });

            jumplist.JumpItems.Add(new JumpTask
            {
                Title = "Restaurar posição da janela",
                CustomCategory = "Ações",
                Arguments = "/resetwindow"
            });

            jumplist.ShowFrequentCategory = false;
            jumplist.ShowRecentCategory = false;

            JumpList.SetJumpList(Application.Current, jumplist);
        }

        private void ProcessCommandLineArgsFromSecondInstance(string[] args)
        {
            // Find the running MainWindow and ask it to process the arguments.
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ProcessCommandLineArgs(args);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IpcManager.StopServer();
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
