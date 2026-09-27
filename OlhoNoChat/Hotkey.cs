using System.Text;
using System.Windows.Input;

namespace OlhoNoChat
{
    public class Hotkey
    {
        // Settable so the settings loader can fill in the saved values: it reuses the default
        // Hotkey instance, and read-only properties made custom hotkeys reset on every start.
        public Key Key { get; set; }

        public ModifierKeys Modifiers { get; set; }

        public Hotkey(Key key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public override string ToString()
        {
            var str = new StringBuilder();

            if (Modifiers.HasFlag(ModifierKeys.Control))
                str.Append("Ctrl + ");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                str.Append("Shift + ");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                str.Append("Alt + ");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                str.Append("Win + ");

            str.Append(Key);

            return str.ToString();
        }
    }
}
