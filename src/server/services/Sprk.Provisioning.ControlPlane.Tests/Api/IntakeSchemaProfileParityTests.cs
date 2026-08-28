// -----------------------------------------------------------------------------
// IntakeSchemaProfileParityTests.cs
//
// COMP-03 (customer-provisioning-orchestration-r1 SESSION 17 pre-dispatch
// remediation, 2026-08-27): contract-parity test that catches drift between
// L2 CODE and scripts/provisioning-prereqs/intake.schema.json enum values.
//
// WHY THIS TEST EXISTS:
//   `RunsEndpoints.KnownProfiles` + `RunsEndpoints.KnownTenancyModels` are the
//   authoritative L2-side enums the CreateRun endpoint validates against
//   (see `TryValidateTenancyProfilePair` — ISH-11). The intake schema declares
//   the SAME enums for batch-mode operators. If either surface drifts (a new
//   profile is added to the schema without a matching L2 constant, or vice
//   versa), the failure mode is silent — batch mode passes ajv validation,
//   dispatches, and either the endpoint 400s (schema strictness > code) OR
//   the endpoint accepts a value the schema said was legal but which no
//   handler knows how to route.
//
//   This test reads the schema JSON at test-time and asserts the two surfaces
//   are equal sets. FAILS THE BUILD the moment either drifts.
//
// ADR-038 alignment:
//   - KEEP category #4 (docs/standards/TEST-ARCHITECTURE.md §5) —
//     "contract parity between two authoritative sources".
//   - No Moq, no HTTP, no external service — pure file read + assertions.
//   - Sibling of Spaarke.ArchTests/CorsOriginRegistryTests + CredentialCensusTests
//     (parity tests that read a manifest and compare against code constants).
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Api;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Api;

public sealed class IntakeSchemaProfileParityTests
{
    /// <summary>
    /// Repository-root-relative path to the intake schema. Kept as a constant
    /// so a rename of the file breaks THIS test (which then points at the
    /// authoritative operator batch-mode contract).
    /// </summary>
    private const string IntakeSchemaRelativePath = "scripts/provisioning-prereqs/intake.schema.json";

    [Fact]
    public void KnownProfiles_MatchesIntakeSchemaEnum_Exactly()
    {
        var enumValues = ReadEnumFromSchema("properties.profile.enum");

        enumValues.Should().BeEquivalentTo(
            RunsEndpoints.KnownProfiles.All,
            "L2 `RunsEndpoints.KnownProfiles.All` and intake.schema.json profile enum MUST stay in sync — " +
            "drift causes silent batch-vs-endpoint validation asymmetry. Update BOTH surfaces together.");
    }

    [Fact]
    public void KnownTenancyModels_MatchesIntakeSchemaEnum_Exactly()
    {
        var enumValues = ReadEnumFromSchema("properties.tenancyModel.enum");

        enumValues.Should().BeEquivalentTo(
            RunsEndpoints.KnownTenancyModels.All,
            "L2 `RunsEndpoints.KnownTenancyModels.All` and intake.schema.json tenancyModel enum MUST stay in sync — " +
            "drift causes silent batch-vs-endpoint validation asymmetry. Update BOTH surfaces together.");
    }

    /// <summary>
    /// Bucket A HIGH#2 (SESSION 18): confirmationAcknowledgment carries the BAT-03 batch-mode
    /// operator attestation phrase (the batch equivalent of the interactive Step 3 confirmation
    /// gate — Wave 0 Decision 3 / NFR-11 auditability). It has a schema `const` of "proceed with
    /// provisioning" AND is enforced by SKILL.md Step 1.0 line 515-517. Prior to this fix the
    /// field was in `properties` but NOT in the top-level `required[]` array — so ajv batch
    /// validation silently passed intakes that were missing the phrase, and the operator only
    /// discovered the gap when the skill hard-stopped mid-dispatch. This test asserts the
    /// field stays required at ajv-validation time, so a future well-meaning removal fails
    /// the build here instead of failing silently in prod.
    /// </summary>
    [Fact]
    public void ConfirmationAcknowledgment_IsRequired_InIntakeSchema()
    {
        var required = ReadStringArrayFromSchema("required");

        required.Should().Contain(
            "confirmationAcknowledgment",
            "intake.schema.json top-level `required[]` MUST include `confirmationAcknowledgment` — " +
            "the field carries the BAT-03 batch-mode operator attestation (const 'proceed with " +
            "provisioning'). Without it in `required[]`, ajv validation silently passes intakes " +
            "that would then hard-stop at SKILL.md Step 1.0 line 515. See Bucket A HIGH#2 SESSION 18.");
    }

    // -------------------------------------------------------------------------
    // Helpers (additional)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> ReadStringArrayFromSchema(string dotPath)
    {
        var schemaPath = ResolveRepoRelativePath(IntakeSchemaRelativePath);
        Assert.True(File.Exists(schemaPath),
            $"Expected intake schema at '{schemaPath}'. If moved, update IntakeSchemaRelativePath.");

        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var current = doc.RootElement;
        foreach (var segment in dotPath.Split('.'))
        {
            Assert.True(current.TryGetProperty(segment, out var next),
                $"intake.schema.json missing property path segment '{segment}' (full path '{dotPath}').");
            current = next;
        }

        Assert.True(current.ValueKind == JsonValueKind.Array,
            $"Expected '{dotPath}' to be a JSON array; got {current.ValueKind}.");

        var results = new List<string>(current.GetArrayLength());
        foreach (var element in current.EnumerateArray())
        {
            Assert.True(element.ValueKind == JsonValueKind.String,
                $"Expected string elements in '{dotPath}'; got {element.ValueKind}.");
            results.Add(element.GetString()!);
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> ReadEnumFromSchema(string dotPath)
    {
        var schemaPath = ResolveRepoRelativePath(IntakeSchemaRelativePath);
        Assert.True(File.Exists(schemaPath),
            $"Expected intake schema at '{schemaPath}' (repo-root-relative '{IntakeSchemaRelativePath}'). " +
            "If the schema was moved/renamed, update IntakeSchemaRelativePath in this test AND the SKILL.md " +
            "Step 1.0 batch-mode reference.");

        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var current = doc.RootElement;
        foreach (var segment in dotPath.Split('.'))
        {
            Assert.True(current.TryGetProperty(segment, out var next),
                $"intake.schema.json missing property path segment '{segment}' (full path '{dotPath}'). " +
                "Schema structure changed — update this parity test.");
            current = next;
        }

        Assert.True(current.ValueKind == JsonValueKind.Array,
            $"Expected '{dotPath}' to be an array in intake.schema.json; got {current.ValueKind}.");

        var results = new List<string>(current.GetArrayLength());
        foreach (var element in current.EnumerateArray())
        {
            Assert.True(element.ValueKind == JsonValueKind.String,
                $"Expected string elements in '{dotPath}' array; got {element.ValueKind}.");
            results.Add(element.GetString()!);
        }
        return results;
    }

    /// <summary>
    /// Walks up from the test binary's working directory until it finds a
    /// `.git` marker (directory in a regular repo checkout, OR file in a git
    /// worktree — a worktree's `.git` is a plain text file containing
    /// `gitdir: /path/to/main/.git/worktrees/&lt;name&gt;`). Returns the repo-root-
    /// relative path joined onto that root. Fails hard if walk-up exhausts.
    /// </summary>
    private static string ResolveRepoRelativePath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
            {
                return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root walking up from '{AppContext.BaseDirectory}'. " +
            "This test requires a git working tree — CI runs in one by default.");
    }
}
