using System.Reflection;

namespace Awk.Cli;

internal static class VersionInfo
{
    internal static string Version
    {
        get
        {
            var assembly = typeof(VersionInfo).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0-dev";
        }
    }
}
