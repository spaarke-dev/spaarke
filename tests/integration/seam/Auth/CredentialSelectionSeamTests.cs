using System;
using System.Collections.Generic;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-A1 (auth-v4 task 010) — credential-selection seam for the Dataverse app-only paths.
///
/// The defect: <c>DataverseAccessDataSource</c> and <c>DataverseWebApiClient</c> selected their
/// credential from *secret presence* rather than from <c>Graph:ManagedIdentity:Enabled</c>. On dev the
/// secret IS present (OBO needs it), so both ran on the client secret even with MI enabled.
///
/// <para><b>Scope of what is asserted here, and why.</b> These tests assert only what the constructor
/// exposes as observable behaviour: which configuration is *required* under each flag state, and that a
/// missing setting fails fast with an actionable message rather than yielding an unusable credential.
/// </para>
///
/// <para><b>What is deliberately NOT asserted here.</b> The concrete credential type selected, and the
/// presence of the OBO confidential client, live in private fields. Reading them would require
/// reflection, which is <b>banned by ADR-038 ban B8</b> (tests/CLAUDE.md — "internal/private method
/// tests via InternalsVisibleTo or reflection"). A behavioural alternative is not available either:
/// <c>DataverseAccessDataSource</c> is deliberately fail-closed and swallows credential errors into
/// <c>AccessRights.None</c>, so the selection is not observable through its public surface.
/// The structural guard for the decoupling therefore belongs in <c>tests/Spaarke.ArchTests/</c> as
/// source analysis — the shape ADR-038 sanctions for exactly this, and the shape task 060 already
/// builds. See notes/decisions/010-credential-gating.md.</para>
/// </summary>
[Collection(DataverseCredentialSeamCollection.Name)]
public class CredentialSelectionSeamTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string AppId = "22222222-2222-2222-2222-222222222222";
    private const string Secret = "test-secret-value";
    private const string DataverseUrl = "https://example.crm.dynamics.com";

    private static IConfiguration Config(bool? miEnabled, bool withSecret)
    {
        var values = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = DataverseUrl,
            ["TENANT_ID"] = Tenant,
            ["API_APP_ID"] = AppId,
        };
        if (miEnabled.HasValue) values["Graph:ManagedIdentity:Enabled"] = miEnabled.Value ? "true" : "false";
        if (withSecret) values["API_CLIENT_SECRET"] = Secret;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static DataverseAccessDataSource CreateAccessDataSource(
        bool? miEnabled, bool withSecret, bool withProvider = true)
        => new(
            Mock.Of<IDataverseService>(),
            new HttpClient(),
            Config(miEnabled, withSecret),
            NullLogger<DataverseAccessDataSource>.Instance,
            credential: null,
            confidentialClients: withProvider ? StubProvider.Instance : null);

    /// <summary>
    /// Stands in for the BFF's <c>OrderedCredentialClientProvider</c>. Task 022 moved credential
    /// CONSTRUCTION out of these constructors, so what they now require in the non-managed-identity
    /// branch is a provider, not a secret. This stub never has to hand back a client: every assertion
    /// in this file is about what the constructor demands, and nothing here acquires a token.
    /// </summary>
    private sealed class StubProvider : IConfidentialClientProvider
    {
        public static readonly StubProvider Instance = new();

        public Task<Microsoft.Identity.Client.IConfidentialClientApplication> GetClientAsync(
            string tenantId, string clientId, CancellationToken ct = default)
            => throw new NotSupportedException("Credential selection is exercised by CredentialOrderingSeamTests.");
    }

    // ---------------------------------------------------------------------------------------------
    // DataverseAccessDataSource
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AccessDataSource_FlagTrue_WithoutSecret_ConstructsSuccessfully()
    {
        // THE DEFECT, stated as behaviour: with the flag on, app-only auth must NOT require a client
        // secret. Before the fix, secret-absence pushed this into the managed-identity branch by
        // accident rather than by decision — and secret-*presence* silently defeated the flag.
        var act = () => CreateAccessDataSource(miEnabled: true, withSecret: false);
        act.Should().NotThrow("with managed identity enabled, no client secret is required for app-only auth");
    }

    [Fact]
    public void AccessDataSource_FlagTrue_WithSecret_ConstructsSuccessfully()
    {
        // The dev configuration: the secret IS present because OBO needs it. Construction must
        // succeed and must not be diverted onto the secret path by its mere presence.
        var act = () => CreateAccessDataSource(miEnabled: true, withSecret: true);
        act.Should().NotThrow();
    }

    [Fact]
    public void AccessDataSource_FlagFalse_WithSecret_ConstructsSuccessfully()
    {
        var act = () => CreateAccessDataSource(miEnabled: false, withSecret: true);
        act.Should().NotThrow("with the flag off, a fully-configured client secret is the supported path");
    }

    [Fact]
    public void AccessDataSource_FlagFalse_WithoutACredentialProvider_ThrowsNamingWhatIsMissing()
    {
        // Negative case: no usable credential must fail fast and actionably at construction, rather
        // than handing back something unusable that fails later at first token request.
        //
        // AMENDED at task 022, and the amendment is the point of the task. This used to demand
        // "*API_CLIENT_SECRET*", because the non-managed-identity branch built a ClientSecretCredential
        // inline and a secret was therefore a construction-time requirement. It no longer is: WHICH
        // credential proves this identity is the provider's ordered decision, and whether ANY credential
        // is obtainable is asserted once at startup by IdentityConfigurationValidator rule 4 rather than
        // re-derived in every consumer. What the constructor still owes the operator is an actionable
        // failure when its WIRING is absent — which is what this now asserts.
        var act = () => CreateAccessDataSource(miEnabled: false, withSecret: true, withProvider: false);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*IConfidentialClientProvider*",
               "the failure must name what is missing, not surface as a null/opaque credential later");
    }

    [Fact]
    public void AccessDataSource_FlagFalse_WithoutSecret_ButWithAProvider_ConstructsSuccessfully()
    {
        // The FR-B5 half, and the reason the assertion above changed rather than being deleted: a
        // secret-free deployment must be constructible. Before task 022 this threw.
        var act = () => CreateAccessDataSource(miEnabled: false, withSecret: false);
        act.Should().NotThrow("the client secret is one of three credentials, and no longer a precondition");
    }

    [Fact]
    public void AccessDataSource_FlagAbsent_TakesTheProviderPath_DocumentingTheDefault()
    {
        // No flag configured at all => not "true" => the non-managed-identity branch. Asserted so the
        // default is a decision on the record rather than something a reader has to infer.
        var act = () => CreateAccessDataSource(miEnabled: null, withSecret: false, withProvider: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*IConfidentialClientProvider*");
    }

    // ---------------------------------------------------------------------------------------------
    // DataverseWebApiClient — same flag, and this class has no OBO path at all
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void WebApiClient_FlagTrue_WithoutSecret_ConstructsSuccessfully()
    {
        var act = () => new DataverseWebApiClient(Config(miEnabled: true, withSecret: false),
                                                  NullLogger<DataverseWebApiClient>.Instance);
        act.Should().NotThrow();
    }

    [Fact]
    public void WebApiClient_FlagTrue_WithSecret_ConstructsSuccessfully()
    {
        var act = () => new DataverseWebApiClient(Config(miEnabled: true, withSecret: true),
                                                  NullLogger<DataverseWebApiClient>.Instance);
        act.Should().NotThrow();
    }

    [Fact]
    public void WebApiClient_FlagFalse_WithoutACredentialProvider_ThrowsNamingWhatIsMissing()
    {
        // Same amendment as AccessDataSource above — see its remarks for why the demanded setting
        // changed from the secret to the provider.
        var act = () => new DataverseWebApiClient(Config(miEnabled: false, withSecret: true),
                                                  NullLogger<DataverseWebApiClient>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*IConfidentialClientProvider*");
    }

    [Fact]
    public void WebApiClient_FlagFalse_WithoutSecret_ButWithAProvider_ConstructsSuccessfully()
    {
        var act = () => new DataverseWebApiClient(Config(miEnabled: false, withSecret: false),
                                                  NullLogger<DataverseWebApiClient>.Instance,
                                                  credential: null,
                                                  confidentialClients: StubProvider.Instance);

        act.Should().NotThrow("a secret-free deployment must be constructible (FR-B5)");
    }
}
