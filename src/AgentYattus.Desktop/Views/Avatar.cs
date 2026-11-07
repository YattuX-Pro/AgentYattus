using Avalonia.Data.Converters;
using AgentYattus.Desktop.Converters;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AgentYattus.Desktop.Views;

/// <summary>
/// Le visage rond d'un collègue, partout où l'on parle d'un agent. Le portrait (560 × 560, tête en haut au centre)
/// est zoomé ×2 sur le visage ; sans photo, les initiales du prénom. Un anneau optionnel dit son état
/// (vert-de-gris : choisi ou au travail, laiton : il frappe à votre porte), et change de couleur en fondu.
/// </summary>
public sealed class Avatar : Border
{
    public static readonly StyledProperty<byte[]?> ImageProperty = AvaloniaProperty.Register<Avatar, byte[]?>(nameof(Image));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Avatar, string?>(nameof(Label));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Avatar, double>(nameof(Size), 36);
    public static readonly StyledProperty<IBrush?> RingProperty = AvaloniaProperty.Register<Avatar, IBrush?>(nameof(Ring));
    public static readonly StyledProperty<double> RingWidthProperty = AvaloniaProperty.Register<Avatar, double>(nameof(RingWidth), 2);

    private static readonly IBrush Empty = new SolidColorBrush(Color.Parse("#1D2024"));
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _initials = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#A7A196")),
    };
    private readonly Canvas _canvas = new();
    private readonly Border _face = new() { ClipToBounds = true, Background = Empty };

    public Avatar()
    {
        _canvas.Children.Add(_image);
        _face.Child = new Panel { Children = { _initials, _canvas } };
        Child = _face;
        BorderBrush = Brushes.Transparent;
        Transitions = [new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        Layout();
    }

    public byte[]? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }

    /// <summary>Le prénom : son initiale remplace la photo absente, et il sert de nom accessible.</summary>
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>La couleur de l'anneau ; null : pas d'anneau.</summary>
    public IBrush? Ring { get => GetValue(RingProperty); set => SetValue(RingProperty, value); }

    /// <summary>L'épaisseur de l'anneau (2 px par défaut), séparé du visage par 2 px de vide.</summary>
    public double RingWidth { get => GetValue(RingWidthProperty); set => SetValue(RingWidthProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageProperty)
        {
            _image.Source = BytesToBitmapConverter.Instance.Convert(Image, typeof(Bitmap), null, System.Globalization.CultureInfo.InvariantCulture) as Bitmap;
            _initials.IsVisible = _image.Source is null;
        }
        else if (change.Property == LabelProperty)
        {
            _initials.Text = string.IsNullOrWhiteSpace(Label) ? "?" : Label.Trim()[..1].ToUpperInvariant();
            AutomationProperties.SetName(this, Label);
        }
        else if (change.Property == SizeProperty || change.Property == RingProperty || change.Property == RingWidthProperty
                 || change.Property == BackgroundProperty)
            Layout();
    }

    private void Layout()
    {
        // Comme le box-shadow de la maquette, l'anneau est dessiné autour du visage, sans le réduire ni décaler la mise en page.
        var inner = Size;
        // Le vide de 2 px prend la couleur de Background (le fond derrière) : des visages qui se chevauchent restent détachés.
        var ring = Ring is null ? 0 : RingWidth;
        var gap = Ring is null && Background is null ? 0 : 2;
        var size = inner + 2 * (ring + gap);
        Width = Height = size;
        Margin = new Thickness(-(ring + gap));
        CornerRadius = new CornerRadius(size / 2);
        BorderThickness = new Thickness(ring);
        Padding = new Thickness(gap);
        if (Ring is not null)
            BorderBrush = Ring;
        _face.Width = _face.Height = inner;
        _face.CornerRadius = new CornerRadius(inner / 2);
        _image.Width = _image.Height = inner * 2;
        Canvas.SetLeft(_image, -inner / 2);
        // Maquette : l'image fait 200 % et remonte de 8 % pour cadrer le visage.
        Canvas.SetTop(_image, -inner * 0.08);
        _initials.FontSize = Math.Max(9, inner * 0.38);
    }
}

