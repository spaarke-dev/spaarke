using System.Reflection;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// UAC task 194b R4b-2 (uac-r2 Q2): an owner question asked for a row of a HARD-DENY table
/// (<see cref="RecordOwnershipResolver.RestrictedContentTables"/>) is built by <see cref="RecordOwnershipContext.ForRow"/> or
/// <see cref="RecordOwnershipContext.ForChild"/>, which name the row's table — never by <see cref="RecordOwnershipContext.ContentOf"/>,
/// <see cref="RecordOwnershipContext.ForParents"/> or a hand-built context. The resolver's hard-deny exit fires only for the table
/// <see cref="RecordOwnershipContext.ChildEntityLogicalName"/> names, so any other question lets an ordinary hard-deny row land on a
/// business unit's default team.
/// </summary>
/// <remarks>
/// <para><b>The signal</b>, per (outermost type, source method), read from IL (<see cref="IlCallScan"/>): the method asks an owner
/// (<see cref="IRecordOwnershipResolver.ResolveOwnerAsync"/> or <see cref="IRecordOwnershipResolver.ResolveOwningTeamAsync"/>) AND
/// writes a hard-deny row — it loads a hard-deny table name, or calls a pinned row builder of one (<see cref="HardDenyRowBuilders"/>:
/// a builder whose table is not a literal in the asking method). Such a method is flagged when its question references
/// <see cref="BannedBuilders"/>, or references neither <c>ForRow</c> nor <c>ForChild</c>. The question is read from the method itself
/// and from each method it calls that returns a <see cref="RecordOwnershipContext"/> (one level: a writer's named question helper,
/// as <c>InvoiceSubmissionPersister.StoredFileOwnerContext</c>).</para>
/// <para><b>Not seen</b>: a method that writes a hard-deny row through a builder not pinned here and names no hard-deny table, and a
/// question built more than one helper call away. The R3b-2 guard (<see cref="ChildRowOwnershipBuilderGuardTests"/>) still sees every
/// hand-named parent anywhere.</para>
/// <para><b>Exemptions</b>: <see cref="IntentionalExemptions"/> holds the permanent, owner-decided exemptions; they are not debt and must
/// not be migrated. <see cref="KnownViolations"/> is the ratchet for debt (a new finding fails); it is empty. An entry in either list that
/// no longer fires must be removed.</para>
/// <para><b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/>.</para>
/// </remarks>
public class HardDenyOwnershipQuestionGuardTests
{
    private sealed record Entry(string Type, string Method, string Reason);

    /// <summary>
    /// Permanent, intended exemptions (owner decisions), each with its reason. Not debt: do not migrate them to ForRow / ForChild.
    /// </summary>
    private static readonly IReadOnlyList<Entry> IntentionalExemptions =
    [
        new("Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService", "CheckVersionOwnerAsync",
            "Intentional (D-197, owner): authoring asks with no child table and KeepCreator so the author keeps the version, including " +
            "root-homed authors; ForRow/ForChild would make 194b refuse them. Pinned by " +
            "Publish_AsksTheResolverWithNoChildTable_SoTheAuthorWins_RootIncluded. Do not migrate."),
    ];

    /// <summary>The ratchet: hard-deny writers whose owner question is not yet built by ForRow / ForChild, each with its reason.</summary>
    private static readonly IReadOnlyList<Entry> KnownViolations = [];

    private static IEnumerable<Entry> Listed => IntentionalExemptions.Concat(KnownViolations);

    /// <summary>The resolver and the context: the rule's own implementation, not a writer.</summary>
    private static readonly Type[] Implementation = [typeof(RecordOwnershipResolver), typeof(RecordOwnershipContext)];

    private static readonly string[] Asks =
        [nameof(IRecordOwnershipResolver.ResolveOwnerAsync), nameof(IRecordOwnershipResolver.ResolveOwningTeamAsync)];

    private static readonly string[] TableBuilders = [nameof(RecordOwnershipContext.ForRow), nameof(RecordOwnershipContext.ForChild)];

    /// <summary>Every way to build a question that does not name the row's table through the one builder.</summary>
    private static readonly string[] BannedBuilders =
    [
        nameof(RecordOwnershipContext.ContentOf),
        nameof(RecordOwnershipContext.ForParents),
        "ForParentsInWriterOrder",
        "set_" + nameof(RecordOwnershipContext.Parents),
        "set_" + nameof(RecordOwnershipContext.TargetEntityLogicalName),
        "set_" + nameof(RecordOwnershipContext.TargetRecordId),
        "set_" + nameof(RecordOwnershipContext.SecureOnlyParents),
        "set_" + nameof(RecordOwnershipContext.ChildEntityLogicalName),
    ];

