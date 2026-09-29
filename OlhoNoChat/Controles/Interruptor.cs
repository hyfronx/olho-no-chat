using System.Windows;
using System.Windows.Controls.Primitives;

namespace OlhoNoChat.Controles;

/// <summary>
/// O liga/desliga das janelas: a chave e, ao lado, "Ligado" ou "Desligado". Tem largura fixa, para todos ficarem
/// alinhados na mesma coluna. O visual está em Estilos/Controles.xaml.
/// </summary>
public class Interruptor : ToggleButton
{
    static Interruptor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Interruptor), new FrameworkPropertyMetadata(typeof(Interruptor)));
    }
}
