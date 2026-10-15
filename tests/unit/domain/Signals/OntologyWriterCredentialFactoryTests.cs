using System.Reflection;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Pins the Signal writer's credential shape (owner-approved amendment 2026-10-07, spec.md §6 ADR-028 row):
/// a tenant-pinned <see cref="DefaultAzureCredential"/> whose ONLY source is the writer's own user-assigned
/// managed identity.
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure option composition plus one fail-closed
/// guard. Reads the options through <see cref="OntologyWriterCredentialFactory.BuildOptions"/> (<c>internal</c>,
/// via the assembly's <c>InternalsVisibleTo</c>) — the reflection below is over the PUBLIC properties of the
/// third-party options type, never into non-public members (tests/CLAUDE.md B8).</para>
/// <para>Why the selection guard has its own tests: in Azure.Identity 1.21.0, <c>AZURE_TOKEN_CREDENTIALS</c>
/// overrides the <c>Exclude*</c> options (measured 2026-10-07), so the exclusions alone would not stop a host
/// setting from handing the writer the CLI login or the environment's service principal.</para>
/// </remarks>
public class OntologyWriterCredentialFactoryTests
{
    private const string WriterClientId = "69040982-612e-469e-a85f-26d5172367c5";
    private const string AzureTenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string LegacyTenantId = "11111111-2222-3333-4444-555555555555";

    // =====================================================================================
    // Exclusions — every source but the writer's managed identity
    // =====================================================================================

    [Fact]
    public void BuildOptions_ExcludesEveryCredentialSourceExceptManagedIdentity()
    {
        var options = OntologyWriterCredentialFactory.BuildOptions(Config(clientId: WriterClientId, azureTenantId: AzureTenantId));

        // Enumerated from the INSTALLED package, not from a list in this file: a source a future Azure.Identity adds
        // arrives as a new Exclude* property defaulting to false, and fails here until the factory excludes it.
        var excludeProperties = typeof(DefaultAzureCredentialOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("Exclude", StringComparison.Ordinal))
            .ToList();

        // Non-vacuity: the enumeration must actually be finding the switches (a rename to e.g. "Include*" would
        // otherwise make the loop below pass over nothing).
        excludeProperties.Select(p => p.Name).Should().Contain(new[]
        {
            nameof(DefaultAzureCredentialOptions.ExcludeManagedIdentityCredential),
            nameof(DefaultAzureCredentialOptions.ExcludeAzureCliCredential),
            nameof(DefaultAzureCredentialOptions.ExcludeEnvironmentCredential),
            nameof(DefaultAzureCredentialOptions.ExcludeWorkloadIdentityCredential),
        });

        var notExcluded = excludeProperties
            .Where(p => p.Name != nameof(DefaultAzureCredentialOptions.ExcludeManagedIdentityCredential))
            .Where(p => !(bool)p.GetValue(options)!)
            .Select(p => p.Name)
            .ToList();

        notExcluded.Should().BeEmpty(
            "the writer's chain must contain NOTHING but its own managed identity — every other Exclude* switch must be true");
        options.ExcludeManagedIdentityCredential.Should().BeFalse("managed identity is the writer's only credential source");
    }

    // =====================================================================================
    // Identity + tenant pinned from configuration
    // =====================================================================================

    [Fact]
    public void BuildOptions_PinsTheWritersClientIdAndTheTenant_FromConfiguration()
    {
        var options = OntologyWriterCredentialFactory.BuildOptions(Config(clientId: WriterClientId, azureTenantId: AzureTenantId));

        options.ManagedIdentityClientId.Should().Be(WriterClientId);
        options.TenantId.Should().Be(AzureTenantId);
    }

    [Fact]
    public void BuildOptions_TenantFallsBackToTenantIdKey_WhenAzureTenantIdIsAbsent()
    {
        var options = OntologyWriterCredentialFactory.BuildOptions(Config(clientId: WriterClientId, legacyTenantId: LegacyTenantId));

        options.TenantId.Should().Be(LegacyTenantId);
    }

    [Fact]
    public void BuildOptions_AzureTenantIdTakesPrecedence_LikeTheCentralFactory()
    {
        var options = OntologyWriterCredentialFactory.BuildOptions(
            Config(clientId: WriterClientId, azureTenantId: AzureTenantId, legacyTenantId: LegacyTenantId));

        options.TenantId.Should().Be(AzureTenantId);
    }

