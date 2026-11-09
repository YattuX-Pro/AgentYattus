using Avalonia.Controls;
using Avalonia.Threading;
using AgentYattus.Client.Core;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AgentYattus.Desktop.Views;

public sealed partial class RecruitmentAssistantView : UserControl
{
    private RecruitmentViewModel? _model;
    private readonly HashSet<RecruitmentMessage> _observed = [];

    public RecruitmentAssistantView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as RecruitmentViewModel);
        AttachedToVisualTree += (_, _) => Observe(DataContext as RecruitmentViewModel);
        DetachedFromVisualTree += (_, _) => Observe(null);
    }

    private void Observe(RecruitmentViewModel? model)
    {
        if (ReferenceEquals(model, _model)) return;
        if (_model is not null) _model.Messages.CollectionChanged -= MessagesChanged;
        foreach (var message in _observed) message.PropertyChanged -= TextChanged;
        _observed.Clear();
        _model = model;
        if (_model is null) return;
        _model.Messages.CollectionChanged += MessagesChanged;
        SyncMessages();
    }

    private void SyncMessages()
    {
        var current = _model!.Messages.ToHashSet();
        foreach (var removed in _observed.Except(current).ToList())
        {
            removed.PropertyChanged -= TextChanged;
            _observed.Remove(removed);
        }
        foreach (var added in current.Except(_observed))
        {
            added.PropertyChanged += TextChanged;
            _observed.Add(added);
        }
    }

    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncMessages();
        FollowLatest(e.NewItems?.OfType<RecruitmentMessage>().Any(m => m.FromUser) == true);
    }

    private void TextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecruitmentMessage.Text)) FollowLatest();
    }

    private void FollowLatest(bool force = false)
    {
        // Suivre le texte seulement si la personne est déjà en bas, pour laisser relire l'historique.
        if (!force && ConversationScroll.Offset.Y < ConversationScroll.Extent.Height - ConversationScroll.Viewport.Height - 32) return;
        var model = _model;
        Dispatcher.UIThread.Post(() =>
        {
            if (model is not null && ReferenceEquals(model, _model)) ConversationScroll.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
