namespace GridSpace.Workbench;

public sealed partial class SpreadsheetWorkbench
{
    private ContentDialog Dialog(string title, object content, string primary = "OK") => new() { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, RequestedTheme = ElementTheme.Light };
    private async Task<string?> PromptAsync(string title, string label, string value, bool multiline = false)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 320 }; panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        var input = OfficeTheme.Field(label); input.Text = value; input.AcceptsReturn = multiline; input.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap; if (multiline) input.Height = 140;
        panel.Children.Add(input); var dialog = Dialog(title, panel); dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task<bool> ConfirmAsync(string title, string message) => await Dialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, "Continue").ShowAsync() == ContentDialogResult.Primary;
    private async Task MessageAsync(string title, string message)
    {
        var dialog = Dialog(title, new ScrollViewer { MaxHeight = 440, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 530, FontSize = 13 } });
        dialog.CloseButtonText = ""; await dialog.ShowAsync();
    }
    private async Task FindAsync(bool replace)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 320 }; panel.Children.Add(OfficeTheme.Label("Find what:"));
        var find = OfficeTheme.Field("Find what"); panel.Children.Add(find); var replacement = OfficeTheme.Field("Replace with");
        if (replace) { panel.Children.Add(OfficeTheme.Label("Replace with:")); panel.Children.Add(replacement); }
        var dialog = Dialog(replace ? "Find and Replace" : "Find", panel, replace ? "Replace All" : "Find Next");
        dialog.Opened += (_, _) => find.Focus(FocusState.Programmatic);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (replace) ShowStatus("Replaced " + Session.ReplaceAll(find.Text, replacement.Text) + " cells");
        else if (Session.Find(find.Text) is { } address) { Surface.RevealSelection(); ShowStatus("Found " + address); }
        else ShowStatus("No matching cells", true);
    }
    private async Task FormatCellsAsync()
    {
        var style = Session.SelectedStyle; var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        var family = OfficeTheme.Field("Font family"); family.Text = style.FontFamily;
        var size = OfficeTheme.Field("Font size"); size.Text = style.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var format = OfficeTheme.Field("Number format"); format.Text = style.NumberFormat;
        var foreground = OfficeTheme.Field("Font color"); foreground.Text = style.Foreground;
        var background = OfficeTheme.Field("Fill color"); background.Text = style.Background;
        foreach (var item in new[] { ("Font family", family), ("Size (points)", size), ("Number format", format), ("Font color (#RRGGBB)", foreground), ("Fill color (#RRGGBB)", background) }) { panel.Children.Add(OfficeTheme.Label(item.Item1)); panel.Children.Add(item.Item2); }
        var bold = new CheckBox { Content = "Bold", IsChecked = style.Bold }; var italic = new CheckBox { Content = "Italic", IsChecked = style.Italic }; var wrap = new CheckBox { Content = "Wrap text", IsChecked = style.Wrap };
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; flags.Children.Add(bold); flags.Children.Add(italic); flags.Children.Add(wrap); panel.Children.Add(flags);
        if (await Dialog("Format Cells", panel).ShowAsync() != ContentDialogResult.Primary) return;
        var points = ParseNumber(size.Text, 4, 200); ValidateColor(foreground.Text); ValidateColor(background.Text);
        Session.ApplyStyle(s => s with { FontFamily = family.Text, FontSize = points, NumberFormat = format.Text, Foreground = foreground.Text, Background = background.Text, Bold = bold.IsChecked == true, Italic = italic.IsChecked == true, Wrap = wrap.IsChecked == true });
    }
    private static void ValidateColor(string color)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Use a six-digit color such as #107C41.");
    }
    private async Task PickColorAsync(bool fill)
    {
        var palette = new[] { "#FFFFFF", "#000000", "#242424", "#666666", "#107C41", "#21A366", "#DCEDE2", "#E8F2EC", "#FFC000", "#FFF2CC", "#C00000", "#F4CCCC", "#0078D4", "#DDEBF7", "#7030A0", "#E4DFEC" };
        var root = new StackPanel { Spacing = 12 }; var grid = new Grid();
        for (var i = 0; i < 8; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        var input = OfficeTheme.Field("Hex color"); input.Text = fill ? Session.SelectedStyle.Background : Session.SelectedStyle.Foreground;
        for (var i = 0; i < palette.Length; i++)
        {
            var color = palette[i]; var button = new OfficeButton { Width = 30, Height = 30, Background = OfficeTheme.Brush(color), BorderBrush = OfficeTheme.Brush("#B8B8B8"), BorderThickness = new Thickness(1) };
            AutomationProperties.SetName(button, color); button.Click += (_, _) => input.Text = color; Grid.SetRow(button, i / 8); Grid.SetColumn(button, i % 8); grid.Children.Add(button);
        }
        root.Children.Add(grid); root.Children.Add(input);
        if (await Dialog(fill ? "Fill Color" : "Font Color", root).ShowAsync() != ContentDialogResult.Primary) return;
        ValidateColor(input.Text); Session.ApplyStyle(s => fill ? s with { Background = input.Text } : s with { Foreground = input.Text });
    }
    private async Task CellStyleAsync()
    {
        var styles = new[] { ("Normal", CellStyle.Default), ("Good", new CellStyle { Background = "#C6EFCE", Foreground = "#006100" }), ("Bad", new CellStyle { Background = "#FFC7CE", Foreground = "#9C0006" }), ("Neutral", new CellStyle { Background = "#FFEB9C", Foreground = "#9C6500" }), ("Heading", new CellStyle { Bold = true, FontSize = 16, Foreground = "#107C41" }), ("Input", new CellStyle { Background = "#FFF2CC", Foreground = "#0000FF" }), ("Total", new CellStyle { Bold = true, Border = true, Background = "#DCEDE2", NumberFormat = "#,##0" }) };
        var panel = new StackPanel { Spacing = 6, MinWidth = 280 }; CellStyle? selected = null;
        var dialog = Dialog("Cell Styles", panel); dialog.PrimaryButtonText = ""; dialog.CloseButtonText = "Close";
        foreach (var item in styles) { var button = new OfficeButton(item.Item1, () => { selected = item.Item2; dialog.Hide(); }) { Background = OfficeTheme.Brush(item.Item2.Background), Foreground = OfficeTheme.Brush(item.Item2.Foreground), Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(button); }
        await dialog.ShowAsync(); if (selected is not null) Session.ApplyStyle(_ => selected);
    }
    private async Task<string?> ChooseCommandAsync(string title, IEnumerable<(string Id, string Label)> items)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 300 }; string? selected = null;
        var dialog = Dialog(title, new ScrollViewer { MaxHeight = 420, Content = panel }); dialog.PrimaryButtonText = ""; dialog.CloseButtonText = "Close";
        foreach (var item in items) { var button = new OfficeButton(item.Label, () => { selected = item.Id; dialog.Hide(); }) { Height = 35, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left }; panel.Children.Add(button); }
        await dialog.ShowAsync(); return selected;
    }
    private async Task BackstageAsync()
    {
        var selected = await ChooseCommandAsync("File · " + Session.Book.Title, new[] { ("new", "New workbook"), ("open", "Open…"), ("save", "Save native workbook (.gridspace)"), ("export-xlsx", "Export Excel workbook (.xlsx)"), ("export-csv", "Export active sheet (.csv)"), ("rename-workbook", "Rename workbook"), ("sample", "Open sample workbook"), ("about", "About GridSpace") });
        if (selected is not null) await ExecuteCoreAsync(selected);
    }
    private async Task SheetMenuAsync()
    {
        var selected = await ChooseCommandAsync(Session.Sheet.Name, new[] { ("rename-sheet", "Rename…"), ("duplicate-sheet", "Move or Copy…"), ("add-sheet", "Insert worksheet"), ("delete-sheet", "Delete worksheet") });
        if (selected is not null) await ExecuteCoreAsync(selected);
    }
    private async Task SearchCommandsAsync()
    {
        var query = await PromptAsync("Search Commands", "Type part of a command name", ""); if (query is null) return;
        var matches = WorkbookRibbon.Create().SelectMany(t => t.Groups).SelectMany(g => g.Commands).Where(c => c.Enabled && c.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).DistinctBy(c => c.Id).Take(25).Select(c => (c.Id, c.Label.Replace('\n', ' '))).ToArray();
        if (matches.Length == 0) { ShowStatus("No matching commands"); return; }
        var selected = await ChooseCommandAsync("Commands", matches); if (selected is not null) await ExecuteCoreAsync(selected);
    }
}
