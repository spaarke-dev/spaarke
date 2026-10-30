using System.Collections.Concurrent;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Signals;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 103: an in-memory Dataverse for the approval tests. Every table the approval touches is a row set; queries EVALUATE their
/// conditions (Equal, In, Null, NotNull, an inner link), conditional writes model row versions, a transaction is all-or-nothing, and the N:N is
/// its intersect table. The REAL resolver, line-decision service, totals service and recorder (on the writer's harness) run over it, so a test
/// asserts the stored state and the ledger, not the calls made.
/// </summary>
internal sealed class ApprovalWorld
{
    public const string CallerToken = "caller-bearer-token";
    public const string Intersect = EligibleApproverResolver.EligibleRelationship;

    public static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    // ── People: contact → user. Dan's contact has no user. ──
    public static readonly Guid AliceUser = Guid.Parse("03000000-0000-4000-8000-0000000000a1");
    public static readonly Guid BobUser = Guid.Parse("03000000-0000-4000-8000-0000000000b1");
    public static readonly Guid CarolUser = Guid.Parse("03000000-0000-4000-8000-0000000000c1");
    public static readonly Guid Alice = Guid.Parse("03000000-0000-4000-8000-0000000000a2");
    public static readonly Guid Bob = Guid.Parse("03000000-0000-4000-8000-0000000000b2");
    public static readonly Guid Carol = Guid.Parse("03000000-0000-4000-8000-0000000000c2");
    public static readonly Guid Dan = Guid.Parse("03000000-0000-4000-8000-0000000000d2");

    public readonly Guid InvoiceId = Guid.NewGuid();
    public readonly Guid ApprovalId = Guid.NewGuid();
    public readonly Guid CoreId = Guid.NewGuid();
    public readonly string CoreEntity;

    public readonly object Gate = new();
    public readonly Dictionary<(string Table, Guid Id), Entity> Rows = new();
    public readonly List<string> WriteLog = [];
    public readonly ConcurrentDictionary<string, Exception> FaultsOnce = new();
    public readonly ConcurrentDictionary<string, Exception> Faults = new();
    public readonly InMemoryLocks Locks = new();
    public readonly DecisionRecordWriterTests.Harness H;
    public readonly Mock<IGenericEntityService> Entities = new();
    public readonly Mock<IFieldMappingDataverseService> Records = new();
    public readonly Mock<IIdentityNormalizationService> Identity = new();

    /// <summary>EB-D-171: the identity the APPROVAL resolves the caller's own contact through (fault key <c>caller-identity</c>); the recorder's
    /// identity is <see cref="Identity"/> (fault key <c>identity</c>), so a test can fault one without the other.</summary>
    public readonly Mock<IIdentityNormalizationService> CallerIdentity = new();

    /// <summary>Each user's OWN linked contact, as the identity service answers it. A user not listed has no contact.</summary>
    public readonly Dictionary<Guid, Guid> ContactOfUser = new() { [AliceUser] = Alice, [BobUser] = Bob, [CarolUser] = Carol };

    public readonly Mock<IRecordOwnershipResolver> Ownership = new();
    public readonly Mock<IInvoiceLifecycleService> Lifecycle = new();
    public readonly Mock<IEbillingSettingsReader> Settings = new();
    public readonly FakeTimeProvider Clock = new(Now);
    public readonly ApprovalListLogger<EligibleApproverResolver> ResolverLog = new();
    public readonly ApprovalListLogger<InvoiceApprovalService> Log = new();
    public readonly Mock<CallerRecordAccessProbe> Probe = new(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!) { CallBase = false };

    public EbillingSettings SettingsValue { get; set; } = new();

    /// <summary>The caller's rights per entity set, answered only for <see cref="CallerToken"/> (EB-D-172: AppendTo on the core, Write on the invoice).</summary>
    public Func<string, Guid, AccessRights> Rights { get; set; } = (_, _) => AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;

    /// <summary>F4: the caller's decide right (Write on the invoice) as the GET asks it.</summary>
    public bool CanDecide { get; set; } = true;

