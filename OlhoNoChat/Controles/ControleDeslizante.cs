using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OlhoNoChat.Controles;

/// <summary>
/// O controle deslizante dos painéis de tamanho do texto e fundo: a rodinha do mouse em cima dele muda o valor na hora,
/// um passo por vez (<see cref="PassoDaRodinha"/>). O visual está em Estilos/JanelaDoChat.xaml.
/// </summary>
public class ControleDeslizante : Slider
{
    public static readonly DependencyProperty PassoDaRodinhaProperty =
        DependencyProperty.Register(nameof(PassoDaRodinha), typeof(double), typeof(ControleDeslizante), new PropertyMetadata(1.0));

    public double PassoDaRodinha { get => (double)GetValue(PassoDaRodinhaProperty); set => SetValue(PassoDaRodinhaProperty, value); }

    /// <summary>Um giro da rodinha: um passo para cima ou para baixo, dentro dos limites.</summary>
    public void Girar(int delta)
    {
        if (delta != 0)
            Value = Math.Clamp(Value + Math.Sign(delta) * PassoDaRodinha, Minimum, Maximum);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        e.Handled = true;
        Girar(e.Delta);
    }
}