    /// <summary>Row builders of a hard-deny table whose table name the asking method does not load itself (pinned by signature).</summary>
    private static readonly Lazy<IReadOnlyList<MethodBase>> HardDenyRowBuilders = new(() =>
    [
        typeof(StoredFileService).GetMethod(nameof(StoredFileService.BuildRow),
            [typeof(StoredFileRowSpec), typeof(StagedBlob), typeof(RecordOwnerResolution), typeof(Guid?)])
        ?? throw new InvalidOperationException("StoredFileService.BuildRow(spec, blob, owner, creator) is gone: re-pin the row builder."),
    ]);

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "194b R4b-2 (uac-r2 Q2): a hard-deny row's owner question is built by ForRow or ForChild, never ContentOf, ForParents or by hand")]
    public void HardDenyRowOwnerQuestionsNameTheRowsTable()
    {
        var found = Findings(BffTypes.Value.Where(t => !Implementation.Contains(IlCallScan.Outermost(t))));

        var unlisted = found
            .Where(f => !Listed.Any(e => e.Type == f.Type && e.Method == f.Method))
            .Select(f => $"{f.Type}.{f.Method}")
            .ToArray();
        Assert.True(unlisted.Length == 0,
            "A hard-deny row (RecordOwnershipResolver.RestrictedContentTables) is owned through a question that does not name its " +
            "table (UAC 194b R4b-2): build it with RecordOwnershipContext.ForRow(<the row's table>, <the lookups it writes>) or " +
            "ForChild(row), so the restricted-content exit applies:\n" + string.Join("\n", unlisted));

        var stale = Listed
            .Where(e => !found.Any(f => f.Type == e.Type && f.Method == e.Method))
            .Select(e => $"{e.Type}.{e.Method}")
            .ToArray();
        Assert.True(stale.Length == 0, "An exemption or baseline entry no longer fires — remove it: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "194b R4b-2 (uac-r2 Q2): control — ContentOf, a hand-built context and a stored-file BuildRow under ContentOf are found; ForRow, a ForRow helper and an ordinary ContentOf are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(HardDenyControlContentOf), typeof(HardDenyControlHandBuilt), typeof(HardDenyControlBuildRowContentOf),
            typeof(HardDenyControlForRow), typeof(HardDenyControlForRowHelper), typeof(HardDenyControlOrdinaryContentOf),
        };

        var found = Findings(fixtures.SelectMany(IlCallScan.WithNested)).Select(f => f.Type).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                typeof(HardDenyControlBuildRowContentOf).FullName!, typeof(HardDenyControlContentOf).FullName!,
                typeof(HardDenyControlHandBuilt).FullName!,
            },
            found);
    }

    private sealed class MethodFacts
    {
        public bool AsksOwner { get; set; }
        public bool WritesHardDenyRow { get; set; }
        public HashSet<string> Question { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The detector: each (outermost type, source method) that owns a hard-deny row through a question not built by ForRow / ForChild.</summary>
    private static IReadOnlyList<(string Type, string Method)> Findings(IEnumerable<Type> types)
    {
        var scanned = types.ToArray();
        var facts = new Dictionary<(string Type, string Method), MethodFacts>();
        MethodFacts Of(Type caller, MethodBase method)
        {
            var key = (IlCallScan.Outermost(caller).FullName!, MethodOf(caller, method));
            return facts.TryGetValue(key, out var known) ? known : facts[key] = new MethodFacts();
        }

        foreach (var (caller, method, target) in IlCallScan.MethodReferences(scanned))
        {
            var fact = Of(caller, method);
            if (IsAsk(target))
                fact.AsksOwner = true;
            if (HardDenyRowBuilders.Value.Any(b => b.HasSameMetadataDefinitionAs(target)))
                fact.WritesHardDenyRow = true;
            if (target.DeclaringType == typeof(RecordOwnershipContext))
                fact.Question.Add(target.Name);
            else if (target is MethodInfo helper && ReturnsAQuestion(helper) && helper.GetMethodBody() is not null)
            {
                // One level: a named question helper's own builder references.
                foreach (var inner in IlCallScan.ReferencedMethods(IlCallScan.CompiledBody(helper)))
                {
                    if (inner.DeclaringType == typeof(RecordOwnershipContext))
                        fact.Question.Add(inner.Name);
                }
            }
        }

        foreach (var (caller, method, value) in IlCallScan.StringLoads(scanned))
        {
            if (RecordOwnershipResolver.IsRestrictedContentTable(value))
                Of(caller, method).WritesHardDenyRow = true;
        }

        return facts
            .Where(f => f.Value.AsksOwner && f.Value.WritesHardDenyRow
                        && (f.Value.Question.Overlaps(BannedBuilders) || !f.Value.Question.Overlaps(TableBuilders)))
            .Select(f => f.Key)
            .OrderBy(k => k.Type, StringComparer.Ordinal).ThenBy(k => k.Method, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsAsk(MethodBase target) =>
        Asks.Contains(target.Name)
        && target.DeclaringType is { } declaring
        && (declaring == typeof(IRecordOwnershipResolver) || typeof(IRecordOwnershipResolver).IsAssignableFrom(declaring));

    private static bool ReturnsAQuestion(MethodInfo method) =>
        method.ReturnType == typeof(RecordOwnershipContext)
        || method.ReturnType == typeof(Task<RecordOwnershipContext>)
        || method.ReturnType == typeof(ValueTask<RecordOwnershipContext>);

    /// <summary>The source method; an async body (<c>&lt;M&gt;d__N.MoveNext</c>) is read as <c>M</c> (as the R3b-2 guard reads it).</summary>
    private static string MethodOf(Type caller, MethodBase method)
    {
        var name = IlCallScan.SourceMethodName(caller, method);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^<(?<m>[^>]+)>d__\d+\.MoveNext$");
        return match.Success ? match.Groups["m"].Value : name;
    }
}

// Compiled control fixtures for HardDenyOwnershipQuestionGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: a stored file owned through its parent's content question.</summary>
internal static class HardDenyControlContentOf
{
    internal static async Task<Entity> Ask(IRecordOwnershipResolver owners, Guid invoiceId)
    {
        var owner = await owners.ResolveOwnerAsync(RecordOwnershipContext.ContentOf("sprk_invoice", invoiceId), CancellationToken.None);
        var row = new Entity(StoredFileSchema.Entity);
        owner.ApplyTo(row);
        return row;
    }
}

/// <summary>MUST FIRE: a stored file owned through a hand-built question, even one naming its table.</summary>
internal static class HardDenyControlHandBuilt
{
    internal static Task<RecordOwnerResolution> Ask(IRecordOwnershipResolver owners, Guid invoiceId) =>
        owners.ResolveOwnerAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_invoice", TargetRecordId = invoiceId, ChildEntityLogicalName = "sprk_storedfile" },
            CancellationToken.None);
}

/// <summary>MUST FIRE: the stored-file row builder under a ContentOf owner (no table literal in the method).</summary>
internal static class HardDenyControlBuildRowContentOf
{
    internal static async Task<object> Ask(IRecordOwnershipResolver owners, StoredFileService files, StoredFileRowSpec spec, StagedBlob blob)
    {
        var owner = await owners.ResolveOwnerAsync(RecordOwnershipContext.ContentOf(spec.Parent.Table, spec.Parent.Id), CancellationToken.None);
        return files.BuildRow(spec, blob, owner, creatorPersonId: null);
    }
}

/// <summary>MUST NOT FIRE: the stored file's own question, through the one builder.</summary>
internal static class HardDenyControlForRow
{
    internal static Task<RecordOwnerResolution> Ask(IRecordOwnershipResolver owners, Guid invoiceId) =>
        owners.ResolveOwnerAsync(
            RecordOwnershipContext.ForRow(
                StoredFileSchema.Entity,
                new[] { KeyValuePair.Create("sprk_invoice", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_invoice", invoiceId)) }),
            CancellationToken.None);
}

/// <summary>MUST NOT FIRE: the stored-file row builder under a question a named helper builds with ForRow.</summary>
internal static class HardDenyControlForRowHelper
{
    internal static async Task<object> Ask(IRecordOwnershipResolver owners, StoredFileService files, StoredFileRowSpec spec, StagedBlob blob)
    {
        var owner = await owners.ResolveOwnerAsync(Question(spec), CancellationToken.None);
        return files.BuildRow(spec, blob, owner, creatorPersonId: null);
    }

    private static RecordOwnershipContext Question(StoredFileRowSpec spec) =>
        RecordOwnershipContext.ForRow(
            StoredFileSchema.Entity,
            new[] { KeyValuePair.Create(spec.Parent.Table, (RecordOwnershipParent?)new RecordOwnershipParent(spec.Parent.Table, spec.Parent.Id)) });
}

/// <summary>MUST NOT FIRE: ContentOf for a row of an ordinary table (no hard-deny table is written).</summary>
internal static class HardDenyControlOrdinaryContentOf
{
    internal static async Task<Entity> Ask(IRecordOwnershipResolver owners, Guid communicationId)
    {
        var owner = await owners.ResolveOwnerAsync(RecordOwnershipContext.ContentOf("sprk_communication", communicationId), CancellationToken.None);
        var row = new Entity("sprk_communicationattachment");
        owner.ApplyTo(row);
        return row;
    }
}
