using System.Data.SqlTypes;
using System.ServiceModel;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// An in-memory Dataverse for the core-ancestor stamp's WRITE side (unified-access-control-r2 task 156): rows that the
/// <see cref="CoreAncestorRestamper"/> and the <see cref="CoreAncestorStampReconciliationJob"/> read, list and PATCH, with
/// every PATCH recorded and APPLIED (so a second pass sees the first one's work). Only the Dataverse module boundary is
/// doubled; <see cref="CoreAncestorResolver"/>, the restamper and the job are the real code.
/// </summary>
/// <remarks>
/// Faithful where it matters: a Retrieve returns ONLY the requested columns (a column left out of a read reads as "not
/// set", task 155 f2); a missing row throws Dataverse's ObjectDoesNotExist fault; <see cref="DBNull.Value"/> clears a
/// column, as <see cref="IGenericEntityService.UpdateAsync"/> documents. A FetchXML scan HONOURS its filter (and / or
/// filters; <c>null</c>, <c>not-null</c>, <c>in</c>, <c>eq</c> and <c>gt</c> conditions) and its paging (<c>count</c> /
/// <c>page</c>, ordered by the row id as SQL orders a <c>uniqueidentifier</c>), and a paged QueryExpression its
/// <see cref="PagingInfo"/> — so what the job's scan READS, and where a page bound stops it, are both asserted (verifier
/// round 1 items 3 and 4: a double that returned every row on one page hid both).
/// </remarks>
internal sealed class StampWorld
{
    /// <summary>Columns each table has in a new world (the probe answers from <see cref="_columns"/>, a copy of these).</summary>
    private static readonly Dictionary<string, string[]> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_todo"] =
        [
            "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment",
            "sprk_regardingservicerequest", "sprk_regardinganalysis", "sprk_regardingcommunication",
            "sprk_regardingdocument", "sprk_regardingevent", "sprk_regardinginvoice", "sprk_regardingagreement",
            "sprk_regardingbudget", "sprk_regardingreportcard", "sprk_regardingrecordid", "sprk_regardingrecordtype",
            "sprk_regardingcontact", "sprk_regardingorganization",
        ],
        ["sprk_event"] =
        [
            "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment",
            "sprk_regardingservicerequest", "sprk_regardinganalysis", "sprk_regardingcommunication",
            "sprk_regardingevent", "sprk_regardinginvoice", "sprk_regardingagreement", "sprk_regardingbudget",
            "sprk_regardingreportcard", "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardingcontact",
            "sprk_regardingorganization", "sprk_regardingaccount",
        ],
        ["sprk_communication"] =
        [
            "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment",
            "sprk_regardingservicerequest", "sprk_regardinginvoice", "sprk_regardingevent", "sprk_regardinganalysis",
            "sprk_regardingbudget", "sprk_regardingreportcard", "sprk_regardingrecordid", "sprk_regardingrecordtype",
            "sprk_regardingperson", "sprk_regardingorganization", "sprk_regardingaccount",
        ],
        ["sprk_analysis"] =
        [
            "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment",
            "sprk_regardingservicerequest", "sprk_regardingbudget", "sprk_regardingcommunication",
            "sprk_regardingdocument", "sprk_regardinginvoice", "sprk_regardingrecordid", "sprk_regardingrecordtype",
            "sprk_documentid",
        ],
        ["sprk_invoice"] = ["sprk_project", "sprk_matter", "sprk_regardingagreement"],
        ["sprk_document"] =
        [
            "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_relatedmatter", "sprk_relatedproject",
            "sprk_relatedworkassignment",
        ],
        ["sprk_agreement"] = ["sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingdocument"],
        ["sprk_reportcard"] = ["sprk_regardingmatter", "sprk_regardingproject"],
        ["sprk_budget"] = ["sprk_matter", "sprk_project"],
    };

    /// <summary>This world's columns per table: <see cref="Columns"/>, less any <see cref="WithoutColumn"/> dropped.</summary>
    private readonly Dictionary<string, HashSet<string>> _columns = Columns.ToDictionary(
        c => c.Key, c => new HashSet<string>(c.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string Entity, Guid Id), Entity> _rows = new();
    private readonly Dictionary<(string Entity, Guid Id), Exception> _readFaults = new();
    private readonly Dictionary<(string Entity, Guid Id), Exception> _writeFaults = new();
    private readonly Dictionary<string, Exception> _scanFaults = new(StringComparer.OrdinalIgnoreCase);

    public StampWorld()
    {
        Service = Substitute.For<IGenericEntityService>();

        Service.RetrieveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = (call.ArgAt<string>(0), call.ArgAt<Guid>(1));
                if (_readFaults.TryGetValue(key, out var fault)) throw fault;
                return _rows.TryGetValue(key, out var row)
                    ? Task.FromResult(Project(row, call.ArgAt<string[]>(2)))
                    : throw NotFound(key.Item1);
            });

        // A create stores the row as sent (a fresh id unless the entity carries one) — task 156, owner round 8 item 2: a
        // TaskActionCore create lands here, so the job then reads exactly what that writer wrote.
        Service.CreateAsync(Arg.Any<Entity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var sent = call.ArgAt<Entity>(0);
                var id = sent.Id != Guid.Empty ? sent.Id : Guid.NewGuid();
                var row = new Entity(sent.LogicalName, id);
                foreach (var attribute in sent.Attributes) row[attribute.Key] = attribute.Value;
                _rows[(sent.LogicalName, id)] = row;
                Created.Add(Clone(row));
                return Task.FromResult(id);
            });

        Service.UpdateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = (call.ArgAt<string>(0), call.ArgAt<Guid>(1));
                var fields = call.ArgAt<Dictionary<string, object>>(2);
                Patches.Add((key.Item1, key.Item2, new Dictionary<string, object>(fields)));
                if (_writeFaults.TryGetValue(key, out var fault)) throw fault;

                var row = _rows[key];
                foreach (var (column, value) in fields)
                {
                    if (value is DBNull) row.Attributes.Remove(column);
                    else row[column] = value;
                }

                return Task.CompletedTask;
            });

        Service.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<QueryExpression>(0);
                if (_scanFaults.TryGetValue(query.EntityName, out var fault)) throw fault;

                var matches = _rows.Values
                    .Where(r => r.LogicalName == query.EntityName)
                    .Where(r => query.Criteria.Conditions.All(c =>
                        c.Operator == ConditionOperator.Equal
                        && r.GetAttributeValue<EntityReference>(c.AttributeName)?.Id == (Guid)c.Values[0]))
                    .OrderBy(r => new SqlGuid(r.Id))
                    .Select(r => query.ColumnSet.AllColumns ? Clone(r) : Project(r, [.. query.ColumnSet.Columns]))
                    .ToList();

                return Task.FromResult(Page(matches, query.PageInfo?.Count ?? 0, query.PageInfo?.PageNumber ?? 1));
            });

        Service.RetrieveMultipleAsync(Arg.Any<FetchExpression>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var fetch = XDocument.Parse(call.ArgAt<FetchExpression>(0).Query);
                var entity = fetch.Root!.Element("entity")!;
                var table = entity.Attribute("name")!.Value;
                FetchXml.Add(call.ArgAt<FetchExpression>(0).Query);
                if (_scanFaults.TryGetValue(table, out var fault)) throw fault;

                var filter = entity.Element("filter");
                var attributes = entity.Elements("attribute").Select(a => a.Attribute("name")!.Value).ToArray();
                var rows = _rows.Values
                    .Where(r => r.LogicalName == table)
                    .Where(r => filter is null || Matches(r, table, filter))
                    .OrderBy(r => new SqlGuid(r.Id))
                    .Select(r => Project(r, attributes))
                    .ToList();

                var count = int.TryParse(fetch.Root.Attribute("count")?.Value, out var c) ? c : 0;
                var page = int.TryParse(fetch.Root.Attribute("page")?.Value, out var p) ? p : 1;
                return Task.FromResult(Page(rows, count, page));
            });

        Resolver = new CoreAncestorResolver(
            Service,
            (entity, _) => Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(
                    _columns.TryGetValue(entity, out var c) ? c : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);

        Restamper = new CoreAncestorRestamper(Service, Resolver, NullLogger<CoreAncestorRestamper>.Instance);
    }

    public IGenericEntityService Service { get; }

    public CoreAncestorResolver Resolver { get; }

    public CoreAncestorRestamper Restamper { get; }

    /// <summary>The user-OBO AI update tool's one app-only helper, over <see cref="Restamper"/> (owner round 8 item 1).</summary>
    public CoreAncestorAfterWriteRestamp AfterWriteRestamp => new(Restamper);

    /// <summary>Every row created through <see cref="IGenericEntityService.CreateAsync"/>, as it was stored.</summary>
    public List<Entity> Created { get; } = [];

    /// <summary>The row stored under <paramref name="entity"/> <paramref name="id"/> (a copy).</summary>
    public Entity RowOf(string entity, Guid id) => Clone(_rows[(entity, id)]);

    /// <summary>A restamper over this world with smaller paging bounds, so the per-intermediate bound is reachable.</summary>
    public CoreAncestorRestamper RestamperWith(int childPageSize, int childrenPerSourceBound) =>
        new(Service, Resolver, NullLogger<CoreAncestorRestamper>.Instance)
        {
            ChildPageSizeInUse = childPageSize,
            ChildrenPerSourceBound = childrenPerSourceBound,
        };

    /// <summary>Every PATCH, in order, with its fields as sent.</summary>
    public List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Patches { get; } = [];

    /// <summary>Every FetchXML scan sent.</summary>
    public List<string> FetchXml { get; } = [];

    /// <summary>Add (or replace) a row: <paramref name="lookups"/> are column → (target, id); <paramref name="pairId"/> is the pair's string id.</summary>
    public StampWorld Row(
        string entity, Guid id, (string Column, string Target, Guid Id)[]? lookups = null, string? pairId = null,
        Guid? pairType = null)
    {
        var row = new Entity(entity, id);
        foreach (var (column, target, targetId) in lookups ?? [])
        {
            row[column] = new EntityReference(target, targetId);
        }

        if (pairId is not null) row[CoreAncestorResolver.RegardingRecordIdColumn] = pairId;
        if (pairType is { } t) row[CoreAncestorResolver.RegardingRecordTypeColumn] = new EntityReference("sprk_recordtype_ref", t);
        _rows[(entity, id)] = row;
        return this;
    }

    /// <summary>Set a plain (non-lookup) column on an existing row — e.g. the primary name a regarding pair copies.</summary>
    public StampWorld Set(string entity, Guid id, string column, object value)
    {
        _rows[(entity, id)][column] = value;
        return this;
    }

    /// <summary>A <c>sprk_recordtype_ref</c> row naming <paramref name="logicalName"/>.</summary>
    public StampWorld RecordType(Guid id, string logicalName)
    {
        _rows[("sprk_recordtype_ref", id)] = new Entity("sprk_recordtype_ref", id) { ["sprk_recordlogicalname"] = logicalName };
        return this;
    }

    /// <summary>Replace this world's rows with <paramref name="other"/>'s and forget every PATCH and fault (a shared host fixture reseeds per test).</summary>
    public void ReplaceWith(StampWorld other)
    {
        _rows.Clear();
        foreach (var (key, row) in other._rows) _rows[key] = Clone(row);
        _readFaults.Clear();
        _writeFaults.Clear();
        _scanFaults.Clear();
        Patches.Clear();
        FetchXml.Clear();
        Created.Clear();
    }

    /// <summary>
    /// An org whose <paramref name="entity"/> table lacks <paramref name="column"/>: the column probe stops reporting it, so
    /// the restamper and the job must neither select nor write it (verifier round 3, seed K5).
    /// </summary>
    public StampWorld WithoutColumn(string entity, string column)
    {
        _columns[entity].Remove(column);
        return this;
    }

    public StampWorld FailWrite(string entity, Guid id, Exception? fault = null)
    {
        _writeFaults[(entity, id)] = fault ?? new InvalidOperationException($"PATCH {entity} {id} refused");
        return this;
    }

    public StampWorld HealWrite(string entity, Guid id)
    {
        _writeFaults.Remove((entity, id));
        return this;
    }

    public StampWorld FailRead(string entity, Guid id, Exception? fault = null)
    {
        _readFaults[(entity, id)] = fault ?? new TimeoutException($"{entity} {id} timed out");
        return this;
    }

    public StampWorld FailScan(string entity, Exception? fault = null)
    {
        _scanFaults[entity] = fault ?? new TimeoutException($"scan of {entity} timed out");
        return this;
    }

    public StampWorld HealScan(string entity)
    {
        _scanFaults.Remove(entity);
        return this;
    }

    /// <summary>The id a row's <paramref name="column"/> currently names, or null.</summary>
    public Guid? Lookup(string entity, Guid id, string column)
        => _rows[(entity, id)].GetAttributeValue<EntityReference>(column)?.Id;

    /// <summary>Set (or clear, with null) one lookup on a row — an out-of-band write the BFF never saw.</summary>
    public StampWorld OutOfBand(string entity, Guid id, string column, string target, Guid? value)
    {
        if (value is { } v) _rows[(entity, id)][column] = new EntityReference(target, v);
        else _rows[(entity, id)].Attributes.Remove(column);
        return this;
    }

    /// <summary>Set (or clear, with null) a row's pair id — the write a pair-only update (<c>PUT /api/v1/events/{id}</c>) makes.</summary>
    public StampWorld SetPair(string entity, Guid id, string? pairId)
    {
        if (pairId is null) _rows[(entity, id)].Attributes.Remove(CoreAncestorResolver.RegardingRecordIdColumn);
        else _rows[(entity, id)][CoreAncestorResolver.RegardingRecordIdColumn] = pairId;
        return this;
    }

    public IEnumerable<(string Entity, Guid Id, Dictionary<string, object> Fields)> PatchesTo(string entity, Guid id)
        => Patches.Where(p => p.Entity == entity && p.Id == id);

    /// <summary>One page of an ordered result: <paramref name="count"/> 0 = everything on one page.</summary>
    private static EntityCollection Page(List<Entity> ordered, int count, int page)
    {
        if (count <= 0)
        {
            return new EntityCollection(ordered) { MoreRecords = false };
        }

        page = Math.Max(1, page);
        var slice = ordered.Skip((page - 1) * count).Take(count).ToList();
        return new EntityCollection(slice)
        {
            MoreRecords = ordered.Count > page * count,
            PagingCookie = $"<cookie page=\"{page}\" />",
        };
    }

    /// <summary>A FetchXML <c>filter</c> over one row (the operators the scan uses; anything else is refused loudly).</summary>
    private static bool Matches(Entity row, string table, XElement filter)
    {
        var results = filter.Elements().Select(e => e.Name.LocalName switch
        {
            "condition" => ConditionHolds(row, table, e),
            "filter" => Matches(row, table, e),
            var other => throw new NotSupportedException($"StampWorld: FetchXML element <{other}> is not modelled."),
        });

        return (filter.Attribute("type")?.Value ?? "and") == "or" ? results.Any(x => x) : results.All(x => x);
    }

    private static bool ConditionHolds(Entity row, string table, XElement condition)
    {
        var attribute = condition.Attribute("attribute")!.Value;
        var op = condition.Attribute("operator")!.Value;
        object? value = attribute == table + "id" ? row.Id : row.Contains(attribute) ? row[attribute] : null;
        Guid? id = value switch
        {
            EntityReference reference => reference.Id,
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null,
        };

        return op switch
        {
            "null" => value is null,
            "not-null" => value is not null,
            "in" => id is { } x && condition.Elements("value").Any(v => Guid.Parse(v.Value) == x),
            "eq" => id is { } y && y == Guid.Parse(condition.Attribute("value")!.Value),
            "gt" => id is { } z && new SqlGuid(z).CompareTo(new SqlGuid(Guid.Parse(condition.Attribute("value")!.Value))) > 0,
            _ => throw new NotSupportedException($"StampWorld: FetchXML operator '{op}' is not modelled."),
        };
    }

    private static Entity Project(Entity row, string[]? columns)
    {
        if (columns is null) return Clone(row);
        var projected = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns.Where(row.Contains)) projected[column] = row[column];
        return projected;
    }

    private static Entity Clone(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
        return copy;
    }

    private static FaultException<OrganizationServiceFault> NotFound(string entity) =>
        new(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason($"{entity} does not exist"));
}
