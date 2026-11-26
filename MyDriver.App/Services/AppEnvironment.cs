using System.Reflection;
using MyDriver.Core.Storage;

namespace MyDriver.App.Services;

/// <summary>Keadaan aplikasi yang ditentukan sekali saat mulai.</summary>
public sealed record AppEnvironment(DataPaths Paths, bool IsAdministrator)
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public bool IsReadOnly => !IsAdministrator;
}
