using System.Data;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Tests.Finance.Files;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 150: an in-memory Dataverse and file store for the AP export tests. The REAL <see cref="ApExportService"/>,
/// <see cref="ApExportAuthorization"/>, <see cref="StoredFileService"/> (over the in-memory blob gateway, as the 090 tests build it) and mapping run over
/// it, so a test asserts the stored state (rows, row versions, files, bytes), not the calls made. Substitution is at the seams: Dataverse
/// (<see cref="IGenericEntityService"/>), the blob gateway, the caller-rights data source and the privilege probe, the lifecycle service
/// (a stand-in that moves the row and can be told to fail), the ownership resolver and the settings reader.
/// </summary>
internal sealed class ApExportWorld
{
    public const string CallerToken = "caller-bearer-token";
    public static readonly Guid CallerSystemUser = Guid.Parse("15000000-0000-4000-8000-0000000000a1");
    public static readonly Guid FirmId = Guid.Parse("15000000-0000-4000-8000-0000000000f1");
    public static readonly Guid ApproverContact = Guid.Parse("15000000-0000-4000-8000-0000000000c1");
    public static readonly Guid TeamId = Guid.Parse("15000000-0000-4000-8000-0000000000e1");
    public static readonly Guid Tenant = Guid.Parse("15000000-0000-4000-8000-0000000000d1");

    public ExportDataverse Dataverse { get; } = new();
    public InMemoryBlobGateway Gateway { get; } = new();
    public FinAuth.RecordingAccessDataSource Access { get; } = new();
    public FinAuth.RecordingPrivilegeProbe Probe { get; } = new();
    public Mock<IRecordOwnershipResolver> Owners { get; } = new(MockBehavior.Strict);
    public List<RecordOwnershipContext> OwnerQuestions { get; } = [];
    public Mock<IEbillingSettingsReader> Settings { get; } = new();
    public WorldLifecycle Lifecycle { get; }
    public StoredFileBlobs Blobs { get; }
    public StoredFileService Files { get; }
    public ApExportAuthorization Authorization { get; }
    public ApExportService Service { get; }

    /// <summary>When set, the settings mapping JSON (null = the D-56 default).</summary>
    public string? MappingJson { get; set; }

    public bool OwnerRefused { get; set; }

    public InvoiceContextService.InvoiceCaller Caller { get; } = new("caller-oid", CallerToken);

