// -----------------------------------------------------------------------------
// H7bSecureRecordSetupHandlerTests.cs
//
// T256 (H7b) — the handler around the procedure: upstream guards, Level-3 idempotency, the dry run, the §4C mapping
// (Resumable vs QuarantineRequired), Dataverse fault codes and the Cosmos state it writes. The environment is the
// in-memory FakeSecureRecordSetupDataverse; the run row a hand-written repository fake (no Moq for the seam, ADR-038).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class H7bSecureRecordSetupHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h7b-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";

    private static readonly SecureRecordOwnerRoleSet Set = SecureRecordOwnerRoleSet.Embedded;

    [Fact]
    public async Task HappyPath_ConfiguresTheEnvironment_RecordsThePhaseAndTheVerifiedGate()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H7bSecureRecordSetupHandler.BuildIdempotencyKey(
            CustomerId, H7bSecureRecordSetupHandler.ComputeSetHash(Set)));
        var run = repo.LastWrittenRun!;
        run.Status.Should().Be(RunStatus.Running);
        run.CompletedPhases.Should().ContainSingle(p => p.Phase == "H7b" && p.IdempotencyKey == success.IdempotencyKey);
        var gate = run.GateStates[H7bSecureRecordSetupHandler.SetupGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.Evidence!.Value.GetProperty("privilegeCount").GetInt32().Should().Be(Set.Tables.Count);
        gate.Evidence.Value.GetProperty("roleId").GetString().Should().Be(dv.Role(dv.Unit(Set.BusinessUnitName)!.Id, Set.RoleName)!.Id.ToString());
        dv.Writes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ASecondRun_InANewRunRow_WritesNothingToDataverse()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Clear();

        var result = await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Should().BeEmpty("a second apply against a configured environment changes nothing");
    }

    [Fact]
    public async Task SameRun_WithTheCompletedPhase_IsANoOp_WithoutTouchingDataverse()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H7b",
            IdempotencyKey = H7bSecureRecordSetupHandler.BuildIdempotencyKey(CustomerId, H7bSecureRecordSetupHandler.ComputeSetHash(Set)),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = RunId,
        });
        var repo = new FakeRepository(run);

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.Reads.Should().Be(0);
        repo.LastWrittenRun.Should().BeNull();
    }

    [Fact]
    public async Task DryRun_WritesNothing_StopsTheRun_AndRecordsThePlan()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        run.Parameters.NonSecret[IntakeParameterCatalog.SecureRecordSetupDryRun] = "true";
        var repo = new FakeRepository(run);

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.DryRunComplete);
        failure.Class.Should().Be(FailureClass.Resumable);
        dv.Writes.Should().BeEmpty();
        dv.Reads.Should().BeGreaterThan(0, "a dry run reads everything");
        var written = repo.LastWrittenRun!;
        written.Status.Should().Be(RunStatus.Failed, "nothing after H7b may run against an environment the dry run left unconfigured");
        written.CompletedPhases.Should().NotContain(p => p.Phase == "H7b");
        var plan = written.GateStates[H7bSecureRecordSetupHandler.PlanGateId].Evidence!.Value.GetProperty("plan");
        plan.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("")]
    public async Task InvalidDryRunValue_IsRefused_BeforeAnyDataverseCall(string value)
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        run.Parameters.NonSecret[IntakeParameterCatalog.SecureRecordSetupDryRun] = value;

        var result = await BuildHandler(new FakeRepository(run), dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.DryRunInvalid);
        dv.Reads.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitFalse_Applies()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        run.Parameters.NonSecret[IntakeParameterCatalog.SecureRecordSetupDryRun] = "false";

        (await BuildHandler(new FakeRepository(run), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("dataverseEnvUrl")]
    [InlineData("bffAppRegId")]
    [InlineData("bffAppRegSystemUserId")]
    [InlineData("systemUserId")]
    [InlineData("customerBusinessUnitId")]
    public async Task MissingUpstreamValue_FailsResumable_BeforeAnyDataverseCall(string missing)
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        switch (missing)
        {
            case "tenantId": run.Parameters.NonSecret.Remove("tenantId"); break;
            case "dataverseEnvUrl": run.InterStepState.DataverseEnvUrl = null; break;
            case "bffAppRegId": run.InterStepState.BffAppRegId = " "; break;
            case "bffAppRegSystemUserId": run.InterStepState.BffAppRegSystemUserId = null; break;
            case "systemUserId": run.InterStepState.SystemUserId = "not-a-guid"; break;
            case "customerBusinessUnitId": run.InterStepState.CustomerBusinessUnitId = null; break;
        }

        var result = await BuildHandler(new FakeRepository(run), dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain(missing);
        dv.Reads.Should().Be(0);
    }

    [Fact]
    public async Task OwnerDecisionRefusal_QuarantinesTheRun()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var unit = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(unit, Set.BusinessUnitName, dv.RootUnitId));
        dv.AddTeam(unit, Set.BusinessUnitName, isDefault: true);
        dv.UserUnit[Guid.NewGuid()] = unit;
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.BusinessUnitHasUsers);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine!.QuarantinedByHandler.Should().Be("H7b");
        repo.LastWrittenRun.GateStates.Should().ContainKey($"h7b-{SecureRecordSetupRejectionCodes.BusinessUnitHasUsers}");
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task T259_ApplicationUserOutsideTheCustomerUnit_QuarantinesTheRun_WritingNothing()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        dv.UserUnit[dv.BffAppUser] = dv.RootUnitId;   // created in the root (pre-T259 H10)
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.AppUserOutsideCustomerBusinessUnit);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task IdentityLinkWriterWithAForeignMember_QuarantinesTheRun_WritingNothing()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        dv.ProfileUsers[dv.LinkWriterProfileId].Add(Guid.NewGuid());
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.GateStates.Should().ContainKey($"h7b-{SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember}");
        repo.LastWrittenRun.CompletedPhases.Should().NotContain(p => p.Phase == "H7b");
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ARunCompletedUnderProcedureVersion1_DoesNotShortCircuit_AndAddsTheIdentityLinkMemberships()
    {
        // An environment H7b configured before S15–S18: the run row records the phase under the v1 key.
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        dv.ProfileTeams[dv.LinkReaderProfileId].Clear();
        dv.ProfileUsers[dv.LinkWriterProfileId].Clear();
        dv.Writes.Clear();
        var v1Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            "secure-setup-procedure=1\n" + Set.RoleName + "\n" + Set.BusinessUnitName + "\n" + Set.SetHash()))).ToLowerInvariant();
        var run = BuildRun(dv);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H7b",
            IdempotencyKey = H7bSecureRecordSetupHandler.BuildIdempotencyKey(CustomerId, v1Hash),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = RunId,
        });

        var result = await BuildHandler(new FakeRepository(run), dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey.Should().NotContain(v1Hash);
        dv.ProfileUsers[dv.LinkWriterProfileId].Should().BeEquivalentTo(new[] { dv.BffAppUser, dv.MiAppUser });
        dv.ProfileTeams[dv.LinkReaderProfileId].Should().BeEquivalentTo(dv.Teams.Where(t => t.IsDefault).Select(t => t.Id));
        dv.Writes.Should().OnlyContain(w => w.StartsWith("AssociateProfile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRun_RecordsTheIdentityLinkMembershipsInThePlan()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var run = BuildRun(dv);
        run.Parameters.NonSecret[IntakeParameterCatalog.SecureRecordSetupDryRun] = "true";
        var repo = new FakeRepository(run);

        await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var plan = repo.LastWrittenRun!.GateStates[H7bSecureRecordSetupHandler.PlanGateId].Evidence!.Value.GetProperty("plan")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        plan.Should().Contain($"add BFF application user {dv.MiAppUser} to '{SecureRecordSetupProcedure.IdentityLinkWriterProfileName}'");
        plan.Should().Contain(a => a!.EndsWith($"to '{SecureRecordSetupProcedure.IdentityLinkReaderProfileName}'", StringComparison.Ordinal));
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task NoAccessEntryMissing_FailsResumable_SoTheBffIsNeverDeployed()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        dv.NoAccessEntryPresent = false;
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.NoAccessEntryMissing);
        failure.Class.Should().Be(FailureClass.Resumable);
        repo.LastWrittenRun!.CompletedPhases.Should().NotContain(p => p.Phase == "H7b", "H9 waits for a COMPLETED H7b");
        dv.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecureRecordSetupFaultKind.Auth, SecureRecordSetupRejectionCodes.DataverseAuthFailure)]
    [InlineData(SecureRecordSetupFaultKind.RateLimited, SecureRecordSetupRejectionCodes.RateLimited)]
    [InlineData(SecureRecordSetupFaultKind.Other, SecureRecordSetupRejectionCodes.DataverseInvocationFailed)]
    public async Task DataverseFault_MapsToTheSharedResumableCode(SecureRecordSetupFaultKind kind, string code)
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        dv.Fault = member => member == nameof(ISecureRecordSetupDataverse.ProbeNoAccessEntryAsync)
            ? new SecureRecordSetupDataverseException(kind, "boom")
            : null;

        var result = await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(code);
        failure.Class.Should().Be(FailureClass.Resumable);
    }

    [Fact]
    public async Task UnexpectedException_IsResumable_AndCancellationPropagates()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        dv.Fault = member => member == nameof(ISecureRecordSetupDataverse.ProbeNoAccessEntryAsync) ? new InvalidDataException("x") : null;

        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.DataverseInvocationFailed);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        dv.Fault = _ => new OperationCanceledException(cts.Token);
        var act = () => BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvalidCodifiedSet_FailsResumable_WithItsOwnCode()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var handler = new H7bSecureRecordSetupHandler(new FakeRepository(BuildRun(dv)), dv, Options(), TimeProvider.System,
            NullLogger<H7bSecureRecordSetupHandler>.Instance,
            () => SecureRecordOwnerRoleSet.Parse("{\"schemaVersion\":2}"));

        var result = await handler.HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.RoleSetInvalid);
        dv.Reads.Should().Be(0);
    }

    [Fact]
    public async Task LegacyChainWithoutASecret_FailsResumable_BeforeAnyDataverseCall()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var handler = new H7bSecureRecordSetupHandler(new FakeRepository(BuildRun(dv)), dv,
            Microsoft.Extensions.Options.Options.Create(new EnvVarValuesOptions { ClientSecret = null }),
            TimeProvider.System, NullLogger<H7bSecureRecordSetupHandler>.Instance);

        var result = await handler.HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.MissingClientSecret);
        dv.Reads.Should().Be(0);
    }

    [Fact]
    public async Task RunNotFound_FailsResumable()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);

        var result = await BuildHandler(new FakeRepository(null), dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(SecureRecordSetupRejectionCodes.RunNotFound);
    }

    [Fact]
    public async Task MismatchedHandlerId_Throws()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(Set);
        var envelope = Envelope() with { HandlerId = "H7" };

        var act = () => BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void SetHash_ChangesWhenTheCodifiedSetChanges_AndNotWithEntryOrder()
    {
        const string Entry = "{{\"logicalName\":\"{0}\",\"privilegeName\":\"{1}\",\"reason\":\"r\",\"evidence\":\"e\"}}";
        static SecureRecordOwnerRoleSet Doc(params (string Table, string Privilege)[] tables) => SecureRecordOwnerRoleSet.Parse(
            "{\"schemaVersion\":1,\"roleName\":\"Secure Record Owner\",\"businessUnitName\":\"Secure Record\",\"access\":\"Read\"," +
            "\"depth\":\"Basic\",\"tables\":[" + string.Join(",", tables.Select(t => string.Format(System.Globalization.CultureInfo.InvariantCulture, Entry, t.Table, t.Privilege))) + "]}");

        var ab = H7bSecureRecordSetupHandler.ComputeSetHash(Doc(("a", "prvReadA"), ("b", "prvReadB")));
        var ba = H7bSecureRecordSetupHandler.ComputeSetHash(Doc(("b", "prvReadB"), ("a", "prvReadA")));
        var abc = H7bSecureRecordSetupHandler.ComputeSetHash(Doc(("a", "prvReadA"), ("b", "prvReadB"), ("c", "prvReadC")));

        ab.Should().Be(ba);
        abc.Should().NotBe(ab, "extending the codified set re-applies on the next run (INCOMING-145 §2.3)");
    }

    // ------------------------------------------------------------ helpers

    private static H7bSecureRecordSetupHandler BuildHandler(IProvisioningRunRepository repo, ISecureRecordSetupDataverse dv)
        => new(repo, dv, Options(), TimeProvider.System, NullLogger<H7bSecureRecordSetupHandler>.Instance);

    private static IOptions<EnvVarValuesOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EnvVarValuesOptions { ClientSecret = "test-client-secret-placeholder" });

    private static HandlerEnvelope Envelope() => new()
    {
        HandlerId = "H7b",
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(FakeSecureRecordSetupDataverse dv)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
            Status = RunStatus.Running,
        };
        run.Parameters.NonSecret["tenantId"] = TenantId;
        run.InterStepState.DataverseEnvUrl = EnvUrl;
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.BffAppRegSystemUserId = dv.BffAppUser.ToString();
        run.InterStepState.SystemUserId = dv.MiAppUser.ToString();
        run.InterStepState.CustomerBusinessUnitId = dv.CustomerUnitId.ToString();   // T259 (H10)
        return run;
    }

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string _etag = "etag-1";

        public FakeRepository(ProvisioningRun? run) => _run = run;

        public ProvisioningRun? LastWrittenRun { get; private set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null ? null : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }
}
