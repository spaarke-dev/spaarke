using System.Reflection;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// UAC task 194b fix round R (R3b-2): an ownership question that names PARENTS is built by the one builder,
/// <see cref="RecordOwnershipContext.ForRow"/> (or <see cref="RecordOwnershipContext.ForChild"/> /
/// <see cref="RecordOwnershipContext.ForExistingRowAsync"/>, which go through it), in the row's table's primary order — the
/// order the secure-child reconciler asks the written row in — or through the closed writer opt-out
/// (<see cref="RecordOwnershipContext.ForParentsInWriterOrder"/>, <see cref="WriterOrderOptOutAllowListTests"/>).
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A compiled reference, outside <see cref="RecordOwnershipContext"/> itself, to
/// <see cref="RecordOwnershipContext.ForParents"/> or to the init setter of <see cref="RecordOwnershipContext.Parents"/>,
/// <see cref="RecordOwnershipContext.TargetEntityLogicalName"/> or <see cref="RecordOwnershipContext.TargetRecordId"/> — every way
/// to name a parent without a table's order. A question that names no parent (the caller's own business unit, I-6) sets none
/// of them and is not seen. Pinned per (outermost type, source method), read from IL (<see cref="IlCallScan"/>), so a lambda
/// or async body counts and a comment or <c>nameof</c> does not. The builders themselves are exempt by METADATA identity
/// (<see cref="Builders"/>: each pinned by its signature), never by name, so a new overload that shares a builder's name is
/// seen like any other caller (194b V-fix 6).</para>
/// <para><b>The baseline.</b> <see cref="Exemptions"/> are PERMANENT: questions that do not decide which owner a child row
/// gets in a table's order (each with its reason). <see cref="KnownViolations"/> is the ratchet: child-row questions not yet
/// on the builder, each with its reason — a new one fails, and an entry that no longer builds a question must be removed.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/>: an object initialiser naming a parent, a
/// <c>with</c> that replaces the parents and a <c>ForParents</c> call are found; a <c>ForRow</c> writer and a question naming
/// only the caller are not. <see cref="Detector_ExemptsABuilderByIdentity_NotByName"/>: an overload sharing an approved
/// builder's name is found; the approved builder is not.</para>
/// </remarks>
public class ChildRowOwnershipBuilderGuardTests
{
    private sealed record Entry(string Type, string Method, string Reason);

    /// <summary>PERMANENT: not a question about which owner a child row gets in its table's order (coordinator, R3b-2).</summary>
    private static readonly IReadOnlyList<Entry> Exemptions =
    [
        new(Bff("Services.Dataverse.RecordOwnershipContext"), "ContentOf",
            "Content an existing record holds is owned like that record: one record, no child row's filing order."),
        new(Bff("Services.Signals.GateDecisionRecordSharing"), "AskOwnerAsync",
            "Whether a Decision Record may be shared, not which owner a child row gets."),
        new(Bff("Services.Access.SecureRootInheritance"), "UnsecureOwnerQuestionOf",
            "The secure-root cascade: a root that follows its parents as it un-secures (a work assignment's asked through ForRow, its parents keyed as creation keys them — SecureRootInheritance.OwnerKeyedParents, owner decision 2026-10-29; a project's over its pair's parent)."),
        new(Bff("Services.Signals.TodoAssigneeOwnerReconciliationJob"), "ParentsAnswerAsync",
            "The To Do job's own parent list (A6: the resolved Signals' subjects), not a row's lookups."),
        new(Bff("Services.Access.SecureChildReconciler"), "Pass.ContextFor",
            "The reconciler's own question (the reference the writers match): its thread filing goes through ForRow under sprk_communicationthread; a grant asks its root."),
        new(Bff("Services.Workspace.TodoGenerationService"), "CreateTodoAsync",
            "A standalone to-do's content source (an event) is asked but deliberately not written: no column holds it, so the reconciler sees an unfiled row."),
        new(Bff("Services.Access.SecureChildReconciler"), "PlaceCascadeRowsAsync",
            "Rows the root's own Assign cascades to: one parent, the root."),
        new(Bff("Api.ExternalAccess.GrantExternalAccessEndpoint"), "ResolveGrantOwnerAsync",
            "A grant is an access row filed under exactly its root (099b K1: the reconciler asks the same one parent)."),
        new(Bff("Api.ExternalAccess.ExternalIntakeEndpoints"), "SubmitServiceRequestAsync",
            "A new service request is a root owned by its requester contact's business unit: no parent order."),
        new(Bff("Services.Dataverse.RecordOwnershipResolver"), "ResolveReparentOwnerAsync",
            "The resolver's own re-file: InPrimaryOrder over the row's columns after the change, then its thread filing and inherited parents."),
        new(Bff("Services.Ontology.OntologyAdminAuthoringService"), "CheckVersionOwnerAsync",
            "A secure-if-any gate over the policy and the matters its rule names (stored and requested): an ordinary answer binds no owner (the version stays the caller's), so no table order decides anything, and the version's own columns would drop the matters the gate needs (R4-2)."),
    ];