    [Fact]
    public void BuildOptions_NeverReadsTheSysadminIdentityKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OntologyWriterCredentialFactory.ConfigKey] = WriterClientId,
                ["AZURE_TENANT_ID"] = AzureTenantId,
                ["Graph:ManagedIdentity:ClientId"] = "99999999-9999-9999-9999-999999999999",
                ["ManagedIdentity:ClientId"] = "88888888-8888-8888-8888-888888888888",
            })
            .Build();

        OntologyWriterCredentialFactory.BuildOptions(configuration).ManagedIdentityClientId.Should().Be(WriterClientId);
    }

    // =====================================================================================
    // Fail closed
    // =====================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildOptions_EmptyWriterClientId_Throws(string? clientId)
    {
        var act = () => OntologyWriterCredentialFactory.BuildOptions(Config(clientId: clientId, azureTenantId: AzureTenantId));

        act.Should().Throw<InvalidOperationException>().WithMessage($"{OntologyWriterCredentialFactory.ConfigKey} is empty*");
    }

    [Fact]
    public void Create_EmptyWriterClientId_Throws()
    {
        var act = () => OntologyWriterCredentialFactory.Create(Config(clientId: "", azureTenantId: AzureTenantId));

        // The empty-key refusal specifically — not merely any refusal (the selection guard also throws in this
        // assembly, and its message names the config key too).
        act.Should().Throw<InvalidOperationException>().WithMessage($"{OntologyWriterCredentialFactory.ConfigKey} is empty*");
    }

    [Fact]
    public void BuildOptions_NoTenantConfigured_Throws()
    {
        var act = () => OntologyWriterCredentialFactory.BuildOptions(Config(clientId: WriterClientId));

        act.Should().Throw<InvalidOperationException>().WithMessage("*TENANT_ID*");
    }

    // =====================================================================================
    // AZURE_TOKEN_CREDENTIALS cannot override the exclusions
    // =====================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData("ManagedIdentityCredential")]
    [InlineData("managedidentitycredential")]
    public void CredentialSelectionGuard_AllowsOnlyManagedIdentitySelections(string? selection)
    {
        var act = () => OntologyWriterCredentialFactory.EnsureCredentialSelectionIsManagedIdentityOnly(selection);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("AzureCliCredential")]
    [InlineData("EnvironmentCredential")]
    [InlineData("WorkloadIdentityCredential")]
    [InlineData("VisualStudioCredential")]
    [InlineData("VisualStudioCodeCredential")]
    [InlineData("AzurePowerShellCredential")]
    [InlineData("AzureDeveloperCliCredential")]
    [InlineData("InteractiveBrowserCredential")]
    [InlineData("BrokerCredential")]
    [InlineData("AzurePipelinesCredential")]
    [InlineData("ManagedIdentityAsFederatedIdentityCredential")]
    [InlineData("dev")]
    [InlineData("not-a-credential")]
    public void CredentialSelectionGuard_RefusesEveryOtherSelection(string selection)
    {
        var act = () => OntologyWriterCredentialFactory.EnsureCredentialSelectionIsManagedIdentityOnly(selection);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AZURE_TOKEN_CREDENTIALS*");
    }

    [Fact]
    public void Create_AppliesTheSelectionGuard_ToTheAmbientEnvironment()
    {
        // End to end through the PUBLIC entry point. This test assembly's module initializer sets
        // AZURE_TOKEN_CREDENTIALS=EnvironmentCredential (TestOutboundNetworkGuard, Layer 1) unless the runner set it
        // already — live seam runs use AzureCliCredential. Both are exactly the overrides the writer must refuse.
        var ambient = Environment.GetEnvironmentVariable(DefaultAzureCredential.DefaultEnvironmentVariableName);
        var configuration = Config(clientId: WriterClientId, azureTenantId: AzureTenantId);

        var act = () => OntologyWriterCredentialFactory.Create(configuration);

        var ambientIsManagedIdentityOnly = string.IsNullOrWhiteSpace(ambient)
            || ambient.Trim().Equals("prod", StringComparison.OrdinalIgnoreCase)
            || ambient.Trim().Equals("ManagedIdentityCredential", StringComparison.OrdinalIgnoreCase);

        if (ambientIsManagedIdentityOnly)
        {
            act.Should().NotThrow().Which.Should().BeOfType<DefaultAzureCredential>();
        }
        else
        {
            act.Should().Throw<InvalidOperationException>().WithMessage("*AZURE_TOKEN_CREDENTIALS*");
        }
    }

    private static IConfiguration Config(string? clientId, string? azureTenantId = null, string? legacyTenantId = null)
    {
        var values = new Dictionary<string, string?> { [OntologyWriterCredentialFactory.ConfigKey] = clientId };
        if (azureTenantId is not null) values["AZURE_TENANT_ID"] = azureTenantId;
        if (legacyTenantId is not null) values["TENANT_ID"] = legacyTenantId;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
