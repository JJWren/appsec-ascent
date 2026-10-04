using System.Security.Cryptography;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Maintainer.Authoring;
using Ascent.Maintainer.Tests.TestSupport;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;
using Ascent.Sealing.Unsealing;

namespace Ascent.Maintainer.Tests;

public sealed class AuthoringTests
{
    private const string Passphrase = "correct horse battery staple";

    private const string Question = """
        id: qb-1.1-001
        objectiveId: "1.1"
        outlineVersion: "2023-09-15"
        type: single
        stem: Which control BEST protects confidentiality of records at rest?
        options:
          - { key: A, text: Encryption, rationale: Encryption protects confidentiality at rest. }
          - { key: B, text: Checksums, rationale: Checksums protect integrity. }
          - { key: C, text: Replication, rationale: Replication protects availability. }
        answer: A
        citations:
          - { title: Example standard, publisher: NIST }
        difficulty: 2
        aiLens: true
        aiTopic: AI-1
        pool: practice
        attestation: original-not-exam-recalled
        """;

    [Fact]
    public void Key_backups_round_trip_under_the_passphrase()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = KeyBackup.Export(key, Passphrase);

        pem.ShouldStartWith("-----BEGIN ENCRYPTED " + "PRIVATE KEY-----");
        using var restored = KeyBackup.Import(pem, Passphrase);
        SignatureVerifier.FingerprintOf(restored).ShouldBe(SignatureVerifier.FingerprintOf(key));
        Should.Throw<AscentException>(() => KeyBackup.Import(pem, "wrong passphrase!")).Message.ShouldContain("passphrase may be wrong");
        Should.Throw<UsageException>(() => KeyBackup.Export(key, "short"));
        KeyBackup.Parameters.IterationCount.ShouldBe(600_000);
    }

    [Fact]
    public void Backups_of_other_curves_are_refused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var pem = p384.ExportEncryptedPkcs8PrivateKeyPem(Passphrase, KeyBackup.Parameters);
        Should.Throw<AscentException>(() => KeyBackup.Import(pem, Passphrase)).Message.ShouldContain("P-256");
    }

    [Fact]
    public void The_ledger_appends_owner_only_json_lines()
    {
        using var fixture = new MaintainerFixture();
        var ledger = new SigningLedger(Path.Join(fixture.MaintainerDirectory, "ledger.jsonl"), Files.OwnerOnly);

        ledger.Read().ShouldBeEmpty();
        ledger.Append(new LedgerEntry { Time = fixture.Clock.GetUtcNow(), Event = "keygen", KeyFingerprint = "ab" });
        ledger.Append(new LedgerEntry { Time = fixture.Clock.GetUtcNow(), Event = "sign", ItemId = "qb-1.1-001", BundleSha256 = "cd", KeyFingerprint = "ab" });

        var entries = ledger.Read();
        entries.Select(e => e.Event).ShouldBe(["keygen", "sign"]);
        entries[1].ItemId.ShouldBe("qb-1.1-001");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(ledger.Path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void The_planner_maps_every_source_kind_to_its_bundle()
    {
        using var fixture = new MaintainerFixture();
        fixture.WriteSource("questions/d1/qb-1.1-001.yaml", Question);
        fixture.WriteSource("questions/sim/qb-1.1-900.yaml", Question.Replace("qb-1.1-001", "qb-1.1-900", StringComparison.Ordinal).Replace("pool: practice", "pool: simulation", StringComparison.Ordinal));
        foreach (var part in new[] { "module", "plant", "tests", "fix" })
        {
            fixture.WriteSource("labs/lab-d5-01/" + part + "/file.txt", part);
        }

        fixture.WriteRepo("labs/lab-d5-01/lab.yaml", "id: lab-d5-01\nobjectiveId: \"5.1\"\n");
        fixture.WriteSource("references/dlv-d3-01.md", "# Reference");
        fixture.WriteSource("releases/3/README.md", "Release 3");
        fixture.WriteSource("drills/drl-1.2-01.md", "Answer");
        fixture.WriteSource("deep-dives/dd-7.1-01/solution.py", "print('x')");

        var plan = SealPlanner.Plan(fixture.Sources, fixture.Repo).ToDictionary(item => item.ItemId);

        plan.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            "dd-7.1-01.solution", "dlv-d3-01.reference", "drl-1.2-01.answer", "lab-d5-01.fix", "lab-d5-01.module", "lab-d5-01.plant",
            "lab-d5-01.tests", "qb-1.1-001", "qb-1.1-900", "release-3",
        ]);
        plan["qb-1.1-001"].ShouldSatisfyAllConditions(
            q => q.Tier.ShouldBe(SealTier.Practice),
            q => q.BundlePath.ShouldBe("sealed/questions/qb-1.1-001.bundle.json"),
            q => q.ExamDomain.ShouldBe("D1"),
            q => q.AiTopic.ShouldBe("AI-1"),
            q => q.ContentType.ShouldBe("application/json"));
        plan["qb-1.1-900"].Tier.ShouldBe(SealTier.Simulation);
        plan["qb-1.1-900"].BundlePath.ShouldBe("sealed/simulation/qb-1.1-900.bundle.json");
        plan["lab-d5-01.tests"].ShouldSatisfyAllConditions(
            l => l.Tier.ShouldBe(SealTier.Earned),
            l => l.ItemType.ShouldBe("lab-tests"),
            l => l.ObjectiveId.ShouldBe("5.1"),
            l => l.BundlePath.ShouldBe("sealed/labs/lab-d5-01.tests.bundle.json"));
        plan["lab-d5-01.module"].Tier.ShouldBe(SealTier.Start);
        plan["dlv-d3-01.reference"].Tier.ShouldBe(SealTier.Submitted);
        plan["release-3"].Tier.ShouldBe(SealTier.Release);
        plan["drl-1.2-01.answer"].ItemType.ShouldBe("drill-answer");
        plan["dd-7.1-01.solution"].ContentType.ShouldBe(SafeArchive.ContentType);
    }

    [Fact]
    public void The_planner_refuses_duplicate_item_ids()
    {
        using var fixture = new MaintainerFixture();
        fixture.WriteSource("questions/a/one.yaml", Question);
        fixture.WriteSource("questions/b/two.yaml", Question);

        Should.Throw<InvalidOperationException>(() => SealPlanner.Plan(fixture.Sources, fixture.Repo)).Message.ShouldContain("qb-1.1-001");
    }

    [Fact]
    public void Sealing_is_idempotent_and_reseals_only_real_changes()
    {
        using var fixture = new MaintainerFixture();
        var keys = new KeyHierarchy(new byte[32]);
        var sealer = new Sealer(fixture.Repo, keys, CryptoRandomSource.Instance, fixture.Clock);
        var source = fixture.WriteSource("references/dlv-d3-01.md", "# Reference v1");
        SealItem Item() => SealPlanner.Plan(fixture.Sources, fixture.Repo).Single();

        sealer.Seal(Item()).ShouldBe(SealOutcome.Sealed);
        var path = Path.Join(fixture.Repo, "sealed", "references", "dlv-d3-01.reference.bundle.json");
        var first = File.ReadAllBytes(path);
        SealedBundle.Read(path).IsSigned.ShouldBeFalse();

        sealer.Seal(Item()).ShouldBe(SealOutcome.Unchanged);
        File.ReadAllBytes(path).ShouldBe(first);

        File.WriteAllText(source, "# Reference v2");
        sealer.Seal(Item()).ShouldBe(SealOutcome.Sealed);
        File.ReadAllBytes(path).ShouldNotBe(first);

        sealer.Seal(Item() with { ObjectiveId = "3.1" }).ShouldBe(SealOutcome.Sealed);
    }

    [Fact]
    public void Orphaned_bundles_are_found()
    {
        using var fixture = new MaintainerFixture();
        var sealer = new Sealer(fixture.Repo, new KeyHierarchy(new byte[32]), CryptoRandomSource.Instance, fixture.Clock);
        sealer.FindOrphans([]).ShouldBeEmpty();
        fixture.WriteRepo("sealed/questions/qb-9.9-999.bundle.json", "{}");

        sealer.FindOrphans([]).ShouldBe(["sealed/questions/qb-9.9-999.bundle.json"]);
    }

    [Fact]
    public void Signing_checks_each_bundle_and_records_it()
    {
        using var fixture = new MaintainerFixture();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = new SignatureVerifier(key.ExportSubjectPublicKeyInfoPem());
        var keys = new KeyHierarchy(new byte[32]);
        fixture.WriteSource("references/dlv-d3-01.md", "# Reference");
        new Sealer(fixture.Repo, keys, CryptoRandomSource.Instance, fixture.Clock).Seal(SealPlanner.Plan(fixture.Sources, fixture.Repo).Single());
        var path = Path.Join(fixture.Repo, "sealed", "references", "dlv-d3-01.reference.bundle.json");
        var ledger = new SigningLedger(Path.Join(fixture.MaintainerDirectory, "ledger.jsonl"), Files.OwnerOnly);
        var signer = new Signer(key, trusted, keys, ledger, fixture.Clock);

        signer.Sign(path).ShouldBe(SignOutcome.NewSignature);
        signer.Sign(path).ShouldBe(SignOutcome.AlreadyValid);

        var bundle = SealedBundle.Read(path);
        trusted.Verify(bundle.SigningInput(), bundle.Signature).ShouldBeTrue();
        var entry = ledger.Read().Single();
        entry.ItemId.ShouldBe("dlv-d3-01.reference");
        entry.BundleSha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        entry.KeyFingerprint.ShouldBe(trusted.Fingerprint);
    }

    [Fact]
    public void Signing_refuses_a_key_the_engine_does_not_trust()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = new SignatureVerifier(other.ExportSubjectPublicKeyInfoPem());
        using var fixture = new MaintainerFixture();

        Should.Throw<AscentException>(() => new Signer(key, trusted, new KeyHierarchy(new byte[32]), new SigningLedger(Path.Join(fixture.Root, "l"), Files.OwnerOnly), fixture.Clock))
            .Message.ShouldContain("doesn't match");
    }

    [Fact]
    public void Signing_refuses_bundles_that_do_not_decrypt_or_have_a_wrong_label()
    {
        using var fixture = new MaintainerFixture();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trusted = new SignatureVerifier(key.ExportSubjectPublicKeyInfoPem());
        var keys = new KeyHierarchy(new byte[32]);
        var signer = new Signer(key, trusted, keys, new SigningLedger(Path.Join(fixture.Root, "l"), Files.OwnerOnly), fixture.Clock);

        var header = BundleHeader.Create("dlv-d3-01.reference", "reference", SealTier.Submitted, "text/markdown", new byte[12], DateTimeOffset.UnixEpoch);
        var garbled = Path.Join(fixture.Repo, "garbled.bundle.json");
        new SealedBundle(header, [1, 2, 3], new byte[16], []).Write(garbled);
        Should.Throw<AscentException>(() => signer.Sign(garbled)).Message.ShouldContain("doesn't decrypt");

        var labelled = Path.Join(fixture.Repo, "labelled.bundle.json");
        new SealedBundle(header, [1, 2, 3], new byte[16], []).Write(labelled);
        File.WriteAllText(labelled, File.ReadAllText(labelled).Replace("item:dlv-d3-01.reference", "item:other", StringComparison.Ordinal));
        Should.Throw<AscentException>(() => signer.Sign(labelled)).Message.ShouldContain("key-derivation label");
    }

    [Fact]
    public void The_dpapi_store_round_trips_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new MaintainerFixture();
        var store = new DpapiKeyStore(Path.Join(fixture.MaintainerDirectory, "signing-key.dpapi"), Files.OwnerOnly);
        store.Exists.ShouldBeFalse();
        store.Save([1, 2, 3]);

        store.Exists.ShouldBeTrue();
        store.Load().ShouldBe(new byte[] { 1, 2, 3 });
        File.ReadAllBytes(store.Location).ShouldNotBe(new byte[] { 1, 2, 3 });
    }
}
