using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;

namespace Spaarke.ArchTests;

/// <summary>
/// The detector behind <see cref="InvoiceFamilyCoreLookupWriterGuardTests"/> (DEF-064, #2643): which outermost types call an enforcing
/// member of <see cref="InvoiceFamilyCoreRecordGuard"/>, and which write an invoice-family row's core lookups without one. It reads the
/// compiled IL through <see cref="IlCallScan"/>; see the test class for the rule and its stated limits.
/// </summary>
internal static class InvoiceFamilyWriterScan
{
    internal static readonly Type Guard = typeof(InvoiceFamilyCoreRecordGuard);

    /// <summary>
    /// The guard members that DECIDE a write (refuse or derive); a caller of one of these has asked the guard. Every other public or
    /// internal static member is a reader, a constant, a code or a helper (<see cref="NotEnforcing"/>), so naming a column list or a
    /// refusal code does not count as a call of the guard.
    /// </summary>
    internal static readonly IReadOnlySet<string> Enforcing = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(InvoiceFamilyCoreRecordGuard.Check),
        nameof(InvoiceFamilyCoreRecordGuard.CheckWriteAsync),
        nameof(InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync),
        nameof(InvoiceFamilyCoreRecordGuard.DeriveForGenericUpdateAsync),
        nameof(InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync),
        nameof(InvoiceFamilyCoreRecordGuard.KeepRegardingPairAgreeingAsync),
        nameof(InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync),
        nameof(InvoiceFamilyCoreRecordGuard.DeriveGenericResolverFieldsAsync),
    };

    /// <summary>
    /// Every other static member of the guard (public or internal), classified so that a NEW member must be placed in one of the two
    /// lists: a decision member goes in <see cref="Enforcing"/>, anything else here.
    /// </summary>
    internal static readonly IReadOnlySet<string> NotEnforcing = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(InvoiceFamilyCoreRecordGuard.CoreRecordLookups),
        nameof(InvoiceFamilyCoreRecordGuard.ColumnsToRead),
        nameof(InvoiceFamilyCoreRecordGuard.GuardedLookups),
        nameof(InvoiceFamilyCoreRecordGuard.WritesCoreLookup),
        nameof(InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord),
        nameof(InvoiceFamilyCoreRecordGuard.WorkAssignmentFiledUnderInvoice),
        "LinesOfAnotherCoreRecordQuery",
        nameof(InvoiceFamilyCoreRecordGuard.NamesTheSameRecord),
        "WithCanonicalPairId",
        nameof(InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl),
        nameof(InvoiceFamilyCoreRecordGuard.CanonicalPairId),
        nameof(InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage),
        nameof(InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget),
        // The update-only write of an invoice or line: it writes and answers a code, it decides nothing.
        nameof(InvoiceFamilyCoreRecordGuard.UpdateExistingAsync),
        nameof(InvoiceFamilyCoreRecordGuard.RefusalException),
        "ColumnOfKey",
        "TryParseBind",
        nameof(InvoiceFamilyCoreRecordGuard.AppOnlyRecords),
        nameof(InvoiceFamilyCoreRecordGuard.AppOnlyRecordTypes),
        "BuildResolverFields",
        "BuildGenericResolverFields",
        nameof(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordChanges),
        "RefuseChangeAfterCreate",
        "RefuseCreateWithoutInvoice",
        "FilingCoreTables",
        "get_FilingCoreTables",
    };

    private static readonly string[] WritePrefixes =
        ["Create", "Update", "Upsert", "Patch", "Post", "BulkUpdate", "Associate", "Execute"];

    /// <summary>The Dataverse write seams: the shared client library, the SDK, and the BFF's per-user Web API client.</summary>
    private static readonly string[] WriteNamespaces =
        ["Spaarke.Dataverse", "Microsoft.Xrm.Sdk", "Microsoft.PowerPlatform", "Sprk.Bff.Api.Infrastructure.Dataverse"];

    private const string BindSuffix = "@odata.bind";

    internal sealed record WriteUnit(string Method, IReadOnlyCollection<string> Writes);

    /// <summary>One outermost type's findings. <see cref="UnguardedWrites"/> is empty unless the type names a family table AND a core column.</summary>
    internal sealed record TypeFindings(
        string Type,
        IReadOnlyCollection<string> EnforcingCalls,
        IReadOnlyCollection<string> Tables,
        IReadOnlyCollection<string> Columns,
        IReadOnlyList<WriteUnit> UnguardedWrites)
    {
        internal bool IsCandidate => UnguardedWrites.Count > 0;

        internal string Describe() =>
            $"{Type} (tables: {string.Join(",", Tables)}; columns: {string.Join(",", Columns)}; unguarded writes: "
            + string.Join(" | ", UnguardedWrites.Select(u => $"{u.Method} -> {string.Join(",", u.Writes)}")) + ")";
    }

    internal static HashSet<string> Tables() => TableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> TableNames() =>
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys.SelectMany(t => new[] { t, t + "s" });

    internal static HashSet<string> Columns() =>
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Values.SelectMany(l => l).Select(l => l.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Columns that are also a core TABLE's name (<c>sprk_matter</c>, <c>sprk_project</c>: an invoice's and a line's typed lookups). A load
    /// of one of these as the entity-name argument of <c>new EntityReference(...)</c> is a table reference, not a column.
    /// </summary>
    private static HashSet<string> Ambiguous() =>
        Columns().Intersect(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Values.SelectMany(l => l).Select(l => l.CoreEntity),
            StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsDataverseWrite(MethodBase target) =>
        target.DeclaringType is { } declaring
        && WriteNamespaces.Any(ns => (declaring.Namespace ?? string.Empty).StartsWith(ns, StringComparison.Ordinal))
        && WritePrefixes.Any(p => target.Name.StartsWith(p, StringComparison.Ordinal));

    private static bool IsEnforcingCall(MethodBase target) =>
        target.DeclaringType is { } declaring && IlCallScan.Outermost(declaring) == Guard && Enforcing.Contains(target.Name);

    /// <summary>The source method a body belongs to, by name; a lambda's body (no source method) is one group per type.</summary>
    private static string SourceKey(Type caller, MethodBase body)
    {
        var name = IlCallScan.SourceMethod(caller, body)?.Name;
        if (name is null)
        {
            return "(closures)";
        }

        var local = Regex.Match(name, "^<(.+?)>g__");
        return local.Success ? local.Groups[1].Value : name;
    }

    /// <summary>
    /// The family table (or entity-set plural) a string names: the name itself, or the name that opens a Web API path
    /// (<c>sprk_invoices(</c>, <c>sprk_invoices/</c>, <c>/api/data/v9.2/sprk_invoices?...</c>; the compiler folds a constant set name and the
    /// text after it into one string). Null when it names none.
    /// </summary>
    private static string? TableOf(string literal, HashSet<string> tables)
    {
        var text = literal.Trim();
        if (tables.Contains(text))
        {
            return text;
        }

        var path = Regex.Match(text, @"^(?:/?api/data/v9\.\d+/)?/?(?<name>[A-Za-z0-9_]+)[(/?]", RegexOptions.CultureInvariant);
        return path.Success && tables.Contains(path.Groups["name"].Value) ? path.Groups["name"].Value : null;
    }

    private static string Normalize(string value)
    {
        var v = value.Trim();
        return v.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase) ? v[..^BindSuffix.Length].Trim() : v;
    }

    internal static IReadOnlyDictionary<string, TypeFindings> Scan(IEnumerable<Type> types)
    {
        var tables = Tables();
        var columns = Columns();
        var ambiguous = Ambiguous();

        var byOwner = types.Where(t => IlCallScan.Outermost(t) != Guard).GroupBy(t => IlCallScan.Outermost(t).FullName!, StringComparer.Ordinal);
        var result = new Dictionary<string, TypeFindings>(StringComparer.Ordinal);

        foreach (var group in byOwner)
        {
            var tableHits = new SortedSet<string>(StringComparer.Ordinal);
            var columnHits = new SortedSet<string>(StringComparer.Ordinal);
            var enforcingCalls = new SortedSet<string>(StringComparer.Ordinal);
            var writes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var guardedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in group)
            {
                foreach (var body in IlCallScan.DeclaredBodies(type))
                {
                    var key = SourceKey(type, body);
                    var instructions = IlCallScan.Instructions(body);
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var instruction = instructions[i];
                        switch (instruction.OpCode.OperandType)
                        {
                            case OperandType.InlineString:
                                var literal = Normalize(body.Module.ResolveString(instruction.Operand));
                                if (TableOf(literal, tables) is { } named)
                                {
                                    tableHits.Add(named.ToLowerInvariant());
                                }

                                if (columns.Contains(literal) && !(ambiguous.Contains(literal) && IsEntityNameArgument(body, instructions, i)))
                                {
                                    columnHits.Add(literal.ToLowerInvariant());
                                }

                                break;

                            case OperandType.InlineMethod:
                                var target = IlCallScan.Resolve(body, instruction.Operand, (m, t, ta, ma) => m.ResolveMethod(t, ta, ma));
                                if (target is null)
                                {
                                    break;
                                }

                                if (IsEnforcingCall(target))
                                {
                                    enforcingCalls.Add(target.Name);
                                    guardedKeys.Add(key);
                                }

                                if (IsDataverseWrite(target))
                                {
                                    if (!writes.TryGetValue(key, out var set))
                                    {
                                        writes[key] = set = new SortedSet<string>(StringComparer.Ordinal);
                                    }

                                    set.Add(target.DeclaringType!.Name + "." + target.Name);
                                }

                                break;
                        }
                    }
                }
            }

            var unguarded = new List<WriteUnit>();
            if (tableHits.Count > 0 && columnHits.Count > 0)
            {
                // A write in a method that calls an enforcing member is decided. A lambda's body cannot be tied to the method it sits in, so
                // it is decided when ANY body of the type calls one.
                unguarded.AddRange(writes
                    .Where(w => !guardedKeys.Contains(w.Key) && !(w.Key == "(closures)" && enforcingCalls.Count > 0))
                    .OrderBy(w => w.Key, StringComparer.Ordinal)
                    .Select(w => new WriteUnit(w.Key, w.Value.ToList())));
            }

            if (enforcingCalls.Count > 0 || unguarded.Count > 0)
            {
                result[group.Key] = new TypeFindings(group.Key, enforcingCalls.ToList(), tableHits.ToList(), columnHits.ToList(), unguarded);
            }
        }

        return result;
    }

    /// <summary>
    /// True when the string load at <paramref name="index"/> is the entity-name argument of a <c>new EntityReference(name, id)</c>: the
    /// constructor follows within three instructions with no other string load between. A dictionary key or an indexer key is followed by
    /// its value, which is a different shape.
    /// </summary>
    private static bool IsEntityNameArgument(MethodBase body, IReadOnlyList<IlCallScan.IlInstruction> instructions, int index)
    {
        for (var j = index + 1; j < instructions.Count && j <= index + 3; j++)
        {
            var next = instructions[j];
            if (next.OpCode == OpCodes.Ldstr)
            {
                return false;
            }

            if (next.OpCode == OpCodes.Newobj)
            {
                var ctor = IlCallScan.Resolve(body, next.Operand, (m, t, ta, ma) => m.ResolveMethod(t, ta, ma));
                return ctor?.DeclaringType == typeof(EntityReference);
            }
        }

        return false;
    }
}
