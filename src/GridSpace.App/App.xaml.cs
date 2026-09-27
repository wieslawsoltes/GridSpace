using GridSpace.Controls;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using GridSpace.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GridSpace.App;

public partial class App : Application
{
    private Window? _window;
    private SpreadsheetWorkbench? _workbench;
#if __WASM__
    private BrowserDiagnostics? _diagnostics;
#endif
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "GridSpace" };
        _window.Content = new Grid { Background = OfficeTheme.Brush("#FFFFFF"), Children = { new TextBlock { Text = "GridSpace", FontSize = 28, Foreground = OfficeTheme.Brush("#107C41"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } }; _window.Activate();
        try
        {
#if __WASM__
            IWorkbookStorage storage = new BrowserWorkbookStorage();
#else
            IWorkbookStorage storage = new DesktopWorkbookStorage();
#endif
            var book = SampleWorkbook.Create(); string? warning = null;
            try { var recovery = await storage.ReadRecoveryAsync(); if (!string.IsNullOrWhiteSpace(recovery)) book = Workbook.FromJson(recovery); }
            catch (Exception error) { warning = "The recovery copy could not be opened: " + error.Message; }
            OfficeTheme.Font = new FontFamily("ms-appx:///Assets/Fonts/Carlito-Regular.ttf#Carlito");
            var session = new SpreadsheetSession(book); _workbench = new SpreadsheetWorkbench(session, storage);
            await FontAssets.LoadAsync(_workbench.Surface.Renderer.Fonts);
            _window.Content = _workbench;
#if __WASM__
            if (BrowserFiles.IsTestMode()) _diagnostics = new BrowserDiagnostics(session, _workbench);
#endif
            _window.Closed += (_, _) =>
            {
#if __WASM__
                _diagnostics?.Dispose();
#endif
                _workbench.Dispose();
            };
            if (warning is not null) _workbench.ShowStatus(warning, true);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); _workbench?.Dispose();
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "GridSpace could not start.\n\n" + error.Message + "\n\nYour recovery data has not been deleted. Reload to retry.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(35), FontSize = 16 } };
        }
    }
}
