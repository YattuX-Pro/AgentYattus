using System.Collections.Specialized;
using System.Diagnostics;
using AgentYattus.Client.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgentYattus.Desktop.Views;

/// <summary>
/// La vue fait battre la scène : à chaque image (environ 60 par seconde), elle dit au ViewModel combien de temps
/// s'est écoulé. Le ViewModel n'a pas de minuteur (testable sans interface) ; la vue n'a pas de logique.
/// </summary>
public sealed partial class OfficeView : UserControl
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();

    public OfficeView()
    {
        InitializeComponent();
        _clock.Tick += (_, _) =>
        {
            var dt = _elapsed.Elapsed;
            _elapsed.Restart();
            (DataContext as OfficeViewModel)?.Tick(dt);
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _elapsed.Restart();
        _clock.Start();
        if (DataContext is OfficeViewModel office)
        {
            office.Conversation.CollectionChanged += ConversationChanged;
            office.PropertyChanged += OfficeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clock.Stop();
        if (DataContext is OfficeViewModel office)
        {
            office.Conversation.CollectionChanged -= ConversationChanged;
            office.PropertyChanged -= OfficeChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }
    private void ConversationChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollConversation();

    private void OfficeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeViewModel.LiveText))
            ScrollConversation();
    }

    private void ScrollConversation()
    {
        if (this.FindControl<ScrollViewer>("FeedScroll") is { } scroll)
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
    }

}