    /// <summary>Every close the approval asked of the Signal writer, and which record closed each Signal.</summary>
    public readonly List<(Guid SignalId, Guid RecordId, SignalAutomatedCloseOutcome Outcome)> Closes = [];
    public readonly Dictionary<Guid, Guid> ClosedBy = new();

    /// <summary>A Signal whose next close fails once: ChangedSinceRead is answered, any other value throws a timeout (recorded as that value).</summary>
    public readonly Dictionary<Guid, SignalAutomatedCloseOutcome> CloseFailsOnce = new();

    /// <summary>Called with the claim's fields before the conditional portion write (a test parks two approvals here).</summary>
    public Func<Task>? BeforeClaim { get; set; }

    /// <summary>The 014 transition commits and THEN throws (a timeout after the commit).</summary>
    public bool TransitionThrowsAfterCommit { get; set; }

    /// <summary>101 fix round, part 3: a fault chosen by a transaction's requests (null: none), checked before the keyed <c>tx:</c> faults.</summary>
    public Func<IReadOnlyList<OrganizationRequest>, Exception?>? TransactionFault { get; set; }

    public int Transitions;
    private long _nextVersion = 100;

    public ApprovalWorld(string coreEntity = "sprk_project", int invoiceStatus = InvoiceStatusCodes.InReview)
    {
        CoreEntity = coreEntity;
        var invoiceCoreColumn = coreEntity == "sprk_workassignment" ? "sprk_regardingworkassignment" : coreEntity;

        Put(new Entity("sprk_invoice", InvoiceId)
        {
            ["statuscode"] = new OptionSetValue(invoiceStatus),
            ["sprk_totalamount"] = new Money(1000m),
            [invoiceCoreColumn] = new EntityReference(coreEntity, CoreId),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
            ["sprk_regardingrecordid"] = CoreId.ToString("D"),
            ["sprk_regardingrecordname"] = "Core",
            ["sprk_regardingrecordurl"] = "https://example.invalid/c",
            ["sprk_regardingrecordnumber"] = "C-1",
        });

        // Default roles (settings): Assigned Attorney 1 (Alice) + Assigned To Internal (Bob). Carol is assigned nowhere; Dan (no user) is Paralegal 1.
        Put(new Entity(coreEntity, CoreId)
        {
            ["sprk_assignedattorney1"] = new EntityReference("contact", Alice),
            ["sprk_assignedtointernal"] = new EntityReference("contact", Bob),
            ["sprk_assignedparalegal1"] = new EntityReference("contact", Dan),
        });
        Put(Contact(Alice, "Alice Adams", AliceUser));
        Put(Contact(Bob, "Bob Brown", BobUser));
        Put(Contact(Carol, "Carol Clark", CarolUser));
        Put(Contact(Dan, "Dan Dawson", null));

        Put(new Entity("sprk_invoiceapproval", ApprovalId)
        {
            ["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionPending),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            [CoreAncestorResolver.StampColumnFor(coreEntity)] = new EntityReference(coreEntity, CoreId),
            ["versionnumber"] = ++_nextVersion,
        });

        H = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        H.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { [invoiceCoreColumn] = new EntityReference(coreEntity, CoreId) };

        Settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => SettingsValue);
        Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => TakeFault("identity") is { } fault
                ? Task.FromException<PersonIdentity>(fault)
                : Task.FromResult(new PersonIdentity(user, ContactId: ContactOfUser.TryGetValue(user, out var c) ? c : Guid.NewGuid())));
        CallerIdentity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => TakeFault("caller-identity") is { } fault
                ? Task.FromException<PersonIdentity>(fault)
                : Task.FromResult(new PersonIdentity(user, ContactId: ContactOfUser.TryGetValue(user, out var c) ? c : null)));

        // F3: the recorder's filing read (the core-ancestor resolver reads the invoice by id).
        Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] _, CancellationToken _) =>
            {
                if (TakeFault("stamps") is { } fault)
                {
                    return Task.FromException<Entity>(fault);
                }

                lock (Gate)
                {
                    return Task.FromResult(Rows.TryGetValue((table, id), out var row) ? Copy(row) : new Entity(table, id));
                }
            });

        // EB-D-172: the caller's rights, asked as the caller (only the caller's own token holds any).
        Entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => table + "s");
        Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? token, string set, Guid id, CancellationToken _) => token == CallerToken ? Rights(set, id) : AccessRights.None);
        Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Owned(Guid.Parse("03000000-0000-4000-8000-0000000000f0")));

        Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Query(query));
        Entities.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) => ClaimAsync(table, id, version, fields));
        Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                var key = fields.ContainsKey("sprk_decisionrecord") ? "link" : fields.ContainsKey("sprk_approvalstatus") ? "portion" : "update";
                if (TakeFault(key) is { } fault)
                {
                    return Task.FromException(fault);
                }

                lock (Gate)
                {
                    Merge(table, id, fields.ToDictionary(f => f.Key, f => (object?)f.Value));
                    WriteLog.Add($"{key}:{table}");
                }

                return Task.CompletedTask;
            });
        Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) => Transaction(requests));
        Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken _) =>
            {
                lock (Gate)
                {
                    if (updates.Any(u => Rows[(table, u.id)].GetAttributeValue<long>("versionnumber") != u.rowVersion))
                    {
                        throw new DBConcurrencyException("simulated row version mismatch: nothing applied");
                    }

                    foreach (var (id, fields, _) in updates)
                    {
                        Merge(table, id, fields.ToDictionary(f => f.Key, f => (object?)f.Value));
                    }

                    WriteLog.Add($"lines:{updates.Count}");
                }

                return Task.CompletedTask;
            });

        // 016 totals: the invoice's billed total and version, and its conditional write.
        Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => TakeFault("totals") is { } fault
                ? Task.FromException<Dictionary<string, object?>>(fault)
                : Task.FromResult(new Dictionary<string, object?> { ["sprk_totalamount"] = 1000m, ["versionnumber"] = 5L }));
        Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, Dictionary<string, object?> fields, long _, CancellationToken _) =>
            {
                lock (Gate)
                {
                    Rows[("sprk_invoice", InvoiceId)]["sprk_approvedtotal"] = new Money((decimal)fields["sprk_approvedtotal"]!);
                }
            })
            .Returns(Task.CompletedTask);
        Records.Setup(r => r.UpdateExistingRecordFieldsAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 014: the status moves only from In Review; a repeat is AlreadyInTarget. Task 104: the same for Rejected and Returned to Firm.
        Lifecycle.Setup(l => l.TransitionAsync(It.IsAny<Guid>(), It.IsAny<InvoiceLifecycleStatus>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, InvoiceLifecycleStatus target, CancellationToken _) =>
            {
                Interlocked.Increment(ref Transitions);
                if (TakeFault("transition") is { } before)
                {
                    return Task.FromException<InvoiceTransitionResult>(before);
                }

                var targetCode = InvoiceStatusCodes.StatusCodeOf(target);
                lock (Gate)
                {
                    var invoice = Rows[("sprk_invoice", id)];
                    var status = invoice.GetAttributeValue<OptionSetValue>("statuscode").Value;
                    if (status == targetCode)
                    {
                        return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.AlreadyInTarget, target, target, null));
                    }

                    if (status != InvoiceStatusCodes.InReview)
                    {
                        return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.Refused, target, null, "invoice-lifecycle.transition-not-allowed"));
                    }

                    invoice["statuscode"] = new OptionSetValue(targetCode);
                    WriteLog.Add("transition");
                }

                if (TransitionThrowsAfterCommit)
                {
                    TransitionThrowsAfterCommit = false;
                    return Task.FromException<InvoiceTransitionResult>(new TimeoutException("the commit landed, the answer did not"));
                }

                return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.Transitioned, target, InvoiceLifecycleStatus.InReview, null));
            });
    }

    // ── Builders ──

    public static Entity Contact(Guid id, string name, Guid? user, bool active = true)
    {
        var contact = new Entity("contact", id) { ["fullname"] = name, ["statecode"] = new OptionSetValue(active ? 0 : 1) };
        if (user is { } u)
        {
            contact["sprk_systemuser"] = new EntityReference("systemuser", u);
        }

        return contact;
    }

    public Entity AddLine(InvoiceLineStatus? status, decimal amount = 100m, InvoiceLineCostType type = InvoiceLineCostType.Fee, string? extraction = null, decimal? approved = null)
    {
        var id = Guid.NewGuid();
        var line = new Entity("sprk_billingevent", id)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_linesequence"] = Rows.Keys.Count(k => k.Table == "sprk_billingevent") + 1,
            ["sprk_costtype"] = new OptionSetValue((int)type),
            ["sprk_amount"] = new Money(amount),
            ["sprk_description"] = "Draft the response",
            ["sprk_quantity"] = 2.5m,
            ["sprk_rate"] = new Money(amount / 2.5m),
            ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["versionnumber"] = ++_nextVersion,
        };
        if (status is not null) line["sprk_linestatus"] = new OptionSetValue((int)status.Value);
        if (approved is not null) line["sprk_approvedamount"] = new Money(approved.Value);
        if (extraction is not null) line["sprk_extractionconfidence"] = extraction;
        Put(line);
        H.Rows[("sprk_billingevent", id)] = new Entity("sprk_billingevent", id)
        {
            [CoreEntity == "sprk_workassignment" ? "sprk_regardingworkassignment" : CoreEntity] = new EntityReference(CoreEntity, CoreId),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        };
        return line;
    }

    /// <summary>A blocked submission (FR-20: held from approval until decided).</summary>
    public void Hold() => Put(new Entity("sprk_invoicesubmission", Guid.NewGuid())
    {
        ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected),
        ["sprk_outcomereasoncode"] = SubmissionReasonCodes.Blocked,
    });

    /// <summary>An Open (or <paramref name="status"/>) Signal on the INVOICE, filed as the Signal writer files one (dedupe key, pair id, subject
    /// lookup, created on). The writer's harness holds it too, so the record's resolved-Signal checks find it.</summary>
    public Guid AddInvoiceSignal(string policyCode = "EB-DUP", int status = SignalWriter.SignalStatusOpen, DateTime? createdOn = null)
    {
        var id = Guid.NewGuid();
        var lookup = SignalWriter.RegardingLookupByEntity["sprk_invoice"];
        Put(new Entity("sprk_signal", id)
        {
            ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, "sprk_invoice", InvoiceId.ToString("D"), 1),
            ["sprk_signalstatus"] = new OptionSetValue(status),
            ["sprk_regardingrecordid"] = InvoiceId.ToString("D"),
            [lookup] = new EntityReference("sprk_invoice", InvoiceId),
            ["createdon"] = createdOn ?? Now.UtcDateTime.AddHours(-1),
        });
        H.Rows[("sprk_signal", id)] = new Entity("sprk_signal", id)
        {
            ["sprk_regardingrecordid"] = InvoiceId.ToString("D"),
            [lookup] = new EntityReference("sprk_invoice", InvoiceId),
        };
        return id;
    }

    /// <summary>Task 104: an Open Signal on a LINE of the invoice (a reject or return resolves the lines' Signals too, D-122).</summary>
    public Guid AddLineSignal(Guid lineId, string policyCode = "EB-RATE", int status = SignalWriter.SignalStatusOpen, DateTime? createdOn = null)
    {
        var id = Guid.NewGuid();
        var lookup = SignalWriter.RegardingLookupByEntity["sprk_billingevent"];
        Put(new Entity("sprk_signal", id)
        {
            ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, "sprk_billingevent", lineId.ToString("D"), 1),
            ["sprk_signalstatus"] = new OptionSetValue(status),
            ["sprk_regardingrecordid"] = lineId.ToString("D"),
            [lookup] = new EntityReference("sprk_billingevent", lineId),
            ["createdon"] = createdOn ?? Now.UtcDateTime.AddHours(-1),
        });
        H.Rows[("sprk_signal", id)] = new Entity("sprk_signal", id)
        {
            ["sprk_regardingrecordid"] = lineId.ToString("D"),
            [lookup] = new EntityReference("sprk_billingevent", lineId),
        };
        return id;
    }

    public int SignalStatus(Guid signalId) => Row("sprk_signal", signalId).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value;

    /// <summary>The app-identity close as the Signal writer behaves: Open/Acknowledged → Resolved Acted by the record; a repeat by the same
    /// record → AlreadyClosedByThisRecord; resolved otherwise → LeftAsIs. Every call is recorded.</summary>
    public Task<SignalAutomatedCloseOutcome> CloseAsync(
        Guid signalId, Guid recordId, DateTime setReadAtUtc, string policyCode, Guid? decidedBy, CancellationToken ct)
    {
        lock (Gate)
        {
            SignalAutomatedCloseOutcome outcome;
            if (CloseFailsOnce.Remove(signalId, out var failure))
            {
                Closes.Add((signalId, recordId, failure));
                return failure == SignalAutomatedCloseOutcome.ChangedSinceRead
                    ? Task.FromResult(failure)
                    : Task.FromException<SignalAutomatedCloseOutcome>(new TimeoutException("the close timed out"));
            }

            if (ClosedBy.TryGetValue(signalId, out var by))
            {
                outcome = by == recordId ? SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord : SignalAutomatedCloseOutcome.LeftAsIs;
            }
            else
            {
                ClosedBy[signalId] = recordId;
                Rows[("sprk_signal", signalId)]["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
                WriteLog.Add("close:sprk_signal");
                outcome = SignalAutomatedCloseOutcome.Closed;
            }

            Closes.Add((signalId, recordId, outcome));
            return Task.FromResult(outcome);
        }
    }

    public void Put(Entity row)
    {
        lock (Gate)
        {
            Rows[(row.LogicalName, row.Id)] = row;
        }
    }

    // ── Views ──

    public Entity Row(string table, Guid id)
    {
        lock (Gate)
        {
            return Rows[(table, id)];
        }
    }

    public Entity Invoice => Row("sprk_invoice", InvoiceId);

    public Entity Portion => Row("sprk_invoiceapproval", ApprovalId);

    public Entity Core => Row(CoreEntity, CoreId);

    public int InvoiceStatus => Invoice.GetAttributeValue<OptionSetValue>("statuscode").Value;

    public List<Entity> ApproveRecords => RecordsOf("approve-invoice");

    /// <summary>Task 104: the Decision Records written for an action code.</summary>
    public List<Entity> RecordsOf(string actionCode) => H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == actionCode).ToList();

    public List<Entity> Corrections
    {
        get
        {
            lock (Gate)
            {
                return Rows.Values.Where(r => r.LogicalName == "sprk_invoicecorrection").ToList();
            }
        }
    }

    public HashSet<Guid> StoredApprovers
    {
        get
        {
            lock (Gate)
            {
                return Rows.Values.Where(r => r.LogicalName == Intersect && (Guid)r["sprk_invoiceapprovalid"] == ApprovalId)
                    .Select(r => (Guid)r["contactid"]).ToHashSet();
            }
        }
    }

    public int Writes
    {
        get
        {
            lock (Gate)
            {
                return WriteLog.Count;
            }
        }
    }

    // ── Services ──

    public EligibleApproverResolver Resolver() => new(Entities.Object, Settings.Object, ResolverLog, Clock);

    public InvoiceTotalsService Totals() => new(Records.Object, Entities.Object, NullLogger<InvoiceTotalsService>.Instance);

    public BillingDecisionRecorder Recorder() => new(H.Writer, Identity.Object, NullLogger<BillingDecisionRecorder>.Instance);

    public InvoiceLineDecisionService LineService() => new(
        Entities.Object, Totals(), Recorder(), Locks, Ownership.Object, Probe.Object,
        (_, _, _, _, _, _) => Task.FromResult(SignalAutomatedCloseOutcome.Closed),
        NullLogger<InvoiceLineDecisionService>.Instance);

    /// <summary>F3: the core-ancestor resolver over this world's rows (every invoice root column exists).</summary>
    public CoreAncestorResolver CoreAncestors() => new(
        Entities.Object,
        (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            CoreAncestorResolver.IntermediateRootColumns.Values.SelectMany(c => c.Select(x => x.Column)), StringComparer.OrdinalIgnoreCase)),
        NullLogger<CoreAncestorResolver>.Instance);

    /// <param name="canDecide">The decide check (F4); the contract host passes the REAL one over its authorization stack. Default: <see cref="CanDecide"/>.</param>
    public InvoiceApprovalService Service(Func<InvoiceContextService.InvoiceCaller, Guid, CancellationToken, Task<bool>>? canDecide = null) => new(
        Entities.Object, Resolver(), LineService(), Totals(), Lifecycle.Object, Recorder(), Locks, CallerIdentity.Object, CoreAncestors(), Probe.Object,
        CloseAsync, canDecide ?? ((_, _, _) => Task.FromResult(CanDecide)), Log, Clock);

    public readonly ApprovalListLogger<InvoiceRejectReturnService> RejectReturnLog = new();

    /// <summary>Task 104: the reject / return service over this world (the same resolver, recorder, locks, identity and close as approve).</summary>
    public InvoiceRejectReturnService RejectReturnService() => new(
        Entities.Object, Resolver(), Lifecycle.Object, Recorder(), Locks, CallerIdentity.Object, CoreAncestors(), Probe.Object,
        CloseAsync, RejectReturnLog, Clock);

    public Task<InvoiceDecisionResult> RejectAs(Guid user, string? reason = "Rate card not followed") =>
        RejectReturnService().RejectAsync(InvoiceId, user, reason, CallerToken, CancellationToken.None);

    public Task<InvoiceDecisionResult> ReturnAs(Guid user, string? reason = "Please correct the task codes") =>
        RejectReturnService().ReturnAsync(InvoiceId, user, reason, CallerToken, CancellationToken.None);

    /// <summary>The GET as the endpoint calls it for <paramref name="user"/> (the decide right asked with the caller's own token).</summary>
    public Task<InvoiceApprovalStateDto?> GetAs(Guid? user) =>
        Service().GetAsync(InvoiceId, user, new InvoiceContextService.InvoiceCaller("oid", CallerToken), CancellationToken.None);

    public Task<ApprovalResult> ApproveAs(Guid user) => Service().ApproveAsync(InvoiceId, user, CallerToken, CancellationToken.None);

    // ── The in-memory Dataverse ──

    private Exception? TakeFault(string key) =>
        FaultsOnce.TryRemove(key, out var once) ? once : Faults.TryGetValue(key, out var always) ? always : null;

    private Task<EntityCollection> Query(QueryExpression query)
    {
        if (TakeFault("read:" + query.EntityName) is { } fault)
        {
            return Task.FromException<EntityCollection>(fault);
        }

        lock (Gate)
        {
            // The Decision Records live in the writer's harness (F2 asks which lines have one).
            var source = query.EntityName == InvoiceReviewReadService.DecisionRecordEntity ? H.Created.AsEnumerable() : Rows.Values;
            var rows = source
                .Where(r => r.LogicalName == query.EntityName && Matches(r, query.Criteria) && query.LinkEntities.All(link => Joins(r, link)))
                .Select(r => WithLinkColumns(Copy(r), query.LinkEntities))
                .Take(query.TopCount ?? int.MaxValue)
                .ToList();
            return Task.FromResult(new EntityCollection(rows) { MoreRecords = false });
        }
    }

    /// <summary>A link's requested columns come back as <c>alias.column</c> aliased values, as Dataverse returns them.</summary>
    private Entity WithLinkColumns(Entity row, IEnumerable<LinkEntity> links)
    {
        foreach (var link in links.Where(l => l.EntityAlias is not null && l.Columns is { Columns.Count: > 0 }))
        {
            var from = row.Contains(link.LinkFromAttributeName) ? row[link.LinkFromAttributeName] : null;
            var id = from switch { EntityReference r => r.Id, Guid g => g, _ => Guid.Empty };
            if (!Rows.TryGetValue((link.LinkToEntityName, id), out var target))
            {
                continue;
            }

            foreach (var column in link.Columns.Columns.Where(target.Contains))
            {
                var value = target[column] is EntityReference r ? new EntityReference(r.LogicalName, r.Id) : target[column];
                row[link.EntityAlias + "." + column] = new AliasedValue(link.LinkToEntityName, column, value);
            }
        }

        return row;
    }

    private bool Joins(Entity row, LinkEntity link)
    {
        var from = row.Contains(link.LinkFromAttributeName) ? row[link.LinkFromAttributeName] : null;
        var id = from switch { EntityReference r => r.Id, Guid g => g, _ => Guid.Empty };
        return Rows.TryGetValue((link.LinkToEntityName, id), out var target) && Matches(target, link.LinkCriteria);
    }

    private static bool Matches(Entity row, FilterExpression? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var results = filter.Conditions.Select(c => Condition(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
        return results.Count == 0 || (filter.FilterOperator == LogicalOperator.Or ? results.Any(x => x) : results.All(x => x));
    }

    private static bool Condition(Entity row, ConditionExpression condition)
    {
        object? actual = condition.AttributeName == row.LogicalName + "id"
            ? row.Id
            : row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
        return condition.Operator switch
        {
            ConditionOperator.Equal => Same(actual, condition.Values.Single()),
            // Task 102's line read leaves out removed Adjustment lines with sprk_costtype ne Adjustment (or null, or active). As in
            // Dataverse, ne never matches an empty column (the read's own Null branch covers that).
            ConditionOperator.NotEqual => actual is not null && !Same(actual, condition.Values.Single()),
            ConditionOperator.In => condition.Values.Any(v => Same(actual, v)),
            ConditionOperator.Null => actual is null,
            ConditionOperator.NotNull => actual is not null,
            // Task 101 B2b-2: the Signal lifecycle finds a (policy, subject)'s latest episode by the dedupe-key prefix.
            ConditionOperator.BeginsWith => actual is string text && text.StartsWith((string)condition.Values.Single(), StringComparison.Ordinal),
            _ => throw new NotSupportedException($"Test world: {condition.Operator} is not evaluated."),
        };
    }

    private static bool Same(object? actual, object? expected) => actual switch
    {
        null => expected is null,
        EntityReference r => (expected is Guid g && r.Id == g) || (expected is EntityReference e && e.Id == r.Id),
        OptionSetValue o => (expected is int i && o.Value == i) || (expected is OptionSetValue e2 && e2.Value == o.Value),
        Money m => expected is decimal d && m.Value == d,
        _ => Equals(actual, expected),
    };

    private async Task ClaimAsync(string table, Guid id, string version, Dictionary<string, object> fields)
    {
        if (BeforeClaim is { } hook)
        {
            await hook();
        }

        if (TakeFault("claim") is { } fault)
        {
            throw fault;
        }

        lock (Gate)
        {
            if (Rows[(table, id)].GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture) != version)
            {
                throw new DBConcurrencyException("simulated row version mismatch: nothing applied");
            }

            Merge(table, id, fields.ToDictionary(f => f.Key, f => (object?)f.Value));
            WriteLog.Add("claim:" + table);
        }
    }

    private Task Transaction(IReadOnlyList<OrganizationRequest> requests)
    {
        if (TransactionFault?.Invoke(requests) is { } chosen)
        {
            return Task.FromException(chosen);
        }

        var key = requests.Any(r => r is AssociateRequest or DisassociateRequest) || requests.All(r => r is UpdateRequest) ? "tx:approvers" : "tx:corrections";
        if (TakeFault(key) is { } fault)
        {
            return Task.FromException(fault);
        }

        lock (Gate)
        {
            // All or nothing: a duplicate association fails the whole transaction before anything is applied.
            foreach (var associate in requests.OfType<AssociateRequest>())
            {
                if (associate.RelatedEntities.Any(c => Rows.Values.Any(r => r.LogicalName == Intersect
                        && (Guid)r["sprk_invoiceapprovalid"] == associate.Target.Id && (Guid)r["contactid"] == c.Id)))
                {
                    return Task.FromException(new InvalidOperationException("Cannot insert duplicate key."));
                }
            }

            foreach (var request in requests)
            {
                switch (request)
                {
                    case UpsertRequest upsert:
                        Rows[(upsert.Target.LogicalName, upsert.Target.Id)] = Copy(upsert.Target);
                        break;
                    case UpdateRequest update:
                        Merge(update.Target.LogicalName, update.Target.Id, update.Target.Attributes.ToDictionary(a => a.Key, a => (object?)a.Value));
                        break;
                    case AssociateRequest associate:
                        foreach (var contact in associate.RelatedEntities)
                        {
                            var row = new Entity(Intersect, Guid.NewGuid()) { ["sprk_invoiceapprovalid"] = associate.Target.Id, ["contactid"] = contact.Id };
                            Rows[(Intersect, row.Id)] = row;
                        }

                        break;
                    case DisassociateRequest disassociate:
                        foreach (var key2 in Rows.Where(kv => kv.Value.LogicalName == Intersect
                                && (Guid)kv.Value["sprk_invoiceapprovalid"] == disassociate.Target.Id
                                && disassociate.RelatedEntities.Any(c => c.Id == (Guid)kv.Value["contactid"])).Select(kv => kv.Key).ToList())
                        {
                            Rows.Remove(key2);
                        }

                        break;
                    default:
                        throw new NotSupportedException(request.GetType().Name);
                }
            }

            WriteLog.Add(key);
        }

        return Task.CompletedTask;
    }

    private void Merge(string table, Guid id, Dictionary<string, object?> fields)
    {
        var row = Rows[(table, id)];
        foreach (var (name, value) in fields)
        {
            if (value is null or DBNull)
            {
                row.Attributes.Remove(name);
            }
            else
            {
                row[name] = value;
            }
        }

        row["versionnumber"] = ++_nextVersion;
    }

    private static Entity Copy(Entity source)
    {
        var copy = new Entity(source.LogicalName, source.Id);
        foreach (var attribute in source.Attributes)
        {
            copy[attribute.Key] = attribute.Value is EntityReference r ? new EntityReference(r.LogicalName, r.Id) { Name = r.Name } : attribute.Value;
        }

        return copy;
    }

    /// <summary>Per-key locks; <see cref="AlwaysGrant"/> defeats them (the K-4 "lock expired" case).</summary>
    internal sealed class InMemoryLocks : IIdempotencyService
    {
        private readonly ConcurrentDictionary<string, string> _held = new();

        public bool AlwaysGrant { get; set; }

        public Exception? Fault { get; set; }

        public readonly ConcurrentBag<string> Acquired = [];

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            if (Fault is { } fault)
            {
                return Task.FromException<bool>(fault);
            }

            var granted = AlwaysGrant || _held.TryAdd(eventId, eventId);
            if (granted)
            {
                Acquired.Add(eventId);
            }

            return Task.FromResult(granted);
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            _held.TryRemove(eventId, out _);
            return Task.CompletedTask;
        }

        /// <summary>Holds a key as another caller would.</summary>
        public void Hold(string key) => _held[key] = "other";

        public bool IsHeld(string key) => _held.ContainsKey(key);
    }
}

/// <summary>Captures log entries (level and rendered message), so a warning is asserted, not assumed.</summary>
internal sealed class ApprovalListLogger<T> : ILogger<T>
{
    public readonly List<(LogLevel Level, string Message)> Entries = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
