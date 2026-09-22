namespace PCSuite.Core.Processes;

/// <summary>
/// Única puerta del proyecto para ejecutar procesos — nunca se dispersan
/// llamadas a <c>Process.Start()</c> por el código. Asíncrono, con
/// cancelación, timeout y streaming de stdout/stderr.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> ExecuteAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}
