using System.Data;
using System.ServiceModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// The Dataverse boundary of the correction tests (ebilling-r1 task 101 part B1), modelled in memory behind a STRICT <see cref="IGenericEntityService"/>:
/// one invoice with an undecided Fee line, an Expense line, a submission with header provenance, a code set, the correction rows (upserts) and row
/// versions on conditional writes. Shared by <c>InvoiceCorrectionServiceTests</c> (the service) and the endpoint contract tests (the HTTP surface).
/// </summary>
internal sealed class CorrectionWorld
{
    public static readonly Guid Reviewer = Guid.Parse("10100000-0000-4000-8000-000000000001");
    public static readonly Guid ReviewerContact = Guid.Parse("10100000-0000-4000-8000-0000000000e1");
    public static readonly Guid Core = Guid.Parse("10100000-0000-4000-8000-0000000000c0");
    public static readonly Guid Firm = Guid.Parse("10100000-0000-4000-8000-0000000000f1");
    public static readonly Guid CodeSetId = Guid.Parse("10100000-0000-4000-8000-0000000000d1");
    public static readonly Guid InvoiceId = Guid.Parse("10100000-0000-4000-8000-0000000000a1");
    public static readonly Guid FeeLineId = Guid.Parse("10100000-0000-4000-8000-0000000000b1");
    public static readonly Guid ExpenseLineId = Guid.Parse("10100000-0000-4000-8000-0000000000b2");
    public static readonly DateTime T0 = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    public const string Narrative = "Draft motion to compel and research privileged settlement terms";

    /// <summary>Captures log entries (level and rendered message), so ADR-015 is asserted: no value is ever logged.</summary>
    public sealed class ListLogger<T> : ILogger<T>
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

    public sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = T0;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public Entity Invoice { get; set; }
    public readonly List<Entity> Lines = [];
    public readonly List<Entity> Corrections = [];
    public Entity? Submission { get; set; }
    public readonly List<Entity> CodeSets = [];
    public readonly List<Entity> Codes = [];

    /// <summary>B2c: the firm's timekeeper roster and the rate card rows 046's real resolution reads.</summary>
    public readonly List<Entity> Roster = [];
    public readonly List<Entity> Rates = [];

    /// <summary>B2c: every one-line input the correction service gave the (real) resolution service, and a fault for it (null = none).</summary>
    public readonly List<InvoiceResolutionInput> ResolveInputs = [];
    public Exception? ResolveFault;

    /// <summary>B2b-2: the Signals, as the writer files them (dedupe key, status, evidence with the reason-set fingerprint, row version).</summary>
    public readonly List<Entity> Signals = [];

    /// <summary>B2b-2: every Signal write (policy, subject, episode) and every system close (Signal id, resolution type) that reached the writer.</summary>
    public readonly List<(string PolicyCode, Guid SubjectId, int Episode)> SignalWrites = [];
    public readonly List<(Guid SignalId, int Resolution)> SignalCloses = [];

    /// <summary>B2b-2: a fault for the Signal writer's write (null = none).</summary>
    public Func<SignalWriteRequest, Exception?>? SignalWriteFault;

    public readonly Mock<IGenericEntityService> Entities = new(MockBehavior.Strict);
    public readonly Mock<IRecordOwnershipResolver> Ownership = new();
    public readonly Mock<IIdentityNormalizationService> Identity = new();
    public readonly ListLogger<InvoiceCorrectionService> Log = new();
    public readonly ManualTime Time = new();

    /// <summary>Every conditional value write that reached the store: entity, id, the fields written.</summary>
    public readonly List<(string Entity, Guid Id, Dictionary<string, object> Fields)> ValueWrites = [];

    /// <summary>How many transactions (correction row upserts) reached the store, counting a retry that wrote the same row again.</summary>
    public int RowTransactions;

    public Exception? ReadFault;
    public Exception? RowFault;
    public Exception? ValueFault;

