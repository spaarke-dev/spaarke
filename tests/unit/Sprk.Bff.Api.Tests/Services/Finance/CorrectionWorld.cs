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
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;

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

    private Entity RowOf(Entity target) => target.LogicalName == "sprk_invoice" ? Invoice : Lines.Single(l => l.Id == target.Id);

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

    public InvoiceCorrectionService Service()
    {
        var settings = new Mock<IEbillingSettingsReader>();
        var suggestions = new Mock<ICodeSetSuggestionProvider>();
        var resolver = new CodeSetResolver(Entities.Object, settings.Object, suggestions.Object, NullLogger<CodeSetResolver>.Instance);
        return new InvoiceCorrectionService(
            Entities.Object, resolver, Ownership.Object, Identity.Object, ResolveApproversAsync, Log, Time);
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
                ConditionOperator.Null => value is null,
                ConditionOperator.NotNull => value is not null,
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

    public Entity SingleRow(Guid id) => Corrections.Single(c => c.Id == id);

    public void AddRow(Entity row) => Corrections.Add(row);
}
