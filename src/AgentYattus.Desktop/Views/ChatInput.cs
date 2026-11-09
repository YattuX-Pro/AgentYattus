using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgentYattus.Desktop.Views;

/// <summary>Entrée envoie un message ; Maj + Entrée laisse le champ insérer une ligne.</summary>
public sealed class ChatInput : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand?> SendProperty =
        AvaloniaProperty.RegisterAttached<ChatInput, TextBox, ICommand?>("Send");

    static ChatInput() => SendProperty.Changed.AddClassHandler<TextBox>((box, _) =>
    {
        box.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        if (GetSend(box) is not null)
            box.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
    });

    public static ICommand? GetSend(TextBox box) => box.GetValue(SendProperty);
    public static void SetSend(TextBox box, ICommand? command) => box.SetValue(SendProperty, command);

    private static void KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;
        e.Handled = true;
        if (GetSend(box) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
