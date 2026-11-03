using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// <see cref="ProcedureService"/> (ebilling-r1 task 081, EB-D-148; procedure model 3.1 I-P1, I-P3, I-P4, I-P5, 7.2, 8.2, 15 answers 1-2).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the invariants the BFF owns for Procedures (no plugin, ADR-002) — in-force versions never overlap and a publish
/// supersedes its predecessor all or nothing, a Rate card names its firm, scope locks once a version left Draft, retiring a version closes
/// nothing, and the retire confirmation counts only what the caller can see from the very selection the close uses. Dataverse is the
/// <see cref="RuleSetServiceTests.Harness"/> fake (real query evaluation, an all-or-nothing transaction with row versions); the rule-set
/// planning, <see cref="AffectedInvoiceCounter"/> and the Signal SELECTION are real; only the Signal close and the caller's rights probe are
/// substituted at their seams (the real close is covered by <c>SignalReevaluationJobTests.ProcedureRetire</c>).
/// </remarks>
public sealed class ProcedureServiceTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid OtherFirmId = Guid.NewGuid();
    private const string Token = "caller-token";
    private static readonly DateOnly Jan1 = new(2027, 1, 1);
    private static readonly DateOnly Mar1 = new(2027, 3, 1);
    private static readonly DateOnly Jun1 = new(2027, 6, 1);
    private const int Published = 100000001;
    private const int Draft = 100000000;

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();
        public readonly Mock<SignalClosureService> Closure;
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);

        public readonly HashSet<Guid> Readable = new();
        public readonly List<IReadOnlyList<(string EntitySet, Guid RecordId)>> RightsCalls = new();
        public readonly List<Guid> SelectedPolicies = new();
        public readonly List<Guid> ClosedPolicies = new();

        /// <summary>Ontology review F1 (2026-10-22): the <c>withBillingSubject</c> each selection and close was asked with.</summary>
        public readonly List<bool> SelectedWithBillingSubject = new();
        public readonly List<bool> ClosedWithBillingSubject = new();
        public Guid? CallerSystemUser = CallerUserId;
        /// <summary>The probe answered no record (a failed exchange or WhoAmI): every entry unknown. The real probe never throws.</summary>
        public bool RightsFault;

        /// <summary>uac-r2 F1: records whose rights question was not answered (a 429 or a 5xx): unknown, not None.</summary>
        public readonly HashSet<Guid> UnknownRights = new();
        public bool CallerFault;
        public bool SelectFault;
        public Func<Guid, bool>? CloseFailsFor;
        public Exception? CloseFault;

        public World()
        {
            H.Seed(new Entity("sprk_organization", FirmId));
            H.Seed(new Entity("sprk_organization", OtherFirmId));
            H.OwnerAnswer = RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = CallerUserId };
            var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)); // never called: the close is substituted
            Closure = new Mock<SignalClosureService>(MockBehavior.Loose, writer, H.Entities.Object, TimeProvider.System,
                NullLogger<SignalClosureService>.Instance) { CallBase = true };

            // The REAL selection over the fake store, recorded.
            Closure.Setup(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, bool withBillingSubject, CancellationToken ct) =>
                {
                    SelectedPolicies.Add(policyId);
                    SelectedWithBillingSubject.Add(withBillingSubject);
                    if (SelectFault) throw new InvalidOperationException("simulated selection fault");
                    return await SelectReal(policyId, withBillingSubject, ct);
                });

            // The close: closes what the SAME selection returns at confirm time (PolicyRetired), as the real method does.
            Closure.Setup(c => c.ClosePolicySignalsRetiredAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, string _, bool withBillingSubject, CancellationToken ct) =>
                {
                    ClosedPolicies.Add(policyId);
                    ClosedWithBillingSubject.Add(withBillingSubject);
                    if (CloseFailsFor?.Invoke(policyId) == true) throw CloseFault ?? new InvalidOperationException("simulated close fault");
                    var open = await Closure.Object.SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject, ct);
                    foreach (var s in open)
                    {
                        H.ConcurrentWrite("sprk_signal", s.Signal.Id, new Dictionary<string, object>
                        {
                            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved),
                            ["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionPolicyRetired),
                        });
                    }

                    return new PolicySignalClosure(new SignalClosureOutcome(open.Count, 0, 0), open);
                });

            Probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CallerFault ? throw new HttpRequestException("simulated token-exchange fault") : CallerSystemUser);
            // uac-r2 F1: the unknown-aware batched question, answered as the real probe answers (never a throw): the rights, None for
            // a 403, null (unknown) for a record not answered, or for every record when the caller's exchange fails.
            Probe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyList<(string, Guid)>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken _) =>
                {
                    RightsCalls.Add(targets);
                    token.Should().Be(Token, "the rights are asked as the caller");
                    return targets.Select(t => RightsFault || UnknownRights.Contains(t.RecordId)
                        ? (AccessRights?)null
                        : Readable.Contains(t.RecordId) ? AccessRights.Read | AccessRights.Write : AccessRights.None).ToList();
                });
        }

        /// <summary>The base selection, bypassing the recording setup (Moq's CallBase for a set-up member is not reachable otherwise).</summary>
        private Task<IReadOnlyList<OpenPolicySignal>> SelectReal(Guid policyId, bool withBillingSubject, CancellationToken ct) =>
            new SignalClosureService((SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)), H.Entities.Object,
                TimeProvider.System, NullLogger<SignalClosureService>.Instance).SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject, ct);

        public ProcedureService Service() => new(
            H.Entities.Object, H.Service(), Closure.Object,
            new AffectedInvoiceCounter(H.Entities.Object, Probe.Object, NullLogger<AffectedInvoiceCounter>.Instance),
            H.Ownership.Object, NullLogger<ProcedureService>.Instance);

        // ── Seeding ──────────────────────────────────────────────────────────────────────────────────

        public Guid Procedure(
            ProcedureType type = ProcedureType.Guideline, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null,
            int status = ProcedureEntity.StatusActive)
        {
            var p = new Entity("sprk_procedure", Guid.NewGuid())
            {
                ["sprk_name"] = "Acme OCG",
                ["sprk_firmfacingname"] = "Acme Outside Counsel Guidelines",
                ["sprk_area"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_proceduretype"] = new OptionSetValue((int)type),
                ["sprk_appliesto"] = new OptionSetValue((int)appliesTo),
                ["sprk_status"] = new OptionSetValue(status),
            };
            if (firm is { } f) p["sprk_firm"] = new EntityReference("sprk_organization", f);
            H.Seed(p);
            return p.Id;
        }

        public Guid RuleSetOf(Guid procedureId)
        {
            var id = H.RuleSet();
            H.Row("sprk_ruleset", id)["sprk_procedure"] = new EntityReference("sprk_procedure", procedureId);
            return id;
        }

        public Guid Version(Guid procedureId, int number, int status, DateOnly? from = null, DateOnly? to = null, Guid? previous = null)
        {
            var v = new Entity("sprk_procedureversion", Guid.NewGuid())
            {
                ["sprk_name"] = $"v{number}",
                ["sprk_procedure"] = new EntityReference("sprk_procedure", procedureId),
                ["sprk_versionnumber"] = number,
                ["sprk_status"] = new OptionSetValue(status),
            };
            if (from is { } f) v["sprk_effectivefrom"] = f.ToDateTime(TimeOnly.MinValue);
            if (to is { } t) v["sprk_effectiveto"] = t.ToDateTime(TimeOnly.MinValue);
            if (previous is { } p) v["sprk_previousversion"] = new EntityReference("sprk_procedureversion", p);
            H.Seed(v);
            return v.Id;
        }

        /// <summary>A rule of the set with its first policy version under <paramref name="procedureVersionId"/>.</summary>
        public (Guid Policy, Guid Version) Rule(
            Guid ruleSetId, string code, Guid procedureVersionId, ProposalState state = ProposalState.Accepted, bool published = true)
        {
            var (policy, version) = H.Rule(ruleSetId, code, state, current: published);
            StampUnder(version, procedureVersionId, published);
            H.Row("sprk_policy", policy)["sprk_enabled"] = true;
            return (policy, version);
        }

        public Guid AddVersion(Guid policyId, int number, Guid procedureVersionId, ProposalState state, bool published)
        {
            var version = H.AddVersion(policyId, number, state);
            StampUnder(version, procedureVersionId, published);
            return version;
        }

        private void StampUnder(Guid version, Guid procedureVersionId, bool published)
        {
            var row = H.Row("sprk_policyversion", version);
            row["sprk_procedureversion"] = new EntityReference("sprk_procedureversion", procedureVersionId);
            row["sprk_publishstate"] = new OptionSetValue(published ? Published : Draft);
        }

        public Guid SourceFile(Guid procedureVersionId, int purpose = ProcedureEntity.StoredFilePurposeProcedureSource,
            int status = ProcedureEntity.StoredFileStatusServable, int statecode = 0)
        {
            var f = new Entity("sprk_storedfile", Guid.NewGuid())
            {
                ["sprk_procedureversion"] = new EntityReference("sprk_procedureversion", procedureVersionId),
                ["sprk_purpose"] = new OptionSetValue(purpose),
                ["sprk_filestatus"] = new OptionSetValue(status),
                ["statecode"] = new OptionSetValue(statecode),
            };
            H.Seed(f);
            return f.Id;
        }

        public Guid Invoice(int statuscode = InvoiceStatusCodes.InReview, int statecode = 0)
        {
            var i = new Entity("sprk_invoice", Guid.NewGuid())
            {
                ["statuscode"] = new OptionSetValue(statuscode),
                ["statecode"] = new OptionSetValue(statecode),
            };
            H.Seed(i);
            return i.Id;
        }

        public Guid Line(Guid invoiceId)
        {
            var l = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId) };
            H.Seed(l);
            return l.Id;
        }

        public Guid Signal(Guid policyId, Guid? invoice = null, Guid? line = null, int status = SignalWriter.SignalStatusOpen)
        {
            var s = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policyId),
                ["sprk_signalstatus"] = new OptionSetValue(status),
            };
            if (invoice is { } i) s["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", i);
            if (line is { } l) s["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", l);
            H.Seed(s);
            return s.Id;
        }

        public int StatusOf(string entity, Guid id) => H.Row(entity, id).GetAttributeValue<OptionSetValue>("sprk_status").Value;

        public DateOnly? DateOf(Guid versionId, string column) =>
            H.Row("sprk_procedureversion", versionId).GetAttributeValue<DateTime?>(column) is { } d ? DateOnly.FromDateTime(d) : null;

        /// <summary>Lands another admin's write on the Procedure row right after the service reads it (the next read sees the change).</summary>
        public void ChangeProcedureAfterEveryRead(Guid procedureId) =>
            H.AfterQuery = q =>
            {
                if (q.EntityName == "sprk_procedure")
                {
                    H.ConcurrentWrite("sprk_procedure", procedureId, new Dictionary<string, object> { ["sprk_notes"] = Guid.NewGuid().ToString() });
                }
            };
    }

    private static ProcedureCreateRequest Create(
        string type = "Guideline", string? appliesTo = null, Guid? firm = null, string name = "Acme OCG") =>
        new(name, null, type, appliesTo, firm, null, null, null, null);

    private static ProcedureUpdateRequest Update(string? appliesTo = null, Guid? firm = null, bool clearFirm = false, string? name = null, string? type = null) =>
        new(name, null, type, appliesTo, firm, clearFirm, null, null, null, null);

    // ═══════════════════════════════════════════════════════════════════════════
    // Create (I-P3, model 5.1, 13)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Guideline", true)]
    [InlineData("RateCard", true)]
    [InlineData("BillingInstructions", true)]
    [InlineData("InternalStandard", false)]
    [InlineData("Other", false)]
    public async Task Create_WritesTheProcedureAndItsRuleSet_InOneTransaction_OwnedByTheCallersTeam_RecordingTheCaller(string type, bool visible)
    {
        var w = new World();

        var outcome = await w.Service().CreateAsync(Create(type, firm: FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Transactions.Should().ContainSingle().Which.Should().HaveCount(2).And.AllBeOfType<CreateRequest>();
        var procedure = w.H.Rows["sprk_procedure"].Single();
        procedure.GetAttributeValue<OptionSetValue>("sprk_area").Value.Should().Be(RuleDomains.PolicyDomainBilling);
        procedure.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(ProcedureEntity.StatusActive);
        procedure.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId, "owned by the caller's business-unit team (model 5.1)");
        procedure.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(CallerUserId, "creator person = the caller (O-23)");
        var set = w.H.Rows["sprk_ruleset"].Single();
        set.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure.Id);
        set.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);
        set.GetAttributeValue<bool>("sprk_visibletofirms").Should().Be(visible);
        set.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        set.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
        set.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Should().NotBeNull(
            "the rule set is created app-only for the caller too, so it records them (task 090, UAC answer to 081 Q5)");
        set.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(CallerUserId);
        outcome.Value!.RuleSetId.Should().Be(set.Id);

        // Two owner questions, each with no parents and naming the caller (model 5.1): the Procedure's (the caller's business-unit team) and
        // the rule set's, a hard-deny row asked for its table (EB-D-168a).
        w.H.Ownership.Verify(o => o.ResolveOwnerAsync(
            It.Is<RecordOwnershipContext>(c => !c.Parents.Any() && c.RequestedBy != null && c.ChildEntityLogicalName == null), It.IsAny<CancellationToken>()), Times.Once);
        w.H.Ownership.Verify(o => o.ResolveOwnerAsync(
            It.Is<RecordOwnershipContext>(c => !c.Parents.Any() && c.RequestedBy != null && c.ChildEntityLogicalName == "sprk_ruleset"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── EB-D-168 / EB-D-168a: the rule set and the copied policy versions are hard-deny rows ──

    private static World WithRealOwnership(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory directory)
    {
        var w = new World();
        var resolver = directory.Resolver();
        w.H.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Returns((RecordOwnershipContext c, CancellationToken t) => resolver.ResolveOwnerAsync(c, t));
        return w;
    }

    private static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory CallerIn(Guid businessUnit) =>
        Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.Standard().WithUser(CallerUserId, CallerOid, businessUnit);

    [Fact(DisplayName = "EB-D-168a: a new Procedure's rule set is owned by the restricted-content owner team; the Procedure by the caller's team")]
    public async Task Create_TheRuleSetIsOwnedByTheRestrictedContentTeam_TheProcedureByTheCallersTeam()
    {
        var w = WithRealOwnership(CallerIn(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.CustomerChildBu));

        var outcome = await w.Service().CreateAsync(Create("Guideline", firm: FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Rows["sprk_ruleset"].Single().GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.RestrictedContentTeam);
        w.H.Rows["sprk_procedure"].Single().GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.CustomerChildTeam, "sprk_procedure is not a hard-deny table");
    }

    [Fact(DisplayName = "EB-D-168a (fail closed): a Procedure whose rule set is answered in the root business unit is refused and nothing is written")]
    public async Task Create_RuleSetAnsweredInTheRootBusinessUnit_IsRefused_NothingWritten()
    {
        var w = WithRealOwnership(CallerIn(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.GeneralBu));

        var outcome = await w.Service().CreateAsync(Create("Guideline", firm: FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        outcome.OwnerRefusal!.RefusalCode.Should().Be(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved);
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "EB-D-168a: a new version's copied policy versions are owned by the restricted-content owner team; the version by the caller's team")]
    public async Task CreateVersion_TheCopiesAreOwnedByTheRestrictedContentTeam()
    {
        var w = WithRealOwnership(CallerIn(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.CustomerChildBu));
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        w.Rule(set, "BILL-A", v1);

        var outcome = await w.Service().CreateVersionAsync(p, Mar1, null, CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var v2 = outcome.Value!.Version.Id;
        w.H.Rows["sprk_policyversion"].Where(v => v.GetAttributeValue<EntityReference>("sprk_procedureversion")?.Id == v2)
            .Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.RestrictedContentTeam);
        w.H.Row("sprk_procedureversion", v2).GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory.CustomerChildTeam);
    }

    [Theory]
    [InlineData("RateCard", null)]
    [InlineData("Guideline", "Firm")]
    public async Task Create_WithoutAFirm_WhenOneIsRequired_Is400FirmRequired_AndWritesNothing(string type, string? appliesTo)
    {
        var w = new World();

        var outcome = await w.Service().CreateAsync(Create(type, appliesTo), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("procedure.firm_required");
        outcome.Errors.Should().ContainSingle().Which.Field.Should().Be("firmId");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_RateCardWithItsFirm_Succeeds()
    {
        var w = new World();

        var outcome = await w.Service().CreateAsync(Create("RateCard", firm: FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.FirmId.Should().Be(FirmId);
    }

    [Fact]
    public async Task Create_WithAnUnknownFirmOrAnUnknownType_Is400_AndWritesNothing()
    {
        var w = new World();

        var badFirm = await w.Service().CreateAsync(Create(firm: Guid.NewGuid()), CallerOid, default);
        var badType = await w.Service().CreateAsync(Create("100000001"), CallerOid, default);

        badFirm.ErrorCode.Should().Be("procedure.invalid");
        badFirm.Errors.Should().ContainSingle(e => e.Field == "firmId");
        badType.Errors.Should().ContainSingle(e => e.Field == "type", "a number is not a type name");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenTheOwnerCannotBeResolved_IsRefused_AndWritesNothing()
    {
        var w = new World();
        w.H.OwnerAnswer = RecordOwnerResolution.Refused("owner.no_team", "no team");

        var outcome = await w.Service().CreateAsync(Create(), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenTheTransactionFails_LeavesNeitherRow()
    {
        var w = new World();
        w.H.FailTransactionRequest = r => r is CreateRequest c && c.Target.LogicalName == "sprk_ruleset";

        var outcome = await w.Service().CreateAsync(Create(), CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.write_failed");
        w.H.Rows.GetValueOrDefault("sprk_procedure").Should().BeNullOrEmpty("the rule set failing undoes the Procedure");
    }

    [Fact]
    public async Task Create_WhenTheTransactionOutcomeIsUnknown_SaysTheProcedureMayExist_AndNamesIt()
    {
        var w = new World();
        w.H.TransactionOutcomeUnknown = true;

        var outcome = await w.Service().CreateAsync(Create(), CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.write_failed");
        var sent = w.H.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == "sprk_procedure").Target.Id;
        outcome.Detail.Should().Contain("may or may not have been created").And.Contain(sent.ToString(),
            "the caller can open the Procedure that may exist instead of creating a second one");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Update (I-P3, I-P4)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Update_ScopeChange_WhileEveryVersionIsDraft_Succeeds_AndTheRuleSetFollows()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusDraft);

        var outcome = await w.Service().UpdateAsync(p, Update(appliesTo: "Firm", firm: FirmId), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Row("sprk_procedure", p).GetAttributeValue<OptionSetValue>("sprk_appliesto").Value.Should().Be((int)ProcedureAppliesTo.Firm);
        w.H.Row("sprk_procedure", p).GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        w.H.Row("sprk_ruleset", set).GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        w.H.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be((int)RuleSetScope.Firm);
        w.H.Transactions.Should().ContainSingle("the Procedure and its rule set change together");
    }

    [Theory]
    [InlineData(ProcedureEntity.VersionStatusActive, "appliesTo")]
    [InlineData(ProcedureEntity.VersionStatusActive, "firm")]
    [InlineData(ProcedureEntity.VersionStatusSuperseded, "firm")]
    public async Task Update_ScopeChange_OnceAVersionLeftDraft_Is409ScopeLocked_AndWritesNothing(int versionStatus, string change)
    {
        var w = new World();
        var p = w.Procedure(appliesTo: ProcedureAppliesTo.Firm, firm: FirmId);
        w.RuleSetOf(p);
        w.Version(p, 1, versionStatus, Jan1);

        var outcome = await w.Service().UpdateAsync(p,
            change == "appliesTo" ? Update(appliesTo: "AssignedRecordsOnly") : Update(firm: OtherFirmId), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("procedure.scope_locked");
        outcome.Detail.Should().Be("Create a new Procedure to change who it governs.");
        w.H.Transactions.Should().BeEmpty();
        w.H.Row("sprk_procedure", p).GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
    }

    [Fact]
    public async Task Update_ANonScopeChange_AfterAVersionIsActive_Succeeds()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);

        var outcome = await w.Service().UpdateAsync(p, Update(name: "Acme OCG 2027"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Row("sprk_procedure", p).GetAttributeValue<string>("sprk_name").Should().Be("Acme OCG 2027");
    }

    [Fact]
    public async Task Update_ClearingTheFirmOfARateCard_Is400FirmRequired()
    {
        var w = new World();
        var p = w.Procedure(ProcedureType.RateCard, firm: FirmId);
        w.RuleSetOf(p);

        var outcome = await w.Service().UpdateAsync(p, Update(clearFirm: true), default);

        outcome.ErrorCode.Should().Be("procedure.firm_required");
        w.H.Transactions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("InternalStandard", "Guideline", true)]
    [InlineData("Guideline", "Other", false)]
    public async Task Update_ATypeChange_SetsTheRuleSetsVisibilityToFirms_InTheSameTransaction(string from, string to, bool visible)
    {
        var w = new World();
        var p = w.Procedure(Enum.Parse<ProcedureType>(from));
        var set = w.RuleSetOf(p);
        w.H.Row("sprk_ruleset", set)["sprk_visibletofirms"] = !visible;
        w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1); // not scope: allowed after a version is in force

        var outcome = await w.Service().UpdateAsync(p, Update(type: to), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.H.Row("sprk_ruleset", set).GetAttributeValue<bool>("sprk_visibletofirms").Should().Be(visible);
        w.H.Transactions.Should().ContainSingle();
    }

    [Theory]
    [InlineData(10, 3, "v3")]
    [InlineData(200, 12, "v12")]
    public void VersionName_FitsTheVersionsNameColumn(int nameLength, int number, string suffix)
    {
        var name = ProcedureService.VersionName(new string('a', nameLength), number);

        name.Length.Should().BeLessThanOrEqualTo(200);
        name.Should().EndWith(" " + suffix).And.StartWith("aaa");
    }

    [Fact]
    public async Task Update_WhenTheSendTimesOut_SaysTheChangeMayHaveBeenSaved_NotThatNothingWas()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.H.TransactionThrows = new TaskCanceledException("simulated HttpClient timeout");

        var outcome = await w.Service().UpdateAsync(p, Update(name: "Acme OCG 2027"), default);

        outcome.ErrorCode.Should().Be("procedure.write_failed");
        outcome.Detail.Should().Contain("may or may not have been saved", "a send cancelled by its own timeout may have committed");
    }

    [Fact]
    public async Task Update_TheAnswerIsBuiltFromWhatWasWritten_SoAFailingReReadCannotTurnACommittedChangeInto500()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        w.H.AfterQuery = q =>
        {
            if (w.H.Transactions.Count > 0 && q.EntityName is "sprk_procedure" or "sprk_procedureversion")
            {
                throw new InvalidOperationException("simulated read fault after the commit");
            }
        };

        var outcome = await w.Service().UpdateAsync(p, Update(name: "Acme OCG 2027", appliesTo: "Firm", firm: FirmId), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.Name.Should().Be("Acme OCG 2027");
        outcome.Value.AppliesTo.Should().Be("Firm");
        outcome.Value.FirmId.Should().Be(FirmId);
        outcome.Value.FirmFacingName.Should().Be("Acme Outside Counsel Guidelines", "unchanged members come from the row as read");
        outcome.Value.RuleSetId.Should().Be(set);
    }

    [Fact]
    public async Task Update_ScopeChange_AfterADraftWasRetiredWithoutPublishing_Succeeds_ItNeverGovernedAnything()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusRetired);
        w.Rule(set, "BILL-A", v1, published: false);

        var outcome = await w.Service().UpdateAsync(p, Update(appliesTo: "Firm", firm: FirmId), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.H.Row("sprk_procedure", p).GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
    }

    [Fact]
    public async Task Update_ScopeChange_AfterARetiredVersionPutARuleInForce_Is409ScopeLocked_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusRetired, Jan1);
        w.Rule(set, "BILL-A", v1, published: true);

        var outcome = await w.Service().UpdateAsync(p, Update(appliesTo: "Firm", firm: FirmId), default);

        outcome.ErrorCode.Should().Be("procedure.scope_locked");
        w.H.Transactions.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Versions and copy-forward (model 7.2)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateVersion_TheFirst_IsDraftNumberOne_WithNothingToCopy()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);

        var outcome = await w.Service().CreateVersionAsync(p, Jan1, null, CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Version.VersionNumber.Should().Be(1);
        outcome.Value.Version.Status.Should().Be("Draft");
        outcome.Value.RulesCopied.Should().Be(0);
        var row = w.H.Rows["sprk_procedureversion"].Single();
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(CallerUserId);
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
    }

    [Fact]
    public async Task CreateVersion_CopiesEveryPublishedRuleOfThePreviousVersion_AsDraftsOfTheSamePolicies_InTheSameTransaction()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var other = w.Version(Guid.NewGuid(), 1, ProcedureEntity.VersionStatusDraft); // another Procedure's draft version
        var (a, a1) = w.Rule(set, "BILL-A", v1);
        var (b, _) = w.Rule(set, "BILL-B", v1);
        var (off, _) = w.Rule(set, "BILL-OFF", v1);
        w.H.Row("sprk_policy", off)["sprk_enabled"] = false; // a rule switched Off is not copied
        var (notHere, _) = w.Rule(set, "BILL-ELSEWHERE", other, published: false); // a draft under another version is not copied

        var outcome = await w.Service().CreateVersionAsync(p, Mar1, null, CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.RulesCopied.Should().Be(2);
        outcome.Value.CopiedFromVersionId.Should().Be(v1);
        var v2 = outcome.Value.Version.Id;
        w.H.Row("sprk_procedureversion", v2).GetAttributeValue<EntityReference>("sprk_previousversion").Id.Should().Be(v1);
        w.H.Transactions.Should().ContainSingle("the version and its copies are created together");

        var copies = w.H.Rows["sprk_policyversion"].Where(v => v.GetAttributeValue<EntityReference>("sprk_procedureversion")?.Id == v2).ToList();
        copies.Select(c => c.GetAttributeValue<EntityReference>("sprk_policy").Id).Should().BeEquivalentTo(new[] { a, b }, "the SAME policies");
        copies.Should().AllSatisfy(c =>
        {
            c.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(Draft);
            c.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);
            c.GetAttributeValue<int>("sprk_versionnumber").Should().Be(2);
            c.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
        });
        copies.Single(c => c.GetAttributeValue<EntityReference>("sprk_policy").Id == a).GetAttributeValue<string>("sprk_rulebody")
            .Should().Be(w.H.Row("sprk_policyversion", a1).GetAttributeValue<string>("sprk_rulebody"));
        w.H.Rows["sprk_policyversion"].Count(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == notHere).Should().Be(1);
        w.H.Row("sprk_policy", a).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(a1, "nothing is in force until publish");
    }

    [Fact]
    public async Task CreateVersion_WhileADraftExists_Is409_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusDraft);

        var outcome = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.draft_exists");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateVersion_WhenItsNumberIsTakenMeanwhile_RetriesOnce_WithTheNextNumber()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var landed = false;
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_procedureversion" && !landed)
            {
                landed = true; // another admin's version 2 (since retired) lands between the read and the write
                w.Version(p, 2, ProcedureEntity.VersionStatusRetired);
            }
        };

        var outcome = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Version.VersionNumber.Should().Be(3);
        w.H.Transactions.Should().HaveCount(2, "one clash, one retry");
        w.H.TransactionFaults.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateVersion_WhenTheProcedureKeepsChanging_Is409VersionConflict_AfterOneRetry_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.ChangeProcedureAfterEveryRead(p);

        var outcome = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.version_conflict");
        w.H.Transactions.Should().HaveCount(2);
        w.H.Rows.GetValueOrDefault("sprk_procedureversion").Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task CreateVersion_WhenTheTransactionOutcomeIsUnknown_IsWriteFailed_AndDoesNotRetry()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.H.TransactionOutcomeUnknown = true;

        var outcome = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.write_failed", "a version that may exist is not reported as a number clash");
        outcome.Detail.Should().Contain("Reload the Procedure");
        w.H.Transactions.Should().ContainSingle("no retry when the first write may have landed");
    }

    [Fact]
    public async Task CreateVersion_WhenTheTransactionFails_LeavesNoVersionAndNoCopy()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        w.Rule(set, "BILL-A", v1);
        w.H.FailTransactionRequest = r => r is UpdateRequest u && u.Target.LogicalName == "sprk_procedure";

        var outcome = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);

        outcome.ErrorCode.Should().Be("procedure.write_failed");
        w.H.Rows["sprk_procedureversion"].Should().ContainSingle("only v1");
        w.H.Rows["sprk_policyversion"].Should().ContainSingle("no copy is left behind");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish (I-P1, model 3.2, 7.2)
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record PublishScene(World W, Guid Procedure, Guid RuleSet, Guid V1, Guid V2, Guid PolicyA, Guid A1, Guid A2, Guid OtherDraft);

    /// <summary>v1 Active from Jan 1 with rule A published; v2 Draft with A's Accepted copy and its Servable source file; and a Draft policy
    /// version of another Procedure Version in the same set.</summary>
    private static PublishScene ScenePublish()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var v2 = w.Version(p, 2, ProcedureEntity.VersionStatusDraft, previous: v1);
        var (a, a1) = w.Rule(set, "BILL-A", v1);
        var a2 = w.AddVersion(a, 2, v2, ProposalState.Accepted, published: false);
        var stranger = Guid.NewGuid();
        var (_, otherDraft) = w.Rule(set, "BILL-Z", stranger, ProposalState.Accepted, published: false);
        w.SourceFile(v2);
        return new PublishScene(w, p, set, v1, v2, a, a1, a2, otherDraft);
    }

    [Fact]
    public async Task Publish_V2FromD_SupersedesV1AtD_PutsItsRulesInForce_InOneTransaction()
    {
        var s = ScenePublish();

        var outcome = await s.W.Service().PublishVersionAsync(s.Procedure, s.V2, Mar1, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.SupersededVersionId.Should().Be(s.V1);
        s.W.StatusOf("sprk_procedureversion", s.V2).Should().Be(ProcedureEntity.VersionStatusActive);
        s.W.DateOf(s.V2, "sprk_effectivefrom").Should().Be(Mar1);
        s.W.StatusOf("sprk_procedureversion", s.V1).Should().Be(ProcedureEntity.VersionStatusSuperseded);
        s.W.DateOf(s.V1, "sprk_effectiveto").Should().Be(Mar1, "I-P1: half-open, v1 ends where v2 begins");
        s.W.H.Transactions.Should().ContainSingle("one BFF operation, all or nothing");

        var a2 = s.W.H.Row("sprk_policyversion", s.A2);
        a2.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(Published);
        s.W.H.Row("sprk_policy", s.PolicyA).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(s.A2);
        s.W.H.Row("sprk_policyversion", s.OtherDraft).GetAttributeValue<OptionSetValue>("sprk_publishstate").Value
            .Should().Be(Draft, "a Draft policy version under another Procedure Version is untouched");
        s.W.H.Writes.Should().NotContain(x => x.Id == s.OtherDraft);
        s.W.H.Row("sprk_policyversion", s.A1).GetAttributeValue<OptionSetValue>("sprk_publishstate").Value
            .Should().Be(Published, "v1's version stays as it was: v1 is still in force for its own window");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("not-servable")]
    [InlineData("other-purpose")]
    [InlineData("inactive")]
    public async Task Publish_WithoutOneServableProcedureSourceFile_Is400File_AndWritesNothing(string file)
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusDraft, Jan1);
        w.Rule(set, "BILL-A", v1, published: false);
        switch (file)
        {
            case "not-servable": w.SourceFile(v1, status: 100000000); break;
            case "other-purpose": w.SourceFile(v1, purpose: 100000001); break;
            case "inactive": w.SourceFile(v1, statecode: 1); break;
        }

        var outcome = await w.Service().PublishVersionAsync(p, v1, null, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("procedure.publish_invalid");
        outcome.Errors.Should().ContainSingle(e => e.Field == "file");
        w.H.Transactions.Should().BeEmpty();
        w.StatusOf("sprk_procedureversion", v1).Should().Be(ProcedureEntity.VersionStatusDraft);
    }

    [Fact]
    public async Task Publish_WithoutAnEffectiveFrom_Is400_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusDraft);
        w.SourceFile(v1);

        var outcome = await w.Service().PublishVersionAsync(p, v1, null, default);

        outcome.Errors.Should().ContainSingle(e => e.Field == "effectiveFrom");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ADateThatOverlapsAVersionInForce_Is409VersionOverlap_AndWritesNothing()
    {
        // v1 Superseded [Jan 1, Mar 1), v2 Active from Mar 1; v3 from Feb 1 would start before v2 and overlap v1.
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusSuperseded, Jan1, Mar1);
        var v2 = w.Version(p, 2, ProcedureEntity.VersionStatusActive, Mar1);
        var v3 = w.Version(p, 3, ProcedureEntity.VersionStatusDraft);
        w.SourceFile(v3);

        var outcome = await w.Service().PublishVersionAsync(p, v3, new DateOnly(2027, 2, 1), default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("procedure.version_overlap");
        w.H.Transactions.Should().BeEmpty();
        w.StatusOf("sprk_procedureversion", v2).Should().Be(ProcedureEntity.VersionStatusActive);
        w.DateOf(v2, "sprk_effectiveto").Should().BeNull();
    }

    [Fact]
    public async Task Publish_AWindowThatEndsInsideALaterVersionInForce_Is409VersionOverlap()
    {
        // v1 Superseded [Jun 1, ...) is impossible; instead: v1 Superseded [Jan 1, Mar 1) and v3 Draft [Feb 1, Jun 1) with no Active version.
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusSuperseded, Jan1, Mar1);
        var v3 = w.Version(p, 3, ProcedureEntity.VersionStatusDraft, new DateOnly(2027, 2, 1), Jun1);
        w.SourceFile(v3);

        var outcome = await w.Service().PublishVersionAsync(p, v3, null, default);

        outcome.ErrorCode.Should().Be("procedure.version_overlap");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenThePredecessorWriteFails_TheNewVersionIsNotLeftActive_AndNoRuleGoesInForce()
    {
        var s = ScenePublish();
        s.W.H.FailTransactionRequest = r => r is UpdateRequest u && u.Target.Id == s.V1;

        var outcome = await s.W.Service().PublishVersionAsync(s.Procedure, s.V2, Mar1, default);

        outcome.ErrorCode.Should().Be("procedure.write_failed");
        s.W.StatusOf("sprk_procedureversion", s.V2).Should().Be(ProcedureEntity.VersionStatusDraft, "the earlier write in the transaction was undone");
        s.W.StatusOf("sprk_procedureversion", s.V1).Should().Be(ProcedureEntity.VersionStatusActive);
        s.W.DateOf(s.V1, "sprk_effectiveto").Should().BeNull();
        s.W.H.Row("sprk_policyversion", s.A2).GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(Draft);
        s.W.H.Row("sprk_policy", s.PolicyA).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(s.A1);
    }

    [Fact]
    public async Task Publish_WithAProposedRule_Is400_AndWritesNothing()
    {
        var s = ScenePublish();
        s.W.H.Row("sprk_policyversion", s.A2)["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed);

        var outcome = await s.W.Service().PublishVersionAsync(s.Procedure, s.V2, Mar1, default);

        outcome.ErrorCode.Should().Be("procedure.publish_invalid");
        outcome.Errors.Should().ContainSingle(e => e.Field == "state" && e.PolicyId == s.PolicyA);
        s.W.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenTheProcedureChangesMeanwhile_Is409ConcurrentEdit_AndWritesNothing()
    {
        var s = ScenePublish();
        s.W.ChangeProcedureAfterEveryRead(s.Procedure);

        var outcome = await s.W.Service().PublishVersionAsync(s.Procedure, s.V2, Mar1, default);

        outcome.ErrorCode.Should().Be("procedure.concurrent_edit");
        s.W.StatusOf("sprk_procedureversion", s.V2).Should().Be(ProcedureEntity.VersionStatusDraft);
        s.W.StatusOf("sprk_procedureversion", s.V1).Should().Be(ProcedureEntity.VersionStatusActive);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("dates")]
    [InlineData("retireVersion")]
    public async Task EveryVersionWrite_WhenTheProcedureChangesMeanwhile_Is409ConcurrentEdit_AndChangesNothing(string operation)
    {
        // Each version write carries a conditional touch of the Procedure, so two admins acting on one Procedure at once cannot both win.
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusDraft, Jan1);
        var (_, a1) = w.Rule(set, "BILL-A", v1, published: false);
        w.SourceFile(v1);
        w.ChangeProcedureAfterEveryRead(p);
        var service = w.Service();

        var outcome = operation switch
        {
            "publish" => (await service.PublishVersionAsync(p, v1, null, default)).ErrorCode,
            "dates" => (await service.ChangeDatesAsync(p, v1, Mar1, null, false, default)).ErrorCode,
            _ => (await service.RetireVersionAsync(p, v1, default)).ErrorCode,
        };

        outcome.Should().Be("procedure.concurrent_edit");
        w.H.Transactions.Should().NotBeEmpty("the write was sent and refused by Dataverse, not skipped");
        w.StatusOf("sprk_procedureversion", v1).Should().Be(ProcedureEntity.VersionStatusDraft);
        w.DateOf(v1, "sprk_effectivefrom").Should().Be(Jan1);
        w.H.Row("sprk_policyversion", a1).GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(Draft);
    }

    [Fact]
    public async Task Publish_AVersionOfAnotherProcedure_Is404_AndWritesNothing()
    {
        var s = ScenePublish();
        var other = s.W.Procedure();

        var outcome = await s.W.Service().PublishVersionAsync(other, s.V2, Mar1, default);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be("procedure.version_not_found");
        s.W.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ARetiredProcedure_Is409()
    {
        var s = ScenePublish();
        s.W.H.Row("sprk_procedure", s.Procedure)["sprk_status"] = new OptionSetValue(ProcedureEntity.StatusRetired);

        var outcome = await s.W.Service().PublishVersionAsync(s.Procedure, s.V2, Mar1, default);

        outcome.ErrorCode.Should().Be("procedure.retired");
    }

    [Fact]
    public async Task CopyForward_ThenRejectingACopy_DropsThatRule_ItStaysEnabled_WithNoVersionPublishedUnderV2()
    {
        // v1 Active with A and B; v2 copies both; the admin rejects B's copy (RuleSetService decide, bound to the Draft version); publish.
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var (a, _) = w.Rule(set, "BILL-A", v1);
        var (b, b1) = w.Rule(set, "BILL-B", v1);
        var created = await w.Service().CreateVersionAsync(p, null, null, CallerOid, default);
        var v2 = created.Value!.Version.Id;
        w.SourceFile(v2);
        var b2 = w.H.Rows["sprk_policyversion"].Single(v =>
            v.GetAttributeValue<EntityReference>("sprk_policy").Id == b && v.GetAttributeValue<EntityReference>("sprk_procedureversion")?.Id == v2).Id;

        var decided = await w.H.Service().DecideAsync(set, b, accept: false, w.H.RowVersions[b2], default, "Not in the 2027 guidelines");
        decided.Kind.Should().Be(RuleSetOutcomeKind.Done, decided.Detail);
        var outcome = await w.Service().PublishVersionAsync(p, v2, Mar1, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.RulesRejected.Should().Be(1);
        outcome.Value.Rules.Select(r => r.PolicyId).Should().Equal(a);
        w.H.Row("sprk_policy", b).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("a dropped rule stays enabled (model 7.2)");
        w.H.Row("sprk_policy", b).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(b1);
        w.H.Rows["sprk_policyversion"].Where(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == b
                && v.GetAttributeValue<EntityReference>("sprk_procedureversion")?.Id == v2)
            .Should().OnlyContain(v => v.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value == Draft, "nothing of B is published under v2");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Dates (I-P1)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChangeDates_OfAnActiveVersion_IntoAnotherVersionInForce_Is409VersionOverlap_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusSuperseded, Jan1, Mar1);
        var v2 = w.Version(p, 2, ProcedureEntity.VersionStatusActive, Mar1);

        var outcome = await w.Service().ChangeDatesAsync(p, v2, new DateOnly(2027, 2, 1), null, false, default);

        outcome.ErrorCode.Should().Be("procedure.version_overlap");
        w.H.Transactions.Should().BeEmpty();
        w.DateOf(v2, "sprk_effectivefrom").Should().Be(Mar1);
    }

    [Fact]
    public async Task ChangeDates_OfADraft_Succeeds_AndAnInvertedRangeIs400()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusDraft);

        var ok = await w.Service().ChangeDatesAsync(p, v1, Mar1, Jun1, false, default);
        var inverted = await w.Service().ChangeDatesAsync(p, v1, Jun1, Mar1, false, default);

        ok.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.DateOf(v1, "sprk_effectivefrom").Should().Be(Mar1);
        w.DateOf(v1, "sprk_effectiveto").Should().Be(Jun1);
        inverted.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
    }

    [Fact]
    public async Task ChangeDates_OfASupersededVersion_Is409NotEditable()
    {
        var w = new World();
        var p = w.Procedure();
        w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusSuperseded, Jan1, Mar1);

        var outcome = await w.Service().ChangeDatesAsync(p, v1, null, Jun1, false, default);

        outcome.ErrorCode.Should().Be("procedure.version_not_editable");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Retire one version (I-P5, model 8.2)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RetireVersion_SetsItRetired_ClosesNoSignal_AndLeavesItsPoliciesEnabled()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var (a, _) = w.Rule(set, "BILL-A", v1);
        var signal = w.Signal(a, w.Invoice());

        var outcome = await w.Service().RetireVersionAsync(p, v1, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.StatusOf("sprk_procedureversion", v1).Should().Be(ProcedureEntity.VersionStatusRetired);
        w.H.Row("sprk_policy", a).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        w.H.Row("sprk_signal", signal).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
        w.ClosedPolicies.Should().BeEmpty();
        w.SelectedPolicies.Should().BeEmpty();
        w.H.Writes.Should().OnlyContain(x => x.Entity == "sprk_procedureversion" || x.Entity == "sprk_procedure");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Retire the Procedure: preview (model 15 answer 2) and confirm (I-P5, D-170)
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record RetireScene(World W, Guid Procedure, Guid PolicyA, Guid PolicyB, Guid Readable1, Guid Readable2, Guid Hidden, Guid[] Signals);

    /// <summary>
    /// Two policies; three affected OPEN invoices (one reached through a line Signal, one with two Signals) of which the caller can Read two;
    /// plus an Approved invoice and a Resolved Signal that are not affected.
    /// </summary>
    private static RetireScene SceneRetire()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var (a, _) = w.Rule(set, "BILL-A", v1);
        var (b, _) = w.Rule(set, "BILL-B", v1);
        var readable1 = w.Invoice();
        var readable2 = w.Invoice();
        var hidden = w.Invoice();
        var approved = w.Invoice(InvoiceStatusCodes.Approved);
        w.Readable.UnionWith(new[] { readable1, readable2, approved });
        var signals = new[]
        {
            w.Signal(a, readable1),
            w.Signal(b, readable1), // the same invoice twice: counted once
            w.Signal(a, line: w.Line(readable2)), // a line Signal counts its invoice
            w.Signal(b, hidden), // an invoice the caller cannot Read
            w.Signal(a, approved), // not open
        };
        w.Signal(a, readable2, status: SignalWriter.SignalStatusResolved); // already resolved: not selected
        return new RetireScene(w, p, a, b, readable1, readable2, hidden, signals);
    }

    [Fact]
    public async Task Preview_CountsOnlyTheOpenInvoicesTheCallerCanRead_FromTheSharedSelection_AndWritesNothing()
    {
        var s = SceneRetire();

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Warning.Should().Be("Retiring this Procedure will close all related rules and remove pending decisions on active invoices.");
        outcome.Value.AffectedText.Should().Be("2 open invoices you can see are affected.");
        outcome.Value.VisibleOpenInvoices.Should().Be(2);

        s.W.SelectedPolicies.Should().BeEquivalentTo(new[] { s.PolicyA, s.PolicyB }, "the preview selects through the close's own method");
        s.W.SelectedWithBillingSubject.Should().Equal(new[] { true, true },
            "the preview maps Signals to invoices, so it opts in to the invoice subject columns (ontology review F1)");
        s.W.RightsCalls.Should().ContainSingle("one batched rights call for the page")
            .Which.Select(t => t.RecordId).Should().BeEquivalentTo(new[] { s.Readable1, s.Readable2, s.Hidden }, "the three open invoices");
        s.W.RightsCalls[0].Should().OnlyContain(t => t.EntitySet == "sprk_invoices");
        s.W.H.Transactions.Should().BeEmpty();
        s.W.H.Writes.Should().BeEmpty("the preview writes nothing");
        s.W.ClosedPolicies.Should().BeEmpty();
    }

    [Fact]
    public void Preview_HasNoOtherNumberAndNoHiddenFlag()
    {
        typeof(ProcedureRetirePreview).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "Warning", "AffectedText", "VisibleOpenInvoices" });
    }

    [Fact]
    public async Task Preview_WhenTheRightsReadFaults_ShowsTheWarningWithNoCount_NeverTheAppOnlyNumber()
    {
        var s = SceneRetire();
        s.W.RightsFault = true;

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Value!.Warning.Should().NotBeNullOrEmpty();
        outcome.Value.AffectedText.Should().BeNull();
        outcome.Value.VisibleOpenInvoices.Should().BeNull();
    }

    [Fact]
    public async Task Preview_WhenOneInvoicesRightsAreUnknown_ShowsNoCount_NeverALowerOne()
    {
        // uac-r2 F1 must-fire: a 429 or 5xx on one readable invoice used to come back None and lower the count from 2 to 1.
        var s = SceneRetire();
        s.W.UnknownRights.Add(s.Readable2);

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Warning.Should().NotBeNullOrEmpty();
        outcome.Value.VisibleOpenInvoices.Should().BeNull("one unanswered invoice means the number cannot be told");
        outcome.Value.AffectedText.Should().BeNull();
        s.W.RightsCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Preview_WhenTheCallerCannotBeEstablished_ShowsNoCount()
    {
        var s = SceneRetire();
        s.W.CallerSystemUser = null;

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Value!.VisibleOpenInvoices.Should().BeNull();
        s.W.RightsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_WhenTheSelectionFaults_ShowsTheWarningWithNoCount()
    {
        var s = SceneRetire();
        s.W.SelectFault = true;

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.VisibleOpenInvoices.Should().BeNull();
    }

    [Fact]
    public async Task Preview_OneVisibleInvoice_IsSingular()
    {
        var s = SceneRetire();
        s.W.Readable.Remove(s.Readable2);

        var outcome = await s.W.Service().PreviewRetireAsync(s.Procedure, Token, default);

        outcome.Value!.AffectedText.Should().Be("1 open invoice you can see is affected.");
    }

    [Fact]
    public async Task Retire_RetiresTheProcedure_SwitchesEveryPolicyOff_AndClosesEveryOpenSignal_IncludingOnesTheCallerCannotRead()
    {
        var s = SceneRetire();

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.RulesSwitchedOff.Should().Be(2);
        outcome.Value.VisibleOpenInvoices.Should().Be(2, "counted as the preview counts: caller-readable open invoices among those closed");
        outcome.Value.AffectedText.Should().Be("2 open invoices you can see are affected.");
        s.W.StatusOf("sprk_procedure", s.Procedure).Should().Be(ProcedureEntity.StatusRetired);
        s.W.H.Row("sprk_policy", s.PolicyA).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        s.W.H.Row("sprk_policy", s.PolicyB).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        s.W.ClosedPolicies.Should().BeEquivalentTo(new[] { s.PolicyA, s.PolicyB });
        s.W.ClosedWithBillingSubject.Should().Equal(new[] { true, true },
            "the retire counts the closed Signals' invoices, so its close opts in to the invoice subject columns (ontology review F1)");
        s.Signals.Should().OnlyContain(id =>
            s.W.H.Row("sprk_signal", id).GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value == SignalWriter.ResolutionPolicyRetired,
            "the action acts on every affected invoice, readable or not");
        s.W.H.Transactions.Should().ContainSingle("the Procedure and its policies go Off together");
    }

    [Fact]
    public async Task Retire_WhenOnePolicysCloseFails_ThePolicyStaysOff_AndTheResultSaysTheCloseIsIncomplete()
    {
        var s = SceneRetire();
        s.W.CloseFailsFor = id => id == s.PolicyB;

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Contain("nightly run closes the rest");
        s.W.H.Row("sprk_policy", s.PolicyB).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse("Off, so the D-170 sweep closes it");
        s.W.H.Row("sprk_signal", s.Signals[3]).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact]
    public async Task Retire_WhenOnePolicysCloseTimesOut_ItGoesOnToTheNextPolicy_AndReportsTheCloseIncomplete_NotA500()
    {
        var s = SceneRetire();
        s.W.CloseFailsFor = id => id == s.PolicyA;
        s.W.CloseFault = new TaskCanceledException("simulated HttpClient timeout");

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CloseComplete.Should().BeFalse();
        s.W.ClosedPolicies.Should().BeEquivalentTo(new[] { s.PolicyA, s.PolicyB }, "a timeout on one policy does not stop the others");
        s.W.H.Row("sprk_signal", s.Signals[3]).GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value
            .Should().Be(SignalWriter.ResolutionPolicyRetired, "policy B's findings were closed");
        s.W.StatusOf("sprk_procedure", s.Procedure).Should().Be(ProcedureEntity.StatusRetired);
    }

    [Fact]
    public async Task Retire_Again_OnARetiredProcedure_FinishesAnIncompleteRun()
    {
        var s = SceneRetire();
        s.W.CloseFailsFor = id => id == s.PolicyB;
        await s.W.Service().RetireAsync(s.Procedure, Token, default);
        s.W.CloseFailsFor = null;

        var again = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        again.Value!.CloseComplete.Should().BeTrue();
        s.W.H.Row("sprk_signal", s.Signals[3]).GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionPolicyRetired);
        s.W.H.Transactions.Should().ContainSingle("nothing left to switch Off the second time");
    }

    [Fact]
    public async Task Retire_WhenTheProcedureChangesMeanwhile_Is409_AndClosesNothing()
    {
        var s = SceneRetire();
        s.W.ChangeProcedureAfterEveryRead(s.Procedure);

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.ErrorCode.Should().Be("procedure.concurrent_edit");
        s.W.ClosedPolicies.Should().BeEmpty();
        s.W.H.Row("sprk_policy", s.PolicyA).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
    }

    [Fact]
    public async Task Retire_WhenTheRightsReadFaults_StillRetiresAndCloses_ButShowsNoCount()
    {
        var s = SceneRetire();
        s.W.RightsFault = true;

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().BeNull();
        outcome.Value.AffectedText.Should().BeNull();
    }

    [Fact]
    public async Task Retire_WhenTheCountThrowsAfterTheCommit_TheRetireIsStillReportedDone_WithNoCount()
    {
        var s = SceneRetire();
        s.W.CallerFault = true; // throws out of the counter (the caller lookup is outside its own catch)

        var outcome = await s.W.Service().RetireAsync(s.Procedure, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, "the Procedure is Retired and its findings closed; a failed count must not read as a failed retire");
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().BeNull();
        s.W.StatusOf("sprk_procedure", s.Procedure).Should().Be(ProcedureEntity.StatusRetired);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RuleSetService on a Procedure's rule set (model 7.2: re-keyed per Procedure Version)
    // ═══════════════════════════════════════════════════════════════════════════

    private static ProposalEdit Headline(string text) => new(null, null, null, text, null, null, null, CallerOid);

    [Theory]
    [InlineData("assign")]
    [InlineData("visibility")]
    public async Task RuleSetAssignOrVisibility_OnAProceduresSet_Is409ProcedureOwned_AndWritesNothing(string route)
    {
        // I-P4: who a Procedure's rules apply to is the Procedure's (scope-locked) Applies to and Firm; the rule-set routes cannot go around it.
        var w = new World();
        var p = w.Procedure(appliesTo: ProcedureAppliesTo.Firm, firm: FirmId);
        var set = w.RuleSetOf(p);
        w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);

        var outcome = route == "assign"
            ? await w.H.Service().AssignAsync(set, RuleSetScope.Firm, OtherFirmId, default)
            : await w.H.Service().SetVisibilityAsync(set, false, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.procedure_owned");
        w.H.Writes.Should().BeEmpty();
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task RuleSetEdit_OnAProceduresSet_ChangesTheDraftVersionsCopyInPlace_AndLeavesThePublishedOneAlone()
    {
        var s = ScenePublish();
        var before = s.W.H.Row("sprk_policyversion", s.A1).GetAttributeValue<string>("sprk_shortheadline");

        var outcome = await s.W.H.Service().EditAsync(s.RuleSet, s.PolicyA, Headline("Edited for 2027"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        s.W.H.Row("sprk_policyversion", s.A2).GetAttributeValue<string>("sprk_shortheadline").Should().Be("Edited for 2027");
        s.W.H.Row("sprk_policyversion", s.A1).GetAttributeValue<string>("sprk_shortheadline").Should().Be(before);
        s.W.H.Rows["sprk_policyversion"].Count(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == s.PolicyA)
            .Should().Be(2, "the Draft copy is edited, no third version is created");
    }

    [Fact]
    public async Task RuleSetEdit_OnAProceduresSet_WithNoDraftVersion_Is409NoDraftVersion_AndWritesNothing()
    {
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        var (a, _) = w.Rule(set, "BILL-A", v1);

        var outcome = await w.H.Service().EditAsync(set, a, Headline("x"), default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.no_draft_version");
        w.H.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task RuleSetEdit_OfARuleWithNoVersionUnderTheDraft_Is404_AndNeverChangesThePublishedVersion()
    {
        // A rule published under v1 that v2's copy-forward did not carry is not a rule of v2 (RuleSetService.LoadRulesAsync): the edit is
        // refused as not found and v1's published version is never touched. Bringing such a rule into a Draft is a recorded follow-up.
        var w = new World();
        var p = w.Procedure();
        var set = w.RuleSetOf(p);
        var v1 = w.Version(p, 1, ProcedureEntity.VersionStatusActive, Jan1);
        w.Version(p, 2, ProcedureEntity.VersionStatusDraft, previous: v1);
        var (a, a1) = w.Rule(set, "BILL-A", v1);
        var before = w.H.Row("sprk_policyversion", a1).GetAttributeValue<string>("sprk_shortheadline");

        var outcome = await w.H.Service().EditAsync(set, a, Headline("Back in 2027"), default);

        outcome.StatusCode.Should().Be(404);
        w.H.Writes.Should().BeEmpty();
        w.H.Row("sprk_policyversion", a1).GetAttributeValue<string>("sprk_shortheadline").Should().Be(before);
    }

    [Fact]
    public async Task RuleSetPublish_OfAProceduresSet_Is409_PublishTheVersionInstead()
    {
        var s = ScenePublish();

        var outcome = await s.W.H.Service().PublishAsync(s.RuleSet, Mar1, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("ruleset.procedure_publish");
        s.W.H.Row("sprk_policy", s.PolicyA).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(s.A1);
    }

    [Theory]
    [InlineData(RuleDomains.PolicyDomainBilling, true, true)]
    [InlineData(RuleDomains.PolicyDomainOntology, true, false)]
    [InlineData(null, true, false)] // no domain reads as Ontology
    [InlineData(RuleDomains.PolicyDomainOntology, false, true)] // no link: nothing to refuse
    public void TheValidator_AllowsAProcedureVersionLinkOnlyOnABillingPolicy(int? domain, bool linked, bool valid)
    {
        var result = PolicyVersionValidator.ValidateProcedureVersionLink(domain, linked ? Guid.NewGuid() : null);

        result.IsValid.Should().Be(valid);
        if (!valid)
        {
            result.Reason.Should().Be(OntologyWriterFailureReason.ProcedureVersionWrongDomain);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2027-01-01", "2027-03-01", "2027-03-01", null, false)] // half-open: touching windows do not overlap
    [InlineData("2027-01-01", null, "2027-03-01", null, true)]
    [InlineData("2027-03-01", "2027-06-01", "2027-01-01", "2027-03-02", true)]
    public void Overlaps_IsHalfOpen(string aFrom, string? aTo, string bFrom, string? bTo, bool expected)
    {
        ProcedureService.Overlaps(DateOnly.Parse(aFrom), aTo is null ? null : DateOnly.Parse(aTo), DateOnly.Parse(bFrom),
            bTo is null ? null : DateOnly.Parse(bTo)).Should().Be(expected);
    }
}
