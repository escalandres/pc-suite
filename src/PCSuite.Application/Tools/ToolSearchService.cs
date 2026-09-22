using PCSuite.Core.Tools;

namespace PCSuite.Application.Tools;

/// <summary>
/// Búsqueda por texto libre sobre los metadatos de las tools
/// (id, nombre, keywords y descripción), sin hardcoding en el frontend.
/// </summary>
public sealed class ToolSearchService
{
    private readonly ToolRegistry _registry;

    public ToolSearchService(ToolRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Devuelve las tools ordenadas por relevancia. Consulta vacía o en
    /// blanco devuelve todas. Varios términos ("json format"): todos deben
    /// coincidir (AND); la puntuación es la suma de los términos.
    /// </summary>
    public IReadOnlyList<ITool> Search(string? query)
    {
        var tools = _registry.Tools;
        var terms = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return tools
                .OrderBy(t => t.Metadata.Id, StringComparer.Ordinal)
                .ToList();
        }

        var results = new List<(ITool Tool, int Score)>();
        foreach (var tool in tools)
        {
            var score = 0;
            var allMatched = true;
            foreach (var term in terms)
            {
                var termScore = Score(tool.Metadata, term);
                if (termScore == 0)
                {
                    allMatched = false;
                    break;
                }

                score += termScore;
            }

            if (allMatched)
            {
                results.Add((tool, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Tool.Metadata.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Tool)
            .ToList();
    }

    /// <summary>Puntuación de un término contra un metadato; 0 = sin coincidencia.</summary>
    private static int Score(ToolMetadata metadata, string term)
    {
        if (string.Equals(metadata.Id, term, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (metadata.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (string.Equals(metadata.Name, term, StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        if (metadata.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (metadata.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        var bestKeyword = 0;
        foreach (var keyword in metadata.Keywords)
        {
            if (string.Equals(keyword, term, StringComparison.OrdinalIgnoreCase))
            {
                return 45;
            }

            if (keyword.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                bestKeyword = Math.Max(bestKeyword, 40);
            }
            else if (keyword.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                bestKeyword = Math.Max(bestKeyword, 30);
            }
        }

        if (bestKeyword > 0)
        {
            return bestKeyword;
        }

        return metadata.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ? 20 : 0;
    }
}
