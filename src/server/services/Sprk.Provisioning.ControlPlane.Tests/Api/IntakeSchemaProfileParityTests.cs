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
using Sprk.Provisioning.ControlPlane.Core.Models;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Sprk.Provisioning.ControlPlane.Models;
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
    /// <summary>
    /// Task 225b (G6): the schema's tenancyModel × profile <c>allOf</c> rules and the endpoint's
    /// <c>TryValidateTenancyProfilePair</c> accept exactly the same pairs — over every known model × profile.
    /// The enum tests above compare only the value lists, not the pairing.
    /// </summary>
    [Fact]
    public void TenancyProfilePairs_SchemaAllOfAndEndpoint_AcceptTheSamePairs()
    {
        var schemaPath = ResolveRepoRelativePath(IntakeSchemaRelativePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var requiredProfileByModel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in doc.RootElement.GetProperty("allOf").EnumerateArray())
        {
            if (rule.TryGetProperty("if", out var cond)
                && cond.TryGetProperty("properties", out var condProps)
                && condProps.TryGetProperty("tenancyModel", out var tm) && tm.TryGetProperty("const", out var model)
                && rule.TryGetProperty("then", out var then)
                && then.TryGetProperty("properties", out var thenProps)
                && thenProps.TryGetProperty("profile", out var prof) && prof.TryGetProperty("const", out var profile))
            {
                requiredProfileByModel[model.GetString()!] = profile.GetString()!;
            }
        }

        requiredProfileByModel.Keys.Should().BeEquivalentTo(RunsEndpoints.KnownTenancyModels.All,
            "every tenancy model needs exactly one schema pairing rule");
        foreach (var model in RunsEndpoints.KnownTenancyModels.All)
        {
            foreach (var profile in RunsEndpoints.KnownProfiles.All)
            {
                var schemaAccepts = requiredProfileByModel[model] == profile;
                RunsEndpoints.TryValidateTenancyProfilePair(model, profile, out _).Should().Be(schemaAccepts,
                    $"schema and POST /api/runs must agree on {model} + {profile}");
            }
        }
    }

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

    /// <summary>
    /// T237: batch-mode intake (ajv against the schema) and the CreateRun edge
    /// (<see cref="CustomerIdStandard"/>) must apply the SAME customerId rule — otherwise a batch
    /// file passes ajv and is then refused by the API, or the reverse.
    /// </summary>
    [Fact]
    public void CustomerIdPattern_MatchesCustomerIdStandard_Exactly()
    {
        var schemaPath = ResolveRepoRelativePath(IntakeSchemaRelativePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var pattern = doc.RootElement.GetProperty("properties").GetProperty("customerId").GetProperty("pattern").GetString();

        pattern.Should().Be(CustomerIdStandard.Pattern,
            "intake.schema.json customerId.pattern and CustomerIdStandard.Pattern (enforced at POST /api/runs) " +
            "MUST be the same rule — see AZURE-RESOURCE-NAMING-CONVENTION.md § \"The customerId standard\".");
    }

    // -------------------------------------------------------------------------
    // Task 245c (G25): batch intake (ajv) and POST /api/runs apply the same
    // operator-intake rules — H11's (UserProvisioningIntake), H14's and H4's.
    // -------------------------------------------------------------------------

    [Fact]
    public void IdentityPresetEnum_MatchesUserProvisioningIntake_Exactly()
    {
        ReadEnumFromSchema("properties.identityPreset.enum").Should().BeEquivalentTo(
            UserProvisioningIntake.IdentityPresets,
            "intake.schema.json identityPreset.enum and the presets H11 / POST /api/runs accept MUST be the same set");
    }

    [Fact]
    public void CommunicationDefaultMailboxPattern_MatchesIntakeParameterCatalog_Exactly()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolveRepoRelativePath(IntakeSchemaRelativePath)));

        doc.RootElement.GetProperty("properties").GetProperty("communicationDefaultMailbox").GetProperty("pattern").GetString()
            .Should().Be(IntakeParameterCatalog.MailboxAddressPattern,
                "the schema pattern and IntakeParameterCatalog.MailboxAddressPattern (POST /api/runs) MUST be the same rule");
    }

    /// <summary>
    /// The schema states each POST /api/runs operator-intake rule. It may be STRICTER than the API (e.g. it refuses a
    /// blank optional Graph resource the API ignores), never looser — a value ajv accepts must not be refused by
    /// POST /api/runs. (Known residue: ECMA and .NET disagree on a few exotic whitespace code points, e.g. U+0085,
    /// for the `\S` non-blank pattern.)
    /// </summary>
    [Fact]
    public void OperatorIntakeRules_AreStatedByTheSchema()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolveRepoRelativePath(IntakeSchemaRelativePath)));
        var root = doc.RootElement;
        var properties = root.GetProperty("properties");

        ReadStringArrayFromSchema("required").Should().Contain(
            ["identityPreset", "users", "exchangePolicyScopeGroupId", "communicationDefaultMailbox"],
            "POST /api/runs refuses a run without any of these");

        var users = properties.GetProperty("users");
        users.GetProperty("minItems").GetInt32().Should().Be(1, "H11 requires at least one user");
        users.GetProperty("maxItems").GetInt32().Should().Be(UserProvisioningIntake.MaxUsers);
        var userFields = users.GetProperty("items").GetProperty("properties");
        foreach (var field in new[] { "firstName", "lastName", "email" })
        {
            userFields.GetProperty(field).GetProperty("pattern").GetString().Should().Be(@"\S",
                $"a blank users[].{field} is refused by POST /api/runs (IsNullOrWhiteSpace), so ajv must refuse it too");
        }
        foreach (var key in new[] { "exchangePolicyScopeGroupId", "communicationGraphResource", "emailGraphResource" })
        {
            properties.GetProperty(key).GetProperty("pattern").GetString().Should().Be(@"\S", $"{key} must be non-blank");
        }
        properties.GetProperty("communicationDefaultMailbox").GetProperty("maxLength").GetInt32()
            .Should().Be(IntakeParameterCatalog.MaxMailboxAddressLength);

        var allOf = root.GetProperty("allOf").EnumerateArray().ToList();
        allOf.Any(IsAtLeastOneGraphResourceRule).Should().BeTrue("at least one Graph resource — H14b's rule");
        allOf.Any(r => IsPresetUsersRule(r, UserProvisioningIntake.NativeAccount, ["firstName", "lastName"]))
            .Should().BeTrue("NativeAccount users need both names — H11's rule");
        allOf.Any(r => IsPresetUsersRule(r, UserProvisioningIntake.B2BGuest, ["email"]))
            .Should().BeTrue("B2BGuest users need an email — H11's rule");

        static IEnumerable<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!);

        static bool IsAtLeastOneGraphResourceRule(JsonElement rule)
            => rule.TryGetProperty("anyOf", out var anyOf)
                && anyOf.EnumerateArray().Select(a => string.Join(",", Strings(a.GetProperty("required")))).Order()
                    .SequenceEqual(["communicationGraphResource", "emailGraphResource"]);

        static bool IsPresetUsersRule(JsonElement rule, string preset, string[] requiredFields)
            => rule.TryGetProperty("if", out var condition)
                && condition.GetProperty("properties").TryGetProperty("identityPreset", out var presetRule)
                && presetRule.GetProperty("const").GetString() == preset
                && Strings(rule.GetProperty("then").GetProperty("properties").GetProperty("users")
                    .GetProperty("items").GetProperty("required")).SequenceEqual(requiredFields);
    }

    /// <summary>
    /// The other direction: each schema example passes POST /api/runs' operator-intake rules, and dropping any
    /// operator value the schema requires is refused by POST /api/runs too — so the schema cannot require something
    /// the API silently lets through.
    /// </summary>
    [Fact]
    public void SchemaExamples_PassTheEndpointRules_AndEachSchemaRequiredOperatorValueIsEnforcedThere()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolveRepoRelativePath(IntakeSchemaRelativePath)));
        var examples = doc.RootElement.GetProperty("examples").EnumerateArray().ToList();
        examples.Should().NotBeEmpty();
        var schemaRequired = ReadStringArrayFromSchema("required").ToHashSet(StringComparer.Ordinal);

        foreach (var example in examples)
        {
            var nonSecret = ToOperatorNonSecret(example);
            RunsEndpoints.ValidateOperatorIntake(nonSecret).Should().BeNull(
                "a schema example ({0}) is a complete intake", example.GetProperty("customerId").GetString());

            foreach (var (schemaKey, apiKey) in OperatorKeys.Where(k => schemaRequired.Contains(k.SchemaKey)))
            {
                var without = new Dictionary<string, string>(nonSecret, StringComparer.Ordinal);
                without.Remove(apiKey);
                RunsEndpoints.ValidateOperatorIntake(without).Should().NotBeNull(
                    "the schema requires '{0}', so POST /api/runs must refuse a run without '{1}'", schemaKey, apiKey);
            }

            var noGraphResource = new Dictionary<string, string>(nonSecret, StringComparer.Ordinal);
            noGraphResource.Remove("communicationGraphResource");
            noGraphResource.Remove("emailGraphResource");
            RunsEndpoints.ValidateOperatorIntake(noGraphResource).Should().NotBeNull(
                "the schema requires at least one Graph resource, so POST /api/runs must too");
        }
    }

    /// <summary>
    /// T228: the schema requires the customer's subscription and Dataverse environment for every model, as POST /api/runs
    /// does, and each example's environment passes the endpoint's rule (DataverseEnvironmentUrlRule) for that example's
    /// customer — so a batch intake ajv accepts is not refused at the edge.
    /// </summary>
    [Fact]
    public void T228_SubscriptionAndEnvironment_AreRequiredByBoth_AndTheExamplesPassTheEndpointRule()
    {
        var schemaRequired = ReadStringArrayFromSchema("required");
        schemaRequired.Should().Contain(new[] { "subscriptionId", "dataverseEnvUrl" });

        using var doc = JsonDocument.Parse(File.ReadAllText(ResolveRepoRelativePath(IntakeSchemaRelativePath)));
        foreach (var example in doc.RootElement.GetProperty("examples").EnumerateArray())
        {
            var customerId = example.GetProperty("customerId").GetString()!;
            Guid.TryParse(example.GetProperty("subscriptionId").GetString(), out _).Should().BeTrue();
            Sprk.Provisioning.ControlPlane.Core.Models.DataverseEnvironmentUrlRule.TryNormalize(
                    example.GetProperty("dataverseEnvUrl").GetString(), customerId,
                    Sprk.Provisioning.ControlPlane.Models.IntakeParameterCatalog.DefaultEnvironmentName, out _, out var error)
                .Should().BeTrue("example '{0}': {1}", customerId, error);
        }
    }

    /// <summary>
    /// T229: the schema's tier enum is exactly the tiers H0 has ceilings for (CostEnvelopeIntake.Tiers — the keys of
    /// H0Options.DefaultCeilingsUsd), both cost inputs are required, and the retired waiver is not in the schema.
    /// </summary>
    [Fact]
    public void T229_TierEnumIsH0sCeilingTable_BothCostInputsAreRequired_AndNoWaiverExists()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolveRepoRelativePath(IntakeSchemaRelativePath)));
        var properties = doc.RootElement.GetProperty("properties");
        properties.GetProperty("tier").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(Sprk.Provisioning.ControlPlane.Handlers.Preflight.CostEnvelopeIntake.Tiers,
                "a tier the schema accepts but H0 has no ceiling for would be refused at POST /api/runs");
        ReadStringArrayFromSchema("required").Should().Contain(new[] { "tier", "estimatedMonthlyUsd" });
        properties.TryGetProperty("costEnvelopePolicy", out _).Should().BeFalse(
            "warnAndProceed was the shared-trial waiver; a dedicated stamp's overrun has none (T229)");
    }

    /// <summary>Schema property → POST /api/runs nonSecretParameters key (the skill sends <c>users</c> as <c>usersJson</c>).</summary>
    private static readonly (string SchemaKey, string ApiKey)[] OperatorKeys =
    [
        ("identityPreset", "identityPreset"),
        ("users", "usersJson"),
        ("exchangePolicyScopeGroupId", "exchangePolicyScopeGroupId"),
        ("communicationGraphResource", "communicationGraphResource"),
        ("emailGraphResource", "emailGraphResource"),
        ("communicationDefaultMailbox", "communicationDefaultMailbox"),
        ("tier", "tier"),                                   // T229
        ("estimatedMonthlyUsd", "estimatedMonthlyUsd"),
    ];

    private static Dictionary<string, string> ToOperatorNonSecret(JsonElement example)
    {
        var nonSecret = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (schemaKey, apiKey) in OperatorKeys)
        {
            if (example.TryGetProperty(schemaKey, out var value))
            {
                nonSecret[apiKey] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            }
        }
        return nonSecret;
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
