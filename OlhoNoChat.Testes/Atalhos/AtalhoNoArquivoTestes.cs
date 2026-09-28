using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Jot;
using Jot.Storage;
using OlhoNoChat.Atalhos;

namespace OlhoNoChat.Testes.Atalhos;

/// <summary>
/// Os atalhos salvos pelas versões anteriores continuam valendo, e o arquivo continua no mesmo formato
/// ({"Key": n, "Modifiers": m}), lido e gravado pelo mesmo caminho do app (Jot).
/// </summary>
public sealed class AtalhoNoArquivoTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));

    // Mesmo registro que o AppSettings do app faz (arquivo AppSettings.json, item "GeneralSettings")
    public sealed class AppSettings
    {
        public GeneralSettings GeneralSettings { get; set; } = new();
    }

    public AtalhoNoArquivoTestes() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private AppSettings Abrir(out Tracker tracker)
    {
        tracker = new Tracker(new JsonFileStore(_pasta));
        tracker.Configure<AppSettings>()
            .Properties(a => new { a.GeneralSettings })
            .Id(a => a.GetType().Name, null, false);
        var configuracoes = new AppSettings();
        tracker.Track(configuracoes);
        return configuracoes;
    }

    private void GravarArquivoAntigo(string atalhos)
    {
        string json = """
            [
              {
                "Type": "OlhoNoChat.GeneralSettings, OlhoNoChat, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "Name": "GeneralSettings",
                "Value": { "Username": "canal", ATALHOS }
              }
            ]
            """.Replace("ATALHOS", atalhos);
        File.WriteAllText(Path.Combine(_pasta, "AppSettings.json"), json);
    }

    [Fact]
    public void ArquivoAntigo_AtalhosPersonalizadosEApagadoSaoLidos()
    {
        GravarArquivoAntigo("""
            "ToggleBordersHotkey": {"Key": 98, "Modifiers": 6},
            "ToggleInteractableHotkey": {"Key": 44, "Modifiers": 1},
            "BringToTopHotkey": null,
            "WriteMessageHotkey": {"Key": 100, "Modifiers": 3}
            """);

        var geral = Abrir(out _).GeneralSettings;

        Assert.Equal(new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Shift), geral.ToggleBordersHotkey);
        Assert.Equal(new Atalho(Key.A, ModifierKeys.Alt), geral.ToggleInteractableHotkey);
        Assert.Null(geral.BringToTopHotkey);
        Assert.Equal(new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt), geral.WriteMessageHotkey);
        Assert.Equal("canal", geral.Username);
    }

    [Fact]
    public void ArquivoSemAtalhos_FicaComOsPadroes()
    {
        GravarArquivoAntigo("\"FadeChat\": false");

        var geral = Abrir(out _).GeneralSettings;

        Assert.Equal(new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt), geral.ToggleBordersHotkey);
        Assert.Equal(new Atalho(Key.F7, ModifierKeys.Control | ModifierKeys.Alt), geral.ToggleInteractableHotkey);
        Assert.Equal(new Atalho(Key.F8, ModifierKeys.Control | ModifierKeys.Alt), geral.BringToTopHotkey);
        Assert.Equal(new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt), geral.WriteMessageHotkey);
    }

    [Fact]
    public void Gravar_MantemOFormatoKeyModifiers()
    {
        GravarArquivoAntigo("\"FadeChat\": false");
        var configuracoes = Abrir(out Tracker tracker);
        configuracoes.GeneralSettings.ToggleBordersHotkey = new Atalho(Key.F10, ModifierKeys.Control | ModifierKeys.Shift);
        configuracoes.GeneralSettings.BringToTopHotkey = null;

        tracker.Persist(configuracoes);

        var valor = JsonNode.Parse(File.ReadAllText(Path.Combine(_pasta, "AppSettings.json")))![0]!["Value"]!;
        var bordas = valor["ToggleBordersHotkey"]!.AsObject();
        Assert.Equal(["Key", "Modifiers"], bordas.Select(p => p.Key).Order());
        Assert.Equal((int)Key.F10, (int)bordas["Key"]!);
        Assert.Equal(6, (int)bordas["Modifiers"]!);
        Assert.Null(valor["BringToTopHotkey"]);
        Assert.True(valor.AsObject().ContainsKey("BringToTopHotkey"));
    }

    [Fact]
    public void GravarELerDeNovo_DaOMesmoAtalho()
    {
        var configuracoes = Abrir(out Tracker tracker);
        configuracoes.GeneralSettings.WriteMessageHotkey = new Atalho(Key.OemPlus, ModifierKeys.Windows | ModifierKeys.Alt);
        tracker.Persist(configuracoes);

        var lidas = Abrir(out _);

        Assert.Equal(new Atalho(Key.OemPlus, ModifierKeys.Windows | ModifierKeys.Alt), lidas.GeneralSettings.WriteMessageHotkey);
    }
}