/// <summary>Vrai → l'anneau vert-de-gris d'un collègue choisi ; faux → pas d'anneau.</summary>
public static class RingConverter
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#5E9E92"));

    public static readonly Avalonia.Data.Converters.IValueConverter Selected =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush?>(on => on ? Green : null);
}

/// <summary>Un prénom → son initiale, pour une photo absente.</summary>
public static class Initial
{
    public static readonly Avalonia.Data.Converters.IValueConverter Of =
        new Avalonia.Data.Converters.FuncValueConverter<string?, string>(name => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant());
}

/// <summary>Le taux de réussite (0 à 1, -1 sans chiffres) : sa couleur (comme l'admin web) et la largeur de sa barre sur une carte.</summary>
public static class RateBrush
{
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#5E9E92"));
    private static readonly IBrush Fair = new SolidColorBrush(Color.Parse("#D8B86E"));
    private static readonly IBrush Poor = new SolidColorBrush(Color.Parse("#D9654B"));
    private static readonly IBrush None = new SolidColorBrush(Color.Parse("#8A857B"));

    public static readonly Avalonia.Data.Converters.IValueConverter Of =
        new Avalonia.Data.Converters.FuncValueConverter<double, IBrush>(rate => rate < 0 ? None : rate >= 0.8 ? Good : rate >= 0.5 ? Fair : Poor);

    /// <summary>La barre d'une carte d'agent : sa part de la piste (taux, largeur de la piste).</summary>
    public static readonly IMultiValueConverter Share = new FuncMultiValueConverter<double, double>(v =>
    {
        var values = v.ToList();
        return values.Count == 2 ? Math.Clamp(values[0], 0, 1) * values[1] : 0;
    });
}

/// <summary>Vos bulles à droite, celles des agents à gauche.</summary>
public static class Side
{
    public static readonly Avalonia.Data.Converters.IValueConverter Of =
        new Avalonia.Data.Converters.FuncValueConverter<bool, HorizontalAlignment>(you => you ? HorizontalAlignment.Right : HorizontalAlignment.Left);
}

/// <summary>L'infobulle de votre visage : le serveur répond-il ?</summary>
public static class Backend
{
    public static readonly IValueConverter Tip =
        new FuncValueConverter<bool, string>(ok => ok ? "Connecté à localhost:5080" : "Serveur injoignable (localhost:5080)");
}

