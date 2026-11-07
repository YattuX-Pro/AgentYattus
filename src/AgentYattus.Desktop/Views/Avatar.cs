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
        else if (change.Property == SizeProperty || change.Property == RingProperty)
            Layout();
    }

    private void Layout()
    {
        var size = Size;
        // Un anneau de 2 px, séparé du visage par 2 px de vide.
        var ring = Ring is null ? 0 : 2;
        var inner = size - ring * 4;
        Width = Height = size;
        CornerRadius = new CornerRadius(size / 2);
        BorderThickness = new Thickness(ring);
        Padding = new Thickness(ring);
        if (Ring is not null)
            BorderBrush = Ring;
        _face.Width = _face.Height = inner;
        _face.CornerRadius = new CornerRadius(inner / 2);
        _image.Width = _image.Height = inner * 2;
        Canvas.SetLeft(_image, -inner / 2);
        Canvas.SetTop(_image, -inner / 8);
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
    private static readonly IBrush None = new SolidColorBrush(Color.Parse("#938E83"));

    public static readonly Avalonia.Data.Converters.IValueConverter Of =
        new Avalonia.Data.Converters.FuncValueConverter<double, IBrush>(rate => rate < 0 ? None : rate >= 0.8 ? Good : rate >= 0.5 ? Fair : Poor);

    /// <summary>La barre d'une carte d'agent : 218 px de large au plus (la carte moins ses marges).</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter Width =
        new Avalonia.Data.Converters.FuncValueConverter<double, double>(rate => Math.Clamp(rate, 0, 1) * 218);
}

/// <summary>Vos bulles à droite, celles des agents à gauche.</summary>
public static class Side
{
    public static readonly Avalonia.Data.Converters.IValueConverter Of =
        new Avalonia.Data.Converters.FuncValueConverter<bool, HorizontalAlignment>(you => you ? HorizontalAlignment.Right : HorizontalAlignment.Left);
}
