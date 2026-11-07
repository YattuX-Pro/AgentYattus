using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace AgentYattus.Desktop.Converters;

/// <summary>
/// Les octets d'un portrait (reçus de l'API) en image affichable. Client.Core ne connaît pas Avalonia :
/// il expose des octets, et la conversion se fait ici, dans la vue. Une image illisible n'affiche rien.
/// </summary>
public sealed class BytesToBitmapConverter : IValueConverter
{
    public static readonly BytesToBitmapConverter Instance = new();

    // ponytail: un cache par tableau d'octets ; les mêmes portraits reviennent à chaque réplique. Sans limite :
    // une équipe n'a que quelques visages.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Bitmap> _cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
            return null;
        if (_cache.TryGetValue(bytes, out var cached))
            return cached;
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _cache.AddOrUpdate(bytes, bitmap);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
