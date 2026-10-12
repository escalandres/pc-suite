using Microsoft.AspNetCore.Components.WebView;

namespace PCSuite.Blazor;

/// <summary>
/// Host del BlazorWebView: genera una página HTML inline con el CSS de
/// PCSuite embebido (los design tokens --pcs-*). El CSS se lee del
/// wwwroot del proyecto Blazor en build time; si no existe, se usan los
/// tokens inline como fallback.
/// </summary>
public sealed class BlazorHost
{
    /// <summary>Página HTML que hospeda el selector #app del root component.</summary>
    public string HostPagePath => CreateHostPage();

    private string CreateHostPage()
    {
        // Leer el CSS del wwwroot si existe; si no, usar fallback mínimo.
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "Components", "wwwroot", "app.css");
        var css = File.Exists(cssPath)
            ? File.ReadAllText(cssPath)
            : """
                :root {
                    --pcs-bg: #0b1220; --pcs-surface: #121a2b; --pcs-surface-2: #182338;
                    --pcs-border: #24314c; --pcs-text: #e6edf7; --pcs-text-muted: #93a3bd;
                    --pcs-accent: #3b82f6; --pcs-accent-hover: #2f6fe0;
                    --pcs-success: #34d399; --pcs-danger: #f87171; --pcs-warning: #fbbf24;
                    --pcs-orange: #f97316;
                }
                """;

        var html = $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>PCSuite — ClamAV (Blazor)</title>
                <style>{{css}}</style>
            </head>
            <body style="margin:0; padding:2rem; background:var(--pcs-bg); color:var(--pcs-text); font-family:'Segoe UI',system-ui,sans-serif">
                <div id="app"></div>
            </body>
            </html>
            """;

        var path = Path.Combine(Path.GetTempPath(), "pcsuite-blazor-host.html");
        File.WriteAllText(path, html);
        return path;
    }
}

