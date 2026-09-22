using System.Runtime.InteropServices;
using System.Security.Principal;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

public sealed class GetSystemInfoOperation : IOperation
{
    public string Id => OperationIds.GetSystemInfo;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var info = new SystemInfoResult
        {
            AppVersion = AppInfo.Version,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            OsDescription = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            IsAdministrator = IsAdministrator(),
        };

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(info)));
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
