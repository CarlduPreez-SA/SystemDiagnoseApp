using System.IO;
using System.Net;
using System.Text;
using SystemDiagnoseApp.Diagnostics;

namespace SystemDiagnoseApp.Services;

/// <summary>Writes a single self-contained HTML report to the Desktop.</summary>
public static class ReportExporter
{
    public static string Export(IReadOnlyList<DiagnosticResult> results, ActionLog actionLog)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string path = Path.Combine(desktop,
            $"SystemDiagnose-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}.html");

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>System diagnosis — {Enc(Environment.MachineName)}</title>");
        sb.AppendLine("""
            <style>
              :root { color-scheme: light dark; }
              body { font: 15px/1.5 system-ui, Segoe UI, Arial, sans-serif; margin: 0; padding: 2rem;
                     background:#fff; color:#1a1a1a; }
              @media (prefers-color-scheme: dark){ body{ background:#161616; color:#e8e8e8; } .card{ background:#1f1f1f; } th{ background:#262626; } }
              h1 { margin: 0 0 .25rem; } .sub { color:#666; margin-bottom:1.5rem; }
              .card { border:1px solid #ccc3; border-radius:10px; padding:1rem 1.25rem; margin:.75rem 0; background:#fafafa; }
              .sev { display:inline-block; padding:.1rem .5rem; border-radius:6px; font-weight:600; font-size:.8rem; color:#fff; }
              .Ok{background:#2e7d32}.Info{background:#0277bd}.Warning{background:#ef6c00}.Critical{background:#c62828}.Unknown{background:#616161}
              pre { white-space:pre-wrap; word-break:break-word; font:13px/1.45 ui-monospace,Consolas,monospace;
                    background:#0000000a; padding:.6rem .75rem; border-radius:6px; }
              .rec { border-left:4px solid #ef6c00; padding:.4rem .75rem; margin-top:.5rem; background:#ef6c000f; }
              table { border-collapse:collapse; width:100%; margin-top:.5rem; }
              th,td { text-align:left; padding:.4rem .6rem; border-bottom:1px solid #ccc4; vertical-align:top; }
              th { background:#f0f0f0; }
            </style></head><body>
            """);

        sb.AppendLine($"<h1>System diagnosis</h1>");
        sb.AppendLine($"<div class=\"sub\">{Enc(Environment.MachineName)} &middot; generated {DateTime.Now:yyyy-MM-dd HH:mm}</div>");

        foreach (var sev in new[] { Severity.Critical, Severity.Warning, Severity.Info, Severity.Unknown, Severity.Ok })
        {
            int n = results.Count(r => r.Severity == sev);
            if (n > 0) sb.Append($"<span class=\"sev {sev}\">{sev}: {n}</span> ");
        }

        foreach (var r in results.OrderByDescending(r => Rank(r.Severity)))
        {
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine($"<h2>{Enc(r.Title)} <span class=\"sev {r.Severity}\">{r.Severity}</span></h2>");
            sb.AppendLine($"<pre>{Enc(r.Detail)}</pre>");
            if (!string.IsNullOrWhiteSpace(r.Recommendation))
                sb.AppendLine($"<div class=\"rec\"><strong>What to do:</strong> {Enc(r.Recommendation)}</div>");
            if (r.Fixes.Count > 0)
            {
                sb.AppendLine("<p><strong>Available in-app fixes:</strong></p><ul>");
                foreach (var f in r.Fixes) sb.AppendLine($"<li>{Enc(f.Title)}</li>");
                sb.AppendLine("</ul>");
            }
            sb.AppendLine("</div>");
        }

        var entries = actionLog.Snapshot();
        sb.AppendLine("<div class=\"card\"><h2>Changes made by this tool</h2>");
        if (entries.Count == 0)
        {
            sb.AppendLine("<p>No changes were applied.</p>");
        }
        else
        {
            sb.AppendLine("<table><tr><th>Time</th><th>Area</th><th>Action</th></tr>");
            foreach (var e in entries)
                sb.AppendLine($"<tr><td>{e.Timestamp:yyyy-MM-dd HH:mm:ss}</td><td>{Enc(e.Category)}</td><td>{Enc(e.Message)}</td></tr>");
            sb.AppendLine("</table>");
        }
        sb.AppendLine("</div>");

        sb.AppendLine("</body></html>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static int Rank(Severity s) => s switch
    {
        Severity.Critical => 5, Severity.Warning => 4, Severity.Info => 3, Severity.Unknown => 2, _ => 1,
    };

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
