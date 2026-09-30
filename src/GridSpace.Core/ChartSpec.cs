namespace GridSpace.Core;

// Preserve the original numeric values for schema-version-1 native workbooks.
public enum ChartKind { Column, Line, Bar, Pie, Area, Scatter, Doughnut, Radar, Combo }
public enum ChartGrouping { Clustered, Stacked, PercentStacked }
public enum ChartLegendPosition { None, Bottom, Right, Top, Left }

/// <summary>One explicitly bound series. Ranges are local A1 vectors on the chart's source sheet.</summary>
public sealed record ChartSeries
{
    public string Name { get; init; } = "";
    public string Values { get; init; } = "B2:B5";
    /// <summary>Orientation hint for a one-cell vector. Larger ranges determine their own orientation.</summary>
    public bool? ValuesHorizontal { get; init; }
    public string Color { get; init; } = "#4472C4";
    public ChartKind? Kind { get; init; }
    public bool SecondaryAxis { get; init; }
    public bool Visible { get; init; } = true;
}

/// <summary>Serializable chart document; viewport/selection/gesture state is deliberately not persisted.</summary>
public sealed record ChartSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Chart title";
    public ChartKind Kind { get; set; }
    public string Range { get; set; } = "A1:B5";
    public string? SourceSheet { get; set; }
    public bool SourceUnavailable { get; set; }
    public bool SeriesInRows { get; set; }
    public bool HasHeaders { get; set; } = true;
    public string? Categories { get; set; }
    /// <summary>Orientation hint for a one-cell category vector; not a viewport or gesture coordinate.</summary>
    public bool? CategoriesHorizontal { get; set; }
    public List<ChartSeries> Series { get; set; } = [];
    public ChartGrouping Grouping { get; set; }
    public ChartLegendPosition Legend { get; set; } = ChartLegendPosition.Bottom;
    public string CategoryAxisTitle { get; set; } = "";
    public string ValueAxisTitle { get; set; } = "";
    public string ValueFormat { get; set; } = "#,##0.##";
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool ShowGridLines { get; set; } = true;
    public bool ShowDataLabels { get; set; }
    public bool ShowMarkers { get; set; } = true;
    public bool PlotHiddenCells { get; set; }
    public int HoleSize { get; set; } = 55;
    public int GapWidth { get; set; } = 120;
    public string Background { get; set; } = "#FFFFFF";
    public string Foreground { get; set; } = "#333333";
    public string? PivotTableId { get; set; }
    public int Row { get; set; } = 3;
    public int Column { get; set; } = 8;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Width { get; set; } = 480;
    public double Height { get; set; } = 300;

    public ChartSpec CloneDocument() => this with { Series = Series.ToList() };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128 || Title is null || Title.Length > 1024)
            throw new ArgumentException("Charts require an identifier and a title of at most 1,024 characters.");
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Grouping) || !Enum.IsDefined(Legend))
            throw new ArgumentException("Unknown chart kind, grouping or legend position.");
        if (Row < 0 || Row >= CellAddress.MaxRows || Column < 0 || Column >= CellAddress.MaxColumns)
            throw new ArgumentException("The chart anchor must be inside the worksheet.");
        if (!double.IsFinite(Width) || Width < 120 || Width > 4000 || !double.IsFinite(Height) || Height < 100 || Height > 4000
            || !double.IsFinite(OffsetX) || OffsetX < 0 || OffsetX > 2_000_000
            || !double.IsFinite(OffsetY) || OffsetY < 0 || OffsetY > 30_000_000)
            throw new ArgumentException("Invalid chart geometry.");
        if (Minimum is { } min && !double.IsFinite(min) || Maximum is { } max && !double.IsFinite(max)
            || Minimum is { } lower && Maximum is { } upper && lower >= upper)
            throw new ArgumentException("Axis limits must be finite and minimum must be below maximum.");
        if (HoleSize is < 10 or > 90 || GapWidth is < 0 or > 500)
            throw new ArgumentException("Doughnut hole must be 10–90%; gap width must be 0–500%.");
        if (CategoryAxisTitle is null || CategoryAxisTitle.Length > 1024 || ValueAxisTitle is null || ValueAxisTitle.Length > 1024
            || ValueFormat is null || ValueFormat.Length > 256) throw new ArgumentException("Invalid chart text or number format.");
        if (Series is null || Series.Count > 32) throw new ArgumentException("A chart supports at most 32 series.");
        if (SourceSheet is not null) Workbook.ValidateSheetName(SourceSheet);
        if (Categories is not null) ValidateVector(Categories);
        if (CellRange.Parse(Range).Count > 100_000) throw new ArgumentException("Chart data is limited to 100,000 cells.");
        foreach (var series in Series)
        {
            ArgumentNullException.ThrowIfNull(series);
            if (series.Name is null || series.Name.Length > 1024) throw new ArgumentException("Invalid series name.");
            ValidateVector(series.Values);
            if (series.Kind is { } kind && kind is not ChartKind.Column and not ChartKind.Line and not ChartKind.Area)
                throw new ArgumentException("Combination series support column, line and area kinds.");
            ValidateColor(series.Color);
        }
        ValidateColor(Background); ValidateColor(Foreground);
    }

    private static void ValidateVector(string text)
    {
        var range = CellRange.Parse(text);
        if (range.Count > 100_000 || range.Left != range.Right && range.Top != range.Bottom)
            throw new ArgumentException("A series range must be a row or column vector of at most 100,000 cells.");
    }

    public static void ValidateColor(string text)
    {
        if (text is null || text.Length != 7 || text[0] != '#' || text.Skip(1).Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Use a six-digit color, such as #4472C4.");
    }
}
