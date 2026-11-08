using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 165d (EB-D-207; unified-access-control-r2 U3 and U4, approved 2026-11-07): the AP export claim
/// (<c>sprk_invoice.sprk_billingexport</c>, <see cref="ApExportService.BatchLookup"/>) is set and cleared only by AP export. It is declared once, as
/// <see cref="RecordTableRule.WriterOnlyColumns"/>, and refused to every generic writer by the one refusal they all ask
/// (<c>InheritedAccessPermission.RefuseAppOnlyWrite</c>, for every identity). This guard pins the two halves of that rule.
/// </summary>
/// <remarks>
/// <para><b>U3 - the writers are covered.</b> The generic update and delete paths are the members that ask
/// <see cref="RecordTableRules.RefuseWriterOnlyTableUpdate"/> / <see cref="RecordTableRules.RefuseWriterOnlyTableDelete"/> (the table-level
/// writer-only rule; <c>WriterOnlyTablesUpdateGuardTests</c> and <c>WriterOnlyTablesDeleteGuardTests</c> pin that every generic path asks it).
/// Each such member must ALSO ask the column refusal - <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> (so the column rule reaches it with no
/// edit of its own) or <c>RecordTableRules.RefuseWriterOnlyColumns</c> directly - or be listed in <see cref="Exempt"/> with its reason. A listed
/// member that does ask the refusal is stale and fails, so the list can only shrink. <c>RecordTableRules.RefuseWriterOnlyColumns</c> is asked from
/// <c>RefuseAppOnlyWrite</c> (the single extension of uac-r2's refusal) and from the exact callers in <see cref="DirectColumnAskers"/>: the two
/// create-path members of <c>RecordCreationService</c> (the CREATE is not an update and asks no table-level check) and the work-product persister
/// (its target column comes from a registry row, configuration, #2257). The pin checks MEMBERS, not branches: a member in the list that asks
/// the refusal for only one of its identities or modes still passes it.</para>
/// <para><b>U4 - AP export writes through its own path.</b> <see cref="ApExportService"/> never asks <c>RefuseAppOnlyWrite</c> or
/// <c>RefuseWriterOnlyColumns</c> and reaches Dataverse through <see cref="IGenericEntityService"/>; and the set of types that name the column
/// (an IL string load, so the inlined constant is found) is exactly the reviewed one, so a new writer must be reviewed before it can name the
/// claim (approach of <c>InvoiceMoneyColumnsAppOnlyWriteGuardTests</c>).</para>
/// <para><b>Not covered</b> (code-review concerns): a writer that builds the column name at run time; a write made as a person under their own
/// Dataverse privileges (the members in <see cref="Exempt"/>, and the confirmed-proposal apply of the action seam), which the column's field
/// security would close (the 165d notes, known limit U5: an owner question, not built here). Enforced by: this guard (CI and the Stop hook),
/// with must-fire and must-not-fire controls below.</para>
/// </remarks>
public sealed class BillingExportClaimWriterOnlyGuardTests
{
    private const string Claim = ApExportService.BatchLookup;

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    /// <summary>
    /// The members that ask the table-level writer-only check and NOT the app-only refusal, each with why. Three kinds: a write AS the person
    /// (their own Dataverse privileges decide it; <c>RefuseAppOnlyWrite</c> is the refusal of payloads built by configuration or a model, and
    /// <c>RefuseAppOnlyWriteCallerIdentityGuardTests</c> allows its Caller identity to the field-mapping push alone); a check that carries no
    /// payload columns; a delete.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ValidatePushRequest"] =
            "The push's request check: it carries the target table only, no columns. The per-rule ask is in ApplyMappingsToChildRecordsAsync (WriteIdentity.Caller).",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler.ValidateChat"] =
            "The chat update tool's validation (before the confirm dialog): the table only. It writes AS the person (user token) after they confirm.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler.ExecuteChatAsync"] =
            "The chat update tool writes AS the person, through the user-token client, after they confirm the exact payload: their own edit, decided by their Dataverse privileges.",
        ["Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync"] =
            "The browser child-record update writes AS the caller (IDataverseUserClient), the shape of a form edit: their own Dataverse privileges decide it.",
        ["Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.RefileAsync"] =
            "The re-file core applies the caller's own PATCH AS the caller (IDataverseUserClient): their own Dataverse privileges decide it.",
    };

    /// <summary>
    /// The only members that ask <c>RecordTableRules.RefuseWriterOnlyColumns</c> other than <c>RefuseAppOnlyWrite</c>, each with why. A new asker must be
    /// reviewed and added here (the list is exact: a member that stops asking, or a new one, fails).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DirectColumnAskers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.RecordCreation.RecordCreationService.CallerBlockedColumnRefusal"] =
            "The caller's create (and the chat create tool's pre-suspend check): an invoice must not be created already claimed.",
        ["Sprk.Bff.Api.Services.RecordCreation.RecordCreationService.DecideAsync"] =
            "The system principal's create (step 2): the caller's column policy is skipped for it, this column's is not.",
        ["Sprk.Bff.Api.Services.Ai.TopicRegistryWorkProductPersister.PersistAsync"] =
            "The target column comes from a registry row (configuration, not reviewed code), so it is asked as a configuration-built payload (#2257): 'fix the registry row'.",
    };

    /// <summary>Why the delete members are not listed: a delete carries no payload columns, so it cannot ask a column refusal; the table-level refusal is the whole rule.</summary>
    private const string DeleteReason = "A delete carries no payload columns; the table-level refusal is the whole rule.";

    [Fact(DisplayName = "165d U3: every member that asks the writer-only table check also asks RefuseAppOnlyWrite, or is listed with its reason")]
    public void EveryWriterOnlyTableAsker_AlsoAsksTheAppOnlyRefusal_OrIsListed()
    {
        var types = BffTypes.Value;
        var updateAskers = AskersOf(types, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyTableUpdate));
        var deleteAskers = AskersOf(types, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyTableDelete));
        var appOnlyAskers = ColumnRefusalAskers(types);

        var problems = Uncovered(updateAskers, appOnlyAskers, Exempt).ToList();

        // The delete paths (the chat delete tool) ask the table-level delete check; a delete carries no payload columns, so they need no column
        // refusal (DeleteReason). Non-vacuity: the scan sees them.
        if (deleteAskers.Count == 0)
            problems.Add("The scan sees no member that asks RefuseWriterOnlyTableDelete; it lost its signal.");

        // Non-vacuity: the scan sees the writers that DO ask both (the three generic writers of the playbooks and the push).
        foreach (var (type, method) in new (Type, string)[]
                 {
                     (typeof(Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore), "UpdateAsync"),
                     (typeof(DataverseUpdateHandler), "UpdateAsync"),
                     (typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints), "ApplyMappingsToChildRecordsAsync"),
                 })
        {
            if (!updateAskers.Contains((type, method)) || !appOnlyAskers.Contains((type, method)))
                problems.Add($"{type.FullName}.{method}: expected to ask both RefuseWriterOnlyTableUpdate and RefuseAppOnlyWrite; the scan lost its signal or a check was removed.");
        }

        Assert.True(problems.Count == 0,
            "The AP export claim is a writer-only column (RecordTableRule.WriterOnlyColumns), refused by InheritedAccessPermission.RefuseAppOnlyWrite "
            + "(or RecordTableRules.RefuseWriterOnlyColumns directly). Every generic write path that asks the writer-only TABLE check must also ask that "
            + "refusal over the payload's column keys, so the column rule reaches it; a path that cannot is listed in Exempt with its reason:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "165d U3: the column refusal is asked from RefuseAppOnlyWrite and the reviewed direct callers alone, never by a writer of its own")]
    public void TheColumnRefusal_IsAskedFromTheAppOnlyRefusalAndTheReviewedCallersAlone()
    {
        var askers = AskersOf(BffTypes.Value, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyColumns));

        Assert.Equal(
            DirectColumnAskers.Keys.Append($"{typeof(InheritedAccessPermission).FullName}.RefuseAppOnlyWrite").Order(StringComparer.Ordinal).ToArray(),
            askers.Select(a => $"{a.Type.FullName}.{a.Method}").Order(StringComparer.Ordinal).ToArray());
    }

    [Fact(DisplayName = "165d U3: the registry's writer-only column set is read by the refusal (it cannot become a hand-written list)")]
    public void TheColumnRefusal_ReadsTheRegistrySet()
    {
        var method = typeof(RecordTableRules).GetMethod(
            nameof(RecordTableRules.RefuseWriterOnlyColumns), BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True(IlCallScan.ReferencedMethods(IlCallScan.CompiledBody(method)).Any(m =>
                m.DeclaringType == typeof(RecordTableRule) && m.Name == "get_" + nameof(RecordTableRule.WriterOnlyColumns)),
            "RecordTableRules.RefuseWriterOnlyColumns must read RecordTableRule.WriterOnlyColumns.");
    }

    [Fact(DisplayName = "165d U4: ApExportService never asks the generic writers' refusal, and reaches Dataverse through IGenericEntityService")]
    public void ApExportService_WritesThroughItsOwnPath()
    {
        var types = IlCallScan.WithNested(typeof(ApExportService)).ToList();

        var asks = IlCallScan.MethodReferences(types)
            .Where(r => (r.Target.DeclaringType == typeof(InheritedAccessPermission) && r.Target.Name == "RefuseAppOnlyWrite")
                        || (r.Target.DeclaringType == typeof(RecordTableRules) && r.Target.Name == nameof(RecordTableRules.RefuseWriterOnlyColumns)))
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {IlCallScan.Describe(r.Target)}")
            .Distinct()
            .ToList();
        Assert.True(asks.Count == 0,
            "ApExportService is the claim's typed writer; it would refuse its own write if it asked the generic writers' refusal: " + string.Join("; ", asks));

        Assert.Contains(IlCallScan.MethodReferences(types), r =>
            r.Target.DeclaringType is { } d && typeof(IGenericEntityService).IsAssignableFrom(d)
            && r.Target.Name == nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync));
    }

    [Fact(DisplayName = "165d U4: only the reviewed types name the AP export claim's column (a new writer must be reviewed first)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(typeof(Program).Assembly.GetTypes().Concat(typeof(IGenericEntityService).Assembly.GetTypes()));

        var unreviewed = naming.Except(Reviewed).Select(t => t.FullName).Order(StringComparer.Ordinal).ToList();
        var stale = Reviewed.Except(naming).Select(t => t.FullName).Order(StringComparer.Ordinal).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name '{Claim}' (the AP export claim on an invoice, and also the AP export batch table) but are not in the reviewed lists: "
            + $"{string.Join(", ", unreviewed)}. Only ApExportService writes the claim. Review what the type does with the name (write the claim, or read it, or use it as the "
            + "batch table) and record it in projects/ebilling-r1/notes/165d-billingexport-claim.md, then add it to Writers or Others here.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name '{Claim}'; remove them: {string.Join(", ", stale)}");
        Assert.Empty(Writers.Intersect(Others));
    }

    // ── Reviewed lists ──

    /// <summary>The one type that writes the claim (sets it at claim, clears it at release).</summary>
    private static readonly Type[] Writers = [typeof(ApExportService)];

    /// <summary>
    /// Types that name the column only to read it, or name the AP export BATCH table (<c>sprk_billingexport</c> is both the batch table and the
    /// invoice's lookup to it), or hold it in a registration. None writes the invoice's claim.
    /// </summary>
    private static readonly Type[] Others =
    [
        typeof(RecordTableRules), // the registration: MappingProtectedColumns and WriterOnlyColumns of sprk_invoice
        typeof(ApExportDeliveryService), // the batch table (Sent to AP date, owner); READS the invoices where the lookup equals the batch (a condition, no write)
        typeof(Sprk.Bff.Api.Services.Communication.CommunicationService), // the COMMUNICATION's own lookup to the batch (sprk_communication.sprk_billingexport) and the batch table, not the invoice's
        typeof(Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema), // the Stored File parent kind: the batch table name
        typeof(Sprk.Bff.Api.Services.Finance.Files.StoredFileReconcileJob), // the batch table name in the parent-table list of the reconcile sweep
    ];

    private static IEnumerable<Type> Reviewed => Writers.Concat(Others);

    // ── Controls ──

    [Fact(DisplayName = "165d controls: the coverage detector finds a table-check asker that skips the app-only refusal, and not the clean shapes or a listed one")]
    public void Coverage_MustFireAndMustNotFire()
    {
        var types = new[]
            {
                typeof(BillingClaimControlTableOnly), typeof(BillingClaimControlBoth), typeof(BillingClaimControlDeleteOnly), typeof(BillingClaimControlListed),
                typeof(BillingClaimControlColumnsDirect),
            }
            .SelectMany(IlCallScan.WithNested).ToList();
        var updateAskers = AskersOf(types, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyTableUpdate));
        var deleteAskers = AskersOf(types, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyTableDelete));
        var appOnlyAskers = ColumnRefusalAskers(types);
        Assert.Contains((typeof(BillingClaimControlDeleteOnly), "Delete"), deleteAskers); // the scan sees a delete asker
        var listed = new Dictionary<string, string>
        {
            [$"{nameof(BillingClaimControlListed)}.Update"] = "listed with a reason",
        };

        // Must fire: the table check without the column refusal. Must not fire: both asked (either form of the column refusal), a listed member, a delete-only member.
        var found = Uncovered(updateAskers, appOnlyAskers, WithFullNames(listed)).Select(p => p.Split(':')[0]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { $"{typeof(BillingClaimControlTableOnly).FullName}.Update" }, found);

        // A listed member that DOES ask the refusal is stale.
        var staleListed = Uncovered(updateAskers, appOnlyAskers,
            WithFullNames(new Dictionary<string, string> { [$"{nameof(BillingClaimControlBoth)}.Update"] = "wrongly listed" }));
        Assert.Contains(staleListed, p => p.Contains(nameof(BillingClaimControlBoth)) && p.Contains("stale"));
    }

    [Fact(DisplayName = "165d controls: the column scan finds a type that names the column, and not one that names another")]
    public void ColumnScan_MustFireAndMustNotFire()
    {
        var naming = TypesNamingTheColumn(IlCallScan.WithNested(typeof(BillingClaimNamesTheColumn)));
        Assert.Contains(typeof(BillingClaimNamesTheColumn), naming);
        Assert.DoesNotContain(typeof(BillingClaimNamesTheColumn), Reviewed);

        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(BillingClaimNamesAnotherColumn))));

        // Every form a writer sends the column in is found, not only the exact string (the folded BatchLookup + "@odata.bind" is one constant).
        foreach (var fixture in new[] { typeof(BillingClaimNamesTheFoldedBind), typeof(BillingClaimNamesTheLowerBind), typeof(BillingClaimNamesTheValueForm), typeof(BillingClaimNamesTheMixedCase) })
        {
            Assert.Contains(fixture, TypesNamingTheColumn(IlCallScan.WithNested(fixture)));
            Assert.DoesNotContain(fixture, Reviewed);
        }

        // Must not fire: neighbouring names, with and without a bind.
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(BillingClaimNamesNeighbours))));

        // C4's detector, over the fixture that asks the refusal and one that does not.
        Assert.NotEmpty(AsksTheRefusal(typeof(BillingClaimControlBoth)));
        Assert.Empty(AsksTheRefusal(typeof(BillingClaimNamesAnotherColumn)));
    }

    // ── The scan ──

    private static IReadOnlyDictionary<string, string> WithFullNames(IDictionary<string, string> listed) =>
        listed.ToDictionary(kv => $"{typeof(BillingClaimControlBoth).Namespace}.{kv.Key}", kv => kv.Value);

    private static IReadOnlyList<string> AsksTheRefusal(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Where(r => r.Target.DeclaringType == typeof(InheritedAccessPermission) && r.Target.Name == "RefuseAppOnlyWrite")
            .Select(r => IlCallScan.SourceMethodName(r.Caller, r.CallerMethod))
            .ToList();

    /// <summary>
    /// The problems: an update-path member that asks the writer-only TABLE check and neither asks the app-only refusal nor is listed; a listed
    /// member that does ask the refusal; and a listed member that no longer asks the table check (both stale).
    /// </summary>
    private static IEnumerable<string> Uncovered(
        IReadOnlySet<(Type Type, string Method)> updateAskers,
        IReadOnlySet<(Type Type, string Method)> appOnlyAskers,
        IReadOnlyDictionary<string, string> exempt)
    {
        static string Name((Type Type, string Method) m) => $"{m.Type.FullName}.{m.Method}";

        foreach (var site in updateAskers.OrderBy(Name, StringComparer.Ordinal))
        {
            var asksRefusal = appOnlyAskers.Contains(site);
            var listed = exempt.ContainsKey(Name(site));
            if (!asksRefusal && !listed)
                yield return $"{Name(site)}: asks a writer-only TABLE check but not InheritedAccessPermission.RefuseAppOnlyWrite, and is not in Exempt.";
            if (asksRefusal && listed)
                yield return $"{Name(site)}: listed in Exempt but it asks RefuseAppOnlyWrite (stale: remove it from the list).";
        }

        var all = updateAskers.Select(Name).ToHashSet(StringComparer.Ordinal);
        foreach (var listed in exempt.Keys.Where(k => !all.Contains(k)).Order(StringComparer.Ordinal))
            yield return $"{listed}: listed in Exempt but it no longer asks the writer-only table update check (stale: remove it from the list).";
    }

    internal static IReadOnlySet<(Type Type, string Method)> AskersOf(IEnumerable<Type> types, Type declaring, string check) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == declaring && r.Target.Name == check)
            .Select(r => (IlCallScan.Outermost(r.Caller), CoreRecordPairUpdateCheckGuardTests.SourceMember(r.Caller, r.CallerMethod)))
            .ToHashSet();

    /// <summary>
    /// Whether a string constant names the claim in ANY form a writer sends it (the refusal's own key matching):
    /// <c>sprk_BillingExport</c>, <c>sprk_billingexport@odata.bind</c> (a compiler-folded <c>BatchLookup + "@odata.bind"</c> is one such constant)
    /// and <c>_sprk_billingexport_value</c>. An exact comparison would let a bind key slip past the pin.
    /// </summary>
    private static bool NamesTheClaim(string? value) =>
        value is not null && string.Equals(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(value), Claim, StringComparison.Ordinal);

    /// <summary>Every member that asks a column refusal: <c>RefuseAppOnlyWrite</c>, or <c>RefuseWriterOnlyColumns</c> directly.</summary>
    private static IReadOnlySet<(Type Type, string Method)> ColumnRefusalAskers(IEnumerable<Type> types)
    {
        var all = types as IReadOnlyCollection<Type> ?? types.ToList();
        return AskersOf(all, typeof(InheritedAccessPermission), "RefuseAppOnlyWrite")
            .Concat(AskersOf(all, typeof(RecordTableRules), nameof(RecordTableRules.RefuseWriterOnlyColumns)))
            .ToHashSet();
    }

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => NamesTheClaim(s.Value))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();
}

