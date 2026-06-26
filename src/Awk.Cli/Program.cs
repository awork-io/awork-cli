using Awk.Cli;
using Awk.Commands;
using Awk.Generated;
using Spectre.Console.Cli;

if (args is ["-v"] or ["--version"])
{
    Console.WriteLine($"awork {VersionInfo.Version}");
    return 0;
}

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("awork");
    config.SetApplicationVersion(VersionInfo.Version);

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
    config.AddBranch("links", links =>
    {
        links.SetDescription("Build awork app deep links");
        links.AddCommand<LinkGetCommand>("get")
            .WithDescription("Build a deep link for a known app destination or entity");
        links.AddCommand<LinkResolveCommand>("resolve")
            .WithDescription("Resolve a task key or entity id to a deep link");
    });
    config.AddBranch("skill", skill =>
    {
        skill.SetDescription("Skill file management for AI coding agents");
        skill.AddCommand<SkillInstallCommand>("install");
        skill.AddCommand<SkillShowCommand>("show");
    });
    GeneratedCli.Register(config);
});

return app.Run(args);
