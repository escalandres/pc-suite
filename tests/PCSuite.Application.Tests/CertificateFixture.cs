using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Certificates;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.PGP;

namespace PCSuite.Application.Tests;

/// <summary>
/// CertificateService + router para los tests de las operaciones cert.*
/// (Task 019): keyring PGP y carpeta de salida en temporales (jamás el
/// %APPDATA% ni el escritorio reales) y las mismas 4 ops que registra MainForm.
/// </summary>
internal sealed class CertificateFixture : IDisposable
{
    private readonly TempExecutablePath _keyring = new();
    private readonly TempExecutablePath _output = new();

    private CertificateFixture(
        TempExecutablePath keyring,
        TempExecutablePath output,
        PgpService pgp,
        CertificateService service,
        CommandRouter router)
    {
        _keyring = keyring;
        _output = output;
        Pgp = pgp;
        Service = service;
        Router = router;
    }

    public PgpService Pgp { get; }

    public CertificateService Service { get; }

    public CommandRouter Router { get; }

    /// <summary>Carpeta temporal donde el servicio escribe certificados y claves.</summary>
    public string OutputDir => _output.DirectoryPath;

    public static CertificateFixture Create()
    {
        var keyring = new TempExecutablePath();
        var output = new TempExecutablePath();
        var pgp = new PgpService(new PgpKeyringStore(keyring.DirectoryPath));
        var service = new CertificateService(pgp, null, output.DirectoryPath);

        var router = new CommandRouter(
            new IOperation[]
            {
                new CertX509GenerateOperation(service),
                new CertInspectOperation(service),
                new CertConvertOperation(service),
                new CertPgpSignFileOperation(service),
                new CertCsrGenerateOperation(service),
            },
            NullLogger<CommandRouter>.Instance);

        return new CertificateFixture(keyring, output, pgp, service, router);
    }

    public void Dispose()
    {
        _keyring.Dispose();
        _output.Dispose();
    }
}
