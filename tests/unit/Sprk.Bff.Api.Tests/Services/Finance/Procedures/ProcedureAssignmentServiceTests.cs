using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// <see cref="ProcedureAssignmentService"/> (ebilling-r1 task 089b, EB-D-148; procedure model 5.1, 6.2, 6.3, 6.5 I-A1).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the invariants the BFF owns for a record's overrides (no plugin, ADR-002): every row is owned through the
/// resolver by its own question with the core record as its one parent and the caller as RequestedBy; one row per (record, Procedure) and one
/// per (record, rule), by the read and by the derived id; a refusal writes nothing. Dataverse is the <see cref="RuleSetServiceTests.Harness"/>
/// fake (real query evaluation, an all-or-nothing transaction that faults a duplicate primary key); the owner is the resolver substitute,
/// except in the two tests that run the REAL resolver over <see cref="OwnershipDirectory"/>.
/// </remarks>
public sealed class ProcedureAssignmentServiceTests
{
    // C19 (ontology): the close's completion log names the closed Signals in a bounded list.
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(50, 50, false)]
    [InlineData(51, 50, true)]
    [InlineData(300, 50, true)]
    public void ClosedSignalIdsForLog_IsBounded(int closed, int named, bool counted)
    {
        var ids = Enumerable.Range(0, closed).Select(_ => Guid.NewGuid()).ToHashSet();

        var text = ProcedureAssignmentService.ClosedSignalIdsForLog(ids);

        ids.Count(id => text.Contains(id.ToString(), StringComparison.Ordinal)).Should().Be(named);
        text.Contains($"(+{closed - ProcedureAssignmentService.MaxClosedSignalIdsLogged} more)", StringComparison.Ordinal).Should().Be(counted);
    }

    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private const string Matter = "sprk_matter";
    private const string Assignment = "sprk_procedureassignment";
    private const string Token = "caller-token";

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Keeps what the service logged: level, the formatted line, and whether an exception object was passed.</summary>
    private sealed class CapturingLogger : ILogger<ProcedureAssignmentService>
    {
        public List<(LogLevel Level, string Message, bool HasException)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception is not null));
    }

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();

        // Task 089c: the removal's close and count, as ProcedureServiceTests substitutes them. The Signal SELECTION is real (over the fake
        // store); the close marks what that selection returns resolved; the caller's rights probe answers from Readable.
        public readonly Mock<SignalClosureService> Closure;
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        public readonly HashSet<Guid> Readable = new();
        public readonly List<Guid> SelectedPolicies = new();
        public readonly List<Guid> ClosedPolicies = new();
        public bool RightsFault;
        public Exception? CloseFault;

        // Task 089e: the change's Superseded close of the base rule's findings, through SupersedeInPassAsync (substituted like the retire close):
        // what it was asked to close, naming which version; a per-Signal write fault counts every Signal failed and closes none (as CloseAsync
        // does when the writer throws); a selection fault throws from the close's own selection.
        public readonly List<(string Code, Guid Version, IReadOnlyList<Guid> Signals)> Superseded = new();
        public bool SupersedeWriteFault;
        public Guid? SelectFaultFor;

        // Task 089e pass-1 K4: the change's selection, bounded to the record's invoices and their lines (real, over the fake store).
        public readonly List<(Guid Policy, IReadOnlyCollection<Guid> Invoices, IReadOnlyCollection<Guid> Lines)> SubjectSelections = new();

        // 082c C19 fix round 1 (owner 2026-11-05, "the effective date wins"): the instant a change puts its record rule in force by. Fixed, so a
        // test can date an invoice's lines before, on and after the in-force day. A test that needs another instant sets Clock.
        public static readonly DateTimeOffset InForceInstant = new(2026, 11, 5, 14, 30, 0, TimeSpan.Zero);
        public static readonly DateOnly InForceDay = new(2026, 11, 5);
        public TimeProvider Clock = new FixedClock(InForceInstant);
        public readonly CapturingLogger Log = new();

        public World()
        {
            H.Seed(new Entity("sprk_organization", FirmId), new Entity(Matter, MatterId));
            H.OwnerAnswer = RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = CallerUserId };

            var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)); // never called: the close is substituted
            Closure = new Mock<SignalClosureService>(MockBehavior.Loose, writer, H.Entities.Object, TimeProvider.System,
                NullLogger<SignalClosureService>.Instance)
            { CallBase = true };
            Closure.Setup(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, bool withBillingSubject, CancellationToken ct) =>
                {
                    SelectedPolicies.Add(policyId);
                    if (SelectFaultFor == policyId) throw new InvalidOperationException("simulated selection fault");
                    return await SelectReal(policyId, withBillingSubject, ct);
                });
            Closure.Setup(c => c.SelectOpenSignalsOfPolicyOnSubjectsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, IReadOnlyCollection<Guid> invoices, IReadOnlyCollection<Guid> lines, CancellationToken ct) =>
                {
                    SubjectSelections.Add((policyId, invoices.ToList(), lines.ToList()));
                    if (SelectFaultFor == policyId) throw new InvalidOperationException("simulated selection fault");
                    return await RealClosure().SelectOpenSignalsOfPolicyOnSubjectsAsync(policyId, invoices, lines, ct);
                });
            Closure.Setup(c => c.SupersedeInPassAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ExistingSignal>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<Func<ExistingSignal, bool>?>()))
                .ReturnsAsync((string code, Guid version, IReadOnlyList<ExistingSignal> existing, CancellationToken _, Func<ExistingSignal, bool>? outside) =>
                {
                    var targeted = existing.Where(s => s.IsOpen && (s.PolicyVersionId != version || (outside is not null && outside(s)))).ToList();
                    Superseded.Add((code, version, targeted.Select(s => s.Id).ToList()));
                    if (SupersedeWriteFault)
                    {
                        return (existing, new SignalClosureOutcome(0, 0, targeted.Count), (IReadOnlySet<Guid>)targeted.Select(s => s.Id).ToHashSet());
                    }

                    foreach (var s in targeted)
                    {
                        H.ConcurrentWrite("sprk_signal", s.Id, new Dictionary<string, object>
                        {
                            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved),
                            ["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionSuperseded),
                        });
                    }

                    var ids = targeted.Select(s => s.Id).ToHashSet();
                    IReadOnlyList<ExistingSignal> after = existing
                        .Select(s => ids.Contains(s.Id) ? s with { Status = SignalWriter.SignalStatusResolved, Resolution = SignalWriter.ResolutionSuperseded } : s)
                        .ToList();
                    return (after, new SignalClosureOutcome(targeted.Count, 0, 0), (IReadOnlySet<Guid>)new HashSet<Guid>());
                });
            Closure.Setup(c => c.ClosePolicySignalsRetiredAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid policyId, string _, bool withBillingSubject, CancellationToken ct) =>
                {
                    ClosedPolicies.Add(policyId);
                    if (CloseFault is not null) throw CloseFault;
                    var open = await SelectReal(policyId, withBillingSubject, ct);
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
            Probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(CallerUserId);
            // 089e pass-1 F3: only the caller's own token is answered (the strict mock refuses any other, so the count is not made); an
            // assertion inside the callback would be swallowed by the counter's fault handling.
            Probe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(It.Is<string?>(t => t == Token), It.IsAny<IReadOnlyList<(string, Guid)>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken _) =>
                {
                    return targets.Select(t => RightsFault
                        ? (AccessRights?)null
                        : Readable.Contains(t.RecordId) ? AccessRights.Read : AccessRights.None).ToList();
                });

            // 089b review 2 F1: the fake used to answer any column set on sprk_organization, so a $select naming sprk_name (which production
            // refuses) passed. Refuse a column the table does not have, as Dataverse does.
            H.AfterQuery = q =>
            {
                var unknown = q.EntityName == "sprk_organization" ? q.ColumnSet.Columns.Except(OrganizationColumns).ToArray() : Array.Empty<string>();
                if (unknown.Length > 0)
                {
                    throw new InvalidOperationException($"sprk_organization has no column {string.Join(", ", unknown)} (its primary name is sprk_organizationname).");
                }
            };
        }

        /// <summary>The columns of <c>sprk_organization</c> a query here may name (the key and the primary name; Entities/sprk_Organization/Entity.xml).</summary>
        private static readonly string[] OrganizationColumns = { "sprk_organizationid", "sprk_organizationname" };

        public ProcedureAssignmentService Service(IRecordOwnershipResolver? ownership = null) =>
            new(H.Entities.Object, ownership ?? H.Ownership.Object, H.Service(), Closure.Object,
                new AffectedInvoiceCounter(H.Entities.Object, Probe.Object, NullLogger<AffectedInvoiceCounter>.Instance),
                Log)
            { Clock = Clock };

        private Task<IReadOnlyList<OpenPolicySignal>> SelectReal(Guid policyId, bool withBillingSubject, CancellationToken ct) =>
            RealClosure().SelectOpenSignalsOfPolicyAsync(policyId, withBillingSubject, ct);

        private SignalClosureService RealClosure() =>
            new((SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)), H.Entities.Object,
                TimeProvider.System, NullLogger<SignalClosureService>.Instance);

        /// <summary>An invoice; its date is the in-force day unless told (the close compares work dates with the change's in-force day).</summary>
        public Guid Invoice(int statuscode = InvoiceStatusCodes.InReview, Guid? matter = null, Guid? firm = null, DateOnly? date = null)
        {
            var i = new Entity("sprk_invoice", Guid.NewGuid())
            {
                ["statuscode"] = new OptionSetValue(statuscode),
                ["statecode"] = new OptionSetValue(0),
                [InvoiceAdjustmentService.InvoiceDateColumn] = (date ?? InForceDay).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            };
            if (matter is { } m) i[Matter] = new EntityReference(Matter, m);
            if (firm is { } f) i["sprk_vendororg"] = new EntityReference("sprk_organization", f);
            H.Seed(i);
            return i.Id;
        }

        /// <summary>A Fee line of the invoice, worked on the in-force day unless told; <paramref name="date"/> null with <paramref name="dated"/> false
        /// is a line with no work date (the evaluator then judges it on the invoice date).</summary>
        public Guid Line(Guid invoice, DateOnly? date = null, bool dated = true, InvoiceLineCostType cost = InvoiceLineCostType.Fee)
        {
            var l = new Entity("sprk_billingevent", Guid.NewGuid())
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice),
                [InvoiceTotalsService.LineCostType] = new OptionSetValue((int)cost),
            };
            if (dated)
            {
                l[InvoiceSubmissionPersister.LineEventDate] = (date ?? InForceDay).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            }

            H.Seed(l);
            return l.Id;
        }

        public Guid Signal(Guid policyId, Guid invoice, Guid? line = null)
        {
            var s = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policyId),
                ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
                ["sprk_policyversion"] = new EntityReference("sprk_policyversion", Guid.NewGuid()),
            };
            if (line is { } l) s["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", l);
            else s["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice);
            H.Seed(s);
            return s.Id;
        }

        public int StatusOf(Guid signal) => H.Row("sprk_signal", signal).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value;

        public IReadOnlyList<Entity> RowsOf(string table) => H.Rows.GetValueOrDefault(table) ?? new List<Entity>();

        public IReadOnlyList<RecordOwnershipContext> OwnerContexts =>
            H.Ownership.Invocations.Where(i => i.Method.Name == nameof(IRecordOwnershipResolver.ResolveOwnerAsync))
                .Select(i => (RecordOwnershipContext)i.Arguments[0]).ToList();

        /// <summary>A Procedure; Billing-area unless <paramref name="area"/> says otherwise (0 leaves the Area empty); <paramref name="state"/> sets statecode when given (1 = deactivated in Dataverse).</summary>
        public Guid Procedure(int status = ProcedureEntity.StatusActive, int area = RuleDomains.PolicyDomainBilling, int? state = null)
        {
            var p = new Entity("sprk_procedure", Guid.NewGuid())
            {
                ["sprk_name"] = "Acme OCG",
                ["sprk_status"] = new OptionSetValue(status),
            };
            if (area != 0)
            {
                p[ProcedureEntity.AreaColumn] = new OptionSetValue(area);
            }

            if (state is { } statecode)
            {
                p["statecode"] = new OptionSetValue(statecode);
            }

            H.Seed(p);
            return p.Id;
        }

        public Guid ProcedureVersion(Guid procedureId, int status)
        {
            var v = new Entity("sprk_procedureversion", Guid.NewGuid())
            {
                ["sprk_procedure"] = new EntityReference("sprk_procedure", procedureId),
                ["sprk_status"] = new OptionSetValue(status),
            };
            H.Seed(v);
            return v.Id;
        }

        /// <summary>An in-force rule in a published Billing set of the given scope (client default unless told).</summary>
        public Guid Rule(string code = "BILL-A", int scope = (int)RuleSetScope.ClientDefault, string type = "DailyHoursCap", string? body = null,
            bool withMatterLookup = false)
        {
            var set = H.RuleSet(RuleSetService.RuleSetPublished, scope: scope);
            if (withMatterLookup)
            {
                H.Row("sprk_ruleset", set)[Matter] = new EntityReference(Matter, MatterId);
            }

            return body is null
                ? H.Rule(set, code, ProposalState.Accepted, current: true, type: type).Policy
                : H.Rule(set, code, ProposalState.Accepted, current: true, type: type, body: body).Policy;
        }

        /// <summary>A policy that belongs to NO rule set (<c>sprk_ruleset</c> empty), with its in-force version: the shape the system policy has
        /// in production (<c>Seed-BillingSystemPolicy.ps1</c>: "the system policy belongs to no rule set").</summary>
        public Guid RuleWithoutSet(string code, string type)
        {
            var policy = H.Rule(H.RuleSet(RuleSetService.RuleSetPublished), code, ProposalState.Accepted, current: true, type: type).Policy;
            H.Row("sprk_policy", policy).Attributes.Remove("sprk_ruleset");
            return policy;
        }

        public List<Entity> Rows => H.Rows.GetValueOrDefault(Assignment) ?? new List<Entity>();
    }

    private static void ShouldBeOwnedByTheResolver(Entity row, Guid team, Guid creator)
    {
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", team));
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id.Should().Be(creator, "the creator person is the caller");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Procedure-level: assign, pin, exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_WritesOneRow_OwnedByItsOwnQuestion_WithTheCoreRecordAsItsOneParent_AndTheCallerAsRequester()
    {
        var w = new World();
        var procedure = w.Procedure();

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, "  client asked  "), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Mode.Should().Be("FollowLatest");
        outcome.Value.Kind.Should().Be("Procedure");
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(outcome.Value.Id).And.Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_procedure", procedure));
        row.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeFollowLatest);
        row.GetAttributeValue<string>("sprk_reason").Should().Be("client asked");
        row.GetAttributeValue<string>("sprk_name").Should().Be("Acme OCG (Follow latest)");
        row.Contains("sprk_excludedpolicy").Should().BeFalse();
        ShouldBeOwnedByTheResolver(row, TeamId, CallerUserId);

        var context = w.OwnerContexts.Should().ContainSingle().Subject;
        context.ChildEntityLogicalName.Should().Be(Assignment, "the row's own question: a hard-deny table");
        context.Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the Procedure is a citation, not a parent");
        context.RequestedBy!.ObjectId.Should().Be(CallerOid);
        context.CallerObjectId.Should().Be(CallerOid);
        w.H.Transactions.Should().ContainSingle().Which.Should().ContainSingle().Which.Should().BeOfType<CreateRequest>();
    }

    [Theory]
    [InlineData("Excluded", ProcedureAssignmentEntity.ModeExcluded)]
    [InlineData("followlatest", ProcedureAssignmentEntity.ModeFollowLatest)]
    public async Task Assign_TakesTheModeByName(string mode, int stored)
    {
        var w = new World();

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), mode, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.Rows.Single().GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(stored);
    }

    [Fact]
    public async Task Pin_NamesAVersionOfThatProcedureThatHasBeenInForce()
    {
        var w = new World();
        var procedure = w.Procedure();
        var superseded = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusSuperseded);

        var pinned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Pinned", superseded, null), CallerOid, default);

        pinned.Kind.Should().Be(RuleSetOutcomeKind.Done);
        pinned.Value!.PinnedVersionId.Should().Be(superseded);
        var row = w.Rows.Single();
        row.GetAttributeValue<EntityReference>("sprk_pinnedversion").Id.Should().Be(superseded);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModePinned);
        w.OwnerContexts.Single().Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the pinned version is a citation");
    }

    [Fact]
    public async Task Pin_RefusesADraftVersion_AnotherProceduresVersion_NoVersion_OrAVersionOnAnUnpinnedMode_AndWritesNothing()
    {
        var w = new World();
        var procedure = w.Procedure();
        var draft = w.ProcedureVersion(procedure, 100000000);
        var otherProcedures = w.ProcedureVersion(w.Procedure(), ProcedureEntity.VersionStatusActive);
        var active = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusActive);

        foreach (var request in new ProcedureAssignmentRequest[]
                 {
                     new(procedure, "Pinned", draft, null),
                     new(procedure, "Pinned", otherProcedures, null),
                     new(procedure, "Pinned", Guid.NewGuid(), null),
                     new(procedure, "Pinned", null, null),
                     new(procedure, "FollowLatest", active, null),
                 })
        {
            var refused = await w.Service().AssignProcedureAsync(Matter, MatterId, request, CallerOid, default);
            refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid, $"{request}");
            refused.ErrorCode.Should().Be("assignment.invalid");
            refused.Errors.Should().ContainSingle(e => e.Field == "pinnedVersionId");
        }

        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_NoProcedureOrAnUnknownMode_Is400_AndWritesNothing()
    {
        var w = new World();

        var noProcedure = await w.Service().AssignProcedureAsync(Matter, MatterId, new(null, null, null, null), CallerOid, default);
        var badMode = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), "Sometimes", null, null), CallerOid, default);
        var longReason = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, new string('x', 2001)), CallerOid, default);

        noProcedure.Errors.Should().ContainSingle(e => e.Field == "procedureId");
        badMode.Errors.Should().ContainSingle(e => e.Field == "mode");
        longReason.Errors.Should().ContainSingle(e => e.Field == "reason");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b I-A1 must-fire: a second row for the same Procedure on the same record is 409 assignment.duplicate, whatever its mode, and writes nothing")]
    public async Task Assign_ASecondRowForTheSameProcedureOnTheSameRecord_Is409Duplicate()
    {
        var w = new World();
        var procedure = w.Procedure();
        (await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var second = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, "Excluded", null, null), CallerOid, default);

        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().ContainSingle();
        w.H.Transactions.Should().ContainSingle("the read refuses before any write");
    }

    [Fact(DisplayName = "089b I-A1 must-fire: a racing create the read cannot see collides on the derived id and is the same 409")]
    public async Task Assign_ARaceTheReadCannotSee_CollidesOnTheDerivedId_AndIsTheSame409()
    {
        var w = new World();
        var procedure = w.Procedure();
        // The racing row is not yet visible to the read (it carries no matter lookup here), but it holds the derived id.
        w.H.Seed(new Entity(Assignment, ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, ProcedureAssignmentEntity.ProcedureColumn, procedure)));

        var raced = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        raced.StatusCode.Should().Be(409);
        raced.ErrorCode.Should().Be("assignment.duplicate");
        w.H.TransactionFaults.Should().ContainSingle().Which.ErrorCode.Should().Be(unchecked((int)0x80040237));
        w.Rows.Should().ContainSingle("only the racing row");
    }

    [Fact(DisplayName = "089b I-A1 must-not-fire: the same Procedure on another record, or another Procedure on the same record, is its own row")]
    public async Task Assign_TheSameProcedureOnAnotherRecord_OrAnotherProcedure_IsNotADuplicate()
    {
        var w = new World();
        var procedure = w.Procedure();
        var otherMatter = Guid.NewGuid();
        var project = Guid.NewGuid();

        var first = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);
        var otherRecord = await w.Service().AssignProcedureAsync(Matter, otherMatter, new(procedure, null, null, null), CallerOid, default);
        var otherTable = await w.Service().AssignProcedureAsync("sprk_project", project, new(procedure, null, null, null), CallerOid, default);
        var otherProcedure = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        new[] { first, otherRecord, otherTable, otherProcedure }.Should().OnlyContain(o => o.Kind == RuleSetOutcomeKind.Done);
        w.Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.HaveCount(4);
        w.Rows.Single(r => r.Contains("sprk_project")).GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(project);
    }

    [Fact]
    public async Task Assign_ARetiredOrMissingProcedure_IsRefused_AndWritesNothing()
    {
        var w = new World();

        var retired = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(ProcedureEntity.StatusRetired), null, null, null), CallerOid, default);
        var missing = await w.Service().AssignProcedureAsync(Matter, MatterId, new(Guid.NewGuid(), null, null, null), CallerOid, default);

        retired.StatusCode.Should().Be(409);
        retired.ErrorCode.Should().Be("assignment.procedure_retired");
        missing.StatusCode.Should().Be(404);
        missing.ErrorCode.Should().Be("assignment.procedure_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    /// <summary>A Procedure Area that is not Billing (the Ontology area of O-24).</summary>
    private const int OtherArea = RuleDomains.PolicyDomainBilling + 1;

    [Theory(DisplayName = "EB-D-228 must-fire: a Procedure whose Area is not Billing (or empty) is 409 assignment.procedure_not_billing in every mode, and writes nothing")]
    [InlineData(OtherArea, null)]
    [InlineData(OtherArea, "Pinned")]
    [InlineData(OtherArea, "Excluded")]
    [InlineData(0, null)]
    public async Task Assign_AProcedureOfAnotherArea_Is409NotBilling_AndWritesNothing(int area, string? mode)
    {
        var w = new World();
        var procedure = w.Procedure(area: area);
        var version = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusActive);

        var refused = await w.Service().AssignProcedureAsync(
            Matter, MatterId, new(procedure, mode, mode == "Pinned" ? version : null, null), CallerOid, default);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(ProcedureAssignmentService.CodeProcedureNotBilling);
        refused.Detail.Should().Be(ProcedureAssignmentService.NotBillingDetail);
        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty("the area refuses before the owner question");
        w.Rows.Should().BeEmpty();
    }

    [Theory(DisplayName = "EB-D-228 must-not-fire: a Billing Procedure is assigned, also when its only versions are Drafts (ahead of publication) or it has none")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assign_ABillingProcedure_IsAccepted_EvenWithOnlyDraftVersions(bool draftOnly)
    {
        var w = new World();
        var procedure = w.Procedure();
        if (draftOnly)
        {
            w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusDraft);
            w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusDraft);
        }

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.Rows.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure);
    }

    [Theory(DisplayName = "EB-D-232 must-fire: a Procedure deactivated in Dataverse (Status Active, statecode 1) is 409 assignment.procedure_inactive in every mode, and writes nothing")]
    [InlineData(null)]
    [InlineData("Pinned")]
    [InlineData("Excluded")]
    public async Task Assign_ADeactivatedProcedure_Is409Inactive_AndWritesNothing(string? mode)
    {
        var w = new World();
        var procedure = w.Procedure(state: 1);
        var version = w.ProcedureVersion(procedure, ProcedureEntity.VersionStatusActive);

        var refused = await w.Service().AssignProcedureAsync(
            Matter, MatterId, new(procedure, mode, mode == "Pinned" ? version : null, null), CallerOid, default);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(ProcedureAssignmentService.CodeProcedureInactive);
        refused.Detail.Should().Be("A deactivated Procedure cannot be assigned, pinned or excluded.");
        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty("the state refuses before the owner question");
        w.Rows.Should().BeEmpty();
    }

    [Theory(DisplayName = "EB-D-232 must-not-fire: an Active Procedure (statecode 0, or none read) is assigned")]
    [InlineData(0)]
    [InlineData(null)]
    public async Task Assign_AnActiveProcedure_IsAccepted(int? state)
    {
        var w = new World();
        var procedure = w.Procedure(state: state);

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.Rows.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_procedure").Id.Should().Be(procedure);
    }

    [Fact(DisplayName = "EB-D-232: removing an existing assignment to a Procedure since deactivated still succeeds (remove does not go through the assign refusal)")]
    public async Task Remove_AnAssignmentToADeactivatedProcedure_StillSucceeds()
    {
        var w = new World();
        var procedure = w.Procedure();
        var assigned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);
        assigned.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Row("sprk_procedure", procedure)["statecode"] = new OptionSetValue(1);

        var removed = await w.Service().RemoveAsync(Matter, MatterId, assigned.Value!.Id, Token, default);

        removed.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_WhenTheOwnerIsRefused_WritesNothing()
    {
        var w = new World();
        w.H.OwnerAnswer = RecordOwnerResolution.Refused("owner.no_team", "no team");

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        outcome.OwnerRefusal!.RefusalCode.Should().Be("owner.no_team");
        w.H.Transactions.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_WhenTheTransactionGivesNoAnswer_SaysItMayHaveBeenSaved_WithTheId()
    {
        var w = new World();
        var procedure = w.Procedure();
        w.H.TransactionOutcomeUnknown = true;

        var outcome = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);

        outcome.StatusCode.Should().Be(500);
        outcome.ErrorCode.Should().Be("assignment.write_failed");
        outcome.Detail.Should().Contain("may or may not")
            .And.Contain(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_procedure", procedure).ToString());
    }

    [Fact]
    public async Task TheCoreTableComesFromTheRoute_AndAnythingElseIsABug()
    {
        var w = new World();
        var procedure = w.Procedure();

        var invoice = () => w.Service().AssignProcedureAsync("sprk_invoice", MatterId, new(procedure, null, null, null), CallerOid, default);
        var empty = () => w.Service().ExcludeRuleAsync(Matter, Guid.Empty, new(Guid.NewGuid(), "x", null), CallerOid, default);

        await invoice.Should().ThrowAsync<ArgumentException>();
        await empty.Should().ThrowAsync<ArgumentException>();
        w.H.Queries.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rule-level: exclude
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExcludeRule_WritesOneRowNamingTheRuleAndNoProcedure_ForOneFirm_WithTheReason()
    {
        var w = new World();
        var policy = w.Rule();
        var setsBefore = w.H.Rows["sprk_ruleset"].Count;

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "agreed with the client", FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Kind.Should().Be("RuleExcluded");
        outcome.Value.PolicyId.Should().Be(policy);
        outcome.Value.FirmId.Should().Be(FirmId);
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_excludedpolicy", policy));
        row.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(policy);
        row.Contains("sprk_procedure").Should().BeFalse("a rule-level row never names a Procedure");
        row.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeExcluded);
        row.GetAttributeValue<string>("sprk_reason").Should().Be("agreed with the client");
        ShouldBeOwnedByTheResolver(row, TeamId, CallerUserId);

        var context = w.OwnerContexts.Single();
        context.Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the rule and the firm are citations");
        context.RequestedBy!.ObjectId.Should().Be(CallerOid);
        w.H.Rows["sprk_ruleset"].Should().HaveCount(setsBefore, "an exclusion creates no record-scoped set");
    }

    [Fact]
    public async Task ExcludeRule_NeedsAReason_AndAFirmThatExists()
    {
        var w = new World();
        var policy = w.Rule();

        var noReason = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "  ", null), CallerOid, default);
        var noFirm = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", Guid.NewGuid()), CallerOid, default);
        var noPolicy = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(null, "x", null), CallerOid, default);

        noReason.Errors.Should().ContainSingle(e => e.Field == "reason");
        noFirm.Errors.Should().ContainSingle(e => e.Field == "firmId");
        noPolicy.Errors.Should().ContainSingle(e => e.Field == "policyId");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review 2 F1 must-fire: the firm existence check selects only sprk_organizationid (sprk_organization has no sprk_name), so an exclude naming a firm succeeds")]
    public async Task ExcludeRule_TheFirmCheck_SelectsOnlyTheKey_NeverSprkName()
    {
        var w = new World();
        var policy = w.Rule();

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", FirmId), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var firmQuery = w.H.Queries.Where(q => q.EntityName == "sprk_organization").Should().ContainSingle().Subject;
        firmQuery.ColumnSet.Columns.Should().Equal(new[] { "sprk_organizationid" });
        firmQuery.ColumnSet.Columns.Should().NotContain("sprk_name");
    }

    [Theory(DisplayName = "089b review 2 K2 must-fire: a Dataverse read that faults before the write is the stable 503 assignment.read_failed, and nothing is written")]
    [InlineData("assign")]
    [InlineData("exclude")]
    public async Task APreWriteRead_ThatFaults_Is503ReadFailed_AndWritesNothing(string route)
    {
        var w = new World();
        var procedure = w.Procedure();
        var policy = w.Rule();
        w.H.AfterQuery = _ => throw new TimeoutException("simulated Dataverse read fault");

        var outcome = route == "assign"
            ? await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default)
            : await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be("assignment.read_failed");
        w.H.Transactions.Should().BeEmpty();
        w.OwnerContexts.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1 must-fire: the system rule, which belongs to NO rule set (as in production), is 409 system_rule, not 404 rule_not_found")]
    public async Task ExcludeRule_RefusesTheSystemRule_AndWritesNothing()
    {
        var w = new World();
        var system = w.RuleWithoutSet(BillingRuleCatalogue.SystemPolicyCode, "SubmissionIntegrity");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(system, "why not", null), CallerOid, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.system_rule");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1: a SubmissionIntegrity rule inside a published set is still 409 system_rule (the version-type rule)")]
    public async Task ExcludeRule_ASubmissionIntegrityRuleInASet_IsStill409SystemRule()
    {
        var w = new World();
        var system = w.Rule("SYS-INTEGRITY", type: "SubmissionIntegrity", body: """{"type":"SubmissionIntegrity"}""");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(system, "why not", null), CallerOid, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.system_rule");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b review F1 must-not-fire: an ordinary rule with no rule set stays 404 rule_not_found")]
    public async Task ExcludeRule_AnOrdinaryRuleWithNoRuleSet_Is404RuleNotFound()
    {
        var w = new World();
        var ordinary = w.RuleWithoutSet("BILL-ORPHAN", "DailyHoursCap");

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(ordinary, "x", null), CallerOid, default);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    [Theory(DisplayName = "089b: a record-scoped set's rule (a record or firm-record scope, or any core lookup) is removed, not overridden: 404 rule_not_found")]
    [InlineData(100000002, false)] // sprk_scope Record (task 080)
    [InlineData(100000003, false)] // sprk_scope Firm record
    [InlineData((int)RuleSetScope.ClientDefault, true)]
    public async Task ExcludeRule_ARecordScopedSetsRule_Is404RuleNotFound(int scope, bool withMatterLookup)
    {
        var w = new World();
        var recordRule = w.Rule("BRR-1", scope: scope, withMatterLookup: withMatterLookup);

        var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(recordRule, "x", null), CallerOid, default);

        outcome.StatusCode.Should().Be(404);
        outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludeRule_AnUnknownOrSwitchedOffRule_OrAnOntologyRule_Is404()
    {
        var w = new World();
        var inactive = w.Rule("BILL-OFF");
        w.H.Row("sprk_policy", inactive)["statecode"] = new OptionSetValue(1);
        var ontologySet = w.H.RuleSet(RuleSetService.RuleSetPublished, domain: null);
        var (ontology, _) = w.H.Rule(ontologySet, "ONT-1", ProposalState.Accepted, current: true, domain: null);

        foreach (var policy in new[] { Guid.NewGuid(), inactive, ontology })
        {
            var outcome = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default);
            outcome.ErrorCode.Should().Be("assignment.rule_not_found");
        }

        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089b I-A1 must-fire: one Exclude per (record, rule), firm or not: a second is 409 and writes nothing")]
    public async Task ExcludeRule_ASecondExcludeOfTheSameRule_ForAnyFirm_Is409Duplicate()
    {
        var w = new World();
        var policy = w.Rule();
        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", FirmId), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var everyFirm = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "y", null), CallerOid, default);

        everyFirm.StatusCode.Should().Be(409);
        everyFirm.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Ownership through the REAL resolver (EB-D-168: a hard-deny table)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "089b must-fire: an ordinary record's override row is owned by the restricted-content owner team, never a business unit's default team, with the caller as creator")]
    public async Task RealResolver_AnOrdinaryRecordsRow_IsOwnedByTheRestrictedContentTeam()
    {
        var w = new World();
        var directory = OwnershipDirectory.Standard()
            .WithRecord(Matter, MatterId, OwnershipDirectory.CustomerChildBu, owningTeam: OwnershipDirectory.CustomerChildTeam);

        var outcome = await w.Service(directory.Resolver())
            .AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), OwnershipDirectory.CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var row = w.Rows.Single();
        ShouldBeOwnedByTheResolver(row, OwnershipDirectory.RestrictedContentTeam, OwnershipDirectory.CallerUserId);
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(OwnershipDirectory.CustomerChildTeam);
    }

    [Fact(DisplayName = "089b must-fire: a Secure record's override row is owned by its Secure team")]
    public async Task RealResolver_ASecureRecordsRow_IsOwnedByItsSecureTeam()
    {
        var w = new World();
        var policy = w.Rule();
        var directory = OwnershipDirectory.Standard()
            .WithRecord(Matter, MatterId, OwnershipDirectory.SecureBu, isSecure: true, owningTeam: OwnershipDirectory.SecureNamedTeam);

        var outcome = await w.Service(directory.Resolver())
            .ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), OwnershipDirectory.CallerOid, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        ShouldBeOwnedByTheResolver(w.Rows.Single(), OwnershipDirectory.SecureNamedTeam, OwnershipDirectory.CallerUserId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: change rule (model 6.3)
    // ═══════════════════════════════════════════════════════════════════════════

    private const string EditedBody = """{"type":"DailyHoursCap","maxHoursPerDay":12}""";
    private static readonly Guid OtherFirmId = Guid.NewGuid();

    private static RuleChangeRequest Change(Guid policy, Guid? firm = null, string? body = EditedBody, string? headline = null,
        string? consequence = null, string? reason = "agreed with the client") =>
        new(policy, reason, firm, body, null, headline, consequence);

    private static (Guid Set, Guid Policy) RecordRuleOf(Guid basePolicy, Guid? firm = null)
    {
        var set = ProcedureAssignmentService.RecordRuleSetIdFor(Matter, MatterId, firm);
        return (set, ProcedureAssignmentService.RecordRulePolicyIdFor(set, basePolicy));
    }

    private static Guid CurrentVersionOf(World w, Guid policy) =>
        w.H.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id;

    [Fact(DisplayName = "089c: a change writes one Exclude row and one Published record rule in a record-scoped set; set, policy and version each owned by their own question (parents [core], [set], [policy])")]
    public async Task Change_WritesTheExcludeRowAndAPublishedRecordRule_EachOwnedByItsOwnQuestion()
    {
        var w = new World();
        var policy = w.Rule();
        var baseVersion = CurrentVersionOf(w, policy);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (setId, ruleId) = RecordRuleOf(policy);

        // The Exclude row: the SAME derived id a plain Exclude of this rule has, so the two collide.
        var row = w.Rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, "sprk_excludedpolicy", policy)).And.Be(outcome.Value!.Override.Id);
        row.GetAttributeValue<OptionSetValue>("sprk_mode").Value.Should().Be(ProcedureAssignmentEntity.ModeExcluded);
        row.GetAttributeValue<EntityReference>("sprk_excludedpolicy").Id.Should().Be(policy);
        row.GetAttributeValue<string>("sprk_name").Should().Be("Rule BILL-A (Changed rule)");
        outcome.Value.Override.Kind.Should().Be("RuleChanged");

        // The record-scoped set.
        var set = w.H.Row("sprk_ruleset", setId);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordRuleSet.ScopeRecord);
        set.GetAttributeValue<EntityReference>(Matter).Id.Should().Be(MatterId);
        set.Contains("sprk_firm").Should().BeFalse();
        set.Contains("sprk_procedure").Should().BeFalse();
        set.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        set.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);

        // The record rule: on, its one version in force, Published, based on the rule's version in force.
        var rule = w.H.Row("sprk_policy", ruleId);
        var code = rule.GetAttributeValue<string>("sprk_policycode");
        code.Should().Be("BRR-" + ruleId.ToString("N")).And.HaveLength(36);
        code[4..].Should().MatchRegex("^[0-9a-f]{32}$");
        rule.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        rule.GetAttributeValue<EntityReference>("sprk_ruleset").Id.Should().Be(setId);
        var versionId = RuleSetService.VersionIdFor(ruleId, 1);
        rule.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(versionId);
        var version = w.H.Row("sprk_policyversion", versionId);
        version.GetAttributeValue<string>("sprk_rulebody").Should().Be(EditedBody);
        version.GetAttributeValue<string>("sprk_shortheadline").Should().Be("Daily hours cap", "an unedited member is the base version's");
        version.GetAttributeValue<EntityReference>("sprk_basedonversion").Id.Should().Be(baseVersion);
        version.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Accepted);
        version.GetAttributeValue<OptionSetValue>("sprk_publishstate").Value.Should().Be(RuleSetService.PublishStatePublished);
        outcome.Value.RecordRule.Should().Be(new RecordRuleView(setId, ruleId, code, versionId, 1, baseVersion, "Record", null));

        // Each row's own question, with the caller as RequestedBy: set [core], policy [set], version [policy], Exclude row [core].
        var contexts = w.OwnerContexts;
        contexts.Select(c => c.ChildEntityLogicalName).Should().Equal("sprk_ruleset", "sprk_policy", Assignment, "sprk_policyversion");
        contexts[0].Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the firm is no ownership parent");
        contexts[1].Parents.Should().Equal(new[] { new RecordOwnershipParent("sprk_ruleset", setId) });
        contexts[2].Parents.Should().Equal(new[] { new RecordOwnershipParent(Matter, MatterId) }, "the rule is a citation");
        contexts[3].Parents.Should().Equal(new[] { new RecordOwnershipParent("sprk_policy", ruleId) }, "the base version is no ownership parent");
        contexts.Should().OnlyContain(c => c.RequestedBy!.ObjectId == CallerOid && c.CallerObjectId == CallerOid);
        foreach (var (table, id) in new[] { ("sprk_ruleset", setId), ("sprk_policy", ruleId), ("sprk_policyversion", versionId) })
        {
            ShouldBeOwnedByTheResolver(w.H.Row(table, id), TeamId, CallerUserId);
        }

        // Stage 3 is one transaction: the Exclude row, the version and the conditional switch-on.
        w.H.Transactions.Should().HaveCount(3);
        w.H.Transactions[2].Select(r => r.GetType()).Should().Equal(typeof(CreateRequest), typeof(CreateRequest), typeof(UpdateRequest));
        // The ORDER is the contract (ontology-r1, 084d-3 K-2): Dataverse stamps createdon per statement, so the Exclude row must be
        // created before the version for the resolver's createdon >= episode-start test to admit the version. A reorder to
        // [version, Exclude] would make the real read fault while the dry run (one rig stamp per write) still showed the change.
        w.H.Transactions[2].Select(r => r switch
        {
            CreateRequest c => c.Target.LogicalName,
            UpdateRequest u => u.Target.LogicalName,
            _ => r.GetType().Name,
        }).Should().Equal(Assignment, "sprk_policyversion", "sprk_policy");
        ((UpdateRequest)w.H.Transactions[2][2]).ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);

        // The base rule is untouched.
        w.H.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        CurrentVersionOf(w, policy).Should().Be(baseVersion);
    }

    [Fact(DisplayName = "089c: a change for one firm writes a Firm record set of that firm, and the Exclude row names it")]
    public async Task Change_ForAFirm_UsesAFirmRecordSet()
    {
        var w = new World();
        var policy = w.Rule();

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (setId, _) = RecordRuleOf(policy, FirmId);
        var set = w.H.Row("sprk_ruleset", setId);
        set.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be(RecordRuleSet.ScopeFirmRecord);
        set.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        w.Rows.Single().GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
        outcome.Value!.RecordRule.Scope.Should().Be("FirmRecord");
    }

    [Fact(DisplayName = "089c: a second change of the record reuses its set")]
    public async Task Change_ASecondChange_ReusesTheRecordsSet()
    {
        var w = new World();
        var first = w.Rule("BILL-A");
        var second = w.Rule("BILL-B");

        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(first), CallerOid, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(second), CallerOid, Token, default);

        again.Kind.Should().Be(RuleSetOutcomeKind.Done, again.Detail);
        var recordSets = w.RowsOf("sprk_ruleset").Where(RecordRuleSet.IsRecordScoped).ToList();
        recordSets.Should().ContainSingle().Which.Id.Should().Be(again.Value!.RecordRule.RuleSetId);
        w.RowsOf("sprk_policy").Count(p => p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == recordSets[0].Id).Should().Be(2);
        w.Rows.Should().HaveCount(2);
    }

    [Fact(DisplayName = "089c must-fire: when the last write fails, neither the Exclude row nor a rule in force is left, and a retry reuses the set and policy")]
    public async Task Change_WhenTheLastWriteFails_LeavesNeither_AndARetryReusesTheRows()
    {
        var w = new World();
        var policy = w.Rule();
        var (setId, ruleId) = RecordRuleOf(policy);
        w.H.FailTransactionRequest = r => r is CreateRequest { Target.LogicalName: "sprk_policyversion" };

        var failed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.StatusCode.Should().Be(500);
        failed.ErrorCode.Should().Be("assignment.write_failed");
        w.Rows.Should().BeEmpty("the Exclude row rolled back with the version");
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse("no record rule is in force");
        w.H.Row("sprk_policy", ruleId).Contains("sprk_currentversion").Should().BeFalse();
        w.RowsOf("sprk_policyversion").Should().NotContain(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == ruleId);
        w.H.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("the base rule still checks the record");

        w.H.FailTransactionRequest = null;
        var retried = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done, retried.Detail);
        w.RowsOf("sprk_ruleset").Count(s => s.Id == setId).Should().Be(1);
        w.RowsOf("sprk_policy").Count(p => p.Id == ruleId).Should().Be(1);
        retried.Value!.RecordRule.VersionNumber.Should().Be(1);
    }

    [Fact(DisplayName = "089c must-fire: a rule of one firm's set changed for another firm, or for every firm, is refused 400 and writes nothing")]
    public async Task Change_AFirmNamedRule_ForAnotherFirm_Is400()
    {
        var w = new World();
        w.H.Seed(new Entity("sprk_organization", OtherFirmId));
        var set = w.H.RuleSet(RuleSetService.RuleSetPublished, scope: (int)RuleSetScope.Firm, firm: FirmId);
        var policy = w.H.Rule(set, "BILL-F", ProposalState.Accepted, current: true).Policy;

        var other = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: OtherFirmId), CallerOid, Token, default);
        var all = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        foreach (var refused in new[] { other, all })
        {
            refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
            refused.ErrorCode.Should().Be("assignment.invalid");
            refused.Errors.Should().ContainSingle().Which.Field.Should().Be("firmId");
        }

        w.H.Transactions.Should().BeEmpty();
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, Token, default))
            .Kind.Should().Be(RuleSetOutcomeKind.Done, "its own firm is accepted (must not fire)");
    }

    [Fact(DisplayName = "089c must-fire: an invalid edit is refused by PolicyVersionValidator and writes nothing")]
    public async Task Change_AnInvalidEdit_IsRefusedByTheValidator()
    {
        var w = new World();
        var policy = w.Rule();

        var badBody = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: """{"type":"DailyHoursCap"}"""), CallerOid, Token, default);
        var badTemplate = await w.Service().ChangeRuleAsync(Matter, MatterId,
            new RuleChangeRequest(policy, "x", null, null, "{{line.nope}}", null, null), CallerOid, Token, default);

        badBody.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        badBody.Errors.Should().Contain(e => e.Field == "ruleBody");
        badTemplate.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        badTemplate.Errors.Should().Contain(e => e.Field == "messageTemplate");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Change_WithoutAReasonOrAnEditOrAValidConsequence_Is400()
    {
        var w = new World();
        var policy = w.Rule();

        var noReason = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, reason: " "), CallerOid, Token, default);
        var noEdit = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: null), CallerOid, Token, default);
        var badConsequence = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, consequence: "Maybe"), CallerOid, Token, default);

        noReason.Errors.Should().ContainSingle().Which.Field.Should().Be("reason");
        noEdit.Errors.Should().ContainSingle().Which.Field.Should().Be("edit");
        badConsequence.Errors.Should().ContainSingle().Which.Field.Should().Be("consequence");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: a plain Exclude and a change of the same rule are one row per (record, rule): either second one is 409")]
    public async Task Change_AndExclude_OfTheSameRule_Collide()
    {
        var w = new World();
        var excluded = w.Rule("BILL-A");
        var changed = w.Rule("BILL-B");

        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(excluded, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(changed), CallerOid, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var changeAfterExclude = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(excluded), CallerOid, Token, default);
        var excludeAfterChange = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(changed, "x", null), CallerOid, default);

        changeAfterExclude.ErrorCode.Should().Be("assignment.duplicate");
        excludeAfterChange.ErrorCode.Should().Be("assignment.duplicate");
        w.Rows.Should().HaveCount(2);
    }

    [Fact(DisplayName = "089c: a record rule, the system rule and a rule with no version in force cannot be changed")]
    public async Task Change_RefusesARecordRule_TheSystemRule_AndARuleNotInForce()
    {
        var w = new World();
        var recordRule = w.Rule("BILL-R", withMatterLookup: true);
        var system = w.RuleWithoutSet(BillingRuleCatalogue.SystemPolicyCode, "DailyHoursCap");
        var set = w.H.RuleSet(RuleSetService.RuleSetPublished);
        var drafted = w.H.Rule(set, "BILL-D", ProposalState.Proposed, current: false).Policy;

        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(recordRule), CallerOid, Token, default)).ErrorCode.Should().Be("assignment.rule_not_found");
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(system), CallerOid, Token, default)).ErrorCode.Should().Be("assignment.system_rule");
        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(drafted), CallerOid, Token, default)).ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c review F1 must-fire: a rule switched off by a retired Procedure (sprk_enabled=false, statecode 0, version kept) is 409 rule_not_in_force and no record rule is created")]
    public async Task Change_ARuleSwitchedOffByARetiredProcedure_Is409_AndCreatesNoRecordRule()
    {
        var w = new World();
        var policy = w.Rule();
        var row = w.H.Row("sprk_policy", policy);
        row["sprk_enabled"] = false; // what retiring a Procedure leaves: off, still active (statecode 0), current version kept
        row["statecode"] = new OptionSetValue(0);
        row.GetAttributeValue<EntityReference>("sprk_currentversion").Should().NotBeNull();
        var (setId, ruleId) = RecordRuleOf(policy);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
        w.RowsOf("sprk_ruleset").Should().NotContain(r => r.Id == setId);
        w.RowsOf("sprk_policy").Should().NotContain(r => r.Id == ruleId);
    }

    [Theory(DisplayName = "089c review F1 must-fire: a rule whose set is not Published is 409 rule_not_in_force and writes nothing; an enabled rule of a Published set is changed (must not fire)")]
    [InlineData(RuleSetService.RuleSetDraft, false)]
    [InlineData(RuleSetService.RuleSetPublished, true)]
    public async Task Change_ARuleOfASetThatIsNotPublished_Is409(int setStatus, bool changes)
    {
        var w = new World();
        var set = w.H.RuleSet(setStatus);
        var policy = w.H.Rule(set, "BILL-S", ProposalState.Accepted, current: true).Policy;

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        if (changes)
        {
            outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
            return;
        }

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    /// <summary>Makes the Procedure the owner of the set the rule belongs to (a Procedure-owned set), as a published Procedure's rules are.</summary>
    private static void OwnedBy(World w, Guid policy, Guid procedure) =>
        w.H.Row("sprk_ruleset", w.H.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_ruleset").Id)["sprk_procedure"] =
            new EntityReference("sprk_procedure", procedure);

    [Theory(DisplayName = "089c pass 2 F1 must-fire: a rule of a RETIRED Procedure's Published set is 409 rule_not_in_force even while the rule is still enabled (a failed later retire chunk) and writes nothing; an active Procedure's rule is changed (must not fire)")]
    [InlineData(ProcedureEntity.StatusRetired, false)]
    [InlineData(ProcedureEntity.StatusActive, true)]
    public async Task Change_ARuleOfARetiredProcedure_Is409_WhateverTheRuleFlagsSay(int procedureStatus, bool changes)
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure(procedureStatus));
        w.H.Row("sprk_policy", policy).GetAttributeValue<bool?>("sprk_enabled").Should().BeTrue("the partly retired shape: the Procedure is retired, the rule is still on");

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        if (changes)
        {
            outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
            return;
        }

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    [Theory(DisplayName = "EB-D-232 must-fire: a rule of a Procedure deactivated in Dataverse (Status Active, statecode 1) is 409 rule_not_in_force, as a Retired one's is, and writes nothing; statecode 0 (or none) is changed (must not fire)")]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(null, true)]
    public async Task Change_ARuleOfADeactivatedProcedure_Is409RuleNotInForce(int? state, bool changes)
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure(state: state));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        if (changes)
        {
            outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
            return;
        }

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.rule_not_in_force");
        w.H.Transactions.Should().BeEmpty();
        w.H.Writes.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "EB-D-232 must-fire: the owning-Procedure read of a rule change selects statecode (without it a deactivated owner reads as active)")]
    public async Task Change_TheOwningProcedureRead_SelectsStatecode()
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure(state: 1));

        await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        w.H.Queries.Single(q => q.EntityName == "sprk_procedure").ColumnSet.Columns.Should().Contain("statecode");
    }

    [Fact(DisplayName = "EB-D-232 (scope): Exclude still accepts a rule of a deactivated Procedure, as it does a retired one's")]
    public async Task Exclude_StillAcceptsARuleOfADeactivatedProcedure()
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure(state: 1));

        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact(DisplayName = "089c pass 2 F1: a rule of a set with no Procedure keeps its behaviour (changed), and the Procedure is not read")]
    public async Task Change_ARuleOfASetWithNoProcedure_IsChanged_AndReadsNoProcedure()
    {
        var w = new World();
        var policy = w.Rule();
        var procedureReads = 0;
        var inner = w.H.AfterQuery;
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_procedure") procedureReads++;
            inner?.Invoke(q);
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        procedureReads.Should().Be(0);
    }

    [Fact(DisplayName = "089c pass 2 F1: a fault reading the owning Procedure is the stable 503 assignment.read_failed and nothing is written")]
    public async Task Change_WhenTheProcedureReadFaults_Is503()
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure());
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_procedure") throw new HttpRequestException("simulated fault");
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be("assignment.read_failed");
        w.H.Transactions.Should().BeEmpty();
        w.Rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c pass 2 F1 (scope): Exclude still accepts a rule of a retired Procedure")]
    public async Task Exclude_StillAcceptsARuleOfARetiredProcedure()
    {
        var w = new World();
        var policy = w.Rule();
        OwnedBy(w, policy, w.Procedure(ProcedureEntity.StatusRetired));

        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(policy, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact(DisplayName = "089c review F1 (scope): Exclude still accepts a rule that is switched off or in an unpublished set")]
    public async Task Exclude_StillAcceptsARuleThatIsNotInForce()
    {
        var w = new World();
        var off = w.Rule("BILL-OFF");
        w.H.Row("sprk_policy", off)["sprk_enabled"] = false;
        var draftSet = w.H.RuleSet(RuleSetService.RuleSetDraft);
        var unpublished = w.H.Rule(draftSet, "BILL-D", ProposalState.Accepted, current: true).Policy;

        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(off, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(unpublished, "x", null), CallerOid, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
    }

    [Fact(DisplayName = "089c must-fire: a row at the record rule's id that is not this record's rule of this base rule is refused 409, never reused")]
    public async Task Change_WhenTheDerivedPolicyIsAnotherRulesRow_Is409()
    {
        var w = new World();
        var policy = w.Rule("BILL-A");
        var other = w.Rule("BILL-B");
        var (setId, ruleId) = RecordRuleOf(policy);

        // A row already at the record rule's id, in the right set, whose version is based on ANOTHER rule's version.
        w.H.Seed(new Entity("sprk_ruleset", setId)
        {
            ["sprk_scope"] = new OptionSetValue(RecordRuleSet.ScopeRecord),
            [Matter] = new EntityReference(Matter, MatterId),
            ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        });
        w.H.Seed(new Entity("sprk_policy", ruleId)
        {
            ["sprk_ruleset"] = new EntityReference("sprk_ruleset", setId),
            ["sprk_policycode"] = ProcedureAssignmentService.RecordRuleCode(ruleId),
            ["sprk_enabled"] = false,
            ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        });
        w.H.Seed(new Entity("sprk_policyversion", Guid.NewGuid())
        {
            ["sprk_policy"] = new EntityReference("sprk_policy", ruleId),
            ["sprk_versionnumber"] = 1,
            ["sprk_basedonversion"] = new EntityReference("sprk_policyversion", CurrentVersionOf(w, other)),
        });

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be("assignment.record_rule_conflict");
        w.Rows.Should().BeEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
    }

    [Fact(DisplayName = "089c: a read fault before the write is a stable 503 assignment.read_failed, and nothing is written")]
    public async Task Change_WhenAReadFaults_Is503()
    {
        var w = new World();
        var policy = w.Rule();
        w.H.AfterQuery = q =>
        {
            // The record-scoped set check's read (RecordSetColumns), not the base rule's set read (which names sprk_visibletofirms).
            if (q.EntityName == "sprk_ruleset" && q.ColumnSet.Columns.Contains("sprk_procedure") && !q.ColumnSet.Columns.Contains("sprk_visibletofirms"))
                throw new HttpRequestException("simulated fault");
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be("assignment.read_failed");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: the legacy rule-set service answers a record-scoped set as not found on its read, list, edit and publish")]
    public async Task RuleSetService_RefusesARecordScopedSet()
    {
        var w = new World();
        var policy = w.Rule();
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        var (setId, ruleId) = (changed.Value!.RecordRule.RuleSetId, changed.Value.RecordRule.PolicyId);
        var ruleSets = w.H.Service();

        (await ruleSets.GetAsync(setId, default)).StatusCode.Should().Be(404);
        (await ruleSets.ListAsync(default)).Value!.Select(s => s.Id).Should().NotContain(setId).And.NotBeEmpty();
        (await ruleSets.EditAsync(setId, ruleId, new ProposalEdit(null, null, null, "x", null, null, null, CallerOid), default)).StatusCode.Should().Be(404);
        (await ruleSets.PublishAsync(setId, new DateOnly(2027, 1, 1), default)).StatusCode.Should().Be(404);
        (await ruleSets.SetVisibilityAsync(setId, false, default)).StatusCode.Should().Be(404);
        w.H.Row("sprk_policyversion", changed.Value.RecordRule.VersionId).GetAttributeValue<string>("sprk_shortheadline").Should().Be("Daily hours cap");
    }

    [Theory(DisplayName = "089c (089a K1): a rule set has one home, checked before every rule-set create")]
    [InlineData(null, false, false, null, true)]                                    // legacy
    [InlineData(null, false, true, null, true)]                                     // a Procedure's set
    [InlineData(RecordRuleSet.ScopeRecord, false, false, "sprk_matter", true)]      // record
    [InlineData(RecordRuleSet.ScopeFirmRecord, true, false, "sprk_project", true)]  // firm record
    [InlineData(RecordRuleSet.ScopeRecord, false, true, "sprk_matter", false)]      // a Procedure and a record
    [InlineData(null, false, false, "sprk_matter", false)]                          // a core lookup without the scope
    [InlineData(RecordRuleSet.ScopeRecord, false, false, null, false)]              // the scope without a core lookup
    [InlineData(RecordRuleSet.ScopeRecord, true, false, "sprk_matter", false)]      // Record with a firm
    [InlineData(RecordRuleSet.ScopeFirmRecord, false, false, "sprk_matter", false)] // Firm record without one
    public void RecordRuleSet_RequireOneHome(int? scope, bool firm, bool procedure, string? core, bool accepted)
    {
        var set = new Entity("sprk_ruleset", Guid.NewGuid());
        if (scope is { } s) set["sprk_scope"] = new OptionSetValue(s);
        if (firm) set["sprk_firm"] = new EntityReference("sprk_organization", FirmId);
        if (procedure) set["sprk_procedure"] = new EntityReference("sprk_procedure", Guid.NewGuid());
        if (core is not null) set[core] = new EntityReference(core, Guid.NewGuid());

        var act = () => RecordRuleSet.RequireOneHome(set);

        if (accepted) act.Should().NotThrow();
        else act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordRuleSet_RequireOneHome_RefusesTwoCoreLookups()
    {
        var set = new Entity("sprk_ruleset", Guid.NewGuid())
        {
            ["sprk_scope"] = new OptionSetValue(RecordRuleSet.ScopeRecord),
            ["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
            ["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid()),
        };

        FluentActions.Invoking(() => RecordRuleSet.RequireOneHome(set)).Should().Throw<InvalidOperationException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: remove, with preview
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A changed rule with open findings of its record rule on three In Review invoices, two of which the caller can Read.</summary>
    private static async Task<(World W, Guid Override, Guid RecordRule, Guid BasePolicy, Guid BaseSignal)> ChangedWithFindingsAsync()
    {
        var w = new World();
        var policy = w.Rule();
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        var ruleId = changed.Value!.RecordRule.PolicyId;
        var (a, b, hidden) = (w.Invoice(), w.Invoice(), w.Invoice());
        w.Readable.UnionWith(new[] { a, b });
        foreach (var invoice in new[] { a, b, hidden })
        {
            w.Signal(ruleId, invoice);
        }

        var baseSignal = w.Signal(policy, a);
        w.SelectedPolicies.Clear(); // the change's own selection (089e: the base rule's findings) is setup here, not the act under test
        return (w, changed.Value.Override.Id, ruleId, policy, baseSignal);
    }

    [Fact(DisplayName = "089c: the removal preview of a changed rule shows the warning and the caller-scoped count (3 affected, 2 readable), and writes nothing")]
    public async Task Preview_AChangedRule_ShowsTheWarningAndTheCallerScopedCount()
    {
        var (w, overrideId, ruleId, _, _) = await ChangedWithFindingsAsync();
        var writes = w.H.Writes.Count;
        var transactions = w.H.Transactions.Count;

        var outcome = await w.Service().PreviewRemovalAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Should().Be(new OverrideRemovalPreview(overrideId, "RuleChanged", true,
            "Removing this override will close the rule it changed and remove pending decisions on active invoices.",
            "2 open invoices you can see are affected.", 2));
        w.SelectedPolicies.Should().Equal(new[] { ruleId }, "the preview selects through the close's own method, for the record rule only");
        w.H.Writes.Should().HaveCount(writes);
        w.H.Transactions.Should().HaveCount(transactions);
        w.ClosedPolicies.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c must-fire: when the rights read faults, the preview shows the warning with no count")]
    public async Task Preview_WhenTheRightsReadFaults_ShowsNoCount()
    {
        var (w, overrideId, _, _, _) = await ChangedWithFindingsAsync();
        w.RightsFault = true;

        var outcome = await w.Service().PreviewRemovalAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Value!.Warning.Should().NotBeNullOrEmpty();
        outcome.Value.AffectedText.Should().BeNull();
        outcome.Value.VisibleOpenInvoices.Should().BeNull();
    }

    [Fact(DisplayName = "089c: a plain Exclude's or a Procedure row's preview has no warning and no count")]
    public async Task Preview_APlainRow_HasNoWarning()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);
        var assigned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(w.Procedure(), null, null, null), CallerOid, default);

        var plain = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var procedure = await w.Service().PreviewRemovalAsync(Matter, MatterId, assigned.Value!.Id, Token, default);

        plain.Value.Should().Be(new OverrideRemovalPreview(excluded.Value.Id, "RuleExcluded", false, null, null, null));
        procedure.Value.Should().Be(new OverrideRemovalPreview(assigned.Value.Id, "Procedure", false, null, null, null));
        w.SelectedPolicies.Should().BeEmpty();
    }

    [Fact(DisplayName = "089c: a plain removal deletes the row and closes nothing")]
    public async Task Remove_APlainExclude_DeletesTheRow_AndClosesNothing()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);

        outcome.Value.Should().Be(new OverrideRemoved(excluded.Value.Id, "RuleExcluded", false, true, null, null, null));
        w.Rows.Should().BeEmpty("deleted, not deactivated: I-A1's duplicate check ignores state");
        w.H.Transactions.Last().Should().ContainSingle().Which.Should().BeOfType<DeleteRequest>();
        w.ClosedPolicies.Should().BeEmpty();
        (await w.Service().ExcludeRuleAsync(Matter, MatterId, new(excluded.Value.PolicyId, "x", null), CallerOid, default))
            .Kind.Should().Be(RuleSetOutcomeKind.Done, "the subject can be overridden again");
    }

    [Fact(DisplayName = "089c: a changed rule's removal deletes the row, switches the record rule Off in the same transaction, and closes its open findings through 081's shared close")]
    public async Task Remove_AChangedRule_SwitchesTheRecordRuleOff_AndClosesItsFindings()
    {
        var (w, overrideId, ruleId, basePolicy, baseSignal) = await ChangedWithFindingsAsync();

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Value.Should().Be(new OverrideRemoved(overrideId, "RuleChanged", true, true, "2 open invoices you can see are affected.", 2, null));
        w.Rows.Should().BeEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        w.H.Transactions.Last().Select(r => r.GetType()).Should().Equal(typeof(DeleteRequest), typeof(UpdateRequest));
        w.ClosedPolicies.Should().Equal(ruleId);
        w.RowsOf("sprk_signal").Where(s => s.GetAttributeValue<EntityReference>("sprk_policy").Id == ruleId)
            .Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusResolved);
        w.H.Row("sprk_signal", baseSignal).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value
            .Should().Be(SignalWriter.SignalStatusOpen, "the base rule's findings are not this removal's");
        w.H.Row("sprk_policy", basePolicy).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
    }

    [Fact(DisplayName = "089c: when the close fails after the commit, the removal is still done, the rule is Off, and the result says the close is incomplete")]
    public async Task Remove_WhenTheCloseFails_IsStillDone()
    {
        var (w, overrideId, ruleId, _, _) = await ChangedWithFindingsAsync();
        w.CloseFault = new InvalidOperationException("simulated close fault");

        var outcome = await w.Service().RemoveAsync(Matter, MatterId, overrideId, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.RuleSwitchedOff.Should().BeTrue();
        outcome.Value.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().NotBeNullOrEmpty();
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
    }

    [Fact(DisplayName = "089c: a removed record rule needed again reuses its policy row with a new version, never a second row")]
    public async Task ChangeAgain_AfterARemoval_ReusesThePolicyRow()
    {
        var w = new World();
        var policy = w.Rule();
        var first = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        (await w.Service().RemoveAsync(Matter, MatterId, first.Value!.Override.Id, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: CapBodyOf(14)), CallerOid, Token, default);

        again.Kind.Should().Be(RuleSetOutcomeKind.Done, again.Detail);
        var ruleId = first.Value.RecordRule.PolicyId;
        again.Value!.RecordRule.PolicyId.Should().Be(ruleId);
        again.Value.RecordRule.VersionNumber.Should().Be(2);
        w.RowsOf("sprk_policy").Count(p => p.Id == ruleId).Should().Be(1);
        w.H.Row("sprk_policy", ruleId).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        CurrentVersionOf(w, ruleId).Should().Be(RuleSetService.VersionIdFor(ruleId, 2));
    }

    private static string CapBodyOf(int hours) => $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""";

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089e: a change closes the base rule's open findings on the record's In Review invoices as Superseded (owner option (b))
    // ═══════════════════════════════════════════════════════════════════════════

    private static int ResolutionOf(World w, Guid signal) =>
        w.H.Row("sprk_signal", signal).GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value ?? 0;

    [Fact(DisplayName = "089e must-fire: a change closes the base rule's open findings on this record's In Review invoices (on the invoice or on a line) as Superseded, and counts only the invoices the caller can Read")]
    public async Task Change_ClosesTheBaseRulesFindingsOnThisRecordsInReviewInvoices_AsSuperseded()
    {
        var w = new World();
        var policy = w.Rule();
        var (a, b, hidden) = (w.Invoice(matter: MatterId), w.Invoice(matter: MatterId), w.Invoice(matter: MatterId));
        w.Readable.UnionWith(new[] { a, b });
        var onInvoice = w.Signal(policy, a);
        var onLine = w.Signal(policy, b, line: w.Line(b));
        var onHidden = w.Signal(policy, hidden);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        foreach (var s in new[] { onInvoice, onLine, onHidden })
        {
            w.StatusOf(s).Should().Be(SignalWriter.SignalStatusResolved, "every base finding on this record's In Review invoices closes, seen or not");
            ResolutionOf(w, s).Should().Be(SignalWriter.ResolutionSuperseded);
        }

        var close = w.Superseded.Should().ContainSingle().Subject;
        close.Code.Should().Be("BILL-A");
        close.Signals.Should().BeEquivalentTo(new[] { onInvoice, onLine, onHidden });
        w.SubjectSelections.Should().ContainSingle().Which.Policy.Should().Be(policy, "the base rule's findings come from the close's own selection");
        w.SelectedPolicies.Should().BeEmpty("the change never selects the base rule's findings org-wide (pass-1 K4)");
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().Be(2, "the hidden invoice is not counted");
        outcome.Value.AffectedText.Should().Be("2 open invoices you can see are affected.");
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseCompleteMessage, "pass-1 F2: the answer says what the close covered");
        outcome.Value.Message.Should().Contain("records directly under it", "082c C19: the close now covers the direct children");
    }

    [Fact(DisplayName = "089e must-not-fire: another record's invoice, another rule's finding, an invoice not In Review and an inactive invoice keep their findings open")]
    public async Task Change_LeavesOtherRecordsRulesAndNotInReviewInvoicesAlone()
    {
        var w = new World();
        var policy = w.Rule();
        var other = w.Rule(code: "BILL-B");
        var mine = w.Invoice(matter: MatterId);
        var otherRecord = w.Signal(policy, w.Invoice(matter: Guid.NewGuid()));
        var otherRule = w.Signal(other, mine);
        var approved = w.Signal(policy, w.Invoice(InvoiceStatusCodes.Approved, matter: MatterId));
        var inactiveInvoice = w.Invoice(matter: MatterId);
        w.H.Row("sprk_invoice", inactiveInvoice)["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateInactive);
        var inactive = w.Signal(policy, inactiveInvoice);
        var noInvoice = w.Signal(policy, Guid.NewGuid());

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        foreach (var s in new[] { otherRecord, otherRule, approved, inactive, noInvoice })
        {
            w.StatusOf(s).Should().Be(SignalWriter.SignalStatusOpen);
        }

        w.Superseded.Should().BeEmpty("nothing on this record's In Review invoices, so nothing is closed");
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().Be(0);
    }

    [Fact(DisplayName = "089e must-not-fire: a firm-scoped change closes only that firm's invoices' findings")]
    public async Task Change_ForOneFirm_ClosesOnlyThatFirmsFindings()
    {
        var w = new World();
        var policy = w.Rule();
        var thisFirm = w.Signal(policy, w.Invoice(matter: MatterId, firm: FirmId));
        var otherFirm = w.Signal(policy, w.Invoice(matter: MatterId, firm: OtherFirmId));
        var noFirm = w.Signal(policy, w.Invoice(matter: MatterId));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.StatusOf(thisFirm).Should().Be(SignalWriter.SignalStatusResolved);
        w.StatusOf(otherFirm).Should().Be(SignalWriter.SignalStatusOpen, "the base rule still checks the other firm's invoices");
        w.StatusOf(noFirm).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "089e: a write fault in the close leaves the change committed (record rule in force) and answers closeComplete=false with a message")]
    public async Task Change_WhenTheCloseFails_IsStillDone_AndSaysTheCloseIsIncomplete()
    {
        var w = new World();
        var policy = w.Rule();
        var invoice = w.Invoice(matter: MatterId);
        var finding = w.Signal(policy, invoice);
        w.Readable.Add(invoice); // pass-1 F3: a count of the TARGETED findings would answer 1; only closed findings count
        w.SupersedeWriteFault = true;

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseIncompleteMessage);
        outcome.Value.VisibleOpenInvoices.Should().Be(0, "nothing was closed, so no invoice is counted");
        w.Rows.Should().ContainSingle("the Exclude row is committed");
        w.H.Row("sprk_policy", outcome.Value.RecordRule.PolicyId).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "089e: a fault selecting the base rule's findings leaves the change committed and answers closeComplete=false")]
    public async Task Change_WhenTheSelectionFaults_IsStillDone_AndSaysTheCloseIsIncomplete()
    {
        var w = new World();
        var policy = w.Rule();
        w.Signal(policy, w.Invoice(matter: MatterId));
        w.SelectFaultFor = policy;

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseIncompleteMessage);
        w.Rows.Should().ContainSingle();
        w.Superseded.Should().BeEmpty();
    }

    [Fact(DisplayName = "089e K4: the change reads the record's In Review invoices, then their lines, then only the base rule's open Signals on those; no org-wide Signal read")]
    public async Task Change_ReadsOnlyTheRecordsInvoicesLinesAndTheirSignals()
    {
        var w = new World();
        var policy = w.Rule();
        var mine = w.Invoice(matter: MatterId);
        var line = w.Line(mine);
        var onLine = w.Signal(policy, mine, line: line);
        var elsewhere = w.Invoice(matter: Guid.NewGuid());
        var otherRecord = w.Signal(policy, elsewhere, line: w.Line(elsewhere));
        w.H.Queries.Clear();

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.StatusOf(onLine).Should().Be(SignalWriter.SignalStatusResolved);
        w.StatusOf(otherRecord).Should().Be(SignalWriter.SignalStatusOpen);
        var selection = w.SubjectSelections.Should().ContainSingle().Subject;
        selection.Invoices.Should().Equal(new[] { mine }, "only the record's In Review invoices");
        selection.Lines.Should().Equal(new[] { line }, "only those invoices' lines");
        w.SelectedPolicies.Should().BeEmpty();

        var signalQueries = w.H.Queries.Where(q => q.EntityName == "sprk_signal").ToList();
        signalQueries.Should().NotBeEmpty();
        signalQueries.Should().OnlyContain(q => q.Criteria.Conditions.Any(c =>
                c.Operator == ConditionOperator.In
                && (c.AttributeName == SignalClosureService.RegardingInvoiceColumn || c.AttributeName == SignalClosureService.RegardingInvoiceLineColumn)),
            "every Signal read is bounded by the record's invoices or their lines");
        var invoiceQueries = w.H.Queries.Where(q => q.EntityName == "sprk_invoice").ToList();
        invoiceQueries.Where(q => q.Criteria.Conditions.Any(c => c.AttributeName == Matter && c.Values.Contains(MatterId)))
            .Should().ContainSingle("one query for the record's In Review invoices, bounded by its core lookup");
        invoiceQueries.Should().OnlyContain(q => q.Criteria.Conditions.Any(c =>
                (c.AttributeName == Matter && c.Values.Contains(MatterId)) || (c.AttributeName == "sprk_invoiceid" && c.Values.Count > 0)),
            "no invoice read is unbounded (the rest are the caller-scoped count's, by id)");
        var lineQueries = w.H.Queries.Where(q => q.EntityName == "sprk_billingevent").ToList();
        lineQueries.Should().Contain(q => q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_invoice" && c.Operator == ConditionOperator.In),
            "the record's invoices' lines are read by invoice");
        lineQueries.Should().OnlyContain(q => q.Criteria.Conditions.Any(c =>
                c.Operator == ConditionOperator.In && (c.AttributeName == "sprk_invoice" || c.AttributeName == "sprk_billingeventid")),
            "no line read is unbounded (the rest are the caller-scoped count's, by id)");
    }

    [Fact(DisplayName = "089e K4: a record with no In Review invoice reads no Signal at all")]
    public async Task Change_OnARecordWithNoInReviewInvoice_ReadsNoSignal()
    {
        var w = new World();
        var policy = w.Rule();
        var elsewhere = w.Signal(policy, w.Invoice(matter: Guid.NewGuid()));
        w.H.Queries.Clear();

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.SubjectSelections.Should().BeEmpty();
        w.H.Queries.Should().NotContain(q => q.EntityName == "sprk_signal");
        w.StatusOf(elsewhere).Should().Be(SignalWriter.SignalStatusOpen);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 082c C19: the change's close selects through the chain (the records DIRECTLY under the record; nearer rows win; fail closed)
    // ═══════════════════════════════════════════════════════════════════════════

    private const string Project = "sprk_project";
    private const string WorkAssignmentTable = "sprk_workassignment";

    /// <summary>A work assignment filed by its typed lookup under <paramref name="parent"/> (a matter or project).</summary>
    private static Guid WorkAssignmentUnder(World w, string parentTable, Guid parent)
    {
        var id = Guid.NewGuid();
        w.H.Seed(new Entity(WorkAssignmentTable, id)
        {
            [parentTable == Matter ? "sprk_regardingmatter" : "sprk_regardingproject"] = new EntityReference(parentTable, parent),
        });
        return id;
    }

    /// <summary>A project filed by its BFF-managed pair under <paramref name="parent"/>.</summary>
    private static Guid ProjectUnder(World w, string parentTable, Guid parent)
    {
        var type = Guid.NewGuid();
        w.H.Seed(new Entity("sprk_recordtype_ref", type) { ["sprk_recordlogicalname"] = parentTable });
        var id = Guid.NewGuid();
        w.H.Seed(new Entity(Project, id)
        {
            ["sprk_regardingrecordid"] = parent.ToString(),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", type),
        });
        return id;
    }

    /// <summary>An invoice filed on exactly one core record, <paramref name="coreTable"/> <paramref name="core"/>.</summary>
    private static Guid InvoiceOn(World w, string coreTable, Guid core, Guid? firm = null, int status = InvoiceStatusCodes.InReview, DateOnly? date = null)
    {
        var column = InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"].Single(l => l.CoreEntity == coreTable).Column;
        var i = new Entity("sprk_invoice", Guid.NewGuid())
        {
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(0),
            [column] = new EntityReference(coreTable, core),
            [InvoiceAdjustmentService.InvoiceDateColumn] = (date ?? World.InForceDay).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
        if (firm is { } f) i["sprk_vendororg"] = new EntityReference("sprk_organization", f);
        w.H.Seed(i);
        return i.Id;
    }

    /// <summary>A record's own rule-level row (a plain Exclude, or a change's Exclude row) of <paramref name="policy"/>.</summary>
    private static void OwnRuleRow(World w, string table, Guid record, Guid policy, Guid? firm = null)
    {
        var row = new Entity(Assignment, Guid.NewGuid())
        {
            [table] = new EntityReference(table, record),
            ["sprk_excludedpolicy"] = new EntityReference("sprk_policy", policy),
            ["sprk_mode"] = new OptionSetValue(ProcedureAssignmentEntity.ModeExcluded),
        };
        if (firm is { } f) row["sprk_firm"] = new EntityReference("sprk_organization", f);
        w.H.Seed(row);
    }

    /// <summary>Makes a query that <paramref name="when"/> matches throw, keeping the world's own check.</summary>
    private static void FailQueriesWhen(World w, Func<QueryExpression, bool> when)
    {
        var before = w.H.AfterQuery;
        w.H.AfterQuery = q =>
        {
            before?.Invoke(q);
            if (when(q)) throw new InvalidOperationException("simulated Dataverse read fault");
        };
    }

    [Fact(DisplayName = "082c C19 must-fire: the change closes the base rule's findings on the record's own invoices (d0) and on the invoices of the records directly under it (d1: a work assignment by its lookup, a project by its pair)")]
    public async Task Change_ClosesTheBaseRulesFindings_OnTheRecordAndOnTheRecordsDirectlyUnderIt()
    {
        var w = new World();
        var policy = w.Rule();
        var own = w.Signal(policy, w.Invoice(matter: MatterId));
        var workAssignment = WorkAssignmentUnder(w, Matter, MatterId);
        var project = ProjectUnder(w, Matter, MatterId);
        var onWorkAssignment = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, workAssignment));
        var projectInvoice = InvoiceOn(w, Project, project);
        var onProjectLine = w.Signal(policy, projectInvoice, line: w.Line(projectInvoice));
        w.Readable.Add(projectInvoice);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        foreach (var s in new[] { own, onWorkAssignment, onProjectLine })
        {
            w.StatusOf(s).Should().Be(SignalWriter.SignalStatusResolved);
            ResolutionOf(w, s).Should().Be(SignalWriter.ResolutionSuperseded);
        }

        w.Superseded.Should().ContainSingle("one close over d0 and d1 together").Which.Signals.Should().BeEquivalentTo(new[] { own, onWorkAssignment, onProjectLine });
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().Be(1, "only the project's invoice is readable by the caller");
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseCompleteMessage);
    }

    [Fact(DisplayName = "082c C19 must-not-fire: a grandchild's invoice (d2: its chain does not reach the record, D-172) and a sibling's under another record keep their findings open")]
    public async Task Change_LeavesAGrandchildAndAnotherRecordsChildAlone()
    {
        var w = new World();
        var policy = w.Rule();
        var project = ProjectUnder(w, Matter, MatterId);
        var onChild = w.Signal(policy, InvoiceOn(w, Project, project));
        var grandchild = WorkAssignmentUnder(w, Project, project);
        var onGrandchild = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, grandchild));
        var otherMatter = Guid.NewGuid();
        w.H.Seed(new Entity(Matter, otherMatter));
        var sibling = WorkAssignmentUnder(w, Matter, otherMatter);
        var onSibling = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, sibling));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.StatusOf(onChild).Should().Be(SignalWriter.SignalStatusResolved);
        w.StatusOf(onGrandchild).Should().Be(SignalWriter.SignalStatusOpen, "a record under a child does not take the change (no second hop)");
        w.StatusOf(onSibling).Should().Be(SignalWriter.SignalStatusOpen, "another matter's work assignment is not under this record");
    }

    [Fact(DisplayName = "082c C19 nearer wins: a child with its own row of the rule (for every firm, or the invoice's firm) is not closed; a child whose own row is for another firm, or is Procedure-level only, is")]
    public async Task Change_RespectsANearerRowOnTheChild()
    {
        var w = new World();
        var policy = w.Rule();
        var procedure = w.Procedure();
        var version = w.ProcedureVersion(procedure, ProcedureEntity.StatusActive);
        var nearer = WorkAssignmentUnder(w, Matter, MatterId);
        var nearerForFirm = WorkAssignmentUnder(w, Matter, MatterId);
        var otherFirmRow = WorkAssignmentUnder(w, Matter, MatterId);
        var procedureLevel = WorkAssignmentUnder(w, Matter, MatterId);
        OwnRuleRow(w, WorkAssignmentTable, nearer, policy);
        OwnRuleRow(w, WorkAssignmentTable, nearerForFirm, policy, firm: FirmId);
        OwnRuleRow(w, WorkAssignmentTable, otherFirmRow, policy, firm: OtherFirmId);
        w.H.Seed(new Entity(Assignment, Guid.NewGuid())
        {
            [WorkAssignmentTable] = new EntityReference(WorkAssignmentTable, procedureLevel),
            ["sprk_procedure"] = new EntityReference("sprk_procedure", procedure),
            ["sprk_pinnedversion"] = new EntityReference("sprk_procedureversion", version),
            ["sprk_mode"] = new OptionSetValue(ProcedureAssignmentEntity.ModePinned),
        });
        var onNearer = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, nearer, firm: FirmId));
        var onNearerForFirm = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, nearerForFirm, firm: FirmId));
        var onOtherFirmRow = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, otherFirmRow, firm: FirmId));
        var onProcedureLevel = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, procedureLevel, firm: FirmId));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.StatusOf(onNearer).Should().Be(SignalWriter.SignalStatusOpen, "the child's own row decides the rule there (the nearer row wins)");
        w.StatusOf(onNearerForFirm).Should().Be(SignalWriter.SignalStatusOpen, "the child's own row for the invoice's firm decides it");
        w.StatusOf(onOtherFirmRow).Should().Be(SignalWriter.SignalStatusResolved, "a row for another firm does not decide this firm's invoice");
        w.StatusOf(onProcedureLevel).Should().Be(SignalWriter.SignalStatusResolved,
            "a Procedure-level row does not decide a rule-level override (the resolver groups rule-level rows by base rule)");
    }

    [Fact(DisplayName = "082c C19: a firm-scoped change closes only that firm's invoices on the children; an invoice on two core records (FR-05) is never closed")]
    public async Task Change_ForOneFirm_OnTheChildren_AndNotAnAmbiguousInvoice()
    {
        var w = new World();
        var policy = w.Rule();
        var child = WorkAssignmentUnder(w, Matter, MatterId);
        var thisFirm = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, child, firm: FirmId));
        var otherFirm = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, child, firm: OtherFirmId));
        var twoCores = InvoiceOn(w, WorkAssignmentTable, child, firm: FirmId);
        w.H.Row("sprk_invoice", twoCores)[Project] = new EntityReference(Project, Guid.NewGuid());
        var ambiguous = w.Signal(policy, twoCores);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, firm: FirmId), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.StatusOf(thisFirm).Should().Be(SignalWriter.SignalStatusResolved);
        w.StatusOf(otherFirm).Should().Be(SignalWriter.SignalStatusOpen);
        w.StatusOf(ambiguous).Should().Be(SignalWriter.SignalStatusOpen, "an invoice filed on two core records has no chain to judge it by");
    }

    [Theory(DisplayName = "082c C19 fix round 1 (ontology 3): a fault reading the records DIRECTLY under the record drops their close only; the record's own findings still close, closeComplete=false, and the message says the records under it stay open")]
    [InlineData("chain")]
    [InlineData("rows")]
    [InlineData("unwritten row")]
    [InlineData("lines of the children's invoices")]
    public async Task Change_WhenTheChildrenCannotBeRead_ClosesTheRecordsOwnOnly(string fault)
    {
        var w = new World();
        var policy = w.Rule();
        var ownInvoice = w.Invoice(matter: MatterId);
        var own = w.Signal(policy, ownInvoice);
        var child = WorkAssignmentUnder(w, Matter, MatterId);
        var childInvoice = InvoiceOn(w, WorkAssignmentTable, child);
        var onChild = w.Signal(policy, childInvoice);
        switch (fault)
        {
            case "chain":
                FailQueriesWhen(w, q => q.EntityName == WorkAssignmentTable && q.TopCount == 1);
                break;
            case "rows":
                FailQueriesWhen(w, q => q.EntityName == Assignment
                    && q.Criteria.Filters.Any(f => f.Conditions.Any(c => c.AttributeName == WorkAssignmentTable)));
                break;
            case "lines of the children's invoices":
                FailQueriesWhen(w, q => q.EntityName == "sprk_billingevent" && q.Criteria.Conditions.Any(c => c.Values.Contains(childInvoice)));
                break;
            default: // a row the BFF never writes on the child (a Procedure and a rule): the resolver's own fault
                w.H.Seed(new Entity(Assignment, Guid.NewGuid())
                {
                    [WorkAssignmentTable] = new EntityReference(WorkAssignmentTable, child),
                    ["sprk_procedure"] = new EntityReference("sprk_procedure", Guid.NewGuid()),
                    ["sprk_excludedpolicy"] = new EntityReference("sprk_policy", policy),
                    ["sprk_mode"] = new OptionSetValue(ProcedureAssignmentEntity.ModeExcluded),
                });
                break;
        }

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseChildrenOpenMessage);
        w.Superseded.Should().ContainSingle("the record's own findings close, in one close").Which.Signals.Should().Equal(new[] { own });
        w.StatusOf(own).Should().Be(SignalWriter.SignalStatusResolved);
        ResolutionOf(w, own).Should().Be(SignalWriter.ResolutionSuperseded);
        w.StatusOf(onChild).Should().Be(SignalWriter.SignalStatusOpen, "all or nothing within the children's side");
        w.H.Row("sprk_policy", outcome.Value.RecordRule.PolicyId).GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("the change is committed");
        w.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("could not be read"));
        w.Log.Entries.Should().OnlyContain(e => !e.HasException, "a log line carries the exception type name, never the exception");
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(child.ToString()) || e.Message.Contains(childInvoice.ToString()),
            "no id of a record under the record is logged");
    }

    [Theory(DisplayName = "082c C19 fix round 1 (ontology 3, must-not-fire): a fault reading the record's OWN invoices or their lines closes nothing, not even the children's findings")]
    [InlineData("invoices")]
    [InlineData("lines")]
    public async Task Change_WhenTheRecordsOwnInvoicesCannotBeRead_ClosesNothing(string fault)
    {
        var w = new World();
        var policy = w.Rule();
        var ownInvoice = w.Invoice(matter: MatterId);
        var own = w.Signal(policy, ownInvoice);
        var onChild = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, WorkAssignmentUnder(w, Matter, MatterId)));
        var ownColumn = InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"].Single(l => l.CoreEntity == Matter).Column;
        if (fault == "invoices")
        {
            FailQueriesWhen(w, q => q.EntityName == "sprk_invoice" && q.Criteria.Conditions.Any(c => c.AttributeName == ownColumn));
        }
        else
        {
            FailQueriesWhen(w, q => q.EntityName == "sprk_billingevent" && q.Criteria.Conditions.Any(c => c.Values.Contains(ownInvoice)));
        }

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseIncompleteMessage);
        w.Superseded.Should().BeEmpty("nothing is closed when the record's own side could not be read");
        w.StatusOf(own).Should().Be(SignalWriter.SignalStatusOpen);
        w.StatusOf(onChild).Should().Be(SignalWriter.SignalStatusOpen);
        w.Log.Entries.Should().OnlyContain(e => !e.HasException);
    }

    [Fact(DisplayName = "082c C19 fix round 1 (ontology 3): more records directly under the record than the bound drops the children's close only; the record's own findings close, and the bound is logged at Warning with the count")]
    public async Task Change_PastTheChildBound_ClosesTheRecordsOwnOnly()
    {
        var w = new World();
        var policy = w.Rule();
        var own = w.Signal(policy, w.Invoice(matter: MatterId));
        var children = Enumerable.Range(0, ProcedureAssignmentService.MaxChildRecords + 1).Select(_ => WorkAssignmentUnder(w, Matter, MatterId)).ToList();
        var onChild = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, children[0]));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeFalse();
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseChildrenOpenMessage);
        w.Superseded.Should().ContainSingle().Which.Signals.Should().Equal(new[] { own });
        w.StatusOf(own).Should().Be(SignalWriter.SignalStatusResolved);
        w.StatusOf(onChild).Should().Be(SignalWriter.SignalStatusOpen, "the records under it are not read in part");
        w.H.Queries.Should().NotContain(q => q.EntityName == WorkAssignmentTable && q.TopCount == 1, "no child's chain is read past the bound");
        var warning = w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain($"more than {ProcedureAssignmentService.MaxChildRecords} core records").And.Contain($"({ProcedureAssignmentService.MaxChildRecords + 1} counted");
        w.Log.Entries.Should().OnlyContain(e => !e.HasException);
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(children[0].ToString()), "no id of a record under the record is logged");
    }

    [Fact(DisplayName = "082c C19 fix round 1 (ontology 2, must-not-fire): a child pinned (Procedure-level) to a version whose rule is a DIFFERENT policy keeps that rule's open findings; only the changed rule's findings close")]
    public async Task Change_LeavesADifferentRulesFindingsOnAPinnedChildOpen()
    {
        var w = new World();
        var changed = w.Rule();
        var procedure = w.Procedure();
        var version = w.ProcedureVersion(procedure, ProcedureEntity.StatusActive);
        var pinnedRule = w.Rule(code: "BILL-PINNED");
        w.H.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<EntityReference>("sprk_policy").Id == pinnedRule)
            [RuleSetService.ProcedureVersionColumn] = new EntityReference("sprk_procedureversion", version);
        var pinned = WorkAssignmentUnder(w, Matter, MatterId);
        w.H.Seed(new Entity(Assignment, Guid.NewGuid())
        {
            [WorkAssignmentTable] = new EntityReference(WorkAssignmentTable, pinned),
            ["sprk_procedure"] = new EntityReference("sprk_procedure", procedure),
            ["sprk_pinnedversion"] = new EntityReference("sprk_procedureversion", version),
            ["sprk_mode"] = new OptionSetValue(ProcedureAssignmentEntity.ModePinned),
        });
        var invoice = InvoiceOn(w, WorkAssignmentTable, pinned, firm: FirmId);
        var onChangedRule = w.Signal(changed, invoice);
        var onPinnedRule = w.Signal(pinnedRule, invoice);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(changed), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.StatusOf(onChangedRule).Should().Be(SignalWriter.SignalStatusResolved, "the changed rule's finding closes: a Procedure-level pin does not decide a rule-level override");
        w.StatusOf(onPinnedRule).Should().Be(SignalWriter.SignalStatusOpen, "a different policy's finding is never the change's to close");
        w.Superseded.Should().ContainSingle().Which.Signals.Should().Equal(new[] { onChangedRule });
    }

    // ── 082c C19 fix round 1 (owner 2026-11-05, "the effective date wins"): a base finding closes only on an invoice whose every judged date is
    //    on or after the change's in-force day. The same table runs at d0 (the record's own invoice) and d1 (a record directly under it). ──

    private static readonly DateOnly Oct30 = new(2026, 10, 30);
    private static readonly DateOnly Nov4 = new(2026, 11, 4);
    private static readonly DateOnly Nov5 = new(2026, 11, 5);
    private static readonly DateOnly Nov6 = new(2026, 11, 6);
    private static readonly DateOnly Nov9 = new(2026, 11, 9);

    /// <summary>Parses a table cell: a date, or "-" for none.</summary>
    private static DateOnly? Day(string cell) => cell == "-" ? null : DateOnly.Parse(cell, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An In Review invoice of the record (<paramref name="side"/> d0) or of a work assignment directly under it (d1), with a finding on the
    /// invoice and one on each line. <paramref name="invoiceDate"/> "-" is an invoice with no date at all; a line cell "-" is a line with no work date.</summary>
    private static (Guid Invoice, List<Guid> Findings) DatedInvoice(World w, Guid policy, string side, string invoiceDate, string[] lines, string[]? adjustmentLines = null)
    {
        var invoice = side == "d0"
            ? w.Invoice(matter: MatterId, date: Day(invoiceDate) ?? Nov6)
            : InvoiceOn(w, WorkAssignmentTable, WorkAssignmentUnder(w, Matter, MatterId), date: Day(invoiceDate) ?? Nov6);
        if (Day(invoiceDate) is null)
        {
            w.H.Row("sprk_invoice", invoice).Attributes.Remove(InvoiceAdjustmentService.InvoiceDateColumn);
        }

        var findings = new List<Guid> { w.Signal(policy, invoice) };
        foreach (var cell in lines)
        {
            findings.Add(w.Signal(policy, invoice, line: w.Line(invoice, date: Day(cell), dated: cell != "-")));
        }

        foreach (var cell in adjustmentLines ?? [])
        {
            findings.Add(w.Signal(policy, invoice, line: w.Line(invoice, date: Day(cell), dated: cell != "-", cost: InvoiceLineCostType.Adjustment)));
        }

        return (invoice, findings);
    }

    // Closed: every judged date is on or after the in-force day (2026-11-05), the day itself included; a line with no work date is judged on the
    // invoice date (the evaluator's own rule); an adjustment line is not judged. Open: any earlier date, or no date at all.
    [Theory(DisplayName = "082c C19 fix round 1 (owner 2026-11-05): a base finding closes only when the resolver runs the record rule, not the base rule, on EVERY date it judges the invoice on (d0 and d1)")]
    [InlineData("d0", "2026-11-06", "2026-11-05,2026-11-09", true)]    // all lines on or after, the in-force day itself among them
    [InlineData("d1", "2026-11-06", "2026-11-05,2026-11-09", true)]
    [InlineData("d0", "2026-11-05", "2026-11-05", true)]               // exactly the in-force day, invoice and line
    [InlineData("d1", "2026-11-05", "2026-11-05", true)]
    [InlineData("d0", "2026-11-06", "2026-10-30", false)]              // an October line (the seed trips here)
    [InlineData("d1", "2026-11-06", "2026-10-30", false)]
    [InlineData("d0", "2026-11-06", "2026-11-09,2026-10-30", false)]   // a mixed-date invoice: one earlier line keeps ALL its findings open
    [InlineData("d1", "2026-11-06", "2026-11-09,2026-10-30", false)]
    [InlineData("d0", "2026-11-06", "2026-11-04", false)]              // the day before the in-force day
    [InlineData("d1", "2026-11-06", "2026-11-04", false)]
    [InlineData("d0", "2026-11-04", "2026-11-05", false)]              // the invoice itself is judged on its date
    [InlineData("d1", "2026-11-04", "2026-11-05", false)]
    [InlineData("d0", "2026-11-06", "-", true)]                        // no work date: judged on the invoice date, on or after
    [InlineData("d1", "2026-11-06", "-", true)]
    [InlineData("d0", "2026-10-30", "-", false)]                       // no work date on an October invoice: judged on October
    [InlineData("d1", "2026-10-30", "-", false)]
    [InlineData("d0", "-", "-", false)]                                // no date to judge by at all
    [InlineData("d1", "-", "-", false)]
    [InlineData("d0", "-", "2026-11-09", false)]                       // the invoice has no date, so its own date cannot be told
    [InlineData("d1", "-", "2026-11-09", false)]
    [InlineData("d0", "2026-11-06", "", true)]                         // no lines: judged on the invoice date alone
    [InlineData("d1", "2026-11-06", "", true)]
    [InlineData("d0", "2026-10-30", "", false)]
    [InlineData("d1", "2026-10-30", "", false)]
    public async Task Change_ClosesAnInvoiceOnlyWhenEveryJudgedDateIsOnOrAfterTheInForceDay(string side, string invoiceDate, string lineDates, bool closed)
    {
        var w = new World();
        var policy = w.Rule();
        var (_, findings) = DatedInvoice(w, policy, side, invoiceDate, lineDates.Length == 0 ? [] : lineDates.Split(','));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CloseComplete.Should().BeTrue("an invoice left open by its dates is not an incomplete close");
        outcome.Value.Message.Should().Be(ProcedureAssignmentService.ChangeCloseCompleteMessage);
        foreach (var finding in findings)
        {
            w.StatusOf(finding).Should().Be(closed ? SignalWriter.SignalStatusResolved : SignalWriter.SignalStatusOpen,
                closed ? "every date the resolver judges the invoice on runs the record rule" : "the base rule still runs on a date before the change took effect");
        }
    }

    [Fact(DisplayName = "082c C19 fix round 1: an adjustment line is not judged, so its early date keeps nothing open (d0 and d1)")]
    public async Task Change_DoesNotJudgeAnAdjustmentLinesDate()
    {
        var w = new World();
        var policy = w.Rule();
        var (_, ownFindings) = DatedInvoice(w, policy, "d0", "2026-11-06", ["2026-11-05"], adjustmentLines: ["2026-10-30"]);
        var (_, childFindings) = DatedInvoice(w, policy, "d1", "2026-11-06", ["2026-11-05"], adjustmentLines: ["2026-10-30"]);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        foreach (var finding in ownFindings.Concat(childFindings))
        {
            w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusResolved);
        }
    }

    [Fact(DisplayName = "082c C19 fix round 1 (uac-r2 b): a mixed-date invoice on a record under the record stays open beside a fully-dated one that closes; the answer counts and says only what the caller can see")]
    public async Task Change_OnTheChildren_ClosesTheFullyDatedInvoice_AndKeepsTheMixedDateOneOpen()
    {
        var w = new World();
        var policy = w.Rule();
        var (closing, closingFindings) = DatedInvoice(w, policy, "d1", "2026-11-06", ["2026-11-05", "2026-11-09"]);
        var (mixed, mixedFindings) = DatedInvoice(w, policy, "d1", "2026-11-06", ["2026-11-09", "2026-10-30"]);
        w.Readable.UnionWith(new[] { closing, mixed });

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        closingFindings.Should().OnlyContain(f => w.StatusOf(f) == SignalWriter.SignalStatusResolved);
        mixedFindings.Should().OnlyContain(f => w.StatusOf(f) == SignalWriter.SignalStatusOpen, "one earlier line keeps every finding of the invoice open");
        w.Superseded.Should().ContainSingle().Which.Signals.Should().BeEquivalentTo(closingFindings, "only findings superseded under the effective-date rule are targeted");
        outcome.Value.VisibleOpenInvoices.Should().Be(1, "the count is of the invoices the caller can read whose findings closed");
        outcome.Value.Message.Should().NotContain(closing.ToString()).And.NotContain(mixed.ToString());
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(closing.ToString()) || e.Message.Contains(mixed.ToString()), "no id of an invoice under the record is logged");
    }

    // The close agrees with the resolver on the day the version takes effect, whatever time of that UTC day it was written at: the version's
    // in-force day is the UTC day of the instant (ProcedureResolver.UtcDate), and the comparison is ProcedureResolution.InForceOn, the one
    // expression RecordRuleVersionOn selects by (ProcedureResolverTests: "082b record rules (C19 fix round 1)" runs the same instants through the resolver).
    [Theory(DisplayName = "082c C19 fix round 1: a line dated on the in-force UTC day closes at any time of that day, and one dated the day before stays open")]
    [InlineData("2026-11-05T00:00:00Z")]
    [InlineData("2026-11-05T14:30:00Z")]
    [InlineData("2026-11-05T23:59:59Z")]
    public async Task Change_AgreesWithTheResolverOnTheInForceDay_WhateverTheTimeOfDay(string instant)
    {
        var w = new World { Clock = new FixedClock(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)) };
        var policy = w.Rule();
        var (_, onTheDay) = DatedInvoice(w, policy, "d0", "2026-11-05", ["2026-11-05"]);
        var (_, theDayBefore) = DatedInvoice(w, policy, "d0", "2026-11-05", ["2026-11-04"]);

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        onTheDay.Should().OnlyContain(f => w.StatusOf(f) == SignalWriter.SignalStatusResolved);
        theDayBefore.Should().OnlyContain(f => w.StatusOf(f) == SignalWriter.SignalStatusOpen);
        var version = w.H.Row("sprk_policyversion", outcome.Value.RecordRule.VersionId);
        version.GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime,
            "the close dates by the very instant the version is written with");
    }

    [Fact(DisplayName = "082c C19: exactly the bound of records under the record is read and closed")]
    public async Task Change_AtTheChildBound_Closes()
    {
        var w = new World();
        var policy = w.Rule();
        var children = Enumerable.Range(0, ProcedureAssignmentService.MaxChildRecords).Select(_ => WorkAssignmentUnder(w, Matter, MatterId)).ToList();
        var onLast = w.Signal(policy, InvoiceOn(w, WorkAssignmentTable, children[^1]));

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Value!.CloseComplete.Should().BeTrue();
        w.StatusOf(onLast).Should().Be(SignalWriter.SignalStatusResolved);
    }

    [Fact(DisplayName = "082c C19 idempotent: a finding already resolved is not targeted, and the repeated change (409 duplicate) closes nothing more")]
    public async Task Change_Repeated_ClosesNothingMore()
    {
        var w = new World();
        var policy = w.Rule();
        var child = WorkAssignmentUnder(w, Matter, MatterId);
        var invoice = InvoiceOn(w, WorkAssignmentTable, child);
        var open = w.Signal(policy, invoice);
        var resolved = w.Signal(policy, invoice);
        w.H.Row("sprk_signal", resolved)["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);

        var first = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        first.Kind.Should().Be(RuleSetOutcomeKind.Done, first.Detail);
        again.ErrorCode.Should().Be("assignment.duplicate");
        w.Superseded.Should().ContainSingle().Which.Signals.Should().Equal(new[] { open }, "an already resolved finding is not targeted");
        w.StatusOf(open).Should().Be(SignalWriter.SignalStatusResolved);
        ResolutionOf(w, resolved).Should().Be(0, "the resolved finding was not written again");
    }

    [Fact(DisplayName = "089e K2: a stage-3 transaction that committed but gave no answer is read back: the change is Done and the close runs")]
    public async Task Change_WhenTheTransactionCommitsButGivesNoAnswer_IsDone_AndCloses()
    {
        var w = new World();
        var policy = w.Rule();
        var invoice = w.Invoice(matter: MatterId);
        var finding = w.Signal(policy, invoice);
        w.Readable.Add(invoice);
        w.H.TransactionCommitsButAnswersUnknown = requests => requests.Count == 3; // stage 3: the Exclude row, the version, the switch-on

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.Rows.Should().ContainSingle("the Exclude row was committed");
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusResolved, "the change committed, so its close runs");
        outcome.Value!.CloseComplete.Should().BeTrue();
        outcome.Value.VisibleOpenInvoices.Should().Be(1);
    }

    [Fact(DisplayName = "089e K2: a stage-3 transaction that gave no answer and did not commit stays 500, and nothing is closed")]
    public async Task Change_WhenTheTransactionGivesNoAnswerAndDidNotCommit_Is500()
    {
        var w = new World();
        var policy = w.Rule();
        var finding = w.Signal(policy, w.Invoice(matter: MatterId));
        w.H.TransactionGivesNoAnswer = requests => requests.Count == 3; // stage 3 only: stages 1 and 2 commit

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        w.H.Transactions.Should().Contain(t => t.Count == 3, "the change reached stage 3");
        outcome.StatusCode.Should().Be(500);
        outcome.ErrorCode.Should().Be("assignment.write_failed");
        w.Rows.Should().BeEmpty();
        w.Superseded.Should().BeEmpty();
        w.SubjectSelections.Should().BeEmpty();
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "089e fix round 2 (F2) must-fire: no answer from stage 3 while an Exclude row exists from a plain Exclude and the record rule's version row is absent is not committed: 500, nothing closed")]
    public async Task Change_WhenTheTransactionGivesNoAnswer_AndOnlyAPlainExcludeRowExists_Is500_AndClosesNothing()
    {
        var w = new World();
        var policy = w.Rule();
        var finding = w.Signal(policy, w.Invoice(matter: MatterId));
        Guid? versionId = null;
        w.H.TransactionGivesNoAnswer = requests =>
        {
            if (requests.Count != 3)
            {
                return false; // stages 1 and 2 commit
            }

            // Stage 3 applies nothing; meanwhile a plain Exclude of the same rule writes the row at the same derived id (I-A1).
            var exclude = ((Microsoft.Xrm.Sdk.Messages.CreateRequest)requests[0]).Target;
            versionId = ((Microsoft.Xrm.Sdk.Messages.CreateRequest)requests[1]).Target.Id;
            var plain = new Entity(exclude.LogicalName, exclude.Id);
            foreach (var attribute in exclude.Attributes)
            {
                plain[attribute.Key] = attribute.Value;
            }

            w.H.Seed(plain);
            return true;
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        w.Rows.Should().ContainSingle("the plain Exclude's row is there");
        versionId.Should().NotBeNull();
        w.RowsOf("sprk_policyversion").Should().NotContain(r => r.Id == versionId, "the record rule's version was never written");
        outcome.StatusCode.Should().Be(500, "an Exclude row alone does not prove that stage 3 committed");
        outcome.ErrorCode.Should().Be("assignment.write_failed");
        w.Superseded.Should().BeEmpty();
        w.SubjectSelections.Should().BeEmpty();
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
    }

    /// <summary>
    /// Fix round 3 (F2): stage 3 gives no answer and applies nothing, while a concurrent change of the same rule on the same record (same derived
    /// version id) writes the record rule's version row meanwhile; <paramref name="edit"/> shapes that row from the one this change would have written.
    /// </summary>
    private static async Task<(RuleSetOutcome<RuleChangedView> Outcome, World W, Guid Finding)> ChangeWithAConcurrentVersionRowAsync(Action<Entity> edit)
    {
        var w = new World();
        var policy = w.Rule();
        var finding = w.Signal(policy, w.Invoice(matter: MatterId));
        w.H.TransactionGivesNoAnswer = requests =>
        {
            if (requests.Count != 3)
            {
                return false; // stages 1 and 2 commit
            }

            var mine = ((Microsoft.Xrm.Sdk.Messages.CreateRequest)requests[1]).Target;
            var theirs = new Entity(mine.LogicalName, mine.Id);
            foreach (var attribute in mine.Attributes)
            {
                theirs[attribute.Key] = attribute.Value;
            }

            edit(theirs);
            w.H.Seed(theirs);
            return true;
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        return (outcome, w, finding);
    }

    [Fact(DisplayName = "089e fix round 3 (F2) must-fire: no answer from stage 3 while the version row at the derived id holds another change's body is not committed: 500, nothing closed")]
    public async Task Change_WhenTheTransactionGivesNoAnswer_AndTheVersionRowHoldsAnotherBody_Is500_AndClosesNothing()
    {
        var (outcome, w, finding) = await ChangeWithAConcurrentVersionRowAsync(v => v["sprk_rulebody"] = CapBodyOf(9));

        outcome.StatusCode.Should().Be(500, "the row at the derived id is another change's, not this one's");
        outcome.ErrorCode.Should().Be("assignment.write_failed");
        w.Superseded.Should().BeEmpty();
        w.SubjectSelections.Should().BeEmpty();
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "089e fix round 3 (F2) must-fire: another change's text or consequence at the derived id is not committed")]
    public async Task Change_WhenTheTransactionGivesNoAnswer_AndTheVersionRowHoldsOtherText_Is500()
    {
        foreach (var edit in new Action<Entity>[]
                 {
                     v => v["sprk_messagetemplate"] = "another template",
                     v => v["sprk_shortheadline"] = "another headline",
                     v => v["sprk_consequence"] = new OptionSetValue(((OptionSetValue)v["sprk_consequence"]).Value + 1),
                     v => v["sprk_basedonversion"] = new EntityReference("sprk_policyversion", Guid.NewGuid()),
                 })
        {
            var (outcome, w, finding) = await ChangeWithAConcurrentVersionRowAsync(edit);

            outcome.StatusCode.Should().Be(500);
            w.Superseded.Should().BeEmpty();
            w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
        }
    }

    [Fact(DisplayName = "089e fix round 3 (F2): no answer from stage 3 while the version row at the derived id holds equal content is committed: Done and the close runs")]
    public async Task Change_WhenTheTransactionGivesNoAnswer_AndTheVersionRowHoldsEqualContent_IsDone_AndCloses()
    {
        var (outcome, w, finding) = await ChangeWithAConcurrentVersionRowAsync(_ => { });

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusResolved, "equal content is a truthful committed, so the close runs");
    }

    [Fact(DisplayName = "089e fix round 3 (K7): a re-change after a removal whose stage 3 gives no answer and does not commit is not committed, though an earlier version row exists")]
    public async Task ChangeAgain_AfterARemoval_WhenStageThreeGivesNoAnswer_AndDoesNotCommit_Is500()
    {
        var w = new World();
        var policy = w.Rule();
        var first = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);
        (await w.Service().RemoveAsync(Matter, MatterId, first.Value!.Override.Id, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var ruleId = first.Value.RecordRule.PolicyId;
        var finding = w.Signal(policy, w.Invoice(matter: MatterId));
        w.RowsOf("sprk_policyversion").Should().Contain(v => v.Id == RuleSetService.VersionIdFor(ruleId, 1), "the earlier version row exists");
        w.H.TransactionGivesNoAnswer = requests => requests.Count == 3;

        var again = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy, body: CapBodyOf(14)), CallerOid, Token, default);

        again.StatusCode.Should().Be(500);
        again.ErrorCode.Should().Be("assignment.write_failed");
        w.RowsOf("sprk_policyversion").Should().NotContain(v => v.Id == RuleSetService.VersionIdFor(ruleId, 2), "the new version was never written");
        w.Superseded.Should().BeEmpty();
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "089e fix round 2 (F2): the read-back after no answer is not cancelled by the request's token")]
    public async Task Change_WhenTheTransactionCommitsButGivesNoAnswer_AndTheRequestIsCancelled_StillReadsBack()
    {
        var w = new World();
        var policy = w.Rule();
        var invoice = w.Invoice(matter: MatterId);
        var finding = w.Signal(policy, invoice, line: w.Line(invoice));
        // ebilling-r1 084d-3 review round 1 (F3-1): a record directly under the matter with an In Review invoice, so the close selects the
        // children (ChildrensInReviewInvoicesAsync returns early with no child): the children's invoice read and the override read of the
        // child's chain (the change's own Exclude row on the matter is the override row that decides the child's invoice) both run.
        var child = WorkAssignmentUnder(w, Matter, MatterId);
        var childInvoice = InvoiceOn(w, WorkAssignmentTable, child);
        var childFinding = w.Signal(policy, childInvoice);
        using var cts = new CancellationTokenSource();
        var readsBefore = -1;
        w.H.TransactionCommitsButAnswersUnknown = requests =>
        {
            if (requests.Count != 3)
            {
                return false;
            }

            cts.Cancel(); // the caller went away while stage 3 committed
            readsBefore = w.H.QueryTokens.Count;
            return true;
        };

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, cts.Token);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        w.StatusOf(finding).Should().Be(SignalWriter.SignalStatusResolved, "the change committed, so its close runs");
        readsBefore.Should().BeGreaterThan(0);
        var after = w.H.Queries.Skip(readsBefore).Zip(w.H.QueryTokens.Skip(readsBefore)).ToList();
        after.Should().Contain(r => r.First.EntityName == "sprk_policyversion", "the read-back asks for the record rule's version row");
        // The read-back (the version row) and the close's Signal reads; the caller-scoped count after them keeps the request's token.
        after.Where(r => r.First.EntityName is "sprk_policyversion" or "sprk_signal")
            .Should().OnlyContain(r => !r.Second.CanBeCanceled, "the read-back and the close run on CancellationToken.None");

        // ebilling-r1 084d-3 (ontology-r1 O-K3): the close's selection is shared with the dry run, which passes ITS request token into it
        // (SelectBaseFindingsAsync and its readers take an optional token since 084d-2b fix round 1). The write never does: every read of the
        // close's selection (the In Review invoices, their lines, and the records directly under this one) runs on CancellationToken.None. The
        // close ends with its last Signal read; the caller-scoped count after it keeps the request's token (must-not-fire: the guard tells them apart).
        var closeEnds = after.FindLastIndex(r => r.First.EntityName == "sprk_signal");
        closeEnds.Should().BeGreaterThan(0, "the close selected the base rule's findings");
        var close = after.Take(closeEnds + 1).ToList();
        var selectionTables = new[]
        {
            "sprk_invoice", "sprk_billingevent", "sprk_matter", "sprk_project", "sprk_workassignment",
            "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion",
        };
        foreach (var table in new[] { "sprk_invoice", "sprk_billingevent" })
        {
            close.Should().Contain(r => r.First.EntityName == table, $"the close's selection reads {table}");
        }

        close.Should().Contain(r => r.First.EntityName == "sprk_project" || r.First.EntityName == "sprk_workassignment",
            "the close's selection reads the records directly under the matter");
        // Premises (084d-3 review round 1, F3-1): the children's In Review invoice read and the override read of the child's chain ran inside
        // the close's window. Without them the token assertion below is vacuous for those two reads.
        static bool Names(QueryExpression q, string attribute, Guid id) => q.Criteria.Conditions.Concat(q.Criteria.Filters.SelectMany(f => f.Conditions))
            .Any(c => c.AttributeName == attribute && c.Values.Contains(id));
        var childInvoiceColumn = InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"].Single(l => l.CoreEntity == WorkAssignmentTable).Column;
        close.Any(r => r.First.EntityName == "sprk_invoice" && Names(r.First, childInvoiceColumn, child))
            .Should().BeTrue("the close read the children's In Review invoices");
        close.Any(r => r.First.EntityName == "sprk_procedureassignment" && Names(r.First, WorkAssignmentTable, child))
            .Should().BeTrue("the close read the override rows on the child's chain (ProcedureResolver.ReadOverridesAsync)");
        w.StatusOf(childFinding).Should().Be(SignalWriter.SignalStatusResolved, "the child's invoice was kept by the override read, so its finding closed");
        close.Where(r => selectionTables.Contains(r.First.EntityName))
            .Should().OnlyContain(r => !r.Second.CanBeCanceled, "the request's token never reaches the write's selection reads");
        after.Skip(closeEnds + 1).Should().Contain(r => r.Second == cts.Token, "the caller-scoped count after the close keeps the request's token");
    }

    [Fact(DisplayName = "089c: an unknown override, or another record's, is 404 assignment.override_not_found and nothing is written")]
    public async Task Remove_AnUnknownOrAnotherRecordsOverride_Is404()
    {
        var w = new World();
        var otherMatter = Guid.NewGuid();
        w.H.Seed(new Entity(Matter, otherMatter));
        var excluded = await w.Service().ExcludeRuleAsync(Matter, otherMatter, new(w.Rule(), "x", null), CallerOid, default);
        var transactions = w.H.Transactions.Count;

        var unknown = await w.Service().RemoveAsync(Matter, MatterId, Guid.NewGuid(), Token, default);
        var others = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var preview = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value.Id, Token, default);

        foreach (var refused in new[] { unknown.ErrorCode, others.ErrorCode, preview.ErrorCode })
        {
            refused.Should().Be("assignment.override_not_found");
        }

        w.H.Transactions.Should().HaveCount(transactions);
        w.Rows.Should().ContainSingle();
        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, excluded.Value.Id, default)).Should().BeNull();
    }

    [Fact(DisplayName = "089c: the subject of an override is the Procedure or the rule it names")]
    public async Task SubjectOfOverride_IsTheProcedureOrTheRule()
    {
        var w = new World();
        var procedure = w.Procedure();
        var policy = w.Rule();
        var assigned = await w.Service().AssignProcedureAsync(Matter, MatterId, new(procedure, null, null, null), CallerOid, default);
        var changed = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, assigned.Value!.Id, default)).Should().Be(new OverrideSubject("sprk_procedures", procedure));
        (await w.Service().SubjectOfOverrideAsync(Matter, MatterId, changed.Value!.Override.Id, default)).Should().Be(new OverrideSubject("sprk_policies", policy));
    }

    [Fact(DisplayName = "089c: a read fault before a removal is a stable 503 and nothing is written")]
    public async Task Remove_WhenAReadFaults_Is503()
    {
        var w = new World();
        var excluded = await w.Service().ExcludeRuleAsync(Matter, MatterId, new(w.Rule(), "x", null), CallerOid, default);
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == Assignment) throw new HttpRequestException("simulated fault");
        };

        var removed = await w.Service().RemoveAsync(Matter, MatterId, excluded.Value!.Id, Token, default);
        var previewed = await w.Service().PreviewRemovalAsync(Matter, MatterId, excluded.Value.Id, Token, default);

        removed.StatusCode.Should().Be(503);
        previewed.StatusCode.Should().Be(503);
        w.Rows.Should().ContainSingle();
    }
}
