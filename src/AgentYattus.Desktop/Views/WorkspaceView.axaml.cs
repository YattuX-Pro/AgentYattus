using System.Collections.Specialized;
using System.ComponentModel;
using AgentYattus.Client.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgentYattus.Desktop.Views;

public sealed partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? _model;
    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private int _typingFrame;
    private readonly HashSet<WorkspaceMessage> _observed = [];
    public WorkspaceView()
    {
        InitializeComponent();
        _typingTimer.Tick += (_, _) =>
        {
            _model?.UpdateWorkingState();
            var animate = _model?.HasWorkingAgents == true && !ReducedMotion.Enabled;
            var dots = new[] { TypingDot1, TypingDot2, TypingDot3 };
            for (var i = 0; i < dots.Length; i++) dots[i].Opacity = !animate || _typingFrame % 3 == i ? 1 : .3;
            _typingFrame++;
        };
        DataContextChanged += (_, _) => Observe(DataContext as WorkspaceViewModel);
        AttachedToVisualTree += (_, _) => Observe(DataContext as WorkspaceViewModel);
        DetachedFromVisualTree += (_, _) => Observe(null);
        MessagesScroll.ScrollChanged += (_, e) =>
        {
            if (_model is null) return;
            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
            {
                if (_model.IsReadingLatest) Follow();
                return;
            }
            _model.IsReadingLatest = MessagesScroll.Offset.Y >= MessagesScroll.Extent.Height - MessagesScroll.Viewport.Height - 36;
            if (_model.IsReadingLatest) { _model.HasNewMessages = false; _model.Presented(); }
        };
    }
    private void Observe(WorkspaceViewModel? model)
    {
        if (ReferenceEquals(_model, model)) return;
        if (_model is not null)
        {
            _model.Messages.CollectionChanged -= Changed; _model.ContentLoaded -= Follow;
            _model.PropertyChanged -= ModelChanged;
            _model.IsPresented = false;
            _ = _model.SaveDraftAsync();
        }
        _typingTimer.Stop();
        foreach (var item in _observed) item.PropertyChanged -= TextChanged;
        _observed.Clear(); _model = model;
        if (model is null) return;
        model.PropertyChanged += ModelChanged;
        if (model.HasWorkingAgents) _typingTimer.Start();
        model.Messages.CollectionChanged += Changed; model.ContentLoaded += Follow;
        Track(); Follow();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.HasWorkingAgents)) return;
        if (_model?.HasWorkingAgents == true) _typingTimer.Start(); else _typingTimer.Stop();
    }
    private void Track()
    {
        if (_model is null) return;
        foreach (var old in _observed.Where(i => !_model.Messages.Contains(i)).ToList()) { old.PropertyChanged -= TextChanged; _observed.Remove(old); }
        foreach (var item in _model.Messages) if (_observed.Add(item)) item.PropertyChanged += TextChanged;
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Track();
        if (_model is { IsReadingLatest: false } && e.Action == NotifyCollectionChangedAction.Add) _model.HasNewMessages = true;
        Follow();
    }
    private void TextChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(WorkspaceMessage.Text)) Follow(); }
    private void Follow()
    {
        var model = _model;
        if (model is null) return;
        if (!model.IsReadingLatest) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_model != model) return;
            MessagesScroll.ScrollToEnd(); model.Presented();
        }, DispatcherPriority.Background);
    }
    private void ReadLatest(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        _model.IsReadingLatest = true; _model.HasNewMessages = false; Follow();
    }
}
