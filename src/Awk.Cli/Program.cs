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
        GeneratedCli.RegisterAuth(auth);
    });

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Validate token and connectivity");
    config.AddBranch("skill", skill =>
    {
        skill.SetDescription("Skill file management for AI coding agents");
        skill.AddCommand<SkillInstallCommand>("install");
        skill.AddCommand<SkillShowCommand>("show");
    });
    GeneratedCli.Register(config);
});

return app.Run(args);