    public ApExportWorld(bool fileStoreConfigured = true)
    {
        Gateway.Now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        Blobs = new StoredFileBlobs(fileStoreConfigured ? Gateway : null, fileStoreConfigured ? Tenant : Guid.Empty, malwareScanningEnabled: true,
            NullLogger.Instance, timeProvider: new GatewayClock(Gateway));
        Owners.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Callback((RecordOwnershipContext context, CancellationToken _) => OwnerQuestions.Add(context))
            .Returns(() => Task.FromResult(OwnerRefused ? RecordOwnerResolution.Refused("record_owner_test_refusal", "test refusal") : RecordOwnerResolution.Owned(TeamId)));
        Settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new EbillingSettings { ApExportMappingJson = MappingJson }));
        Lifecycle = new WorldLifecycle(Dataverse);
        Files = new StoredFileService(Dataverse.Service, Blobs, new RecordingRightsProbe(), Owners.Object,
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection()), NullLogger<StoredFileService>.Instance);
        Authorization = new ApExportAuthorization(
            new AuthorizationService(Access, Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance), Probe,
            NullLogger<ApExportAuthorization>.Instance);
        Service = new ApExportService(Dataverse.Service, Lifecycle, Settings.Object, Authorization, Files, Blobs, Owners.Object, NullLogger<ApExportService>.Instance);

        Dataverse.Put(new Entity("sprk_organization", FirmId) { ["sprk_organizationname"] = "Acme Law LLP" });
        Dataverse.Put(new Entity("contact", ApproverContact) { ["fullname"] = "Pat Approver" });
    }

    /// <summary>The caller holds Create on the batch table and the decide right (Read + Write) on every invoice so far added.</summary>
    public void GrantEverything()
    {
        Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
        foreach (var id in Dataverse.IdsOf("sprk_invoice"))
        {
            Access.Grant("sprk_invoices", id, AccessRights.Read | AccessRights.Write);
        }
    }

    /// <summary>Adds an invoice (default: Approved, no batch) with one approval and <paramref name="lines"/> lines, and returns it.</summary>
    public Entity AddInvoice(
        string number, int status = InvoiceStatusCodes.Approved, decimal approved = 900m, int lines = 2, bool grant = true, Guid? batch = null)
    {
        var id = Guid.NewGuid();
        var invoice = new Entity("sprk_invoice", id)
        {
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateCodeOf(StatusOf(status))),
            ["sprk_invoicenumber"] = number,
            ["sprk_trackingnumber"] = number + ".1",
            ["sprk_invoicedate"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_billingperiodstart"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_billingperiodend"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_currency"] = "USD",
            ["sprk_totalamount"] = new Money(1000m),
            ["sprk_lineadjustmentstotal"] = new Money(60m),
            ["sprk_invoiceadjustmentstotal"] = new Money(40m),
            ["sprk_approvedtotal"] = new Money(approved),
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
            ["sprk_regardingrecordnumber"] = "M-1001",
            ["sprk_regardingrecordname"] = "Smith v. Jones, et al.",
        };
        if (batch is { } b)
        {
            invoice["sprk_billingexport"] = new EntityReference("sprk_billingexport", b);
        }

        Dataverse.Put(invoice);
        for (var i = 1; i <= lines; i++)
        {
            Dataverse.Put(new Entity("sprk_billingevent", Guid.NewGuid())
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", id),
                ["sprk_linesequence"] = i,
                ["sprk_eventdate"] = new DateTime(2026, 9, 10 + i, 0, 0, 0, DateTimeKind.Utc),
                ["sprk_costtype"] = new OptionSetValue(100000000),
                ["sprk_timekeeper"] = "T. Keeper",
                ["sprk_quantity"] = 1.5m * i,
                ["sprk_rate"] = new Money(300m),
                ["sprk_amount"] = new Money(450m * i),
                ["sprk_taskcodeasbilled"] = "L110",
            });
        }

        Dataverse.Put(new Entity("sprk_invoiceapproval", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", id),
            ["sprk_approvalstatus"] = new OptionSetValue(100000001),
            ["sprk_decidedby"] = new EntityReference("contact", ApproverContact),
            ["sprk_decidedon"] = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc),
        });
        if (grant)
        {
            Access.Grant("sprk_invoices", id, AccessRights.Read | AccessRights.Write);
        }

        return invoice;
    }

    private static InvoiceLifecycleStatus StatusOf(int statuscode) =>
        InvoiceStatusCodes.TryFromStatusCode(statuscode, out var s) ? s : InvoiceLifecycleStatus.InReview;

    public Entity Invoice(Guid id) => Dataverse.Get("sprk_invoice", id);

    public int StatusOfInvoice(Guid id) => Invoice(id).GetAttributeValue<OptionSetValue>("statuscode").Value;

    public Guid? BatchOfInvoice(Guid id) => Invoice(id).GetAttributeValue<EntityReference>("sprk_billingexport")?.Id;

    public List<Entity> Batches() => Dataverse.RowsOf("sprk_billingexport");

    public List<Entity> StoredFiles() => Dataverse.RowsOf(StoredFileSchema.Entity);

    /// <summary>The bytes of a stored file row's blob.</summary>
    public string TextOf(Entity file)
    {
        var path = file.GetAttributeValue<string>(StoredFileSchema.BlobPath);
        var blob = Gateway.Get(StoredFileSchema.ContainerOf(StoredFilePurpose.ExportFile), path)
                   ?? throw new InvalidOperationException($"No blob at {path}.");
        return Encoding.UTF8.GetString(blob.Content);
    }

    public Entity FileOf(Guid batchId, string contentType) =>
        StoredFiles().Single(f => f.GetAttributeValue<EntityReference>("sprk_billingexport")?.Id == batchId
                                  && f.GetAttributeValue<string>(StoredFileSchema.ContentType) == contentType);

    /// <summary>Every invoice that names a batch which is not Completed (the claim a failed batch must never leave behind).</summary>
    public IEnumerable<Guid> InvoicesClaimedByANonCompletedBatch() =>
        Dataverse.RowsOf("sprk_invoice")
            .Where(i => i.GetAttributeValue<EntityReference>("sprk_billingexport") is { } r
                        && Dataverse.Get("sprk_billingexport", r.Id).GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value != ApExportService.StatusCompleted)
            .Select(i => i.Id);

    public static ApExportRequest Select(params Entity[] invoices) => new(invoices.Select(i => i.Id).ToList(), null);

    public Task<ApExportResult> ExportAsync(params Entity[] invoices) =>
        Service.CreateBatchAsync(Select(invoices), Caller, CallerSystemUser, CancellationToken.None);
}

