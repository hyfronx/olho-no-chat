using OlhoNoChat.Atalhos;
using System.Windows;
using System.Windows.Controls;

namespace OlhoNoChat.View.Settings;

/// <summary>
/// "Geral" tab: chat window behaviour, hotkeys, updates and advanced options.
/// Sound options are on the "Som" tab (SoundSettingsPage).
/// </summary>
public partial class GeneralSettingsPage : UserControl
{
    public event Action CheckForUpdateRequested;
    public event Action ResetToDefaultsRequested;

    public GeneralSettingsPage()
    {
        InitializeComponent();
    }

    public void SetupValues()
    {
        this.cbAutoHideBorders.IsOn = App.Opcoes.EsconderBordasAoAbrir;
        this.cbTaskbar.IsOn = App.Opcoes.EsconderIconeDaBarraDeTarefas;
        this.cbInteraction.IsOn = App.Opcoes.PermitirCliqueComBordas;
        this.cbCheckForUpdates.IsOn = App.Opcoes.ProcurarAtualizacoes;
        this.cbMultiInstance.IsOn = App.Opcoes.PermitirVariasCopias;

        this.hotkeyInputToggleBorders.Atalho = App.Opcoes.AtalhoBordas;
        this.hotkeyInputToggleInteractable.Atalho = App.Opcoes.AtalhoModoRolagem;
        this.hotkeyInputBringToTop.Atalho = App.Opcoes.AtalhoSempreNoTopo;
        this.hotkeyInputWriteMessage.Atalho = App.Opcoes.AtalhoEscrever;
    }

    public void SaveValues()
    {
        App.Opcoes.EsconderBordasAoAbrir = this.cbAutoHideBorders.IsOn;
        App.Opcoes.EsconderIconeDaBarraDeTarefas = this.cbTaskbar.IsOn;
        App.Opcoes.PermitirCliqueComBordas = this.cbInteraction.IsOn;
        App.Opcoes.ProcurarAtualizacoes = this.cbCheckForUpdates.IsOn;
        App.Opcoes.PermitirVariasCopias = this.cbMultiInstance.IsOn;

        // Hotkeys
        App.Opcoes.AtalhoBordas = hotkeyInputToggleBorders.Atalho;
        App.Opcoes.AtalhoModoRolagem = hotkeyInputToggleInteractable.Atalho;
        App.Opcoes.AtalhoSempreNoTopo = hotkeyInputBringToTop.Atalho;
        App.Opcoes.AtalhoEscrever = hotkeyInputWriteMessage.Atalho;
    }

    // "Mudar atalho" / "Confirmar" of a hotkey row (its editor is in the same row)
    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var editor = ((Grid)button.Parent).Children.OfType<EditorDeAtalho>().Single();

        if (editor.Gravando)
        {
            editor.PararDeGravar();
            button.Content = "Mudar atalho";
        }
        else
        {
            editor.ComecarAGravar();
            button.Content = "Confirmar";
        }
    }

    private void btnCheckForUpdatesNow_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdateRequested?.Invoke();
    }

    // The settings window asks first and then restores every tab (SettingsWindow.ResetToDefaultsAsync)
    private void btnResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultsRequested?.Invoke();
    }
}
