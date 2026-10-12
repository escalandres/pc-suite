using PCSuite.Application.Operations;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Diagnostics;
using System.Text.Json;

namespace PCSuite.Application.Operations;

/// <summary>
/// Operación puente "explorer.openPath": abre una ruta en el Explorador de Windows.
/// </summary>
public sealed class ExplorerOpenPathOperation : IOperation
{
    /// <inheritdoc />
    public string Id => OperationIds.ExplorerOpenPath;

    /// <inheritdoc />
    public bool RequiresElevation => false;

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Payload.HasValue)
        {
            return OperationResult.Fail("error", "El parámetro 'path' es obligatorio.");
        }

        ExplorerOpenPathPayload? payload;
        try
        {
            payload = context.Payload.Value.Deserialize<ExplorerOpenPathPayload>(BridgeJson.Options);
        }
        catch
        {
            return OperationResult.Fail("error", "Payload inválido.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Path))
        {
            return OperationResult.Fail("error", "El parámetro 'path' es obligatorio.");
        }

        var path = payload.Path.Trim();

        try
        {
            // Verificar si la ruta existe (archivo o directorio)
            var exists = File.Exists(path) || Directory.Exists(path);
            if (!exists)
            {
                return OperationResult.Fail("not_found", $"La ruta no existe: {path}");
            }

            // Abrir en el Explorador de Windows
            // Usar 'explorer.exe' con argumentos apropiados
            if (File.Exists(path))
            {
                // Para archivos, usar /select para resaltar el archivo
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                // Para directorios, abrir directamente
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                });
            }

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("error", $"No se pudo abrir la ruta: {ex.Message}");
        }
    }
}