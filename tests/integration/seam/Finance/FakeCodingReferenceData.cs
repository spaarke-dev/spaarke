using System.Data;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// An in-memory stand-in for the Dataverse tables the coding path reads (ebilling-r1 task 047): <c>sprk_billingcodeset</c>,
/// <c>sprk_billingcode</c>, <c>sprk_mattertype_ref</c>, <c>sprk_practicearea_ref</c> and the three core records. It sits behind a
/// mocked <see cref="IGenericEntityService"/> and EVALUATES the <see cref="QueryExpression"/> the production code sends (entity,
/// equality / in conditions, ordering, paging, column set), so a query that filters on the wrong column or forgets a column returns
/// the wrong rows here instead of passing against a canned response.
/// </summary>
/// <remarks>
/// Only <see cref="ConditionOperator.Equal"/>, <see cref="ConditionOperator.In"/>, <see cref="ConditionOperator.Null"/> and <see cref="ConditionOperator.NotNull"/> over a flat AND filter are supported; anything
/// else throws, so a new kind of query in the production code makes this fake fail loudly rather than silently match nothing.
/// Thread-safe (task 054 fix-round-1): every read, write and builder takes one lock, because the proposals list matches rows in parallel.
/// </remarks>
internal sealed class FakeCodingReferenceData
{
    /// <summary>What Dataverse returns per page when a query sets no page size (it silently truncates there).</summary>
    private const int DataverseDefaultPageSize = 5000;

    private readonly object _sync = new();
    private readonly List<Entity> _rows = [];
    private int _created;

