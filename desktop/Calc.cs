using System.Globalization;
using System.Text;

namespace Marschzeit;

public class Waypoint
{
    public double X, Y;           // map pixels (800x600)
    public string Comment = "";
    public int Pause;             // minutes
}

public class Row
{
    public int Nr; public string Name = "", Comment = "", Coord = "";
    public int Height, Pause;
    public double DHeight, Dist, DDist, Effort, DEffort, Gehzeit, DGehzeit, Time;
}

public class Result
{
    public List<Row> Rows = new();
    public double Dist, Effort, Ascent, Descent, Gehzeit, PauseMin, Arrival;
}

public static class Calc
{
    public const double MPerPx = 10, MapW = 800, MapH = 600;

    // Fake terrain, same function as the web prototype
    public static double Terrain(double x, double y) => 800
        + 300 * Math.Exp(-(Math.Pow(x - 500, 2) + Math.Pow(y - 300, 2)) / (2 * 150 * 150))
        + 100 * Math.Exp(-(Math.Pow(x - 200, 2) + Math.Pow(y - 400, 2)) / (2 * 100 * 100));

    // Rules of ckolin/marschzeittabelle: 100 m ascent = 1 Lkm, descent only when slope < -20% (150 m = 1 Lkm)
    public static Result Compute(IList<Waypoint> pts, double speed, double start)
    {
        var res = new Result();
        double dist = 0, effort = 0, brk = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var r = new Row
            {
                Nr = i + 1, Name = "Punkt " + (i + 1), Comment = p.Comment, Pause = p.Pause,
                Coord = $"{Math.Round(600000 + p.X * MPerPx)}/{Math.Round(200000 + (MapH - p.Y) * MPerPx)}",
                Height = (int)Math.Round(Terrain(p.X, p.Y)),
            };
            if (i > 0)
            {
                var a = pts[i - 1];
                double run = Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2)) * MPerPx;
                double rise = r.Height - res.Rows[i - 1].Height;
                double slope = run > 0 ? rise / run : 0;
                double e = run / 1000;
                if (slope > 0) e += rise / 100; else if (slope < -0.2) e += Math.Abs(rise) / 150;
                if (rise > 0) res.Ascent += rise; else res.Descent -= rise;
                brk += res.Rows[i - 1].Pause;
                dist += run / 1000; effort += e;
                r.DHeight = rise; r.DDist = run / 1000; r.DEffort = e;
            }
            r.Dist = dist; r.Effort = effort; r.Gehzeit = effort / speed;
            r.Time = r.Gehzeit + brk / 60 + start;
            r.DGehzeit = i > 0 ? r.Gehzeit - res.Rows[i - 1].Gehzeit : 0;
            res.Rows.Add(r);
        }
        var last = res.Rows[^1];
        res.Dist = last.Dist; res.Effort = last.Effort; res.Gehzeit = last.Gehzeit; res.Arrival = last.Time;
        res.PauseMin = res.Rows.Take(res.Rows.Count - 1).Sum(r => r.Pause);
        return res;
    }

    static string P(int n) => n < 10 ? "0" + n : n.ToString();
    public static string FTime(double h) { int H = (int)Math.Floor(h); int m = (int)Math.Floor((h - H) * 60); return $"{P(H % 24)}:{P(m)}"; }
    public static string FDur(double h) { int H = (int)Math.Floor(h); int m = (int)Math.Floor((h - H) * 60); return $"{H}:{P(m)}"; }

    public static string Csv(Result r)
    {
        var ci = CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.00", ci);
        string Q(object v) => "\"" + v.ToString()!.Replace("\"", "\"\"") + "\"";
        var head = new[] { "Nr.", "Wegpunkt", "Koordinaten", "Kommentar", "Höhe / m", "Differenz Höhe / m", "Total Distanz / km", "Differenz Distanz / km", "Total Aufwand / Lkm", "Differenz Aufwand / Lkm", "Total Gehzeit / h", "Differenz Gehzeit / h", "Uhrzeit / hh:mm", "Pause / min" };
        var sb = new StringBuilder();
        sb.Append(string.Join(",", head.Select(Q)));
        foreach (var w in r.Rows)
        {
            sb.Append("\r\n").Append(string.Join(",", new object[] { w.Nr, w.Name, w.Coord, w.Comment, w.Height, w.DHeight, N(w.Dist), N(w.DDist), N(w.Effort), N(w.DEffort), N(w.Gehzeit), N(w.DGehzeit), FTime(w.Time), w.Pause }.Select(Q)));
        }
        return sb.ToString();
    }
}
