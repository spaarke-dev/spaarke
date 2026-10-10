// -----------------------------------------------------------------------------
// WorkerFicTrustTests.cs
//
// ISS-015 (GitHub #1524): H6 / H7 / H7b sign in to the customer's Dataverse AS the per-customer BFF registration (D-13),
// secret-free, with an assertion from the L2 Worker's own UAMI (WorkerDataverseCredentialFactory). H3 used to give that
// registration ONE federated credential (subject = the stamp BFF UAMI), so nothing trusted the Worker UAMI and every
// new stamp failed at H6's first token request with AADSTS700213. H3 now keeps a second credential,
// `spaarke-l2-worker`, subject = ControlPlaneIdentityOptions.PrincipalObjectId (the Worker UAMI's OBJECT id).
//
// ADR-038 CATEGORY: Path #1 — pure C# unit tests, no live Graph (the provisioner's Graph bodies follow the project's
// no-live-Graph test precedent; the decisions are the pure BuildRequiredFicSpecs / PlanFederatedCredentials, which the
// write path AND the re-GET verification both run). A42FicReconciliationTests stays the single-triple baseline.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class WorkerFicTrustTests
{
    private const string SpaarkeTenantId = "11111111-1111-1111-1111-111111111111";
    private const string CustomerTenantId = "22222222-2222-2222-2222-222222222222";
    private const string StampUamiPrincipalId = "ffffffff-1111-2222-3333-000000000000";
    private const string WorkerPrincipalId = "abababab-1234-4321-8888-000000000001";
    private const string WorkerClientId = "cdcdcdcd-5678-8765-9999-000000000002";
    private const string Audience = "api://AzureADTokenExchange";
    private const string StampFicName = "spaarke-uami-trust";
    private const string WorkerFicName = "spaarke-l2-worker";
    private const string Model1Profile = "model1-dedicated";

    private static readonly string Issuer = $"https://login.microsoftonline.com/{SpaarkeTenantId}/v2.0";

    // =====================================================================
    // BuildRequiredFicSpecs — what H3 keeps
    // =====================================================================

    [Fact]
    public void SpaarkeTenantProfile_KeepsTheStampAndTheWorkerCredential()
    {
        var plan = Specs();

        plan.Error.Should().BeNull();
        plan.Specs.Should().HaveCount(2);
        plan.Names.Should().Equal(StampFicName, WorkerFicName);

        var stamp = plan.Specs[0];
        stamp.Subject.Should().Be(StampUamiPrincipalId);
        stamp.Issuer.Should().Be(Issuer);
        stamp.Audience.Should().Be(Audience);

        var worker = plan.Specs[1];
        worker.Name.Should().Be(WorkerFicName);
        worker.Issuer.Should().Be(Issuer, "the Worker UAMI lives in Spaarke's tenant — MI-FIC is same-tenant only");
        worker.Audience.Should().Be(Audience);
    }

    [Fact]
    public void WorkerCredentialSubject_IsTheWorkerPrincipalId_NeverItsClientId()
    {
        var worker = Specs().Specs.Single(s => s.Name == WorkerFicName);

        worker.Subject.Should().Be(WorkerPrincipalId,
            "an MI assertion's subject is the UAMI's object id (principalId)");
        worker.Subject.Should().NotBe(WorkerClientId,
            "a clientId subject creates cleanly and dies at exchange with AADSTS700213 (auth-v4 §11 invariant 1)");
    }

    [Fact]
    public void WorkerPrincipalId_IsCanonicalised_SoTheTripleMatchIsStable()
    {
        var worker = Specs(workerPrincipal: $"  {WorkerPrincipalId.ToUpperInvariant()} ").Specs.Single(s => s.Name == WorkerFicName);

        worker.Subject.Should().Be(WorkerPrincipalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingWorkerPrincipal_IsRefused_BeforeAnythingIsWritten(string? workerPrincipal)
    {
        var plan = Specs(workerPrincipal: workerPrincipal);

        plan.Specs.Should().BeEmpty("nothing may be planned — the provisioner returns before its first Graph call");
        plan.Error.Should().Contain(EntraAppRegRejectionCodes.WorkerFicIdentityMissing)
            .And.Contain("ControlPlaneIdentity__PrincipalObjectId");
    }

    [Fact]
    public void WorkerPrincipalEqualToTheStampUami_IsRefused()
    {
        var plan = Specs(workerPrincipal: StampUamiPrincipalId.ToUpperInvariant());

        plan.Specs.Should().BeEmpty();
        plan.Error.Should().Contain(EntraAppRegRejectionCodes.WorkerFicIdentityMissing);
    }

    [Theory]
    [InlineData(StampFicName)]
    [InlineData("")]
    public void WorkerFicName_BlankOrEqualToTheStampName_IsRefused(string workerFicName)
    {
        var plan = Specs(options: Options(workerFicName: workerFicName));

        plan.Specs.Should().BeEmpty();
        plan.Error.Should().Contain(EntraAppRegRejectionCodes.WorkerFicIdentityMissing);
    }

    [Fact]
    public void CustomerOwnedModel2_KeepsOnlyTheStampCredential_AndNeedsNoWorkerPrincipal()
    {
        // The registration lives in the customer's tenant; the Worker UAMI in Spaarke's — MI-FIC cannot cross tenants
        // (Model 2 out of scope, owner 2026-09-30).
        var plan = GraphAppRegistrationProvisioner.BuildRequiredFicSpecs(
            "customer-owned-model2", CustomerTenantId, SpaarkeTenantId, StampUamiPrincipalId, null, Options());

        plan.Error.Should().BeNull();
        plan.Names.Should().Equal(StampFicName);
        plan.Specs[0].Issuer.Should().Be($"https://login.microsoftonline.com/{CustomerTenantId}/v2.0");
    }

    [Fact]
    public void BlankSpaarkeTenant_IsStillAConfigFault_ReportedBeforeAnyWrite()
    {
        var plan = GraphAppRegistrationProvisioner.BuildRequiredFicSpecs(
            Model1Profile, SpaarkeTenantId, null, StampUamiPrincipalId, WorkerPrincipalId, Options());

        plan.Specs.Should().BeEmpty();
        plan.Error.Should().Contain(EntraAppRegRejectionCodes.FicCreationFailed).And.Contain("SpaarkeTenantId");
    }

    [Fact]
    public void CrossTenantRun_IsRefused_BeforeTheWorkerCredentialIsPlanned()
    {
        var act = () => GraphAppRegistrationProvisioner.BuildRequiredFicSpecs(
            Model1Profile, CustomerTenantId, SpaarkeTenantId, StampUamiPrincipalId, WorkerPrincipalId, Options());

        act.Should().Throw<CrossTenantFicRefusedException>();
    }

    // =====================================================================
    // PlanFederatedCredentials — create, idempotent re-run, repair
    // =====================================================================

    [Fact]
    public void NewRegistration_CreatesBoth()
    {
        var plan = Reconcile();

        plan.Deletes.Should().BeEmpty();
        plan.Creates.Select(s => s.Name).Should().Equal(StampFicName, WorkerFicName);
    }

    [Fact]
    public void IdempotentReRun_BothInPlace_WritesNothing()
    {
        var plan = Reconcile(
            Fic(StampFicName, Issuer, StampUamiPrincipalId),
            Fic(WorkerFicName, Issuer, WorkerPrincipalId));

        plan.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public void StampFromBeforeTheFix_GetsOnlyTheWorkerCredential()
    {
        var stamp = Fic(StampFicName, Issuer, StampUamiPrincipalId);

        var plan = Reconcile(stamp);

        plan.Deletes.Should().BeEmpty("the stamp's own trust is untouched");
        plan.Creates.Select(s => s.Name).Should().Equal(WorkerFicName);
    }

    [Fact]
    public void WorkerNameHeldByTheClientIdSubject_IsReplacedWithThePrincipalId()
    {
        var wrongSubject = Fic(WorkerFicName, Issuer, WorkerClientId, id: "wrong-subject");

        var plan = Reconcile(Fic(StampFicName, Issuer, StampUamiPrincipalId), wrongSubject);

        plan.Deletes.Should().ContainSingle().Which.Should().BeSameAs(wrongSubject,
            "subject/issuer cannot be PATCHed — drift under our name is deleted and re-created (A42 parity §4)");
        plan.Creates.Should().ContainSingle().Which.Subject.Should().Be(WorkerPrincipalId);
    }

    [Fact]
    public void WorkerNameHeldByAnotherTriple_IsRepaired_StampLeftAlone()
    {
        var stamp = Fic(StampFicName, Issuer, StampUamiPrincipalId);
        var squatter = Fic(WorkerFicName, $"https://login.microsoftonline.com/{CustomerTenantId}/v2.0", WorkerPrincipalId);

        var plan = Reconcile(stamp, squatter);

        plan.Deletes.Should().ContainSingle().Which.Should().BeSameAs(squatter);
        plan.Deletes.Should().NotContain(stamp);
        plan.Creates.Select(s => s.Name).Should().Equal(WorkerFicName);
    }

    [Fact]
    public void StampTripleUnderTheWorkerName_IsMoved_NotLeftToSatisfyTheStamp()
    {
        // Deleting the worker-named credential to free its name must not silently drop the stamp's trust: the stamp
        // triple is recreated under its own name in the same plan (deletes run first — (issuer, subject) is unique).
        var misnamed = Fic(WorkerFicName, Issuer, StampUamiPrincipalId);

        var plan = Reconcile(misnamed);

        plan.Deletes.Should().ContainSingle().Which.Should().BeSameAs(misnamed);
        plan.Creates.Select(s => s.Name).Should().Equal(StampFicName, WorkerFicName);
    }

    [Fact]
    public void SwappedNames_AreBothRepaired()
    {
        var a = Fic(StampFicName, Issuer, WorkerPrincipalId, id: "a");
        var b = Fic(WorkerFicName, Issuer, StampUamiPrincipalId, id: "b");

        var plan = Reconcile(a, b);

        plan.Deletes.Should().HaveCount(2).And.Contain(a).And.Contain(b);
        plan.Creates.Select(s => s.Name).Should().Equal(StampFicName, WorkerFicName);
    }

    [Fact]
    public void EquivalentTripleUnderALegacyName_StillSatisfies_SF7()
    {
        var plan = Reconcile(
            Fic("legacy-label", Issuer, StampUamiPrincipalId),
            Fic(WorkerFicName, Issuer, WorkerPrincipalId));

        plan.IsSatisfied.Should().BeTrue("the FIC name is a label — Entra matches assertions against the triple");
    }

    [Fact]
    public void AnUnrelatedCredential_IsNeverDeleted()
    {
        // Adoption refuses a registration carrying a foreign credential; the planner itself never removes one.
        var unrelated = Fic("someone-elses", Issuer, "99999999-0000-0000-0000-999999999999");

        var plan = Reconcile(unrelated);

        plan.Deletes.Should().BeEmpty();
        plan.Creates.Should().HaveCount(2);
    }

    [Fact]
    public void WorkerCredentialWithTwoAudiences_IsNotEquivalent_AndIsReplaced()
    {
        var twoAudiences = new FederatedIdentityCredential
        {
            Id = "two",
            Name = WorkerFicName,
            Issuer = Issuer,
            Subject = WorkerPrincipalId,
            Audiences = [Audience, "api://SomethingElse"],
        };

        var plan = Reconcile(Fic(StampFicName, Issuer, StampUamiPrincipalId), twoAudiences);

        plan.Deletes.Should().ContainSingle().Which.Should().BeSameAs(twoAudiences);
        plan.Creates.Select(s => s.Name).Should().Equal(WorkerFicName);
    }

    // ---------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------

    private static EntraAppRegOptions Options(string workerFicName = WorkerFicName) => new()
    {
        FicName = StampFicName,
        WorkerFicName = workerFicName,
        FicAudience = Audience,
    };

    private static GraphAppRegistrationProvisioner.RequiredFics Specs(
        string? workerPrincipal = WorkerPrincipalId, EntraAppRegOptions? options = null)
        => GraphAppRegistrationProvisioner.BuildRequiredFicSpecs(
            Model1Profile, SpaarkeTenantId, SpaarkeTenantId, StampUamiPrincipalId, workerPrincipal, options ?? Options());

    private static GraphAppRegistrationProvisioner.FicReconcilePlan Reconcile(params FederatedIdentityCredential[] existing)
        => GraphAppRegistrationProvisioner.PlanFederatedCredentials(existing, Specs().Specs);

    private static FederatedIdentityCredential Fic(string name, string issuer, string subject, string? id = null) => new()
    {
        Id = id ?? $"{name}-id",
        Name = name,
        Issuer = issuer,
        Subject = subject,
        Audiences = [Audience],
    };
}
