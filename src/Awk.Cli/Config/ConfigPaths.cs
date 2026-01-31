namespace Awk.Config;

internal static class ConfigPaths
{
    internal static string UserConfigDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(baseDir, "awork-cli");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "awork-cli");
        }
    }

    internal static string UserConfigFile => Path.Combine(UserConfigDirectory, "config.json");
}
