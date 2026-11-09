using AgentYattus.Client.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Animation;

namespace AgentYattus.Desktop.Views;

public sealed partial class AgentToastView : UserControl
{
    private ConversationInbox? _inbox;
    public AgentToastView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as ConversationInbox);
        AttachedToVisualTree += (_, _) => Observe(DataContext as ConversationInbox);
        DetachedFromVisualTree += (_, _) => Observe(null);
        PointerEntered += (_, _) => (DataContext as ConversationInbox)?.PauseToast();
        PointerExited += (_, _) => (DataContext as ConversationInbox)?.ResumeToast();
        GotFocus += (_, _) => (DataContext as ConversationInbox)?.PauseToast();
        LostFocus += (_, _) => (DataContext as ConversationInbox)?.ResumeToast();
    }
    private void Observe(ConversationInbox? inbox)
    {
        if (_inbox is not null) _inbox.PropertyChanged -= Changed;
        _inbox = inbox;
        if (_inbox is not null) _inbox.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConversationInbox.Toast) || _inbox?.Toast is not { } toast) return;
        SpeechTail.Points = new Points
        {
            new(Math.Clamp(toast.Data.Author?.MouthX ?? .56, 0, 1) * 100, Math.Clamp(toast.Data.Author?.MouthY ?? .37, 0, 1) * 100),
            new(97, 30), new(97, 48),
        };
        if (ReducedMotion.Enabled) return;
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(200) };
        animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } });
        animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } });
        _ = animation.RunAsync(this);
    }
}
