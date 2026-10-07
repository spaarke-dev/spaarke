// -----------------------------------------------------------------------------
// WorkerDataverseCredentialFactoryTests.cs
//
// Unit tests over the A44.5 FR-39 ordered credential seam (task 205i,
// 2026-08-25 — the H7/task-142 half of A30's sentinel contract).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. Create() performs NO network I/O (token
//   acquisition happens later at GetTokenAsync, never invoked here), so the
//   REAL factory is exercised over an in-memory IConfiguration — no fakes,
//   no Mock<HttpMessageHandler>.
//
// COVERAGE (POML goal (a)-(e) at the factory boundary):
//   A1  Secret-free chain (MI-FIC only + RequireSecretFreeIdentity) + empty
//       secret → ManagedIdentityFederated selected; ClientAssertionCredential.
//   A2  Unconfigured (legacy) chain + secret present → ClientSecret selected;
//       ClientSecretCredential (pre-migration env fallback preserved).
//   A3  Transitional chain [MIF, ClientSecret] + secret present → MI-FIC
//       still wins (secret never used on the secret-free branch).
//   A4  Unconfigured (legacy) chain + EMPTY secret → fail-closed throw whose
//       message forbids the sentinel workaround (§9.1).
//   A5  Unknown kind in order → fail-fast at selection too (not only at
//       options Validate).
//   A6  RequireSecretFreeIdentity + ClientSecret listed → fail-fast.
//   A7  Model 1 (shared Worker + shared UAMI): ONE env-level chain — the same
//       factory pins the SAME shared UAMI clientId for two different customer
//       tenants (per-tenant request context is the isolation wall, I6).
//   A8  Model 2 (per-customer Worker + per-stamp UAMI): two Workers (two
//       factories over their own stamp config) each pin their OWN UAMI —
//       byte-inequality (SF-2 plumbing-chain discipline).
//   A9  UAMI clientId lookup: canonical ManagedIdentity:ClientId first, then
//       Graph:ManagedIdentity:ClientId (mirror of DataverseServiceClientImpl).
//   A10 No UAMI configured → system-assigned (ManagedIdentityClientId null).
// -----------------------------------------------------------------------------

using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.Credentials;

public sealed class WorkerDataverseCredentialFactoryTests
{
    private const string SectionName = "EnvVarValues";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string CustomerTenantB = "99999999-8888-7777-6666-555555555555";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string SharedUamiClientId = "11111111-aaaa-bbbb-cccc-222222222222";
    private const string StampAUamiClientId = "33333333-aaaa-bbbb-cccc-444444444444";
    private const string StampBUamiClientId = "55555555-aaaa-bbbb-cccc-666666666666";
    private const string Secret = "test-transitional-secret-placeholder";

    // ---------- A1 secret-free chain ----------

    [Fact]
    public void SecretFreeChain_EmptySecret_SelectsManagedIdentityFederated()
    {
        var factory = BuildFactory(("ManagedIdentity:ClientId", SharedUamiClientId));

        var selected = factory.Create(SecretFreeChain(), SectionName, TenantId, BffAppRegId, clientSecret: null);

        selected.Kind.Should().Be(CredentialKind.ManagedIdentityFederated);
        selected.ManagedIdentityClientId.Should().Be(SharedUamiClientId);
        selected.Credential.Should().BeOfType<ClientAssertionCredential>(
            "MI-FIC is a client assertion minted by the UAMI (audience api://AzureADTokenExchange) that " +
            "the BFF app-reg trusts via the H3-created FIC");
    }

    // ---------- A2 legacy default ----------

    [Fact]
    public void UnconfiguredChain_SecretPresent_SelectsClientSecret_LegacyBehaviorPreserved()
    {
        var factory = BuildFactory();

        var selected = factory.Create(new WorkerCredentialSelectionOptions(), SectionName, TenantId, BffAppRegId, Secret);

        selected.Kind.Should().Be(CredentialKind.ClientSecret);
        selected.ManagedIdentityClientId.Should().BeNull();
        selected.Credential.Should().BeOfType<ClientSecretCredential>(
            "prong-3 unmigrated environments (task 142 / 204a shape) keep the transitional secret path unchanged");
    }

    // ---------- A3 transitional chain ----------

    [Fact]
    public void TransitionalChain_MifFirst_SecretPresent_StillSelectsMif()
    {
        var factory = BuildFactory(("ManagedIdentity:ClientId", SharedUamiClientId));
        var chain = new WorkerCredentialSelectionOptions
        {
            Order =
            {
                nameof(CredentialKind.ManagedIdentityFederated),
                nameof(CredentialKind.ClientSecret),
            },
        };

        var selected = factory.Create(chain, SectionName, TenantId, BffAppRegId, Secret);

        selected.Kind.Should().Be(CredentialKind.ManagedIdentityFederated,
            "the ordered selection is most-preferred-first — a present transitional secret must never " +
            "shadow the secret-free credential (ADR-028 A4)");
    }

    // ---------- A4 fail-closed, no sentinel ----------

    [Fact]
    public void UnconfiguredChain_EmptySecret_FailsClosed_AndForbidsSentinel()
    {
        var factory = BuildFactory();

        var act = () => factory.Create(new WorkerCredentialSelectionOptions(), SectionName, TenantId, BffAppRegId, "");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No credential could be selected*")
            .WithMessage("*NEVER unblock by writing a placeholder*",
                "the exhausted-chain message must steer the operator AWAY from the §9.1 sentinel " +
                "pathology (AADSTS7000215) — empty is the signal");
    }

