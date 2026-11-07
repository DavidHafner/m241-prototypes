using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Marschzeit;

public partial class MainWindow : Window
{
    readonly List<Waypoint> pts = new();
    double speed = 4, start = 8;
    Result? result;
    bool rendering;

    public MainWindow()
    {
        InitializeComponent(); DrawMap();
        ActualThemeVariantChanged += (_, _) => { if (Screen2.IsVisible) Render(); };
    }

    IBrush ThemeBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    // ---- Screen 1 ----
    void DrawMap()
    {
        for (int i = MapCanvas.Children.Count - 1; i >= 1; i--) MapCanvas.Children.RemoveAt(i);
        if (pts.Count > 1)
        {
            var line = new Polyline { Stroke = Brushes.Crimson, StrokeThickness = 3, IsHitTestVisible = false };
            line.Points = pts.Select(p => new Point(p.X, p.Y)).ToList();
            MapCanvas.Children.Add(line);
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var e = new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, Stroke = Brushes.Crimson, StrokeThickness = 3, IsHitTestVisible = false };
            Canvas.SetLeft(e, pts[i].X - 10); Canvas.SetTop(e, pts[i].Y - 10);
            var t = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeight.Bold, Width = 20, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black, IsHitTestVisible = false };
            Canvas.SetLeft(t, pts[i].X - 10); Canvas.SetTop(t, pts[i].Y - 8);
            MapCanvas.Children.Add(e); MapCanvas.Children.Add(t);
        }
        CountText.Text = $"{pts.Count} Wegpunkte";
        CalcBtn.IsEnabled = pts.Count >= 2;
    }

    void Map_Click(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(MapCanvas).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(MapCanvas);
        int hit = pts.FindIndex(p => Math.Abs(p.X - pos.X) < 10 && Math.Abs(p.Y - pos.Y) < 10);
        if (hit >= 0) pts.RemoveAt(hit); else pts.Add(new Waypoint { X = pos.X, Y = pos.Y });
        DrawMap();
    }

    void Clear_Click(object? s, RoutedEventArgs e) { pts.Clear(); DrawMap(); }
    void Calc_Click(object? s, RoutedEventArgs e) { Screen1.IsVisible = false; Screen2.IsVisible = true; Render(); }
    void Back_Click(object? s, RoutedEventArgs e) { Screen2.IsVisible = false; Screen1.IsVisible = true; }
    void Flip_Click(object? s, RoutedEventArgs e) { pts.Reverse(); DrawMap(); Render(); }

    // ---- Screen 2 ----
    void Params_Changed(object? s, RoutedEventArgs e)
    {
        if (rendering) return;
        if (double.TryParse(SpeedBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) && sp > 0) speed = sp;
        if (TimeSpan.TryParse(StartBox.Text, out var ts)) start = ts.TotalHours;
        Render();
    }

    void Grid_CellEditEnded(object? s, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.DataContext is not RowVm vm) return;
        var p = pts[vm.Nr - 1];
        p.Comment = vm.Comment; p.Pause = Math.Max(0, vm.Pause);
        Avalonia.Threading.Dispatcher.UIThread.Post(Render);
    }

    void Render()
    {
        if (pts.Count < 2) return;
        rendering = true;
        var r = result = Calc.Compute(pts, speed, start);
        Grid.ItemsSource = r.Rows.Select(w => new RowVm(w)).ToList();
        SpeedBox.Text = speed.ToString(CultureInfo.InvariantCulture);
        StartBox.Text = Calc.FTime(start);
        SumStartZiel.Text = $"Start: {r.Rows[0].Name}\nZiel: {r.Rows[^1].Name}";
        SumDist.Text = $"Horizontaldistanz: {r.Dist:0.0} km";
        SumAsc.Text = $"Auf-/Abstieg: ↑ {Math.Round(r.Ascent)} m ↓ {Math.Round(r.Descent)} m";
        SumEffort.Text = $"Aufwand: {r.Effort:0.0} Lkm";
        SumArrival.Text = $"Ankunft: {Calc.FTime(r.Arrival)}";
        SumGehzeit.Text = $"Gehzeit: {Calc.FDur(r.Gehzeit)} h";
        SumPausen.Text = $"Pausen: {Calc.FDur(r.PauseMin / 60)} h";
        DrawProfile(r);
        rendering = false;
    }

    void DrawProfile(Result r)
    {
        ProfileCanvas.Children.Clear();
        double W = Math.Max(ProfileCanvas.Bounds.Width, 800), Hh = 150;
        var samples = new List<(double d, double h)>();
        double d = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i];
            double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            int n = Math.Max(2, (int)Math.Round(len / 4));
            for (int k = i == 1 ? 0 : 1; k <= n; k++)
            {
                double t = (double)k / n;
                samples.Add((d + len * t, Calc.Terrain(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t)));
            }
            d += len;
        }
        double lo = samples.Min(s => s.h) - 20, hi = samples.Max(s => s.h) + 20;
        double X(double v) => 10 + v / d * (W - 20);
        double Y(double v) => Hh - (v - lo) / (hi - lo) * (Hh - 15);
        var pp = new List<Point> { new(X(0), Hh) };
        pp.AddRange(samples.Select(s => new Point(X(s.d), Y(s.h))));
        pp.Add(new Point(X(d), Hh));
        ProfileCanvas.Children.Add(new Polygon { Points = pp, Fill = ThemeBrush("ProfileFill", Brushes.LightGreen) });
        foreach (var w in r.Rows)
        {
            double cx = X(w.Dist * 1000 / Calc.MPerPx), cy = Y(w.Height);
            var fg = ThemeBrush("CardFg", Brushes.White);
            var dot = new Ellipse { Width = 10, Height = 10, Fill = fg, Stroke = ThemeBrush("CardBg", Brushes.Black) };
            Canvas.SetLeft(dot, cx - 5); Canvas.SetTop(dot, cy - 5);
            var tx = new TextBlock { Text = w.Nr.ToString(), Foreground = fg, FontSize = 11 };
            Canvas.SetLeft(tx, cx - 3); Canvas.SetTop(tx, cy - 22);
            ProfileCanvas.Children.Add(dot); ProfileCanvas.Children.Add(tx);
        }
    }

    async void Save_Click(object? s, RoutedEventArgs e)
    {
        if (result == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "marschzeittabelle.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file == null) return;
        await using var st = await file.OpenWriteAsync();
        st.SetLength(0);
        var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(Calc.Csv(result))).ToArray();
        await st.WriteAsync(bytes);
    }
}

public class RowVm
{
    readonly Row w;
    public RowVm(Row w) { this.w = w; }
    public int Nr => w.Nr; public string Name => w.Name; public string Coord => w.Coord;
    public string Comment { get => w.Comment; set => w.Comment = value ?? ""; }
    public int Height => w.Height;
    public double Dist => w.Dist; public double Effort => w.Effort;
    public string GehzeitText => Calc.FDur(w.Gehzeit); public string TimeText => Calc.FTime(w.Time);
    public int Pause { get => w.Pause; set => w.Pause = value; }
}