    public FakeCodingReferenceData()
    {
        Entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => Query(query));
        Entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Guid id, string[] columns, CancellationToken _) => Get(name, id, columns));
        Entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string name, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                lock (_sync)
                {
                    Updates.Add((name, id, new Dictionary<string, object>(fields)));
                    Apply(name, id, fields);
                    return Task.CompletedTask;
                }
            });
        Records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string name, Guid id, Dictionary<string, object?> fields, long expectedVersion, CancellationToken _) =>
            {
                lock (_sync)
                {
                    ConditionalWrites.Add((name, id, expectedVersion));
                    var row = Find(name, id);
                    if (row is null)
                    {
                        return Task.FromException(new KeyNotFoundException($"{name} record was not found."));
                    }

                    if (row.GetAttributeValue<long>(VersionColumn) != expectedVersion)
                    {
                        // What DataverseWebApiService does on HTTP 412: nothing is written.
                        return Task.FromException(new DBConcurrencyException($"{name} record changed since it was read; the update was not applied."));
                    }

                    var applied = fields.ToDictionary(f => WebApiColumn(f.Key), f => WebApiValue(f.Key, f.Value));
                    Updates.Add((name, id, applied));
                    Apply(name, id, applied);
                    return Task.CompletedTask;
                }
            });
    }

    /// <summary>
    /// The Web API write seam (<c>IFieldMappingDataverseService</c>). Only the conditional update is doubled: it honours the row's
    /// <c>versionnumber</c> like Dataverse's If-Match (a stale version throws <see cref="DBConcurrencyException"/>, nothing written), turns
    /// a <c>{nav}@odata.bind</c> of <c>/{entityset}({id})</c> into the lookup column, and records the write in <see cref="Updates"/>.
    /// </summary>
    public Mock<IFieldMappingDataverseService> Records { get; } = new();

    /// <summary>Every conditional write attempted (entity, id, the version it expected), including refused ones.</summary>
    public List<(string Entity, Guid Id, long ExpectedVersion)> ConditionalWrites { get; } = [];

    /// <summary>Called after each query has been answered: lets a test land a concurrent change between a read and a write.</summary>
    public Action<QueryExpression>? AfterQuery { get; set; }

    private const string VersionColumn = "versionnumber";
    private long _version = 1000;

    private static string WebApiColumn(string key) =>
        key.EndsWith("@odata.bind", StringComparison.Ordinal) ? key[..^"@odata.bind".Length].ToLowerInvariant() : key;

    private static object WebApiValue(string key, object? value)
    {
        if (!key.EndsWith("@odata.bind", StringComparison.Ordinal))
        {
            return value ?? DBNull.Value;
        }

        // "/sprk_billingcodesets(<guid>)" -> EntityReference("sprk_billingcodeset", <guid>)
        var bind = (string)value!;
        var open = bind.IndexOf('(');
        var entitySet = bind[1..open];
        var id = Guid.Parse(bind[(open + 1)..^1]);
        return new EntityReference(entitySet.EndsWith('s') ? entitySet[..^1] : entitySet, id);
    }

    /// <summary>Every <c>UpdateAsync</c> received, in order (the fake also applies it to the stored row).</summary>
    public List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Updates { get; } = [];

    public Mock<IGenericEntityService> Entities { get; } = new();

    /// <summary>Every <c>RetrieveMultipleAsync</c> query received, in order.</summary>
    public List<QueryExpression> Queries { get; } = [];

    /// <summary>How many <c>RetrieveMultipleAsync</c> calls were made.</summary>
    public int QueryCount => Queries.Count;

    // ── Builders ────────────────────────────────────────────────────────────────

    public Guid AddSet(string name, bool active = true, string? body = null, string? description = null, string? matchHints = null)
    {
        lock (_sync)
        {
            return AddSetLocked(name, active, body, description, matchHints);
        }
    }

    private Guid AddSetLocked(string name, bool active, string? body, string? description, string? matchHints)
    {
        var set = new Entity(CodeSetTable, Guid.NewGuid());
        set["sprk_name"] = name;
        if (body is not null)
        {
            set["sprk_body"] = body;
        }

        if (description is not null)
        {
            set["sprk_description"] = description;
        }

        if (matchHints is not null)
        {
            set["sprk_matchhints"] = matchHints;
        }

        set["statecode"] = new OptionSetValue(active ? 0 : 1);
        set["createdon"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_created++);
        set["modifiedon"] = set["createdon"];
        _rows.Add(set);
        return set.Id;
    }

    public void AddCode(
        Guid setId,
        string code,
        BillingCodeKind kind,
        string description = "",
        string? guidance = null,
        bool active = true)
    {
        lock (_sync)
        {
            AddCodeLocked(setId, code, kind, description, guidance, active);
        }
    }

    /// <summary>
    /// Edits a code's description IN PLACE, without touching its set row (as an admin editing the code record does): the set's
    /// <c>modifiedon</c> stays the same, so only a cache key that covers the Task-code text sees the change.
    /// </summary>
    public void EditCode(Guid setId, string code, string description)
    {
        lock (_sync)
        {
            var row = _rows.Single(r => r.LogicalName == CodeTable
                && r.GetAttributeValue<EntityReference>("sprk_codeset")?.Id == setId
                && r.GetAttributeValue<string>("sprk_code") == code);
            row["sprk_description"] = description;
        }
    }

    private void AddCodeLocked(Guid setId, string code, BillingCodeKind kind, string description, string? guidance, bool active)
    {
        var row = new Entity(CodeTable, Guid.NewGuid());
        row["sprk_code"] = code;
        row["sprk_codeset"] = new EntityReference(CodeSetTable, setId);
        row["sprk_codetype"] = new OptionSetValue((int)kind);
        row["sprk_description"] = description;
        if (guidance is not null)
        {
            row["sprk_guidancetext"] = guidance;
        }

        row["statecode"] = new OptionSetValue(active ? 0 : 1);
        _rows.Add(row);
    }

    public Guid AddMatterType(Guid? defaultSetId, string? name = null, bool active = true) =>
        AddReference("sprk_mattertype_ref", defaultSetId, name is null ? null : ("sprk_mattertypename", name), active);

    public Guid AddPracticeArea(Guid? defaultSetId, string? name = null, bool active = true) =>
        AddReference("sprk_practicearea_ref", defaultSetId, name is null ? null : ("sprk_practiceareaname", name), active);

    /// <summary>The reference or set row as stored (for asserting what a write changed).</summary>
    public Entity? Find(string entityLogicalName, Guid id)
    {
        lock (_sync)
        {
            return _rows.FirstOrDefault(r => r.LogicalName == entityLogicalName && r.Id == id);
        }
    }

    /// <summary>Applies an <c>UpdateAsync</c> to the stored row, as Dataverse would (a C# null skips, DBNull clears).</summary>
    public void Apply(string entityLogicalName, Guid id, Dictionary<string, object> fields)
    {
        lock (_sync)
        {
            var row = Find(entityLogicalName, id) ?? throw new InvalidOperationException($"{entityLogicalName} {id} does not exist in the fake.");
            foreach (var (key, value) in fields)
            {
                if (value is null)
                {
                    continue;
                }

                row[key] = value is DBNull ? null : value;
            }

            // Every write moves the row to a new version and a later modifiedon, as in Dataverse.
            row[VersionColumn] = ++_version;
            row["modifiedon"] = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_version);
        }
    }

    public CodingCoreRecord AddCoreRecord(string entityLogicalName, Guid? matterTypeId = null, Guid? practiceAreaId = null)
    {
        lock (_sync)
        {
            return AddCoreRecordLocked(entityLogicalName, matterTypeId, practiceAreaId);
        }
    }

    private CodingCoreRecord AddCoreRecordLocked(string entityLogicalName, Guid? matterTypeId, Guid? practiceAreaId)
    {
        var row = new Entity(entityLogicalName, Guid.NewGuid());
        if (matterTypeId is { } matterType)
        {
            row["sprk_mattertype"] = new EntityReference("sprk_mattertype_ref", matterType);
        }

        if (practiceAreaId is { } practiceArea)
        {
            row["sprk_practicearea"] = new EntityReference("sprk_practicearea_ref", practiceArea);
        }

        _rows.Add(row);
        return new CodingCoreRecord(entityLogicalName, row.Id);
    }

    /// <summary>
    /// The shapes the seeded UTBMS sets have (task 018): task codes in per-area sets, activity and expense codes in their own
    /// shared sets. Small, but with the same overlap risks (a task code present in two area sets).
    /// </summary>
    public Utbms SeedUtbms()
    {
        var litigation = AddSet("UTBMS Litigation");
        AddCode(litigation, "L110", BillingCodeKind.Task, "Fact Investigation/Development", "Use for gathering facts.");
        AddCode(litigation, "L120", BillingCodeKind.Task, "Analysis/Strategy", "Use for case strategy.");
        AddCode(litigation, "L210", BillingCodeKind.Task, "Pleadings", "Complaints, answers and replies.");
        AddCode(litigation, "L320", BillingCodeKind.Task, "Document Production", null);

        var counseling = AddSet("UTBMS Counseling");
        AddCode(counseling, "C100", BillingCodeKind.Task, "Fact Gathering", "Counseling fact gathering.");
        AddCode(counseling, "C200", BillingCodeKind.Task, "Researching Law", null);
        AddCode(counseling, "L120", BillingCodeKind.Task, "Analysis/Strategy (shared with litigation)", null);

        var activity = AddSet("UTBMS Activity");
        AddCode(activity, "A101", BillingCodeKind.Activity, "Plan and prepare for");
        AddCode(activity, "A102", BillingCodeKind.Activity, "Research");
        AddCode(activity, "A103", BillingCodeKind.Activity, "Draft/revise");
        AddCode(activity, "A104", BillingCodeKind.Activity, "Review/analyze");

        var expense = AddSet("UTBMS Expense");
        AddCode(expense, "E101", BillingCodeKind.Expense, "Copying");
        AddCode(expense, "E112", BillingCodeKind.Expense, "Court fees");

        return new Utbms(litigation, counseling, activity, expense);
    }

    public sealed record Utbms(Guid Litigation, Guid Counseling, Guid Activity, Guid Expense);

    // ── The query evaluator ─────────────────────────────────────────────────────

    private const string CodeSetTable = "sprk_billingcodeset";
    private const string CodeTable = "sprk_billingcode";

    private Guid AddReference(string entityLogicalName, Guid? defaultSetId, (string Column, string Value)? name = null, bool active = true)
    {
        lock (_sync)
        {
            return AddReferenceLocked(entityLogicalName, defaultSetId, name, active);
        }
    }

    private Guid AddReferenceLocked(string entityLogicalName, Guid? defaultSetId, (string Column, string Value)? name, bool active)
    {
        var row = new Entity(entityLogicalName, Guid.NewGuid());
        if (name is { } named)
        {
            row[named.Column] = named.Value;
        }

        row["statecode"] = new OptionSetValue(active ? 0 : 1);
        row[VersionColumn] = ++_version;
        if (defaultSetId is { } setId)
        {
            row["sprk_defaultbillingcodeset"] = new EntityReference(CodeSetTable, setId);
        }

        _rows.Add(row);
        return row.Id;
    }

    private Entity Get(string entityLogicalName, Guid id, string[] columns)
    {
        lock (_sync)
        {
            var row = _rows.FirstOrDefault(r => r.LogicalName == entityLogicalName && r.Id == id)
                ?? throw new InvalidOperationException($"{entityLogicalName} {id} does not exist in the fake.");
            return Project(row, columns);
        }
    }

    private EntityCollection Query(QueryExpression query)
    {
        lock (_sync)
        {
            return QueryLocked(query);
        }
    }

    private EntityCollection QueryLocked(QueryExpression query)
    {
        Queries.Add(query);

        if (query.Criteria.Filters.Count > 0 || query.Criteria.FilterOperator != LogicalOperator.And)
        {
            throw new NotSupportedException("The fake supports one flat AND filter.");
        }

        IEnumerable<Entity> rows = _rows.Where(r => r.LogicalName == query.EntityName);
        foreach (var condition in query.Criteria.Conditions)
        {
            var captured = condition;
            rows = rows.Where(r => Matches(r, captured));
        }

        IOrderedEnumerable<Entity>? ordered = null;
        foreach (var order in query.Orders)
        {
            var attribute = order.AttributeName;
            var descending = order.OrderType == OrderType.Descending;
            ordered = ordered is null
                ? (descending ? rows.OrderByDescending(r => SortKey(r, attribute)) : rows.OrderBy(r => SortKey(r, attribute)))
                : (descending ? ordered.ThenByDescending(r => SortKey(r, attribute)) : ordered.ThenBy(r => SortKey(r, attribute)));
        }

        var all = (ordered ?? rows).ToList();
        if (query.TopCount is { } top)
        {
            all = all.Take(top).ToList();
        }

        var count = query.PageInfo?.Count > 0 ? query.PageInfo.Count : DataverseDefaultPageSize;
        var page = Math.Max(1, query.PageInfo?.PageNumber ?? 1);
        var slice = all.Skip((page - 1) * count).Take(count).Select(r => Project(r, query.ColumnSet)).ToList();

        var result = new EntityCollection(slice) { EntityName = query.EntityName };
        if (all.Count > page * count)
        {
            result.MoreRecords = true;
            result.PagingCookie = $"page-{page}";
        }

        AfterQuery?.Invoke(query);
        return result;
    }

    private static bool Matches(Entity row, ConditionExpression condition)
    {
        var actual = Normalize(AttributeValue(row, condition.AttributeName));
        var expected = condition.Values.Select(Normalize).ToList();

        return condition.Operator switch
        {
            ConditionOperator.Equal => expected.Count == 1 && AreEqual(actual, expected[0]),
            ConditionOperator.In => expected.Any(e => AreEqual(actual, e)),
            ConditionOperator.Null => actual is null,
            ConditionOperator.NotNull => actual is not null,
            _ => throw new NotSupportedException($"The fake does not support {condition.Operator}."),
        };
    }

    private static object? AttributeValue(Entity row, string attribute)
    {
        // The primary key is not an attribute of a retrieved row but a query may filter on it.
        if (attribute == row.LogicalName + "id")
        {
            return row.Id;
        }

        return row.Contains(attribute) ? row[attribute] : null;
    }

    private static object? Normalize(object? value) => value switch
    {
        EntityReference reference => reference.Id,
        OptionSetValue option => option.Value,
        Money money => money.Value,
        _ => value,
    };

    private static bool AreEqual(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase),
        _ => Equals(left, right),
    };

    private static object SortKey(Entity row, string attribute) =>
        Normalize(AttributeValue(row, attribute)) switch
        {
            null => string.Empty,
            string text => text.ToLowerInvariant(),
            var other => other,
        };

    private static Entity Project(Entity row, ColumnSet columns) =>
        columns.AllColumns ? Project(row, row.Attributes.Keys.ToArray()) : Project(row, columns.Columns.ToArray());

    private static Entity Project(Entity row, string[] columns)
    {
        var projected = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns)
        {
            if (row.Contains(column))
            {
                projected[column] = row[column];
            }
        }

        return projected;
    }
}
