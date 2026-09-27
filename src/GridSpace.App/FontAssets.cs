using GridSpace.Controls;
using GridSpace.Skia;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace GridSpace.App;

internal static class FontAssets
{
    public static async Task LoadAsync(TypefaceCatalog fonts)
    {
        var bytes = new List<byte[]>(4);
        foreach (var style in new[] { "Regular", "Bold", "Italic", "BoldItalic" })
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/Carlito-" + style + ".ttf"));
            using var stream = await file.OpenStreamForReadAsync(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory); bytes.Add(memory.ToArray());
        }
        fonts.Register("Carlito", bytes[0], bytes[1], bytes[2], bytes[3], "Arial", "Calibri", "Aptos");
        OfficeTheme.Font = new FontFamily("ms-appx:///Assets/Fonts/Carlito-Regular.ttf#Carlito");
    }
}