    /// <summary>B2b-1: a fault for a whole transaction, chosen by its requests (null = none); the transaction then applies nothing.</summary>
    public Func<IReadOnlyList<OrganizationRequest>, Exception?>? TransactionFault;

    /// <summary>B2b-1: the settings default code set the resolver falls back to (FR-22 step 5) for an uncoded invoice's re-code.</summary>
    public Guid? DefaultCodeSetId;

    /// <summary>Runs just before a conditional write is checked, so a test can change the row under the service (a concurrent writer).</summary>
    public Action? BeforeValueWrite;

    public RecordOwnerResolution Owner = RecordOwnerResolution.Owned(Guid.Parse("10100000-0000-4000-8000-0000000000f0"));

    /// <summary>Contacts that are eligible approvers of the core record; the caller's own contact is <see cref="ReviewerContact"/>.</summary>
    public readonly List<Guid> EligibleContacts = [];

    public Exception? ApproverFault;
    public Guid? CallerContact = ReviewerContact;
    public int ApproverCalls;

    public CorrectionWorld(int invoiceStatus = InvoiceStatusCodes.InReview)
    {
        Invoice = new Entity("sprk_invoice", InvoiceId)
        {
            ["statuscode"] = new OptionSetValue(invoiceStatus),
            ["versionnumber"] = 5L,
            ["sprk_invoicenumber"] = "INV-1001",
            ["sprk_currency"] = "USD",
            ["sprk_totalamount"] = new Money(2500m),
            ["sprk_billingperiodstart"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_billingperiodend"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_project"] = new EntityReference("sprk_project", Core),
            ["sprk_vendororg"] = new EntityReference("sprk_organization", Firm),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
            ["sprk_regardingrecordid"] = Core.ToString("D"),
            ["sprk_regardingrecordname"] = "Project",
            ["sprk_regardingrecordurl"] = "https://example.invalid/p",
            ["sprk_regardingrecordnumber"] = "P-1",
        };
        Lines.Add(FeeLine());
        Lines.Add(ExpenseLine());
        Submission = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_findingsjson"] = HeaderFindingsJson,
            ["createdon"] = T0.AddDays(-2),
            ["versionnumber"] = 30L,
        };
        CodeSets.Add(new Entity("sprk_billingcodeset", CodeSetId) { ["sprk_name"] = "UTBMS Litigation", ["statecode"] = new OptionSetValue(0) });
        foreach (var (code, type) in new[]
        {
            ("L100", BillingCodeKind.Task), ("L110", BillingCodeKind.Task), ("L120", BillingCodeKind.Task),
            ("A101", BillingCodeKind.Activity), ("A102", BillingCodeKind.Activity), ("E101", BillingCodeKind.Expense),
        })
        {
            Codes.Add(new Entity("sprk_billingcode", Guid.NewGuid())
            {
                ["sprk_code"] = code, ["sprk_codetype"] = new OptionSetValue((int)type), ["sprk_description"] = code,
                ["sprk_codeset"] = new EntityReference("sprk_billingcodeset", CodeSetId), ["statecode"] = new OptionSetValue(0),
            });
        }

        Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, CallerContact));
        Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Owner);

        // B2b-1: the core record as the code-set resolver reads it (no matter type, no practice area: the resolver falls through to the settings default).
        Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => new Entity(entity, id));

        Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (ReadFault is not null)
                {
                    return Task.FromException<EntityCollection>(ReadFault);
                }

                lock (Lines)
                {
                    IEnumerable<Entity> source = query.EntityName switch
                    {
                        "sprk_invoice" => [Invoice],
                        "sprk_billingevent" => Lines,
                        "sprk_invoicecorrection" => Corrections,
                        "sprk_invoicesubmission" => Submission is null ? [] : [Submission],
                        "sprk_billingcodeset" => CodeSets,
                        "sprk_billingcode" => Codes,
                        "sprk_signal" => Signals,
                        "sprk_timekeeper" => Roster,
                        "sprk_ratecardrate" => Rates,
                        _ => [],
                    };
                    var rows = source.Where(r => Matches(r, query.Criteria)).Select(Copy).ToList();
                    return Task.FromResult(new EntityCollection(rows));
                }
            });

        Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
            {
                // ONE transaction, all or nothing: the correction row upserts and the conditional value update commit together or not at all.
                if (TransactionFault?.Invoke(requests) is { } fault)
                {
                    return Task.FromException(fault);
                }

                var upserts = requests.OfType<UpsertRequest>().ToList();
                var updates = requests.OfType<UpdateRequest>().ToList();
                if (upserts.Count > 0 && RowFault is not null)
                {
                    return Task.FromException(RowFault);
                }

                if (updates.Count > 0)
                {
                    BeforeValueWrite?.Invoke();
                    if (ValueFault is not null)
                    {
                        return Task.FromException(ValueFault);
                    }
                }

                lock (Lines)
                {
                    // The conditional updates are checked first: a stale row version fails the whole transaction and nothing is applied.
                    foreach (var update in updates)
                    {
                        if (update.ConcurrencyBehavior != ConcurrencyBehavior.IfRowVersionMatches
                            || update.Target.RowVersion != RowOf(update.Target).GetAttributeValue<long>("versionnumber").ToString())
                        {
                            return Task.FromException(new DataverseTransactionException(
                                "simulated row version mismatch: the transaction was rolled back", requests.ToList().IndexOf(update), false,
                                new FaultException<OrganizationServiceFault>(
                                    new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch })));
                        }
                    }

                    foreach (var upsert in upserts)
                    {
                        RowTransactions++;
                        Corrections.RemoveAll(c => c.Id == upsert.Target.Id);
                        Corrections.Add(Copy(upsert.Target));
                    }

                    foreach (var update in updates)
                    {
                        var row = RowOf(update.Target);
                        var fields = update.Target.Attributes.ToDictionary(a => a.Key, a => a.Value);
                        foreach (var (name, value) in fields)
                        {
                            row[name] = value;
                        }

                        row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
                        ValueWrites.Add((update.Target.LogicalName, update.Target.Id, fields));
                    }
                }

                return Task.CompletedTask;
            });
    }

    private Entity RowOf(Entity target) => target.LogicalName switch
    {
        "sprk_invoice" => Invoice,
        "sprk_invoicesubmission" => Submission!,
        _ => Lines.Single(l => l.Id == target.Id),
    };

    public const string HeaderFindingsJson =
        """
        {"provenance":{"analyzerId":"sprk_invoice_v1","apiVersion":"2025-11-01","header":{
          "FirmInvoiceNumber":{"value":"INV-1001","confidence":0.9,"combined":0.9},
          "BillingPeriodStart":{"value":"2026-09-01","confidence":0.8,"combined":0.8},
          "BillingPeriodEnd":{"value":"2026-09-30","confidence":0.8,"combined":0.8},
          "Currency":{"value":"USD","confidence":0.99,"combined":0.99},
          "TotalDue":{"value":"2500.00","confidence":0.7,"combined":0.65}}}}
        """;

    public static string ExtractionJson(bool provenance = true, bool coded = true, string? taskCode = "L110", string? firmTask = "L100") =>
        "{\"fields\":{"
        + "\"Hours\":{\"confidence\":0.62,\"combined\":0.55,\"page\":1},"
        + "\"Rate\":{\"confidence\":0.9,\"combined\":0.88},"
        + "\"Amount\":{\"confidence\":0.95,\"combined\":0.93},"
        + "\"Narrative\":{\"confidence\":0.8,\"combined\":0.79}},"
        + (coded
            ? "\"coding\":{\"status\":\"Coded\",\"codeSetId\":\"" + CodeSetId.ToString("D") + "\",\"codeSetName\":\"UTBMS Litigation\",\"codeSetSource\":\"FirmCodes\","
              + "\"needsReview\":false,\"confidence\":0.77,"
              + "\"task\":{\"code\":" + (taskCode is null ? "null" : $"\"{taskCode}\"") + ",\"firmCode\":" + (firmTask is null ? "null" : $"\"{firmTask}\"")
              + ",\"mismatch\":" + (taskCode is not null && firmTask is not null && taskCode != firmTask ? "true" : "false") + "},"
              + "\"activity\":{\"code\":\"A101\",\"firmCode\":null,\"mismatch\":false},"
              + "\"expense\":{\"code\":null,\"firmCode\":null,\"mismatch\":false},"
              + "\"firmCodeMismatch\":" + (taskCode is not null && firmTask is not null && taskCode != firmTask ? "true" : "false") + ",\"refused\":[],\"observations\":[]}"
            : "\"coding\":{\"status\":\"NotCoded\",\"hold\":null}")
        + (provenance
            ? ",\"provenance\":{\"analyzerId\":\"sprk_invoice_v1\",\"apiVersion\":\"2025-11-01\""
              + (coded ? ",\"actionCode\":\"" + LineCodingService.ActionCode + "\",\"actionVersion\":" + LineCodingService.ActionVersion : string.Empty) + "}"
            : string.Empty)
        + "}";

    public static Entity FeeLine(int status = (int)InvoiceLineStatus.Flagged, string? extraction = null) => new("sprk_billingevent", FeeLineId)
    {
        ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        ["sprk_linesequence"] = 3,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
        ["sprk_linestatus"] = new OptionSetValue(status),
        ["sprk_eventdate"] = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
        ["sprk_timekeeper"] = "A. Lee",
        ["sprk_quantity"] = 25m,
        ["sprk_rate"] = new Money(100m),
        ["sprk_amount"] = new Money(2500m),
        ["sprk_description"] = Narrative,
        ["sprk_taskcodeasbilled"] = "L100",
        ["sprk_extractionconfidence"] = extraction ?? ExtractionJson(),
        ["versionnumber"] = 10L,
    };

    public static Entity ExpenseLine() => new("sprk_billingevent", ExpenseLineId)
    {
        ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        ["sprk_linesequence"] = 4,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Expense),
        ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean),
        ["sprk_quantity"] = 1m,
        ["sprk_rate"] = new Money(40m),
        ["sprk_amount"] = new Money(40m),
        ["sprk_description"] = "Court filing fee",
        ["sprk_extractionconfidence"] = ExtractionJson(coded: true).Replace("\"code\":\"A101\"", "\"code\":null").Replace("\"expense\":{\"code\":null", "\"expense\":{\"code\":\"E101\""),
        ["versionnumber"] = 20L,
    };

    public Entity Line(Guid id = default) => Lines.Single(l => l.Id == (id == default ? FeeLineId : id));

    /// <summary>The follow-through the service runs after a saved value (the B2 seam); null is the B1 no-op.</summary>
    public Func<CorrectionApplied, CancellationToken, Task>? Seam { get; set; }

    public InvoiceCorrectionService Service()
    {
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new EbillingSettings { DefaultCodeSetId = DefaultCodeSetId });
        var suggestions = new Mock<ICodeSetSuggestionProvider>();
        var resolver = new CodeSetResolver(Entities.Object, settings.Object, suggestions.Object, NullLogger<CodeSetResolver>.Instance);
        var ai = new Mock<IInvoiceAi>();
        ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>([0f, 0f, 0f, 1f]));
        var resolution = new InvoiceResolutionService(
            Entities.Object, settings.Object, new TimekeeperMatcher(ai.Object, NullLogger<TimekeeperMatcher>.Instance), NullLogger<InvoiceResolutionService>.Instance);
        return new InvoiceCorrectionService(
            Entities.Object, resolver, Ownership.Object, Identity.Object, ResolveApproversAsync, Log, Time, Seam,
            (input, ct) =>
            {
                ResolveInputs.Add(input);
                return ResolveFault is not null ? Task.FromException<InvoiceResolutionResult>(ResolveFault) : resolution.ResolveAsync(input, ct);
            });
    }

    // ── B2c: the roster and the rate cards 046 reads ──

    public const int ApprovedStatus = 1;
    public const int PublishedCard = 659490000;

    /// <summary>A roster entry of the firm (046's fixture shape: status 1 = Approved, active).</summary>
    public Entity AddPerson(string name, Guid? id = null, Guid? firm = null, int status = ApprovedStatus, DateTime? from = null, DateTime? to = null)
    {
        var row = new Entity("sprk_timekeeper", id ?? Guid.NewGuid())
        {
            ["sprk_name"] = name,
            ["sprk_firm"] = new EntityReference("sprk_organization", firm ?? Firm),
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(status),
            ["sprk_classification"] = new OptionSetValue(1),
        };
        if (from is { } f) row["sprk_effectivefrom"] = f;
        if (to is { } t) row["sprk_effectiveto"] = t;
        Roster.Add(row);
        return row;
    }

    /// <summary>A Published, firm-wide rate card row that names <paramref name="timekeeper"/> at <paramref name="approved"/>.</summary>
    public Entity AddRate(Guid timekeeper, decimal approved)
    {
        var row = new Entity("sprk_ratecardrate", Guid.NewGuid())
        {
            ["sprk_ratecard"] = new EntityReference("sprk_ratecard", Guid.NewGuid()),
            ["sprk_timekeeper"] = new EntityReference("sprk_timekeeper", timekeeper),
            ["sprk_approvedrate"] = new Money(approved),
            ["sprk_rackrate"] = new Money(approved + 100m),
            ["statecode"] = new OptionSetValue(0),
            ["card.sprk_firm"] = new AliasedValue("sprk_ratecard", "sprk_firm", new EntityReference("sprk_organization", Firm)),
            ["card.statuscode"] = new AliasedValue("sprk_ratecard", "statuscode", new OptionSetValue(PublishedCard)),
            ["card.statecode"] = new AliasedValue("sprk_ratecard", "statecode", new OptionSetValue(0)),
        };
        Rates.Add(row);
        return row;
    }

    private Task<EligibleApproverSet> ResolveApproversAsync(string coreEntity, Guid coreId, CancellationToken ct)
    {
        ApproverCalls++;
        if (ApproverFault is not null)
        {
            return Task.FromException<EligibleApproverSet>(ApproverFault);
        }

        return Task.FromResult(new EligibleApproverSet(
            coreEntity, coreId, [], FromDefault: false,
            EligibleContacts.Select(c => new EligibleApprover(c, Guid.NewGuid(), "Approver")).ToList()));
    }

    // ── A minimal evaluator of the conditions the service uses ──

    private static bool Matches(Entity row, FilterExpression? filter)
    {
        if (filter is null)
        {
            return true;
        }

        foreach (var condition in filter.Conditions)
        {
            var value = ValueOf(row, condition.AttributeName);
            var ok = condition.Operator switch
            {
                ConditionOperator.Equal => value is not null && Equals(Norm(condition.Values[0]), value),
                ConditionOperator.In => value is not null && condition.Values.Select(Norm).Contains(value),
                ConditionOperator.Null => value is null,
                ConditionOperator.NotNull => value is not null,
                ConditionOperator.BeginsWith => value is string text && text.StartsWith((string)condition.Values[0], StringComparison.Ordinal),
                _ => throw new NotSupportedException($"The test store does not model {condition.Operator}."),
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static object? ValueOf(Entity row, string attribute)
    {
        if (attribute == row.LogicalName + "id")
        {
            return row.Id;
        }

        return row.Contains(attribute) ? Norm(row[attribute]) : null;
    }

    private static object? Norm(object? raw) => raw switch
    {
        EntityReference r => r.Id,
        OptionSetValue o => o.Value,
        Money m => m.Value,
        _ => raw,
    };

    public static Entity Copy(Entity source)
    {
        var copy = new Entity(source.LogicalName, source.Id);
        foreach (var (name, value) in source.Attributes)
        {
            copy[name] = value;
        }

        return copy;
    }

    /// <summary>An existing Signal of (policy, subject) at <paramref name="episode"/> with <paramref name="fingerprint"/>, as 049 left it.</summary>
    public Entity AddSignal(string policyCode, string subjectEntity, Guid subjectId, int episode, string fingerprint, bool resolved = false)
    {
        var row = new Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, subjectEntity, subjectId.ToString("D").ToLowerInvariant(), episode),
            ["sprk_signalstatus"] = new OptionSetValue(resolved ? SignalWriter.SignalStatusResolved : SignalWriter.SignalStatusOpen),
            ["sprk_evidencerefs"] = Evidence(fingerprint),
            ["versionnumber"] = 1L,
        };
        lock (Lines)
        {
            Signals.Add(row);
        }

        return row;
    }

    private static string Evidence(string fingerprint) =>
        "[{\"kind\":\"" + BillingFindingSignalMapper.FingerprintEvidenceKind + "\",\"ref\":\"" + fingerprint + "\"}]";

    /// <summary>The Signal rows of (policy, subject), by episode: whether resolved, the resolution type, the stored fingerprint.</summary>
    public List<(int Episode, bool Resolved, int? Resolution, string? Fingerprint)> SignalsOf(string policyCode, Guid subjectId)
    {
        lock (Lines)
        {
            var found = new List<(int, bool, int?, string?)>();
            foreach (var row in Signals)
            {
                if (SignalWriter.TryParseDedupeKey(row.GetAttributeValue<string>("sprk_dedupekey"), out var code, out _, out var id, out var episode)
                    && code == policyCode && id == subjectId)
                {
                    found.Add((episode,
                        row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value == SignalWriter.SignalStatusResolved,
                        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
                        SignalEpisodeLifecycle.FingerprintOf(row.GetAttributeValue<string>("sprk_evidencerefs"))));
                }
            }

            return found.OrderBy(f => f.Item1).ToList();
        }
    }

    /// <summary>The writer's create-or-refresh by dedupe key (a row of the same key is refreshed in place and stays Open).</summary>
    public Task<SignalWriteResult> WriteSignalAsync(SignalWriteRequest request, CancellationToken ct)
    {
        if (SignalWriteFault?.Invoke(request) is { } fault)
        {
            return Task.FromException<SignalWriteResult>(fault);
        }

        lock (Lines)
        {
            SignalWrites.Add((request.PolicyCode, request.SubjectId, request.Episode));
            var key = SignalWriter.BuildDedupeKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D").ToLowerInvariant(), request.Episode);
            var fingerprint = (request.EvidenceRefs ?? []).FirstOrDefault(e => e.Kind == BillingFindingSignalMapper.FingerprintEvidenceKind)?.Ref ?? string.Empty;
            var row = Signals.FirstOrDefault(r => r.GetAttributeValue<string>("sprk_dedupekey") == key);
            var created = row is null;
            if (row is null)
            {
                row = new Entity("sprk_signal", Guid.NewGuid()) { ["sprk_dedupekey"] = key, ["versionnumber"] = 0L };
                Signals.Add(row);
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen);
            row["sprk_evidencerefs"] = Evidence(fingerprint);
            row["sprk_name"] = request.ShortHeadline;
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            return Task.FromResult(new SignalWriteResult(row.Id, created, Guid.Empty, Guid.Empty));
        }
    }

    /// <summary>The writer's system close, conditional on the row version (ChangedSinceRead when it moved).</summary>
    public Task<SignalCloseOutcome> CloseSignalAsync(Guid id, string rowVersion, int resolution, string policyCode, CancellationToken ct)
    {
        lock (Lines)
        {
            var row = Signals.Single(r => r.Id == id);
            if (row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture) != rowVersion)
            {
                return Task.FromResult(SignalCloseOutcome.ChangedSinceRead);
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row["sprk_resolutiontype"] = new OptionSetValue(resolution);
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            SignalCloses.Add((id, resolution));
            return Task.FromResult(SignalCloseOutcome.Closed);
        }
    }

    public Entity SingleRow(Guid id) => Corrections.Single(c => c.Id == id);

    public void AddRow(Entity row) => Corrections.Add(row);
}
