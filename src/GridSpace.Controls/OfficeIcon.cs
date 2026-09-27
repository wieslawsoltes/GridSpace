using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace GridSpace.Controls;

public enum OfficeIconKind { New, Open, Save, Undo, Redo, Cut, Copy, Paste, Bold, Italic, Underline, Border, Fill, Font, AlignLeft, AlignCenter, AlignRight, Wrap, Merge, Sum, Sort, Filter, Find, Chart, InsertRow, InsertColumn, Clear, Freeze, Grid, Note, Print, Help, Plus, Close, Check, ChevronDown }

/// <summary>Original vector iconography, independent of platform symbol fonts or proprietary assets.</summary>
public sealed class OfficeIcon : SKCanvasElement
{
    public OfficeIconKind Kind { get; set; }
    public string Ink { get; set; } = "#454545";
    public OfficeIcon() { Width = 20; Height = 20; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)area.Width / 24, (float)area.Height / 24);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(Ink), Style = SKPaintStyle.Stroke, StrokeWidth = 1.45f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        void Path(string data) { using var path = SKPath.ParseSvgPathData(data); canvas.DrawPath(path, paint); }
        switch (Kind)
        {
            case OfficeIconKind.New: Path("M6 3H15L19 7V21H6Z M15 3V8H19 M9 12H16 M9 16H16"); break;
            case OfficeIconKind.Open: Path("M3 7H10L12 9H21L18 20H3Z M3 7V4H10L12 6H19V9"); break;
            case OfficeIconKind.Save: Path("M4 3H18L21 6V21H3V3Z M7 3V9H17V3 M7 21V13H17V21 M14 5V7"); break;
            case OfficeIconKind.Undo: Path("M8 4L3 9L8 14 M3 9H14C23 9 23 20 13 20"); break;
            case OfficeIconKind.Redo: Path("M16 4L21 9L16 14 M21 9H10C1 9 1 20 11 20"); break;
            case OfficeIconKind.Copy: Path("M8 7H20V21H8Z M5 17H3V3H15V5"); break;
            case OfficeIconKind.Paste: Path("M8 5H4V21H20V5H16 M9 3H15V7H9Z M8 12H16 M8 16H16"); break;
            case OfficeIconKind.Cut: canvas.DrawCircle(6, 18, 3, paint); canvas.DrawCircle(18, 18, 3, paint); Path("M8 16L20 3 M16 16L4 3"); break;
            case OfficeIconKind.Bold: paint.StrokeWidth = 2.5f; Path("M7 4V20H13C21 20 21 12 13 12H7 M7 4H12C20 4 20 12 12 12"); break;
            case OfficeIconKind.Italic: Path("M11 4H19 M5 20H13 M15 4L9 20"); break;
            case OfficeIconKind.Underline: Path("M6 4V12C6 20 18 20 18 12V4 M4 22H20"); break;
            case OfficeIconKind.Border: Path("M3 3H21V21H3Z M12 3V21 M3 12H21"); break;
            case OfficeIconKind.Fill: Path("M8 3L18 13L11 20L2 11L10 3 M3 11H16 M19 14C15 20 23 20 19 14Z"); paint.Color = SKColor.Parse("#FFC000"); paint.StrokeWidth = 3; Path("M2 23H22"); break;
            case OfficeIconKind.Font: Path("M5 18L12 3L19 18 M8 13H16"); paint.Color = SKColor.Parse("#C4314B"); paint.StrokeWidth = 3; Path("M3 22H21"); break;
            case OfficeIconKind.AlignLeft: Path("M3 5H21 M3 10H15 M3 15H21 M3 20H15"); break;
            case OfficeIconKind.AlignCenter: Path("M3 5H21 M6 10H18 M3 15H21 M6 20H18"); break;
            case OfficeIconKind.AlignRight: Path("M3 5H21 M9 10H21 M3 15H21 M9 20H21"); break;
            case OfficeIconKind.Wrap: Path("M3 5H21 M3 11H16C22 11 22 18 16 18H10 M13 15L10 18L13 21 M3 18H6"); break;
            case OfficeIconKind.Merge: Path("M3 4H21V20H3Z M12 4V7 M12 17V20 M5 12H19 M8 9L5 12L8 15 M16 9L19 12L16 15"); break;
            case OfficeIconKind.Sum: Path("M19 4H5L13 12L5 20H19"); break;
            case OfficeIconKind.Sort: Path("M17 3V21 M13 17L17 21L21 17 M3 8L6 2L9 8 M4 6H8 M3 14H9L3 21H9"); break;
            case OfficeIconKind.Filter: Path("M3 4H21L14 12V20L10 22V12Z"); break;
            case OfficeIconKind.Find: canvas.DrawCircle(10, 10, 6, paint); Path("M15 15L21 21"); break;
            case OfficeIconKind.Chart: paint.Color = SKColor.Parse("#107C41"); Path("M4 21V13H8V21 M10 21V4H14V21 M16 21V9H20V21 M2 22H22"); break;
            case OfficeIconKind.InsertRow: Path("M3 3H21V21H3Z M3 8H21 M3 15H21 M12 3V8"); paint.Color = SKColor.Parse("#107C41"); Path("M8 11H16 M12 9V14"); break;
            case OfficeIconKind.InsertColumn: Path("M3 3H21V21H3Z M8 3V21 M16 3V21 M3 9H8 M16 9H21"); paint.Color = SKColor.Parse("#107C41"); Path("M10 12H14 M12 10V14"); break;
            case OfficeIconKind.Clear: Path("M3 15L13 4L21 11L12 21H8Z M8 10L16 17 M12 21H22"); break;
            case OfficeIconKind.Freeze: Path("M3 3H21V21H3Z M3 9H21 M9 3V21"); paint.Color = SKColor.Parse("#21A366"); paint.StrokeWidth = 3; Path("M4 5H20"); break;
            case OfficeIconKind.Grid: Path("M3 3H21V21H3Z M3 9H21 M3 15H21 M9 3V21 M15 3V21"); break;
            case OfficeIconKind.Note: Path("M3 4H21V17H10L5 21V17H3Z M7 8H17 M7 12H15"); break;
            case OfficeIconKind.Print: Path("M7 9V3H17V9 M7 17H3V9H21V17H17 M7 14H17V22H7Z M17 12H18"); break;
            case OfficeIconKind.Help: canvas.DrawCircle(12, 12, 9, paint); Path("M9 8C9 3 18 5 15 10L12 13V15 M12 18V18.1"); break;
            case OfficeIconKind.Plus: Path("M12 4V20 M4 12H20"); break;
            case OfficeIconKind.Close: Path("M6 6L18 18 M18 6L6 18"); break;
            case OfficeIconKind.Check: Path("M4 12L10 18L21 5"); break;
            case OfficeIconKind.ChevronDown: Path("M5 9L12 16L19 9"); break;
        }
        canvas.Restore();
    }
}
