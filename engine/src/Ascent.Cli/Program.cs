using Ascent.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("ascent");

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

var exitCode = await app.RunAsync(args);

// Spectre returns a negative code for usage errors; the Engine uses 2 (REL-U1-01).
return exitCode < 0 ? 2 : exitCode;