/// <summary>La couleur de l'état d'un agent : vert-de-gris en mission, laiton s'il attend votre accord.</summary>
public static class StateBrush
{
    private static readonly IBrush Busy = new SolidColorBrush(Color.Parse("#5E9E92"));
    private static readonly IBrush Waiting = new SolidColorBrush(Color.Parse("#D8B86E"));
    private static readonly IBrush Free = new SolidColorBrush(Color.Parse("#A7A196"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#3A3F45"));

    public static readonly IValueConverter Of = new FuncValueConverter<AgentYattus.Client.Core.AgentState, IBrush>(s => s switch
    {
        AgentYattus.Client.Core.AgentState.Busy => Busy,
        AgentYattus.Client.Core.AgentState.Waiting => Waiting,
        _ => Free,
    });

    /// <summary>La pastille sous un visage de l'accueil : grise quand il est libre.</summary>
    public static readonly IValueConverter Dot = new FuncValueConverter<AgentYattus.Client.Core.AgentState, IBrush>(s => s switch
    {
        AgentYattus.Client.Core.AgentState.Busy => Busy,
        AgentYattus.Client.Core.AgentState.Waiting => Waiting,
        _ => Off,
    });
}

/// <summary>La teinte d'une mission : vert-de-gris en cours, laiton à valider, cendre terminée, cinabre en échec.</summary>
public static class Tone
{
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#5E9E92"));
    private static readonly IBrush Awaiting = new SolidColorBrush(Color.Parse("#D8B86E"));
    private static readonly IBrush Done = new SolidColorBrush(Color.Parse("#8A857B"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#D9654B"));
    private static readonly IBrush Parchment = new SolidColorBrush(Color.Parse("#ECE6D8"));

    /// <summary>La pastille d'un rapport : vert-de-gris abouti, cinabre en échec.</summary>
    public static readonly IValueConverter Report = new FuncValueConverter<AgentYattus.Contracts.MissionStatus, IBrush>(s =>
        s == AgentYattus.Contracts.MissionStatus.Failed ? Failed : Active);

    /// <summary>L'anneau de l'agent dans un rapport : cinabre si la mission a échoué.</summary>
    public static readonly IValueConverter Agent = new FuncValueConverter<bool, IBrush>(failed => failed ? Failed : Active);

    /// <summary>La note d'un outil : laiton s'il demande votre accord.</summary>
    public static readonly IValueConverter Warn = new FuncValueConverter<bool, IBrush>(w => w ? Awaiting : Done);

    /// <summary>Un compte d'écritures approuvées : laiton dès qu'il y en a une.</summary>
    public static readonly IValueConverter Count = new FuncValueConverter<int, IBrush>(n => n > 0 ? Awaiting : Parchment);

    public static readonly IValueConverter Of = new FuncValueConverter<AgentYattus.Contracts.MissionStatus, IBrush>(s => s switch
    {
        AgentYattus.Contracts.MissionStatus.AwaitingApproval => Awaiting,
        AgentYattus.Contracts.MissionStatus.Completed => Done,
        AgentYattus.Contracts.MissionStatus.Failed => Failed,
        _ => Active,
    });
}

/// <summary>Le cadrage d'un portrait dans une tuile carrée (maquette : 200 %, -50 %, -8 %).</summary>
public static class Zoom
{
    public static readonly IValueConverter Double = new FuncValueConverter<double, double>(w => w * 2);
    public static readonly IValueConverter Left = new FuncValueConverter<double, double>(w => -w / 2);
    public static readonly IValueConverter Top = new FuncValueConverter<double, double>(w => -w * 0.08);
}

/// <summary>La pastille d'état de la mission, dans la barre de l'open space (maquette : en cours, attend votre accord, terminée).</summary>
public static class Chip
{
    private static bool Pending(AgentYattus.Contracts.MissionStatus s) => s == AgentYattus.Contracts.MissionStatus.AwaitingApproval;
    private static bool Finished(AgentYattus.Contracts.MissionStatus s) => s is AgentYattus.Contracts.MissionStatus.Completed
        or AgentYattus.Contracts.MissionStatus.Failed or AgentYattus.Contracts.MissionStatus.Cancelled;

    public static readonly IValueConverter Label = new FuncValueConverter<AgentYattus.Contracts.MissionStatus, string>(s =>
        Pending(s) ? "ATTEND VOTRE ACCORD" : s == AgentYattus.Contracts.MissionStatus.Failed ? "ÉCHEC" : Finished(s) ? "TERMINÉE" : "EN COURS");

    public static readonly IValueConverter Fg = new FuncValueConverter<AgentYattus.Contracts.MissionStatus, IBrush>(s =>
        new SolidColorBrush(Color.Parse(Pending(s) ? "#D8B86E" : s == AgentYattus.Contracts.MissionStatus.Failed ? "#D9654B" : Finished(s) ? "#A7A196" : "#5E9E92")));

    public static readonly IValueConverter Bg = new FuncValueConverter<AgentYattus.Contracts.MissionStatus, IBrush>(s =>
        new SolidColorBrush(Color.Parse(Pending(s) ? "#21D8B86E" : s == AgentYattus.Contracts.MissionStatus.Failed ? "#21D9654B" : Finished(s) ? "#1D2024" : "#245E9E92")));
}