    /// <summary>The ratchet: child-row questions not on the builder yet, each with its reason (found by this guard, R3b-2).</summary>
    private static readonly IReadOnlyList<Entry> KnownViolations =
    [
        new(Bff("Api.ExternalAccess.ExternalProjectDataEndpoints"), "ResolveChildOwnerAsync",
            "Single parent: the route's root (its payloads write only the root lookup), so order decides nothing (R4-2)."),
        new(Bff("Services.Ai.AnalysisResultPersistence"), "PersistReviewMemoAsync",
            "Single parent: the analysis (sprk_analysisoutput writes only sprk_analysisid as a parent; R4-2)."),
        new(Bff("Services.Ai.AnalysisResultPersistence"), "StoreDocumentProfileOutputsAsync",
            "Single parent: the analysis (sprk_analysisoutput writes only sprk_analysisid as a parent; R4-2)."),
        new(Bff("Services.Ai.AppOnlyAnalysisService"), "ExecutePlaybookAnalysisAsync",
            "Single parent: the analysis (sprk_analysisoutput writes only sprk_analysisid as a parent; R4-2)."),
        new(Bff("Services.Finance.SpendSnapshotService"), "ResolveSnapshotOwnerAsync",
            "Single parent: the matter OR the project, one per path, never both (R4-2)."),
    ];

    private static string Bff(string type) => "Sprk.Bff.Api." + type;

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "194b R3b-2: a child row's ownership question is built by ForRow (or the closed opt-out), or is listed")]
    public void ChildRowOwnershipQuestionsGoThroughTheOneBuilder()
    {
        var found = Constructions(BffTypes.Value);
        var listed = Exemptions.Concat(KnownViolations).ToArray();

        var unlisted = found
            .Where(f => !listed.Any(e => e.Type == f.Type && e.Method == f.Method))
            .Select(f => $"{f.Type}.{f.Method}")
            .ToArray();
        Assert.True(unlisted.Length == 0,
            "An ownership question names parents outside RecordOwnershipContext.ForRow (UAC 194b R3b-2). Ask through ForRow — the " +
            "row's table plus the column each parent is written to — so the writer asks what the reconciler asks; if the " +
            "table's order is wrong, change it in SecureChildLineage with a reason:\n" + string.Join("\n", unlisted));

        var stale = listed
            .Where(e => !found.Any(f => f.Type == e.Type && f.Method == e.Method))
            .Select(e => $"{e.Type}.{e.Method}")
            .ToArray();
        Assert.True(stale.Length == 0, "A baseline entry no longer builds a question — remove it: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "194b R3b-2: control — an initialiser, a with and a ForParents are found; ForRow and a caller-only question are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(ChildRowOwnershipControlInitialiser), typeof(ChildRowOwnershipControlWith), typeof(ChildRowOwnershipControlForParents),
            typeof(ChildRowOwnershipControlForRow), typeof(ChildRowOwnershipControlCallerOnly),
        };
        var found = Constructions(fixtures.SelectMany(IlCallScan.WithNested)).Select(f => f.Type).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                typeof(ChildRowOwnershipControlForParents).FullName!, typeof(ChildRowOwnershipControlInitialiser).FullName!,
                typeof(ChildRowOwnershipControlWith).FullName!,
            },
            found);
    }

    [Fact(DisplayName = "194b V-fix 6: control — a builder is exempt by its metadata identity; an overload sharing its name is found")]
    public void Detector_ExemptsABuilderByIdentity_NotByName()
    {
        var approved = typeof(ChildRowOwnershipControlBuilders).GetMethod(
                           nameof(ChildRowOwnershipControlBuilders.ForChild), BindingFlags.NonPublic | BindingFlags.Static, [typeof(Entity)])
                       ?? throw new InvalidOperationException("The control's approved ForChild(Entity) is missing.");

        var found = Constructions([typeof(ChildRowOwnershipControlBuilders)], [approved]);

        // The synthetic ForChild(string, Guid) overload names a parent with no table order: found, though its name is a
        // builder's. The approved ForChild(Entity) is not.
        Assert.Equal(
            new[] { (typeof(ChildRowOwnershipControlBuilders).FullName!, nameof(ChildRowOwnershipControlBuilders.ForChild)) },
            found);
    }

    private static readonly string[] ParentSetters =
    [
        "set_" + nameof(RecordOwnershipContext.Parents),
        "set_" + nameof(RecordOwnershipContext.TargetEntityLogicalName),
        "set_" + nameof(RecordOwnershipContext.TargetRecordId),
        // UAC task 194b V-fix 3: the Secure-only parents (a copy's canonical) are part of the question, read from the row by
        // the same builders (RecordOwnershipResolver.IsSecureOnlyParent) — never set by hand outside them.
        "set_" + nameof(RecordOwnershipContext.SecureOnlyParents),
        // UAC task 194b R4b-2: the row's table decides the hard-deny exit (RecordOwnershipResolver.IsRestrictedContentTable).
        "set_" + nameof(RecordOwnershipContext.ChildEntityLogicalName),
    ];

    /// <summary>
    /// The builders themselves — the one entry point, what goes through it, and the closed opt-out — each pinned by its
    /// SIGNATURE and matched by metadata identity (<see cref="MemberInfo.HasSameMetadataDefinitionAs"/>), so a new overload
    /// that shares a builder's name is not exempt (194b V-fix 6). A signature that no longer exists fails loud here.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MethodBase>> Builders = new(() =>
    [
        Builder(nameof(RecordOwnershipContext.ForParents), typeof(IEnumerable<RecordOwnershipParent>), typeof(Guid?), typeof(Guid?)),
        Builder(nameof(RecordOwnershipContext.ForRow), typeof(string),
            typeof(IEnumerable<KeyValuePair<string, RecordOwnershipParent>>), typeof(string), typeof(Guid?), typeof(Guid?)),
        Builder(nameof(RecordOwnershipContext.ForChild), typeof(Entity)),
        Builder(nameof(RecordOwnershipContext.ForParentsInWriterOrder), typeof(IEnumerable<RecordOwnershipParent>)),
        Builder(nameof(RecordOwnershipContext.ForExistingRowAsync), typeof(Entity), typeof(ExistingRowReads), typeof(CancellationToken)),
    ]);

    private static MethodBase Builder(string name, params Type[] parameters) =>
        typeof(RecordOwnershipContext).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, parameters)
        ?? throw new InvalidOperationException(
            $"RecordOwnershipContext.{name}({string.Join(", ", parameters.Select(t => t.Name))}) is gone: re-pin the builder.");

    /// <summary>The detector: each (outermost type, source method) that names a parent outside the builders.</summary>
    private static IReadOnlyList<(string Type, string Method)> Constructions(IEnumerable<Type> types) =>
        Constructions(types, Builders.Value);

    private static IReadOnlyList<(string Type, string Method)> Constructions(IEnumerable<Type> types, IReadOnlyList<MethodBase> builders) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(RecordOwnershipContext)
                        && (r.Target.Name == nameof(RecordOwnershipContext.ForParents) || ParentSetters.Contains(r.Target.Name)))
            .Where(r => !(IlCallScan.SourceMethod(r.Caller, r.CallerMethod) is { } source
                          && builders.Any(b => b.HasSameMetadataDefinitionAs(source))))
            .Select(r => (Type: IlCallScan.Outermost(r.Caller).FullName!, Method: MethodOf(r.Caller, r.CallerMethod)))
            .Distinct()
            .ToArray();

    /// <summary>The source method; an async body (<c>&lt;M&gt;d__N.MoveNext</c>) is read as <c>M</c>, so the baseline does not
    /// depend on the compiler's numbering. A lambda's body stays its own name (never the method it sits in).</summary>
    private static string MethodOf(Type caller, MethodBase method)
    {
        var name = IlCallScan.SourceMethodName(caller, method);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^<(?<m>[^>]+)>d__\d+\.MoveNext$");
        return match.Success ? match.Groups["m"].Value : name;
    }
}

