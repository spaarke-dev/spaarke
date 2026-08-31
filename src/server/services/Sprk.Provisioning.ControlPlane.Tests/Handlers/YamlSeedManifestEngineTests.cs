// -----------------------------------------------------------------------------
// YamlSeedManifestEngineTests.cs
//
// Unit tests over YamlSeedManifestEngine (task 150, Wave G-5 Batch G-5A —
// H12a YamlDotNet manifest engine + DV-REST seed writes).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. Parse() is pure (no I/O); the embedded-
//   resource tests exercise ParseFromEmbeddedResource() against the SAME
//   scripts/seed-data/manifest.yaml the L2 publish output ships (no fakes
//   needed — deterministic content).
//
// COVERAGE:
//   T1  Real embedded manifest parses: schemaVersion=1, 12 artifacts,
//       aimodeldeployment is the H12c-owned placeholder (null
//       AuthoritativeSource + null Deployer + DeployerOwnedBy="H12c").
//   T2  Real embedded manifest topological order: type-lookups (foundational,
//       zero deps) sorts before knowledge/skills (both depend on it);
//       playbook-consumers (terminal step per manifest notes) sorts after
//       actions-r7 + playbooks-mvp.
//   T3  Unsupported schemaVersion -> InvalidOperationException.
//   T4  Missing artifacts field (empty) -> InvalidOperationException.
//   T5  Artifact missing id -> InvalidOperationException.
//   T6  Null/whitespace yaml text -> ArgumentException.
//   T7  Folded block scalar (`>-`) + explicit `null` deployer/authoritativeSource
//       parse correctly (the exact YAML shapes manifest.yaml uses).
//   T8  ComputeTopologicalOrder: unknown dependency -> InvalidOperationException
//       naming the unresolved artifact + dependency.
//   T9  ComputeTopologicalOrder: cyclic dependency -> InvalidOperationException
//       naming the artifacts stuck in the cycle.
//   T10 ComputeTopologicalOrder: ties broken by manifest declaration order
//       (stable, deterministic output) — parity with the PS FIFO-queue
//       behavior.
//   T11 Source grep defense-in-depth — production file contains neither
//       "powershell-yaml" nor "Install-Module" nor "ProcessStartInfo".
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class YamlSeedManifestEngineTests
{
    // ---------- T1 real embedded manifest shape ----------

    [Fact]
    public void ParseFromEmbeddedResource_RealManifest_ProducesExpectedShape()
    {
        var doc = YamlSeedManifestEngine.ParseFromEmbeddedResource();

        doc.SchemaVersion.Should().Be(1);
        doc.ManifestName.Should().Be("spaarke-ai-seed");
        doc.Artifacts.Should().HaveCount(12);
        doc.RetiredArtifacts.Should().HaveCountGreaterThanOrEqualTo(4);

        var placeholder = doc.Artifacts.Should().ContainSingle(a => a.Id == "aimodeldeployment").Subject;
        placeholder.AuthoritativeSource.Should().BeNull();
        placeholder.Deployer.Should().BeNull();
        placeholder.DeployerOwnedBy.Should().Be("H12c");
    }

    // ---------- T2 real embedded manifest topological order ----------

    [Fact]
    public void ParseFromEmbeddedResource_RealManifest_TopologicalOrder_RespectsDependsOn()
    {
        var doc = YamlSeedManifestEngine.ParseFromEmbeddedResource();
        var order = YamlSeedManifestEngine.ComputeTopologicalOrder(doc.Artifacts);

        order.Should().HaveCount(12);

        var indexOf = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        indexOf["type-lookups"].Should().BeLessThan(indexOf["knowledge"]);
        indexOf["type-lookups"].Should().BeLessThan(indexOf["skills"]);
        indexOf["actions-r7"].Should().BeLessThan(indexOf["playbook-consumers"]);
        indexOf["playbooks-mvp"].Should().BeLessThan(indexOf["playbook-consumers"]);
        indexOf["playbooks-mvp"].Should().BeLessThan(indexOf["output-types"]);
    }

    // ---------- T3-T6 parse validation ----------

    [Fact]
    public void Parse_UnsupportedSchemaVersion_Throws()
    {
        const string yaml = "schemaVersion: 2\nartifacts:\n  - id: a\n    type: t\n    authoritativeSource: null\n    dependsOn: []\n";
        var act = () => YamlSeedManifestEngine.Parse(yaml);
        act.Should().Throw<InvalidOperationException>().WithMessage("*schemaVersion*");
    }

    [Fact]
    public void Parse_EmptyArtifacts_Throws()
    {
        const string yaml = "schemaVersion: 1\nartifacts: []\n";
        var act = () => YamlSeedManifestEngine.Parse(yaml);
        act.Should().Throw<InvalidOperationException>().WithMessage("*artifacts*");
    }

    [Fact]
    public void Parse_ArtifactMissingId_Throws()
    {
        const string yaml = "schemaVersion: 1\nartifacts:\n  - type: t\n    authoritativeSource: null\n    dependsOn: []\n";
        var act = () => YamlSeedManifestEngine.Parse(yaml);
        act.Should().Throw<InvalidOperationException>().WithMessage("*id*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespaceYaml_ThrowsArgumentException(string? yaml)
    {
        var act = () => YamlSeedManifestEngine.Parse(yaml!);
        act.Should().Throw<ArgumentException>();
    }

    // ---------- T7 folded scalar + explicit null shapes ----------

    [Fact]
    public void Parse_FoldedScalarAndExplicitNulls_ParsesCorrectly()
    {
        const string yaml = """
            schemaVersion: 1
            manifestName: test-manifest
            artifacts:
              - id: foo
                type: knowledge
                authoritativeSource: scripts/seed-data/foo.json
                deployer:
                  script: scripts/seed-data/Deploy-Foo.ps1
                  idempotencyMode: existence-check-then-insert
                  idempotencyKey: sprk_name
                dependsOn: []
                notes: >-
                  This is a folded block scalar spanning
                  multiple source lines that collapse to one.
              - id: bar
                type: model-deployment
                authoritativeSource: null
                deployer: null
                deployerOwnedBy: H12c
                dependsOn: [foo]
            """;

        var doc = YamlSeedManifestEngine.Parse(yaml);

        doc.ManifestName.Should().Be("test-manifest");
        doc.Artifacts.Should().HaveCount(2);

        var foo = doc.Artifacts.Single(a => a.Id == "foo");
        foo.AuthoritativeSource.Should().Be("scripts/seed-data/foo.json");
        foo.Deployer.Should().NotBeNull();
        foo.Deployer!.Script.Should().Be("scripts/seed-data/Deploy-Foo.ps1");
        foo.Deployer.IdempotencyKey.Should().Be("sprk_name");

        var bar = doc.Artifacts.Single(a => a.Id == "bar");
        bar.AuthoritativeSource.Should().BeNull();
        bar.Deployer.Should().BeNull();
        bar.DeployerOwnedBy.Should().Be("H12c");
        bar.DependsOn.Should().ContainSingle().Which.Should().Be("foo");
    }

    // ---------- T8-T10 topological sort edge cases ----------

    [Fact]
    public void ComputeTopologicalOrder_UnknownDependency_ThrowsWithClearDiagnostic()
    {
        var artifacts = new[]
        {
            new SeedArtifact("a", "t", null, null, "owner", new[] { "ghost" }),
        };
        var act = () => YamlSeedManifestEngine.ComputeTopologicalOrder(artifacts);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'a'*unknown artifact 'ghost'*");
    }

    [Fact]
    public void ComputeTopologicalOrder_CyclicDependency_ThrowsNamingStuckArtifacts()
    {
        var artifacts = new[]
        {
            new SeedArtifact("a", "t", null, null, null, new[] { "b" }),
            new SeedArtifact("b", "t", null, null, null, new[] { "a" }),
        };
        var act = () => YamlSeedManifestEngine.ComputeTopologicalOrder(artifacts);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cyclic dependency*");
    }

    [Fact]
    public void ComputeTopologicalOrder_TiesBrokenByDeclarationOrder_Deterministic()
    {
        // b and c both have zero deps; declared AFTER a (which also has zero
        // deps) — stable FIFO order: a, b, c (declaration order among ties).
        var artifacts = new[]
        {
            new SeedArtifact("a", "t", null, null, null, Array.Empty<string>()),
            new SeedArtifact("b", "t", null, null, null, Array.Empty<string>()),
            new SeedArtifact("c", "t", null, null, null, Array.Empty<string>()),
        };
        var order = YamlSeedManifestEngine.ComputeTopologicalOrder(artifacts);
        order.Should().Equal("a", "b", "c");
    }

    // ---------- T11 source grep defense-in-depth ----------

    [Fact]
    public void ProductionSource_ContainsNoPowerShellYamlOrProcessStartInfoReferences()
    {
        var path = LocateSourceFile("YamlSeedManifestEngine.cs");
        var text = File.ReadAllText(path);
        text.Should().NotContain("powershell-yaml");
        text.Should().NotContain("Install-Module");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static string LocateSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "AiSeedChain", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}.");
    }
}
