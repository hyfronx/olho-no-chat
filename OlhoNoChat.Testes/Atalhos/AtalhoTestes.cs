using System.Windows.Input;
using OlhoNoChat.Atalhos;

namespace OlhoNoChat.Testes.Atalhos;

public class AtalhoTestes
{
    [Fact]
    public void Texto_MostraOsModificadoresNaOrdemCtrlShiftAltWin()
    {
        var atalho = new Atalho(Key.F9, ModifierKeys.Windows | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control);
        Assert.Equal("Ctrl + Shift + Alt + Win + F9", atalho.ToString());
    }

    [Fact]
    public void Texto_DoPadraoDoApp()
    {
        Assert.Equal("Ctrl + Alt + F9", new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt).ToString());
        Assert.Equal("Ctrl + Alt + F11", new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt).ToString());
    }

    [Fact]
    public void Texto_TeclaSemModificadorEhAceita()
    {
        Assert.Equal("F9", new Atalho(Key.F9, ModifierKeys.None).ToString());
    }

    [Fact]
    public void Texto_UsaONomeDaTeclaDoWpf()
    {
        Assert.Equal("Ctrl + D1", new Atalho(Key.D1, ModifierKeys.Control).ToString());
        Assert.Equal("Alt + NumPad1", new Atalho(Key.NumPad1, ModifierKeys.Alt).ToString());
    }

    [Fact]
    public void SemAtalho_AparecenComoNenhum()
    {
        Assert.Equal("Nenhum", Atalho.TextoOuNenhum(null));
        Assert.Equal("Nenhum", Atalho.TextoOuNenhum(new Atalho(Key.None, ModifierKeys.Control)));
        Assert.Equal("Ctrl + A", Atalho.TextoOuNenhum(new Atalho(Key.A, ModifierKeys.Control)));
    }

    [Fact]
    public void Existe_SoComUmaTeclaDeVerdade()
    {
        Assert.False(Atalho.Existe(null));
        Assert.False(Atalho.Existe(new Atalho(Key.None, ModifierKeys.Control | ModifierKeys.Alt)));
        Assert.True(Atalho.Existe(new Atalho(Key.F8, ModifierKeys.None)));
    }

    [Fact]
    public void DoisAtalhosIguais_SaoIguais()
    {
        Assert.Equal(new Atalho(Key.F7, ModifierKeys.Control), new Atalho(Key.F7, ModifierKeys.Control));
        Assert.NotEqual(new Atalho(Key.F7, ModifierKeys.Control), new Atalho(Key.F7, ModifierKeys.Alt));
    }
}