// Compiled control fixtures for ChildRowOwnershipBuilderGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: names a parent in an object initialiser.</summary>
internal static class ChildRowOwnershipControlInitialiser
{
    internal static RecordOwnershipContext Ask(Guid matterId) =>
        new() { TargetEntityLogicalName = "sprk_matter", TargetRecordId = matterId };
}

/// <summary>MUST FIRE: replaces a built question's parents.</summary>
internal static class ChildRowOwnershipControlWith
{
    internal static RecordOwnershipContext Ask(RecordOwnershipContext built, Guid matterId) =>
        built with { Parents = [new RecordOwnershipParent("sprk_matter", matterId)] };
}

/// <summary>MUST FIRE: lists parents with no table.</summary>
internal static class ChildRowOwnershipControlForParents
{
    internal static RecordOwnershipContext Ask(Guid matterId) =>
        RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new("sprk_matter", matterId) });
}

/// <summary>MUST NOT FIRE: asks through the one builder.</summary>
internal static class ChildRowOwnershipControlForRow
{
    internal static RecordOwnershipContext Ask(Guid matterId) =>
        RecordOwnershipContext.ForRow(
            "sprk_event",
            new[] { KeyValuePair.Create("sprk_regardingmatter", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_matter", matterId)) });
}

/// <summary>
/// 194b V-fix 6: builders by identity. <c>ForChild(Entity)</c> is the control's approved builder (MUST NOT FIRE);
/// <c>ForChild(string, Guid)</c> is a synthetic overload that shares its name and names a parent with no table order (MUST FIRE).
/// </summary>
internal static class ChildRowOwnershipControlBuilders
{
    internal static RecordOwnershipContext ForChild(Entity child) =>
        new() { Parents = [new RecordOwnershipParent(child.LogicalName, child.Id)] };

    internal static RecordOwnershipContext ForChild(string table, Guid id) =>
        new() { TargetEntityLogicalName = table, TargetRecordId = id };
}

/// <summary>MUST NOT FIRE: names only the caller (the caller's own business unit, I-6).</summary>
internal static class ChildRowOwnershipControlCallerOnly
{
    internal static RecordOwnershipContext Ask(Guid callerObjectId) => new() { CallerObjectId = callerObjectId };
}