/// <summary>An in-memory <see cref="IGenericEntityService"/> with row versions, optimistic concurrency, evaluated queries and fault / interleaving hooks.</summary>
internal sealed class ExportDataverse
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Table, Guid Id), Entity> _rows = new();
    private long _version = 5000;

    public ExportDataverse()
    {
        Mock.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] _, CancellationToken _) => Run("retrieve", table, id, () => Copy(Find(table, id))));
        Mock.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity entity, CancellationToken _) => Run("create", entity.LogicalName, entity.Id, () =>
            {
                Put(Copy(entity));
                return entity.Id;
            }));
        Mock.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object> fields, CancellationToken _) => Run("update", table, id, () =>
            {
                Apply(Find(table, id), fields);
                return true;
            }));
        Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                BeforeConditionalUpdate?.Invoke(table, id);
                return Run("update-if", table, id, () =>
                {
                    var row = Find(table, id);
                    if (!string.Equals(row.RowVersion, version, StringComparison.Ordinal))
                    {
                        throw new DBConcurrencyException("ConcurrencyVersionMismatch");
                    }

                    Apply(row, fields);
                    return true;
                });
            });
        Mock.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                lock (_gate)
                {
                    Log.Add($"query {q.EntityName}");
                    if (FaultOn?.Invoke("query", q.EntityName, Guid.Empty) is { } fault)
                    {
                        return Task.FromException<EntityCollection>(fault);
                    }

                    var matches = _rows.Values.Where(r => r.LogicalName == q.EntityName && q.Criteria.Conditions.All(c => Matches(r, c))).ToList();
                    foreach (var order in q.Orders.Reverse())
                    {
                        matches = (order.OrderType == OrderType.Ascending
                            ? matches.OrderBy(r => r.Contains(order.AttributeName) ? r[order.AttributeName] : null, Comparer<object?>.Create(CompareValues))
                            : matches.OrderByDescending(r => r.Contains(order.AttributeName) ? r[order.AttributeName] : null, Comparer<object?>.Create(CompareValues))).ToList();
                    }

                    if (q.TopCount is { } top)
                    {
                        matches = matches.Take(top).ToList();
                    }

                    return Task.FromResult(new EntityCollection(matches.Select(Copy).ToList()));
                }
            });
        Mock.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, CancellationToken _) => Run("delete", table, id, () =>
            {
                _rows.Remove((table, id));
                return true;
            }));
    }

    public Mock<IGenericEntityService> Mock { get; } = new(MockBehavior.Strict);

    public IGenericEntityService Service => Mock.Object;

    /// <summary>Every call, as "op table".</summary>
    public List<string> Log { get; } = [];

    /// <summary>Runs before a conditional update, outside the lock: lets a rival write between this caller's read and its claim.</summary>
    public Action<string, Guid>? BeforeConditionalUpdate { get; set; }

    /// <summary>Returns a fault to throw for (op, table, id), or null.</summary>
    public Func<string, string, Guid, Exception?>? FaultOn { get; set; }

    public int Writes
    {
        get
        {
            lock (_gate)
            {
                return Log.Count(l => l.StartsWith("create", StringComparison.Ordinal) || l.StartsWith("update", StringComparison.Ordinal) || l.StartsWith("delete", StringComparison.Ordinal));
            }
        }
    }

    public int WritesTo(string table)
    {
        lock (_gate)
        {
            return Log.Count(l => (l.StartsWith("create ", StringComparison.Ordinal) || l.StartsWith("update", StringComparison.Ordinal)) && l.EndsWith(" " + table, StringComparison.Ordinal));
        }
    }

    public void Put(Entity row)
    {
        lock (_gate)
        {
            row.RowVersion = (++_version).ToString(System.Globalization.CultureInfo.InvariantCulture);
            row["versionnumber"] = _version;
            _rows[(row.LogicalName, row.Id)] = row;
        }
    }

    public Entity Get(string table, Guid id)
    {
        lock (_gate)
        {
            return Copy(Find(table, id));
        }
    }

    public List<Entity> RowsOf(string table)
    {
        lock (_gate)
        {
            return _rows.Values.Where(r => r.LogicalName == table).Select(Copy).ToList();
        }
    }

    public List<Guid> IdsOf(string table) => RowsOf(table).Select(r => r.Id).ToList();

    /// <summary>Changes a row out of band (a rival writer): the row version moves.</summary>
    public void Touch(string table, Guid id, Dictionary<string, object> fields)
    {
        lock (_gate)
        {
            Apply(Find(table, id), fields);
        }
    }

    private Task<T> Run<T>(string op, string table, Guid id, Func<T> body)
    {
        lock (_gate)
        {
            Log.Add($"{op} {table}");
            if (FaultOn?.Invoke(op, table, id) is { } fault)
            {
                return Task.FromException<T>(fault);
            }

            try
            {
                return Task.FromResult(body());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }

    private Entity Find(string table, Guid id) =>
        _rows.TryGetValue((table, id), out var row) ? row : throw StoredFileDataverse.NotFound();

    private void Apply(Entity row, Dictionary<string, object> fields)
    {
        foreach (var (key, value) in fields)
        {
            if (value is DBNull)
            {
                row.Attributes.Remove(key);
            }
            else
            {
                row[key] = value;
            }
        }

        row.RowVersion = (++_version).ToString(System.Globalization.CultureInfo.InvariantCulture);
        row["versionnumber"] = _version;
    }

    private static Entity Copy(Entity source)
    {
        var copy = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
        foreach (var attribute in source.Attributes)
        {
            copy[attribute.Key] = attribute.Value;
        }

        return copy;
    }

    private static bool Matches(Entity row, ConditionExpression condition)
    {
        var has = row.Contains(condition.AttributeName) && row[condition.AttributeName] is not null;
        var value = has ? Comparable(row[condition.AttributeName]) : null;
        return condition.Operator switch
        {
            ConditionOperator.Null => !has,
            ConditionOperator.NotNull => has,
            ConditionOperator.Equal => has && condition.Values.Any(v => Equals(value, Comparable(v))),
            ConditionOperator.In => has && condition.Values.Any(v => Equals(value, Comparable(v))),
            _ => throw new NotSupportedException($"Condition operator {condition.Operator} is not modelled."),
        };
    }

    private static object? Comparable(object? raw) => raw switch
    {
        EntityReference r => r.Id,
        OptionSetValue o => o.Value,
        Money m => m.Value,
        _ => raw,
    };

    private static int CompareValues(object? a, object? b)
    {
        a = Comparable(a);
        b = Comparable(b);
        if (a is null || b is null)
        {
            return a is null && b is null ? 0 : a is null ? -1 : 1;
        }

        return a is IComparable c && a.GetType() == b.GetType() ? c.CompareTo(b) : 0;
    }
}

/// <summary>A lifecycle stand-in over the world: Approved to Sent to AP moves the row (version bumped); named invoices can be made to fail.</summary>
internal sealed class WorldLifecycle(ExportDataverse dataverse) : IInvoiceLifecycleService
{
    public HashSet<Guid> Fails { get; } = [];

    public List<Guid> Sent { get; } = [];

    public Task<InvoiceTransitionResult> TransitionAsync(Guid invoiceId, InvoiceLifecycleStatus target, CancellationToken ct = default)
    {
        if (Fails.Contains(invoiceId))
        {
            throw new InvalidOperationException("dataverse down");
        }

        var row = dataverse.Get("sprk_invoice", invoiceId);
        var current = row.GetAttributeValue<OptionSetValue>("statuscode").Value;
        if (target != InvoiceLifecycleStatus.SentToAp || current != InvoiceStatusCodes.Approved)
        {
            return Task.FromResult(InvoiceTransitionResultFactory.Refused(target, invoiceId));
        }

        dataverse.Touch("sprk_invoice", invoiceId, new Dictionary<string, object>
        {
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.SentToAp),
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateActive),
        });
        Sent.Add(invoiceId);
        return Task.FromResult(InvoiceTransitionResultFactory.Moved());
    }

    public InvoiceCreateRequestResult BuildInvoiceCreateRequest(Entity invoice, InvoiceLifecycleStatus initialStatus) =>
        throw new NotSupportedException();
}

/// <summary>The result record's factories are internal to the BFF; the BFF's own results are built here through the real service's contract.</summary>
internal static class InvoiceTransitionResultFactory
{
    public static InvoiceTransitionResult Moved() =>
        new(InvoiceTransitionOutcome.Transitioned, InvoiceLifecycleStatus.SentToAp, InvoiceLifecycleStatus.Approved, null);

    public static InvoiceTransitionResult Refused(InvoiceLifecycleStatus target, Guid _) =>
        new(InvoiceTransitionOutcome.Refused, target, null, InvoiceTransitionReasons.NotAllowed);
}