// Compiled control fixtures for BillingExportClaimWriterOnlyGuardTests. Top-level types (the scan attributes nested types to their
// outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: asks the table-level update check only.</summary>
internal static class BillingClaimControlTableOnly
{
    internal static bool Update(string table) => RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null;
}

/// <summary>MUST NOT FIRE: asks both the table check and the app-only refusal.</summary>
internal static class BillingClaimControlBoth
{
    internal static bool Update(string table, IEnumerable<string> keys) =>
        RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null && InheritedAccessPermission.RefuseAppOnlyWrite(table, keys) is null;
}

/// <summary>A delete asker: a delete has no payload columns (MUST NOT FIRE as a class; the fixture only proves the scan sees it).</summary>
internal static class BillingClaimControlDeleteOnly
{
    internal static bool Delete(string table) => RecordTableRules.RefuseWriterOnlyTableDelete(table) is null;
}

/// <summary>MUST NOT FIRE: asks the table check only, but is listed with a reason.</summary>
internal static class BillingClaimControlListed
{
    internal static bool Update(string table) => RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null;
}

/// <summary>Names the column, so the column scan sees this type (must fire).</summary>
internal static class BillingClaimNamesTheColumn
{
    public static string Name() => "sprk_billingexport";
}

/// <summary>Names the column as a compiler-folded bind key (a constant concatenation), which an exact-string pin would miss (must fire).</summary>
internal static class BillingClaimNamesTheFoldedBind
{
    public static string Name() => ApExportService.BatchLookup + "@odata.bind";
}