    // ---------- A5 invalid chain at selection ----------

    [Fact]
    public void UnknownKindInOrder_ThrowsAtSelectionToo()
    {
        var factory = BuildFactory();
        var chain = new WorkerCredentialSelectionOptions { Order = { "KeyVaultCertificate" } };

        var act = () => factory.Create(chain, SectionName, TenantId, BffAppRegId, Secret);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a known credential kind*");
    }

    // ---------- A6 RequireSecretFreeIdentity contradiction ----------

    [Fact]
    public void RequireSecretFree_WithClientSecretListed_ThrowsAtSelectionToo()
    {
        var factory = BuildFactory();
        var chain = new WorkerCredentialSelectionOptions
        {
            Order = { nameof(CredentialKind.ClientSecret) },
            RequireSecretFreeIdentity = true,
        };

        var act = () => factory.Create(chain, SectionName, TenantId, BffAppRegId, Secret);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequireSecretFreeIdentity*");
    }

    // ---------- A7 Model 1: shared Worker + shared UAMI, one chain per env ----------

    [Fact]
    public void Model1_SharedWorker_OneChainPerEnvironment_PinsSharedUamiForEveryCustomerTenant()
    {
        // Model 1 (design.md D2/D3 + task 029 slot-swap fix): ONE shared L2
        // Worker per environment, ONE shared UAMI, ONE credential-chain
        // config. Per-customer isolation is per-tenant REQUEST context (I6),
        // not per-customer credential config.
        var factory = BuildFactory(("ManagedIdentity:ClientId", SharedUamiClientId));
        var chain = SecretFreeChain();

        var customerA = factory.Create(chain, SectionName, TenantId, BffAppRegId, null);
        var customerB = factory.Create(chain, SectionName, CustomerTenantB, BffAppRegId, null);

        customerA.Kind.Should().Be(CredentialKind.ManagedIdentityFederated);
        customerB.Kind.Should().Be(CredentialKind.ManagedIdentityFederated);
        customerA.ManagedIdentityClientId.Should().Be(SharedUamiClientId);
        customerB.ManagedIdentityClientId.Should().Be(SharedUamiClientId,
            "Model 1 has exactly ONE shared UAMI — both customer tenants' assertions are minted by it; " +
            "the tenant authority (per-request tenantId) is the isolation wall");
    }

    // ---------- A8 Model 2: per-customer Worker + per-stamp UAMI ----------

    [Fact]
    public void Model2_PerCustomerWorkers_EachPinTheirOwnStampUami()
    {
        // Model 2 (Wave G-1 task 101): each customer stamp deploys its OWN
        // Worker (modules/controlplane-worker-app-service.bicep per stamp)
        // with its OWN ManagedIdentity__ClientId app setting → per-customer
        // credential chain. SF-2 discipline: assert byte-level pinning, not
        // just non-null.
        var stampAWorker = BuildFactory(("ManagedIdentity:ClientId", StampAUamiClientId));
        var stampBWorker = BuildFactory(("ManagedIdentity:ClientId", StampBUamiClientId));
        var chain = SecretFreeChain();

        var stampA = stampAWorker.Create(chain, SectionName, TenantId, BffAppRegId, null);
        var stampB = stampBWorker.Create(chain, SectionName, TenantId, BffAppRegId, null);

        stampA.ManagedIdentityClientId.Should().Be(StampAUamiClientId);
        stampB.ManagedIdentityClientId.Should().Be(StampBUamiClientId);
        stampA.ManagedIdentityClientId.Should().NotBe(stampB.ManagedIdentityClientId,
            "per-stamp UAMIs are distinct identities — a swapped mapping creates successfully and fails " +
            "only at exchange (AADSTS700213), which is why the pinning is asserted byte-for-byte");
    }

    // ---------- A9 lookup precedence ----------

    [Fact]
    public void UamiClientIdLookup_CanonicalKeyFirst_ThenGraphFallback()
    {
        var canonicalWins = BuildFactory(
            ("ManagedIdentity:ClientId", StampAUamiClientId),
            ("Graph:ManagedIdentity:ClientId", StampBUamiClientId));
        var fallbackOnly = BuildFactory(("Graph:ManagedIdentity:ClientId", StampBUamiClientId));

        canonicalWins.ResolveManagedIdentityClientId().Should().Be(StampAUamiClientId);
        fallbackOnly.ResolveManagedIdentityClientId().Should().Be(StampBUamiClientId,
            "mirror of DataverseServiceClientImpl's ManagedIdentity:ClientId ?? Graph:ManagedIdentity:ClientId lookup");
    }

    // ---------- A10 system-assigned ----------

    [Fact]
    public void NoUamiConfigured_SelectsSystemAssigned()
    {
        var factory = BuildFactory();

        var selected = factory.Create(SecretFreeChain(), SectionName, TenantId, BffAppRegId, null);

        selected.Kind.Should().Be(CredentialKind.ManagedIdentityFederated);
        selected.ManagedIdentityClientId.Should().BeNull("null pins nothing — system-assigned, mirror of master's '(system-assigned)' convention");
    }

    // ---------- helpers ----------

    private static WorkerCredentialSelectionOptions SecretFreeChain() => new()
    {
        Order = { nameof(CredentialKind.ManagedIdentityFederated) },
        RequireSecretFreeIdentity = true,
    };

    private static WorkerDataverseCredentialFactory BuildFactory(
        params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new WorkerDataverseCredentialFactory(
            configuration, NullLogger<WorkerDataverseCredentialFactory>.Instance);
    }
}
