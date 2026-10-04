using System.Reflection;
using Ascent.Core.Errors;
using Ascent.Maintainer.Hosting;
using Ascent.Maintainer.Tests.TestSupport;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Maintainer.Tests;

/// <summary>The maintainer workflow end to end: init-shield → keygen → seal → sign → verify (P6, P7).</summary>
public sealed class CommandTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public async Task The_full_workflow_produces_verifiable_bundles()
    {
        using var fixture = new MaintainerFixture();
        fixture.WriteSource("references/dlv-d3-01.md", "# Reference answer");
        fixture.WriteSource("labs/lab-d5-01/tests/Tests.cs", "// tests");

        (await fixture.RunAsync("init-shield")).ExitCode.ShouldBe(0);
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        var (keygenExit, keygenOutput) = await fixture.RunAsync("keygen");
        var (sealExit, sealOutput) = await fixture.RunAsync("seal", "--sources", fixture.Sources);
        var (verifyBeforeExit, verifyBefore) = await fixture.RunAsync("verify");
        var (signExit, signOutput) = await fixture.RunAsync("sign");
        var (resignExit, resignOutput) = await fixture.RunAsync("sign");
        var (verifyExit, verifyOutput) = await fixture.RunAsync("verify");

        keygenExit.ShouldBe(0);
        keygenOutput.ShouldContain("Fingerprint (SHA-256): ");
        keygenOutput.ShouldContain("password manager");
        keygenOutput.ShouldNotContain(Passphrase);
        File.ReadAllText(MaintainerContext.PublicKeyPath(fixture.Repo)).ShouldStartWith("-----BEGIN PUBLIC KEY-----");
        File.ReadAllText(Path.Join(fixture.MaintainerDirectory, "signing-key-backup.p8.pem")).ShouldStartWith("-----BEGIN ENCRYPTED " + "PRIVATE KEY-----");
        fixture.KeyStore.Exists.ShouldBeTrue();

        sealExit.ShouldBe(0);
        sealOutput.ShouldContain("sealed dlv-d3-01.reference");
        sealOutput.ShouldContain("Sealed 2, unchanged 0, orphaned 0.");
        sealOutput.ShouldNotContain("Reference answer");

        verifyBeforeExit.ShouldBe(ExitCodes.CheckFailed);
        verifyBefore.ShouldContain("unsigned");

        signExit.ShouldBe(0);
        signOutput.ShouldContain("Signed 2, already signed 0.");
        resignExit.ShouldBe(0);
        resignOutput.ShouldContain("Signed 0, already signed 2.");

        verifyExit.ShouldBe(0);
        verifyOutput.ShouldContain("Verified 2 of 2 bundle(s)");

        var ledger = fixture.Context(TextWriter.Null).Ledger.Read();
        ledger.Select(e => e.Event).ShouldBe(["keygen", "sign", "sign"]);
    }

    [Fact]
    public async Task Verify_catches_tampering_after_signing()
    {
        using var fixture = await SignedRepositoryAsync();
        var path = Path.Join(fixture.Repo, "sealed", "references", "dlv-d3-01.reference.bundle.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"tier\": \"submitted\"", "\"tier\": \"practice\"", StringComparison.Ordinal));

        var (exitCode, output) = await fixture.RunAsync("verify");

        exitCode.ShouldBe(ExitCodes.CheckFailed);
        output.ShouldContain("FAIL: sealed/references/dlv-d3-01.reference.bundle.json: bad signature");
    }

    [Fact]
    public async Task Verify_reports_malformed_bundles()
    {
        using var fixture = await SignedRepositoryAsync();
        fixture.WriteRepo("sealed/questions/broken.bundle.json", "{}");

        var (exitCode, output) = await fixture.RunAsync("verify");

        exitCode.ShouldBe(ExitCodes.CheckFailed);
        output.ShouldContain("broken.bundle.json");
    }

    [Fact]
    public async Task Keygen_refuses_to_replace_a_key_unless_rotating()
    {
        using var fixture = await SignedRepositoryAsync();
        var original = File.ReadAllText(MaintainerContext.PublicKeyPath(fixture.Repo));

        var (refusedExit, refused) = await fixture.RunAsync("keygen");
        refusedExit.ShouldBe(ExitCodes.CheckFailed);
        refused.ShouldContain("already exists");

        fixture.Prompter.Secrets.Enqueue(Passphrase);
        fixture.Prompter.Secrets.Enqueue("a different passphrase");
        var (mismatchExit, mismatch) = await fixture.RunAsync("keygen", "--rotate");
        mismatchExit.ShouldBe(ExitCodes.Usage);
        mismatch.ShouldContain("don't match");

        fixture.Prompter.Secrets.Enqueue(Passphrase);
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        (await fixture.RunAsync("keygen", "--rotate")).ExitCode.ShouldBe(0);
        File.ReadAllText(MaintainerContext.PublicKeyPath(fixture.Repo)).ShouldNotBe(original);

        // After rotation every bundle must be signed again.
        (await fixture.RunAsync("verify")).ExitCode.ShouldBe(ExitCodes.CheckFailed);
        (await fixture.RunAsync("sign")).Output.ShouldContain("Signed 1, already signed 0.");
        (await fixture.RunAsync("verify")).ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Key_import_restores_the_key_from_its_backup()
    {
        using var fixture = await SignedRepositoryAsync();
        var backup = Path.Join(fixture.MaintainerDirectory, "signing-key-backup.p8.pem");
        var stored = fixture.KeyStore.Load();
        fixture.KeyStore.Save([0]);

        fixture.Prompter.Secrets.Enqueue(Passphrase);
        var (exitCode, output) = await fixture.RunAsync("key", "import", backup);

        exitCode.ShouldBe(0);
        output.ShouldContain("Signing key restored");
        output.ShouldNotContain("WARN");
        fixture.KeyStore.Load().ShouldBe(stored);
        (await fixture.RunAsync("verify")).ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Key_import_warns_about_a_key_the_engine_does_not_trust()
    {
        using var fixture = await SignedRepositoryAsync();
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var backup = fixture.WriteRepo("other.p8.pem", other.ExportEncryptedPkcs8PrivateKeyPem(Passphrase, Authoring.KeyBackup.Parameters));

        fixture.Prompter.Secrets.Enqueue(Passphrase);
        var (exitCode, output) = await fixture.RunAsync("key", "import", backup);

        exitCode.ShouldBe(0);
        output.ShouldContain("WARN: This key doesn't match");
        (await fixture.RunAsync("sign")).Output.ShouldContain("doesn't match the key the Engine trusts");
    }

    [Fact]
    public async Task Seal_needs_existing_sources_and_reports_orphans()
    {
        using var fixture = await SignedRepositoryAsync();

        (await fixture.RunAsync("seal")).ExitCode.ShouldBe(ExitCodes.Usage);
        (await fixture.RunAsync("seal", "--sources", Path.Join(fixture.Root, "missing"))).ExitCode.ShouldBe(ExitCodes.Usage);

        File.Delete(Path.Join(fixture.Sources, "references", "dlv-d3-01.md"));
        var (warnExit, warn) = await fixture.RunAsync("seal", "--sources", fixture.Sources);
        warnExit.ShouldBe(0);
        warn.ShouldContain("WARN: orphaned bundle sealed/references/dlv-d3-01.reference.bundle.json");

        var (pruneExit, prune) = await fixture.RunAsync("seal", "--sources", fixture.Sources, "--prune");
        pruneExit.ShouldBe(0);
        prune.ShouldContain("pruned sealed/references/dlv-d3-01.reference.bundle.json");
        File.Exists(Path.Join(fixture.Repo, "sealed", "references", "dlv-d3-01.reference.bundle.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Sign_explains_what_is_missing()
    {
        using var fixture = new MaintainerFixture();
        (await fixture.RunAsync("init-shield")).ExitCode.ShouldBe(0);

        (await fixture.RunAsync("sign")).Output.ShouldContain("Run 'ascent-maint keygen' first");
        fixture.WriteRepo("engine/src/Ascent.Sealing/Keys/maintainer.pub.pem", "placeholder");
        (await fixture.RunAsync("sign")).Output.ShouldContain("No signing key is stored");
        (await fixture.RunAsync("init-shield")).Output.ShouldContain("already exists");
    }

    [Fact]
    public async Task Commands_refuse_to_run_outside_the_repository()
    {
        using var fixture = new MaintainerFixture();
        var elsewhere = Path.Join(fixture.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);

        var (exitCode, output) = await fixture.RunAsync("init-shield", "--public", elsewhere);

        exitCode.ShouldBe(ExitCodes.Usage);
        output.ShouldContain("This isn't the AppSec Ascent repository");
        Directory.Exists(Path.Join(elsewhere, "sealed")).ShouldBeFalse();
    }

    [Fact]
    public async Task Keygen_says_which_file_to_keep_and_which_to_move()
    {
        using var fixture = new MaintainerFixture();
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        fixture.Prompter.Secrets.Enqueue(Passphrase);

        var (_, output) = await fixture.RunAsync("keygen");

        output.ShouldContain("KEEP this file");
        output.ShouldContain(MaintainerContext.PublicKeyPath(fixture.Repo));
        output.ShouldContain("MOVE this file into your password manager");
        output.ShouldContain("signing-key-backup.p8.pem");
    }

    [Fact]
    public async Task Unknown_commands_and_unexpected_errors_are_reported()
    {
        using var fixture = new MaintainerFixture();

        (await fixture.RunAsync("frobnicate")).ExitCode.ShouldBe(ExitCodes.Usage);
        var context = fixture.Context(TextWriter.Null);
        MaintainerApp.Handle(new InvalidOperationException("boom"), context).ShouldBe(ExitCodes.CheckFailed);
    }

    [Fact]
    public void The_default_maintainer_folder_is_under_application_data()
    {
        var context = new MaintainerContext();
        context.MaintainerDirectory.ShouldEndWith(Path.Join("AppSecAscent", "maintainer"));
        context.Ledger.Path.ShouldEndWith("signing-ledger.jsonl");
        context.Out.ShouldBe(Console.Out);
        context.Time.ShouldBe(TimeProvider.System);
        context.Random.ShouldNotBeNull();
        context.Files.ShouldNotBeNull();
        if (!OperatingSystem.IsWindows())
        {
            Should.Throw<AscentException>(context.KeyStore).Message.ShouldContain("needs Windows");
        }
    }

    [Fact]
    public void The_maintainer_tool_references_only_the_sealing_runtime()
    {
        var references = typeof(MaintainerApp).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => name.StartsWith("Ascent.", StringComparison.Ordinal) || name == "ascent")
            .ToList();

        references.ShouldBeSubsetOf(["Ascent.Sealing", "Ascent.Core", "Ascent.Content"]);
        references.ShouldNotContain("ascent");
        Assembly.GetExecutingAssembly().GetReferencedAssemblies().ShouldNotContain(r => r.Name == "ascent");
    }

    private static async Task<MaintainerFixture> SignedRepositoryAsync()
    {
        var fixture = new MaintainerFixture();
        fixture.WriteSource("references/dlv-d3-01.md", "# Reference answer");
        (await fixture.RunAsync("init-shield")).ExitCode.ShouldBe(0);
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        fixture.Prompter.Secrets.Enqueue(Passphrase);
        (await fixture.RunAsync("keygen")).ExitCode.ShouldBe(0);
        (await fixture.RunAsync("seal", "--sources", fixture.Sources)).ExitCode.ShouldBe(0);
        (await fixture.RunAsync("sign")).ExitCode.ShouldBe(0);
        SealedBundle.Read(Path.Join(fixture.Repo, "sealed", "references", "dlv-d3-01.reference.bundle.json")).IsSigned.ShouldBeTrue();
        using var trusted = new SignatureVerifier(File.ReadAllText(MaintainerContext.PublicKeyPath(fixture.Repo)));
        trusted.Fingerprint.ShouldNotBeNullOrEmpty();
        return fixture;
    }
}
