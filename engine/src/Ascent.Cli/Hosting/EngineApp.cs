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

            config.AddCommand<StartCommand>("start")
                .WithDescription("Set up your profile and see what's next.");
            config.AddCommand<RulesCommand>("rules")
                .WithDescription("Read and accept the rules of engagement.");
            config.AddCommand<ConfigCommand>("config")
                .WithDescription("Show or change your settings.");
            config.AddCommand<NextCommand>("next")
                .WithDescription("Show the next Quest in exam order.");
            config.AddCommand<QuestCommand>("quest")
                .WithDescription("Open a Quest: its lesson and activities.");
            config.AddCommand<TeachBackCommand>("teachback")
                .WithDescription("Explain a lesson in your own words to complete it.");
            config.AddCommand<StandUpCommand>("standup")
                .WithDescription("Run today's Stand-up review.");
            config.AddCommand<DiagnosticCommand>("diagnostic")
                .WithDescription("Take the Diagnostic and get a study plan.");
            config.AddCommand<BossCommand>("boss")
                .WithDescription("Take a Domain's Boss Fight.");
            config.AddCommand<SimCommand>("sim")
                .WithDescription("Take a full Simulation exam.");
            config.AddCommand<StatusCommand>("status")
                .WithDescription("Show your Rank, XP, badges and weekly goal.");
            config.AddBranch("lab", branch =>
            {
                branch.SetDescription("Work on a Lab: Red, then Blue, then Explain.");
                branch.AddCommand<LabUpCommand>("up")
                    .WithDescription("Start a Lab and plant a fresh Flag (add --cloud for its Cloud Stage).");
                branch.AddCommand<LabDownCommand>("down")
                    .WithDescription("Stop a Lab's Local Stage.");
                branch.AddCommand<LabResetCommand>("reset")
                    .WithDescription("Put a Lab's original module files back.");
            });
            config.AddCommand<FlagCommand>("flag")
                .WithDescription("Submit the Flag you captured.");
            config.AddCommand<VerifyCommand>("verify")
                .WithDescription("Run a Lab's security tests against your fix.");
            config.AddCommand<TeardownCommand>("teardown")
                .WithDescription("Delete every Cloud Stage deployment and check nothing is left.");
            config.AddBranch("guardrails", branch =>
            {
                branch.SetDescription("Cost guardrails for Cloud Stages.");
                branch.AddCommand<GuardrailsCheckCommand>("check")
                    .WithDescription("Check the budget alerts and the Policy allow-list.");
                branch.AddCommand<GuardrailsApplyCommand>("apply")
                    .WithDescription("Deploy the budget alerts and the Policy allow-list.");
            });
            config.AddCommand<DeliverCommand>("deliver")
                .WithDescription("Start, check and submit a Deliverable or a drill.");
            config.AddCommand<ReviewCommand>("review")
                .WithDescription("Get optional feedback on a Deliverable from your AI model.");
            config.AddBranch("release", branch =>
            {
                branch.SetDescription("Move between Releases of the Throughline System.");
                branch.AddCommand<ReleaseNextCommand>("next")
                    .WithDescription("Archive your Domain work and switch to the next Release.");
            });
            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Check which Stages your environment supports.");
            config.AddCommand<BugCommand>("bug")
                .WithDescription("Report a Content Bug about an item.");
            config.AddCommand<SyncCommand>("sync")
                .WithDescription("Earn XP for your confirmed Content Bugs.");
            config.AddCommand<PortfolioCommand>("portfolio")
                .WithDescription("Write PORTFOLIO.md for your fork.");
            config.AddBranch("progress", branch =>
            {
                branch.SetDescription("Back up or restore your progress.");
                branch.AddCommand<ProgressExportCommand>("export")
                    .WithDescription("Write your progress to a private JSON file.");
                branch.AddCommand<ProgressImportCommand>("import")
                    .WithDescription("Replace your progress with an export.");
            });

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
                .WithDescription("Check Sealed Bundle metadata and maintainer signatures.");
        });

        return app;
    }
}
