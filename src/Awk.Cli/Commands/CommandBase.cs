using Awk.Cli;
using Awk.Config;
using Awk.Generated;
using Awk.Services;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal abstract class CommandBase<TSettings> : AsyncCommand<TSettings> where TSettings : BaseSettings
{
    protected async Task<AworkClient> CreateClient(TSettings settings, CancellationToken cancellationToken)
    {
        var loaded = await ConfigLoader.Load(
            settings.EnvFile,
            settings.BaseUrl,
            settings.Token,
            settings.ConfigPath,
            cancellationToken);

        var authMode = AuthModeParser.Parse(settings.AuthMode);
        var auth = await AuthResolver.Resolve(
            loaded.BaseConfig,
            loaded.EffectiveConfig,
            authMode,
            cancellationToken);

        if (auth.UpdatedConfig is not null)
        {
            await ConfigLoader.SaveUserConfig(auth.UpdatedConfig, loaded.ConfigPath, cancellationToken);
        }

        return new AworkClientFactory().Create(loaded.EffectiveConfig.ApiBaseUrl, auth.Token);
    }

    protected int Output(object payload) => JsonConsole.Write(payload);

    protected int OutputError(Exception ex) => JsonConsole.WriteError(ex);
}
