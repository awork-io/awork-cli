using Awk.Commands;
using Awk.Generated;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("awork");

    config.AddBranch("auth", auth =>
    {
        auth.SetDescription("Authentication helpers");
        auth.AddCommand<AuthLoginCommand>("login");
        auth.AddCommand<AuthStatusCommand>("status");
        auth.AddCommand<AuthLogoutCommand>("logout");
    });

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Validate token and connectivity");
    GeneratedCli.Register(config);
});

return app.Run(args);
