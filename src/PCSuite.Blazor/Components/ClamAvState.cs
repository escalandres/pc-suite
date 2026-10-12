using System.Diagnostics;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Jobs;
using PCSuite.Contracts;

namespace PCSuite.Blazor.Components;

/// <summary>
/// Estado de ClamAV observable por el componente Blazor. SIN IPC: el
/// componente C# suscribe directamente a estos eventos C# y Blazor
/// agrupa los re-renders automáticamente (no hace falta throttle).
/// </summary>
public sealed class ClamAvState
{
    private readonly IClamAvService _service;
    private readonly IJobManager _jobs;

    public event Action? StateChanged;

    public ClamAvStatus? Status { get; private set; }
    public ClamAvScanProgress? ScanProgress { get; private set; }
    public ClamAvScanOutcome? LastScan { get; private set; }
    public IReadOnlyList<ClamAvScanHistoryEntry>? History { get; private set; }
    public string? JobId { get; private set; }
    public Job? Job { get; private set; }
    public bool IsScanning => Job is { Status: JobStatus.Running };
    public string? Notice { get; private set; }
    public bool NoticeIsError { get; private set; }
    public bool IsInstalling { get; private set; }
    public bool IsUpdatingDefinitions { get; private set; }
    public ClamAvFreshclamProgress? FreshclamProgress { get; private set; }

    public ClamAvState(IClamAvService service, IJobManager jobs)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            Status = await _service.GetStatusAsync(ct).ConfigureAwait(false);
            if (Status.Installed)
            {
                History = await _service.GetHistoryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (ClamAvException ex)
        {
            SetNotice(ex.ErrorMessage, isError: true);
        }
        FireStateChanged();
    }

    public async Task InstallAsync()
    {
        if (IsInstalling || IsScanning || IsUpdatingDefinitions) return;
        IsInstalling = true;
        ClearNotice();
        FireStateChanged();

        try
        {
            var outcome = await _service.InstallAsync().ConfigureAwait(false);
            if (outcome.Succeeded)
            {
                SetNotice(outcome.Detail ?? "ClamAV se instaló correctamente.", isError: false);
                await RefreshAsync().ConfigureAwait(false);
            }
            else
            {
                SetNotice(outcome.Detail ?? $"winget terminó con el código {outcome.ExitCode}.", isError: true);
            }
        }
        catch (ClamAvException ex)
        {
            SetNotice(ex.ErrorMessage, isError: true);
        }
        catch (Exception)
        {
            SetNotice("No se pudo instalar ClamAV.", isError: true);
        }
        finally
        {
            IsInstalling = false;
            FireStateChanged();
        }
    }

    public async Task UpdateDefinitionsAsync()
    {
        if (IsInstalling || IsScanning || IsUpdatingDefinitions) return;
        IsUpdatingDefinitions = true;
        ClearNotice();
        FireStateChanged();

        try
        {
            var progress = new Progress<ClamAvFreshclamProgress>(p =>
            {
                FreshclamProgress = p;
                FireStateChanged();
            });
            var outcome = await _service.UpdateDefinitionsAsync(progress: progress).ConfigureAwait(false);
            if (outcome.Succeeded)
            {
                SetNotice("Definiciones actualizadas.", isError: false);
                await RefreshAsync().ConfigureAwait(false);
            }
            else
            {
                SetNotice(outcome.Detail ?? $"freshclam terminó con el código {outcome.ExitCode}.", isError: true);
            }
        }
        catch (ClamAvException ex)
        {
            SetNotice(ex.ErrorMessage, isError: true);
        }
        catch (Exception)
        {
            SetNotice("No se pudieron actualizar las definiciones.", isError: true);
        }
        finally
        {
            IsUpdatingDefinitions = false;
            FireStateChanged();
        }
    }

    public async Task StartScanAsync(string kind, string? path = null)
    {
        if (IsInstalling || IsScanning || IsUpdatingDefinitions) return;

        ClearNotice();
        ScanProgress = null;
        LastScan = null;

        try
        {
            var job = _jobs.Start($"clamav.{kind}Scan");
            JobId = job.Id;
            var token = _jobs.GetCancellationToken(job.Id);

            // Suscripción directa al progreso — sin IPC, sin throttle.
            var progress = new Progress<ClamAvScanProgress>(p =>
            {
                ScanProgress = p;
                _jobs.SetProgress(job.Id, (p.Percent ?? 0) / 100d);
                FireStateChanged();
            });

            var outcome = kind switch
            {
                "quick" => await _service.QuickScanAsync(token, progress).ConfigureAwait(false),
                "full" => await _service.FullScanAsync(token, progress).ConfigureAwait(false),
                "custom" => await _service.CustomScanAsync(path ?? "", token, progress).ConfigureAwait(false),
                _ => throw new ArgumentException($"Tipo de escaneo desconocido: {kind}"),
            };

            LastScan = outcome;
            _jobs.Complete(job.Id, outcome.ExitCode);

            if (outcome.Succeeded)
            {
                var threats = outcome.InfectedFiles > 0
                    ? $" ¡Amenazas encontradas! {outcome.InfectedFiles} archivo(s) infectado(s)."
                    : " Escaneo completado: no se detectaron amenazas.";
                SetNotice(threats, isError: outcome.InfectedFiles > 0);
            }
            else
            {
                SetNotice(outcome.Detail ?? $"clamscan terminó con el código {outcome.ExitCode}.", isError: true);
            }

            // Refrescar historial tras el escaneo
            History = await _service.GetHistoryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ClamAvException ex)
        {
            SetNotice(ex.ErrorMessage, isError: true);
        }
        catch (OperationCanceledException)
        {
            if (JobId is { } id) _jobs.MarkCancelled(id);
            SetNotice("Escaneo cancelado.", isError: true);
        }
        catch (Exception)
        {
            SetNotice("El escaneo falló.", isError: true);
        }
        finally
        {
            JobId = null;
            Job = null;
            ScanProgress = null;
            FireStateChanged();
        }
    }

    public bool CancelScan()
    {
        return JobId is { } id && _jobs.RequestCancel(id);
    }

    private void SetNotice(string text, bool isError)
    {
        Notice = text;
        NoticeIsError = isError;
    }

    private void ClearNotice()
    {
        Notice = null;
        NoticeIsError = false;
    }

    private void FireStateChanged() => StateChanged?.Invoke();
}
