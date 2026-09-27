using GridSpace.Core;
using SkiaSharp;

namespace GridSpace.Skia;

/// <summary>Owns registered font families independently from its bounded native-font cache.</summary>
public sealed class TypefaceCatalog : IDisposable
{
    private readonly Dictionary<string, SKTypeface[]> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _native = [];
    private bool _disposed;
    public int RegisteredFamilyCount => _families.Count;

    /// <summary>Loads independent native typefaces from caller-owned bytes. The catalog owns the resulting handles, not the byte arrays.</summary>
    public void Register(string family, byte[] regular, byte[] bold, byte[] italic, byte[] boldItalic, params string[] aliases)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentException.ThrowIfNullOrWhiteSpace(family);
        var loaded = new List<SKTypeface>(4);
        try
        {
            foreach (var bytes in new[] { regular, bold, italic, boldItalic })
            {
                using var data = SKData.CreateCopy(bytes);
                loaded.Add(SKTypeface.FromData(data) ?? throw new InvalidDataException("Invalid font data for " + family));
            }
            if (_families.Remove(family, out var old)) foreach (var face in old) face.Dispose();
            _families.Add(family, loaded.ToArray()); loaded.Clear();
            _aliases[family] = family; foreach (var alias in aliases) _aliases[alias] = family;
        }
        finally { foreach (var face in loaded) face.Dispose(); }
    }
    public SKTypeface Resolve(CellStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aliases.TryGetValue(style.FontFamily, out var family) && _families.TryGetValue(family, out var faces)) return faces[(style.Bold ? 1 : 0) + (style.Italic ? 2 : 0)];
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (_native.TryGetValue(key, out var cached)) return cached;
        if (_native.Count >= 128) { foreach (var face in _native.Values) face.Dispose(); _native.Clear(); }
        var weight = style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        SKTypeface? result = null;
        foreach (var candidate in new[] { style.FontFamily, "Arial", "Liberation Sans", "DejaVu Sans", "Noto Sans" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var face = SKTypeface.FromFamilyName(candidate, weight, SKFontStyleWidth.Normal, slant);
            if (face is null) continue;
            if (face.FamilyName.Equals(candidate, StringComparison.OrdinalIgnoreCase)) { result = face; break; }
            face.Dispose();
        }
        result ??= SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, slant) ?? throw new InvalidOperationException("No usable typeface is available. Register a font family before rendering.");
        _native.Add(key, result); return result;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var face in _native.Values) face.Dispose(); _native.Clear();
        foreach (var family in _families.Values) foreach (var face in family) face.Dispose(); _families.Clear(); _aliases.Clear();
    }
}
