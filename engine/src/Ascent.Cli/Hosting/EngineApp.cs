using Ascent.Cli.Commands;
using Spectre.Console.Cli;

namespace Ascent.Cli.Hosting;

/// <summary>Builds the <c>ascent</c> command app over an <see cref="EngineHost"/>.</summary>
public static class EngineApp
{
    /// <summary>Creates the app. Tests call this with a host built from test options.</summary>
    public static CommandApp Create(EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var app = new CommandApp(new TypeRegistrar(host));
        app.Configure(config =>
        {
            config.SetApplicationName("ascent");
            config.ConfigureConsole(host.SpectreConsole());
            config.SetInterceptor(new EngineInterceptor(host));
            config.SetExceptionHandler((exception, _) => ErrorHandler.Handle(exception, host));

            config.AddCommand<LintCommand>("lint")
                .WithDescription("Check curriculum content against the framework's rules.");

            config.AddCommand<CoverageCommand>("coverage")
                .WithDescription("Report coverage against the exam outline (counts only).");

            config.AddBranch("exceptions", branch =>
            {
                branch.SetDescription("Work with the security exception register.");
                branch.AddCommand<ExceptionsCheckCommand>("check")
                    .WithDescription("Fail on expired, invalid or over-long risk acceptances.");
                branch.AddCommand<ExceptionsEmitCommand>("emit")
                    .WithDescription("Generate scanner suppression files from the register.");
            });

            config.AddCommand<VerifyBundlesCommand>("verify-bundles")
                .WithDescription("Check Sealed Bundle metadata (signature verification ships with the sealing module).");
        });

        return app;
    }
}