/// <summary>Names the column as a lower-case bind key (must fire).</summary>
internal static class BillingClaimNamesTheLowerBind
{
    public static string Name() => "sprk_billingexport@odata.bind";
}

/// <summary>Names the column in the read-back form (must fire).</summary>
internal static class BillingClaimNamesTheValueForm
{
    public static string Name() => "_sprk_billingexport_value";
}

/// <summary>Names the column in mixed case with a bind (must fire).</summary>
internal static class BillingClaimNamesTheMixedCase
{
    public static string Name() => "sprk_BillingExport@odata.bind";
}

/// <summary>Must-not-fire control for the column scan: neighbouring names, with and without a bind.</summary>
internal static class BillingClaimNamesNeighbours
{
    public static string[] Names() => ["sprk_billingexports@odata.bind", "_sprk_billingexports_value", "xsprk_billingexport@odata.bind"];
}

/// <summary>MUST NOT FIRE: asks the table check and the column refusal directly (the form the work-product persister uses).</summary>
internal static class BillingClaimControlColumnsDirect
{
    internal static bool Update(string table, IEnumerable<string> keys) =>
        RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null && RecordTableRules.RefuseWriterOnlyColumns(table, keys) is null;
}

/// <summary>Must-not-fire control for the column scan: names another column only.</summary>
internal static class BillingClaimNamesAnotherColumn
{
    public static string Name() => "sprk_billingexports";
}
