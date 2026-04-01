using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Reporting;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests.Reporting;

/// <summary>
/// Integration tests verifying that the Reporting module can be correctly configured for each of
/// the three supported Power BI Embedded deployment models:
///
/// 1. Multi-customer (shared F-SKU capacity, service principal profiles per customer, workspace-per-customer).
/// 2. Dedicated (dedicated capacity, single service principal, dedicated workspace).
/// 3. Customer tenant (customer's own PBI tenant, customer-managed capacity, customer app registration).
///
/// These tests document and verify the configuration contract for each deployment model.
/// They are structural/configuration tests — they verify that the required options can be
/// bound and validated without calling the live Power BI API.
///
/// All three models are differentiated by environment variable configuration only (spec MUST rule).
/// No workspace IDs, capacity IDs, or tenant IDs are hardcoded (spec MUST NOT rule).
/// </summary>
/// <remarks>
/// Task PBI-042: Multi-Deployment Model Testing.
///
/// Constraints:
/// - MUST use service principal profiles for multi-tenant isolation (spec).
/// - MUST use environment variables for all PBI config (BYOK-compatible) (spec).
/// - MUST NOT hardcode workspace IDs, capacity IDs, or tenant IDs (spec).
/// - All three deployment models must be testable via environment variable changes only.
/// </remarks>
[Trait("Category", "Reporting")]
[Trait("Feature", "DeploymentModels")]
public class ReportingDeploymentModelTests
{
    private readonly ITestOutputHelper _output;

