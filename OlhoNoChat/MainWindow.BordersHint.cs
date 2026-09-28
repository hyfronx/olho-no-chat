namespace OlhoNoChat;

using OlhoNoChat.Atalhos;
using System.Windows.Input;

/// <summary>
/// After the borders are hidden, shows a short notice on top of the chat explaining how to
/// bring them back (a first-time user otherwise has no visible way to do it).
/// </summary>
public partial class MainWindow
{
    private bool _bordersHintPending = false;

    private const string ShowToastScript = """
        (function (text) {
            var old = document.getElementById('onc-toast');
            if (old) old.remove();
            var el = document.createElement('div');
            el.id = 'onc-toast';
            el.textContent = text;
            el.style.cssText = 'position:fixed;top:8px;left:8px;right:8px;z-index:2147483647;' +
                'background:#141414;color:#fff;border:2px solid #FF8A65;border-radius:8px;padding:10px 14px;' +
                'font:700 17px "Segoe UI",Arial,sans-serif;line-height:1.35;white-space:pre-line;' +
                'text-shadow:none;letter-spacing:0;pointer-events:none;transition:opacity .6s;';
            document.body.appendChild(el);
            setTimeout(function () { el.style.opacity = '0'; }, 10000);
            setTimeout(function () { el.remove(); }, 10700);
        })
        """;

    private const string HideToastScript = "(function () { var t = document.getElementById('onc-toast'); if (t) t.remove(); })();";

    private void RequestBordersHint()
    {
        _bordersHintPending = true;
        TryShowBordersHint();
    }

    // Called when the borders are hidden and again when a page finishes loading
    // (at startup the borders are hidden before the chat page exists).
    private void TryShowBordersHint()
    {
        bool chatPageLoaded = _chatPageLoadedAt != DateTime.MinValue && !_chatNavigationPending;
        if (!_bordersHintPending || !_hiddenBorders || !chatPageLoaded || this.webView?.CoreWebView2 == null)
            return;

        _bordersHintPending = false;

        Atalho hotkey = App.Opcoes.AtalhoBordas;
        string howTo = Atalho.Existe(hotkey)
            ? $"Para mostrar de novo: aperte {hotkey}, ou clique com o botão direito no ícone do Olho no Chat perto do relógio."
            : "Para mostrar de novo: clique com o botão direito no ícone do Olho no Chat perto do relógio.";

        string text = "Bordas ocultas: agora só o chat fica por cima do jogo.\n" + howTo;
        ShowChatToast(text);
    }

    // Configurações ilegíveis na abertura (decisão 20): o aviso diz onde ficou a cópia do arquivo. Mostrado uma vez,
    // quando a primeira página do chat carrega.
    private string _settingsNotice = App.ArquivoDeConfiguracoes.Aviso;

    private void TryShowSettingsNotice()
    {
        bool chatPageLoaded = _chatPageLoadedAt != DateTime.MinValue && !_chatNavigationPending;
        if (_settingsNotice == null || !chatPageLoaded || this.webView?.CoreWebView2 == null)
            return;

        ShowChatToast(_settingsNotice);
        _settingsNotice = null;
    }

    private void HideBordersHint()
    {
        _bordersHintPending = false;
        if (this.webView?.CoreWebView2 != null)
            _ = this.webView.CoreWebView2.ExecuteScriptAsync(HideToastScript);
    }
}