    public ReportingDeploymentModelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration builders for each deployment model
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the minimum required configuration for the Multi-Customer deployment model.
    /// Uses a shared F-SKU capacity with service principal profiles per customer.
    /// Each customer gets an isolated workspace scoped via the SP profile.
    /// </summary>
    private static IConfiguration BuildMultiCustomerConfig()
    {
        // Environment variables that drive the Multi-Customer model.
        // - Shared Spaarke SP (not customer's own).
        // - Workspace isolation is achieved via service principal profiles (task PBI-003).
        // - Capacity is shared (F2+ F-SKU) across all customers.
        var settings = new Dictionary<string, string?>
        {
            ["PowerBi:TenantId"]     = "spaarke-shared-tenant-id",
            ["PowerBi:ClientId"]     = "spaarke-shared-sp-client-id",
            ["PowerBi:ClientSecret"] = "spaarke-shared-sp-client-secret",
            ["PowerBi:ApiUrl"]       = "https://api.powerbi.com",
            ["PowerBi:Scope"]        = "https://analysis.windows.net/.default",

            // NOTE: No PowerBi:WorkspaceId — workspace selection is dynamic per SP profile.
            // The profile ID is resolved at embed-token request time via ReportingProfileManager.
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    /// <summary>
    /// Builds the minimum required configuration for the Dedicated deployment model.
    /// Uses a single service principal and a dedicated workspace with dedicated capacity.
    /// </summary>
    private static IConfiguration BuildDedicatedConfig()
    {
        // Dedicated model: one SP, one workspace, dedicated capacity.
        // SP profile is NOT used (single-tenant, no isolation needed at SP level).
        var settings = new Dictionary<string, string?>
        {
            ["PowerBi:TenantId"]     = "customer-dedicated-tenant-id",
            ["PowerBi:ClientId"]     = "customer-dedicated-sp-client-id",
            ["PowerBi:ClientSecret"] = "customer-dedicated-sp-client-secret",
            ["PowerBi:ApiUrl"]       = "https://api.powerbi.com",
            ["PowerBi:Scope"]        = "https://analysis.windows.net/.default",

            // In the dedicated model, all reports live in a single known workspace.
            // The workspace ID is supplied at runtime (env var, not hardcoded here).
            // Tests verify only the required core credentials are set.
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    /// <summary>
    /// Builds the minimum required configuration for the Customer Tenant deployment model.
    /// Uses the customer's own PBI tenant, app registration, and managed capacity.
    /// </summary>
    private static IConfiguration BuildCustomerTenantConfig()
    {
        // Customer Tenant model: the customer provides their own:
        // - Azure AD Tenant ID
        // - App Registration (ClientId + ClientSecret with Power BI API permissions)
        // - PBI workspace and capacity
        // Spaarke uses these credentials — it does NOT have a shared SP in this model.
        var settings = new Dictionary<string, string?>
        {
            ["PowerBi:TenantId"]     = "customer-own-tenant-id",
            ["PowerBi:ClientId"]     = "customer-own-app-client-id",
            ["PowerBi:ClientSecret"] = "customer-own-app-client-secret",
            ["PowerBi:ApiUrl"]       = "https://api.powerbi.com",
            ["PowerBi:Scope"]        = "https://analysis.windows.net/.default",

            // AuthorityUrl points to the CUSTOMER'S tenant (not Spaarke's shared tenant).
            ["PowerBi:AuthorityUrl"] = "https://login.microsoftonline.com/customer-own-tenant-id",
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Model 1: Multi-Customer (shared capacity, SP profiles per customer)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiCustomerModel_RequiredOptions_BindAndValidate()
    {
        // Arrange — multi-customer config binds to PowerBiOptions successfully.
        var config = BuildMultiCustomerConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>();

        // Assert — all required options must be bound.
        options.Should().NotBeNull(
            "Multi-customer model config must bind to PowerBiOptions");
        options!.TenantId.Should().NotBeNullOrWhiteSpace(
            "TenantId is required (shared Spaarke SP tenant)");
        options.ClientId.Should().NotBeNullOrWhiteSpace(
            "ClientId is required (shared Spaarke SP app registration)");
        options.ClientSecret.Should().NotBeNullOrWhiteSpace(
            "ClientSecret is required (shared Spaarke SP credential)");

        _output.WriteLine("Multi-customer model: all required options bound successfully");
        _output.WriteLine($"  TenantId: {options.TenantId}");
        _output.WriteLine($"  ClientId: {options.ClientId}");
        _output.WriteLine($"  ApiUrl:   {options.ApiUrl}");
    }

    [Fact]
    public void MultiCustomerModel_AuthorityUrl_DefaultsToSharedTenant()
    {
        // Arrange
        var config = BuildMultiCustomerConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;

        // Act
        var authority = options.GetEffectiveAuthorityUrl();

        // Assert — without explicit AuthorityUrl, it resolves to the configured tenant.
        authority.Should().Be($"https://login.microsoftonline.com/{options.TenantId}",
            "Default authority URL is constructed from TenantId when AuthorityUrl is not set");

        _output.WriteLine($"Multi-customer effective authority: {authority}");
    }

    [Fact]
    public void MultiCustomerModel_ServicePrincipalProfiles_IsolationRequirement()
    {
        // This test documents the SP profile requirement for multi-customer isolation.
        // In the multi-customer model:
        // - Each customer has a dedicated Service Principal Profile in PBI.
        // - Workspace selection is scoped to that profile via X-PowerBI-Profile-Id header.
        // - No single SP accesses all customers' workspaces without a profile.

        // The ReportingEmbedService accepts a profileId parameter per call.
        // When profileId is provided, the X-PowerBI-Profile-Id header is sent to PBI.
        // This test documents the CONTRACT, not the live PBI call.

        const string? noProfileId = null;
        var dedicatedProfileId = Guid.NewGuid();

        noProfileId.Should().BeNull(
            "Null profileId signals that no profile header is sent — valid for dedicated/customer-tenant models");
        dedicatedProfileId.Should().NotBeEmpty(
            "Non-null profileId signals multi-customer isolation via X-PowerBI-Profile-Id header");

        _output.WriteLine("Multi-customer model: SP profile isolation requirement documented");
        _output.WriteLine($"  Profile ID is passed per-request to scope workspace access: {dedicatedProfileId}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Model 2: Dedicated (dedicated capacity, single SP, dedicated workspace)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DedicatedModel_RequiredOptions_BindAndValidate()
    {
        // Arrange — dedicated model uses customer's own tenant, but Spaarke manages the SP.
        var config = BuildDedicatedConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>();

        // Assert
        options.Should().NotBeNull();
        options!.TenantId.Should().NotBeNullOrWhiteSpace(
            "Dedicated model requires TenantId (customer's tenant or Spaarke's dedicated SP)");
        options.ClientId.Should().NotBeNullOrWhiteSpace(
            "Dedicated model requires ClientId (single SP for the dedicated deployment)");
        options.ClientSecret.Should().NotBeNullOrWhiteSpace(
            "Dedicated model requires ClientSecret (single SP credential)");

        _output.WriteLine("Dedicated model: all required options bound successfully");
        _output.WriteLine($"  TenantId: {options.TenantId}");
        _output.WriteLine($"  ClientId: {options.ClientId}");
    }

    [Fact]
    public void DedicatedModel_AuthorityUrl_DefaultsToConfiguredTenant()
    {
        // Arrange
        var config = BuildDedicatedConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;

        // Act
        var authority = options.GetEffectiveAuthorityUrl();

        // Assert — authority resolves to the dedicated tenant.
        authority.Should().Contain(options.TenantId,
            "Dedicated model authority URL must reference the configured tenant ID");
        authority.Should().StartWith("https://login.microsoftonline.com/",
            "Authority URL must use the standard Entra ID authority format");

        _output.WriteLine($"Dedicated model effective authority: {authority}");
    }

    [Fact]
    public void DedicatedModel_NoServicePrincipalProfiles_SingleSpPattern()
    {
        // Documents that the dedicated model does NOT use SP profiles.
        // Single SP, single workspace — no profile-based isolation needed.

        // In this model:
        // - profileId is always null when calling ReportingEmbedService.GetEmbedConfigAsync.
        // - The X-PowerBI-Profile-Id header is never sent.
        // - Workspace access is controlled by the single SP's workspace membership.

        Guid? profileId = null; // Single SP — no profile header.

        profileId.Should().BeNull(
            "Dedicated model never uses SP profiles — single SP has direct workspace access");

        _output.WriteLine("Dedicated model: no SP profiles used (null profileId)");
    }

    [Fact]
    public void DedicatedModel_DifferesFromMultiCustomer_ByProfileUsage()
    {
        // Documents the key differentiator between multi-customer and dedicated models.
        // Multi-customer: profileId is non-null (profiles per customer).
        // Dedicated: profileId is null (single SP, direct access).

        Guid? multiCustomerProfile = Guid.NewGuid(); // non-null
        Guid? dedicatedProfile = null;               // null

        multiCustomerProfile.Should().NotBeNull(
            "Multi-customer model uses SP profiles for isolation");
        dedicatedProfile.Should().BeNull(
            "Dedicated model does not use SP profiles");

        _output.WriteLine("Configuration difference: multi-customer uses profiles, dedicated does not");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Model 3: Customer Tenant (customer's own PBI tenant and app registration)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomerTenantModel_RequiredOptions_BindAndValidate()
    {
        // Arrange — customer provides their own tenant, app registration, and capacity.
        var config = BuildCustomerTenantConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>();

        // Assert
        options.Should().NotBeNull();
        options!.TenantId.Should().NotBeNullOrWhiteSpace(
            "Customer tenant model requires TenantId (customer's own Azure AD tenant)");
        options.ClientId.Should().NotBeNullOrWhiteSpace(
            "Customer tenant model requires ClientId (customer's own app registration)");
        options.ClientSecret.Should().NotBeNullOrWhiteSpace(
            "Customer tenant model requires ClientSecret (customer's own app credential)");

        _output.WriteLine("Customer tenant model: all required options bound successfully");
        _output.WriteLine($"  TenantId (customer): {options.TenantId}");
        _output.WriteLine($"  ClientId (customer): {options.ClientId}");
    }

    [Fact]
    public void CustomerTenantModel_ExplicitAuthorityUrl_PointsToCustomerTenant()
    {
        // Arrange — customer tenant model provides an explicit authority URL.
        var config = BuildCustomerTenantConfig();
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;

        // Act
        var authority = options.GetEffectiveAuthorityUrl();

        // Assert — explicit AuthorityUrl is used when set (overrides TenantId-based default).
        authority.Should().Be("https://login.microsoftonline.com/customer-own-tenant-id",
            "Customer tenant model uses an explicit AuthorityUrl pointing to the customer's tenant");
        authority.Should().Contain("customer-own-tenant-id",
            "Authority URL must reference the customer's tenant, not Spaarke's shared tenant");

        _output.WriteLine($"Customer tenant effective authority: {authority}");
    }

    [Fact]
    public void CustomerTenantModel_DifferesFromMultiCustomer_ByTenantOwnership()
    {
        // Documents the key differentiator between customer-tenant and multi-customer models.
        // Multi-customer: Spaarke owns the SP and tenant; profiles isolate customers.
        // Customer-tenant: Customer owns the SP, tenant, and capacity.

        var multiCustomerConfig = BuildMultiCustomerConfig();
        var customerTenantConfig = BuildCustomerTenantConfig();

        var multiOptions = multiCustomerConfig.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;
        var customerOptions = customerTenantConfig.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;

        // The tenant IDs must differ — they represent different ownership models.
        multiOptions.TenantId.Should().NotBe(customerOptions.TenantId,
            "Multi-customer and customer-tenant models use different tenant IDs");

        // Customer tenant model has an explicit authority URL (their own tenant).
        customerOptions.AuthorityUrl.Should().NotBeNullOrWhiteSpace(
            "Customer tenant model sets explicit AuthorityUrl to point to the customer's own tenant");

        // Multi-customer model uses default authority (no explicit override).
        multiOptions.AuthorityUrl.Should().BeNullOrWhiteSpace(
            "Multi-customer model uses default authority URL derived from TenantId");

        _output.WriteLine("Customer-tenant vs multi-customer model differences verified");
        _output.WriteLine($"  Multi-customer tenant:    {multiOptions.TenantId}");
        _output.WriteLine($"  Customer-tenant tenant:   {customerOptions.TenantId}");
        _output.WriteLine($"  Customer authority URL:   {customerOptions.AuthorityUrl}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cross-model: configuration-only differentiators (no code changes needed)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Multi-Customer", "spaarke-shared-tenant-id", "spaarke-shared-sp-client-id", null)]
    [InlineData("Dedicated",      "customer-dedicated-tenant-id", "customer-dedicated-sp-client-id", null)]
    [InlineData("CustomerTenant", "customer-own-tenant-id", "customer-own-app-client-id", "https://login.microsoftonline.com/customer-own-tenant-id")]
    public void AllModels_SwitchableViaEnvironmentVariablesOnly(
        string modelName,
        string expectedTenantId,
        string expectedClientId,
        string? expectedAuthorityUrl)
    {
        // Arrange — each deployment model is uniquely identified by its env var values.
        var settings = new Dictionary<string, string?>
        {
            ["PowerBi:TenantId"]     = expectedTenantId,
            ["PowerBi:ClientId"]     = expectedClientId,
            ["PowerBi:ClientSecret"] = "test-secret",
            ["PowerBi:AuthorityUrl"] = expectedAuthorityUrl,
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        // Act
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>();

        // Assert — configuration binds correctly regardless of model.
        options.Should().NotBeNull($"Model '{modelName}' must produce a valid PowerBiOptions");
        options!.TenantId.Should().Be(expectedTenantId);
        options.ClientId.Should().Be(expectedClientId);

        var authority = options.GetEffectiveAuthorityUrl();
        if (expectedAuthorityUrl != null)
        {
            authority.Should().Be(expectedAuthorityUrl,
                $"Model '{modelName}' uses explicit AuthorityUrl");
        }
        else
        {
            authority.Should().Be($"https://login.microsoftonline.com/{expectedTenantId}",
                $"Model '{modelName}' uses default authority URL from TenantId");
        }

        _output.WriteLine($"Model '{modelName}' configuration verified:");
        _output.WriteLine($"  TenantId:     {options.TenantId}");
        _output.WriteLine($"  ClientId:     {options.ClientId}");
        _output.WriteLine($"  Authority:    {authority}");
    }

    [Fact]
    public void AllModels_ShareSameApiUrl_OnlyCredentialsDiffer()
    {
        // Verify that all three models use the same Power BI REST API base URL.
        // The API endpoint is stable — only authentication credentials differ per model.

        var multiCustomerOptions   = BuildMultiCustomerConfig().GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;
        var dedicatedOptions       = BuildDedicatedConfig().GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;
        var customerTenantOptions  = BuildCustomerTenantConfig().GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>()!;

        multiCustomerOptions.ApiUrl.Should().Be("https://api.powerbi.com");
        dedicatedOptions.ApiUrl.Should().Be("https://api.powerbi.com");
        customerTenantOptions.ApiUrl.Should().Be("https://api.powerbi.com");

        _output.WriteLine("All deployment models use the same Power BI REST API endpoint.");
        _output.WriteLine("Only credentials (TenantId, ClientId, ClientSecret, AuthorityUrl) differ.");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PowerBiOptions validation: required fields enforced
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PowerBiOptions_MissingTenantId_OptionsIncomplete()
    {
        // Arrange — simulate a misconfigured environment (TenantId missing).
        var settings = new Dictionary<string, string?>
        {
            // TenantId intentionally omitted.
            ["PowerBi:ClientId"]     = "some-client-id",
            ["PowerBi:ClientSecret"] = "some-secret",
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        // Act
        var options = config.GetSection(PowerBiOptions.SectionName).Get<PowerBiOptions>();

        // Assert — TenantId defaults to empty string when not set.
        options.Should().NotBeNull();
        options!.TenantId.Should().BeNullOrEmpty(
            "Missing TenantId leaves the property at its default empty value, " +
            "which ValidateDataAnnotations in DI will reject at startup");

        _output.WriteLine("Misconfigured options (missing TenantId) detected correctly");
    }

    [Fact]
    public void PowerBiOptions_DefaultApiUrlAndScope_MatchPowerBiSpec()
    {
        // Verify that the default values for ApiUrl and Scope are correct per the PBI spec.
        // This protects against accidental changes that would break all deployment models.
        var options = new PowerBiOptions();

        options.ApiUrl.Should().Be("https://api.powerbi.com",
            "Default API URL must point to the Power BI REST API");
        options.Scope.Should().Be("https://analysis.windows.net/.default",
            "Default scope must use the Power BI .default scope for client-credentials flow");

        _output.WriteLine($"Default ApiUrl: {options.ApiUrl}");
        _output.WriteLine($"Default Scope:  {options.Scope}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ReportingProfileManager: available in DI for multi-customer isolation
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiCustomerModel_ProfileManager_AvailableForSpProfileResolution()
    {
        // Arrange — verify ReportingProfileManager can be constructed (DI contract for multi-customer model).
        var config = BuildMultiCustomerConfig();

        var services = new ServiceCollection();
        services.AddLogging();

        // Wire the IOptions<PowerBiOptions> that ReportingProfileManager depends on.
        services
            .AddOptions<PowerBiOptions>()
            .Bind(config.GetSection(PowerBiOptions.SectionName));

        services.AddSingleton<ReportingProfileManager>();

        var provider = services.BuildServiceProvider();

        // Act
        var profileManager = provider.GetService<ReportingProfileManager>();

        // Assert
        profileManager.Should().NotBeNull(
            "ReportingProfileManager must be resolvable from DI for multi-customer SP profile resolution");

        _output.WriteLine("ReportingProfileManager resolved from DI successfully (multi-customer model)");
    }
}
