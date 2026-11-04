using System.Reflection;
using System.Text.RegularExpressions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The core record pair (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>) of a Signal and a Decision Record is fixed after
/// create on BFF update paths (<c>RecordTableRule.FixedAfterCreateColumns</c>). Every BFF method that sends an update payload
/// through a generic update API must therefore ask <see cref="RecordTableRules.RefuseFixedAfterCreateColumns"/>, or be listed
/// below with a one-line reason it cannot carry the pair (its table or its columns are fixed).
/// </summary>
/// <remarks>
/// <para><b>Generic update APIs</b>: <see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/> (the field-dictionary
/// update of the Update Record node, a playbook's output mapping and the field-mapping push) and
/// <see cref="IDataverseUserClient.PatchAsync(string, string, CancellationToken)"/> with its row-version overload (the caller's
/// own PATCH, which the chat update tool sends). A call through the interface or through a type that implements it counts.</para>
/// <para><b>Why IL.</b> <see cref="IlCallScan"/> reads the compiled method references, so the receiver's TYPE decides (a Graph
/// SDK <c>PatchAsync</c> is not this API) and a comment, a string or a <c>nameof</c> is not a call. <b>Per source method</b>:
/// a lambda, a local function or an async state machine is attributed to the member it is written in (the
/// <c>&lt;Member&gt;</c> prefix the compiler gives it), so the check and the write must be in the same member; a check in a
/// caller does not cover a helper that writes, which is listed instead. Overloads of one name are one entry. Types that
/// implement a scanned API are the API itself and are not callers.</para>
/// <para><b>Not covered</b>: an update sent through another API (<c>UpdateExistingRecordFieldsAsync</c>,
/// <c>IGenericEntityService.UpdateAsync</c>, a hand-written <c>HttpClient</c> PATCH) and a method chosen at run time
/// (<see cref="IlCallScan"/> remarks). Enforced by: this guard (ArchTests in CI and the Stop hook) and the behaviour tests
/// (<c>CoreRecordPairUpdateWriterTests</c>, <c>SecureChildOwnershipAiToolTests.CoreRecordPairUpdate</c>).</para>
/// <para><b>Closed list, no baseline</b>: a listed method that no longer calls a scanned API, or now asks the check, fails the
/// test, so the list only shrinks. <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over
/// the <c>CoreRecordPairUpdateControl*</c> fixtures below.</para>
/// </remarks>
public class CoreRecordPairUpdateCheckGuardTests
{
    /// <summary>The closed list: methods that send an update through a scanned API without the check, each with its reason.</summary>
    private static readonly IReadOnlyList<(Type Type, string Method, string Reason)> FixedPayloadWriters = new[]
    {
        // A helper whose only caller asks the check first.
        (typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler), "PatchAsync",
            "Private write helper of UpdateAsync (through WriteAsync / UpdateWithOptimisticConcurrencyAsync), which asks the check first."),

        // Fixed tables: none carries the core record pair.
        (typeof(Sprk.Bff.Api.Services.Dataverse.ProjectMatterFiling), "FileAsync",
            "Writes a sprk_project's regarding pair only."),
        (typeof(Sprk.Bff.Api.Api.Events.EventDueAssigneeWrite), "ApplyAsync",
            "Writes a sprk_event's due date and assignee columns; a fixed column set."),
        (typeof(Sprk.Bff.Api.Api.Reporting.ReportingEndpoints), "UpdateReport",
            "Writes a sprk_report row's own columns; a fixed table."),
        (typeof(Sprk.Bff.Api.Services.Finance.SignalEvaluationService), "UpsertSignalAsync",
            "Writes a sprk_spendsignal (not sprk_signal); a fixed column set."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "SavePolicyAsync",
            "Writes a sprk_policy's current-version pointer; a fixed column set."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "StampAndMoveAsync",
            "Writes a sprk_policy's pointer and scope; a fixed column set."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService), "StampInForceToAsync",
            "Writes a sprk_policyversion's sprk_inforceto only."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminClassificationService), "PatchAsync",
            "Writes a sprk_triagecategory row; a fixed table."),
        (typeof(Sprk.Bff.Api.Services.Ontology.OntologyAdminRulesService), "WriteAsync",
            "Writes a sprk_policy row; a fixed table."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.ExtendResponseDateExecutor), "WriteAsync",
            "Writes a sprk_workassignment's sprk_responseduedate only."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.RecordTheResponseExecutor), "WriteAsync",
            "Writes a sprk_workassignment's sprk_respondedon and sprk_responseoutcome only."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.ReviseBudgetExecutor), "ExecuteAsync",
            "Writes a sprk_budget's sprk_totalbudget only."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "CompleteTodoAsync",
            "Writes a sprk_todo's completion columns; a fixed column set."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "ConfirmSuggestedAssociationAsync",
            "Writes a sprk_communication's association columns; a fixed column set."),
        (typeof(Sprk.Bff.Api.Services.Signals.Actions.InquiryDispositionService), "RecordDispositionAsync",
            "Writes a sprk_servicerequest's disposition columns; a fixed column set."),

        // A create, not an update.
        (typeof(Sprk.Bff.Api.Services.RecordCreation.RecordCreationService), "CreateAsync",
            "The unified create (a fresh id): a create takes the pair; the fixed-after-create rule is update-only."),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "Core record pair: every BFF method that sends a generic update asks RefuseFixedAfterCreateColumns, or is listed")]
    public void EveryGenericUpdateWriter_AsksTheFixedAfterCreateCheck_OrIsListed()
    {
        var methods = Classify(BffTypes.Value);

        var problems = new List<string>();
        foreach (var ((type, method), asks) in methods.OrderBy(m => $"{m.Key.Type.FullName}.{m.Key.Method}", StringComparer.Ordinal))
        {
            var listed = FixedPayloadWriters.Any(a => a.Type == type && string.Equals(a.Method, method, StringComparison.Ordinal));
            if (!asks && !listed)
                problems.Add($"{type.FullName}.{method}: sends a generic update without asking RecordTableRules.RefuseFixedAfterCreateColumns");
            if (asks && listed)
                problems.Add($"STALE entry {type.FullName}.{method}: it now asks the check; remove it from the list.");
        }

        foreach (var (type, method, _) in FixedPayloadWriters)
        {
            if (!methods.ContainsKey((type, method)))
                problems.Add($"STALE entry {type.FullName}.{method}: it no longer sends a generic update; remove it from the list.");
        }

        // Non-vacuity: the update paths that ask the check in the member that writes are seen, with the check.
        foreach (var (type, method) in new[]
                 {
                     (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler), "ExecuteChatAsync"),
                     (typeof(Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore), "UpdateAsync"),
                     (typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints), "ApplyMappingsToChildRecordsAsync"),
                     (typeof(Sprk.Bff.Api.Api.ChildRecordEndpoints), "UpdateAsync"),
                     (typeof(Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite), "RefileAsync"),
                     (typeof(Sprk.Bff.Api.Services.Ai.TopicRegistryWorkProductPersister), "PersistAsync"),
                 })
        {
            if (!methods.TryGetValue((type, method), out var asks) || !asks)
                problems.Add($"{type.FullName}.{method}: expected to be seen sending a generic update and asking the check; the scan lost its signal.");
        }

        // Non-vacuity: the output mapping's DataverseUpdateHandler.UpdateAsync writes through its listed PatchAsync helper, so it
        // is no writer here; it must still reference the check.
        var askers = Askers(BffTypes.Value);
        foreach (var (type, method) in new[] { (typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler), "UpdateAsync") })
        {
            if (!askers.Contains((type, method)))
                problems.Add($"{type.FullName}.{method}: expected to reference RecordTableRules.RefuseFixedAfterCreateColumns; the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "The core record pair of a Signal and a Decision Record is fixed after create on BFF update paths. A method that sends a "
            + "generic update (IFieldMappingDataverseService.UpdateRecordFieldsAsync, IDataverseUserClient.PatchAsync) asks "
            + "RecordTableRules.RefuseFixedAfterCreateColumns before it writes, or is listed in FixedPayloadWriters with the reason "
            + "its table or columns are fixed:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Core record pair guard control: an update without the check is found (direct, lambda, other member); with it, or another API, is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(CoreRecordPairUpdateControlFieldsWithoutCheck), typeof(CoreRecordPairUpdateControlPatchInLambdaWithoutCheck),
            typeof(CoreRecordPairUpdateControlCheckInAnotherMember), typeof(CoreRecordPairUpdateControlCheckThenLocalFunctionWrite),
            typeof(CoreRecordPairUpdateControlNameOnly), typeof(CoreRecordPairUpdateControlOtherPatchApi),
        };
        var methods = Classify(fixtures.SelectMany(IlCallScan.WithNested));

        // Must fire: the writers that do not ask, including a lambda's write and a check that sits in another member.
        Assert.Equal(
            new[]
            {
                $"{nameof(CoreRecordPairUpdateControlCheckInAnotherMember)}.Write",
                $"{nameof(CoreRecordPairUpdateControlFieldsWithoutCheck)}.Write",
                $"{nameof(CoreRecordPairUpdateControlPatchInLambdaWithoutCheck)}.Write",
            },
            methods.Where(m => !m.Value).Select(m => $"{m.Key.Type.Name}.{m.Key.Method}").Order(StringComparer.Ordinal).ToArray());

        // Must not fire: the check then a write in a local function, as UpdateRecordActionCore does.
        Assert.Equal(
            new[] { $"{nameof(CoreRecordPairUpdateControlCheckThenLocalFunctionWrite)}.Write" },
            methods.Where(m => m.Value).Select(m => $"{m.Key.Type.Name}.{m.Key.Method}").ToArray());

        // Not a writer at all: a nameof of the API and another type's PatchAsync.
        Assert.DoesNotContain(methods.Keys, k => k.Type == typeof(CoreRecordPairUpdateControlNameOnly));
        Assert.DoesNotContain(methods.Keys, k => k.Type == typeof(CoreRecordPairUpdateControlOtherPatchApi));
    }

    /// <summary>The member a compiled body was written in: a lambda, local function or state machine maps to its <c>&lt;Member&gt;</c>.</summary>
    private static readonly Regex GeneratedName = new(@"^<+([^<>]+)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string SourceMember(Type caller, MethodBase callerMethod)
    {
        // The source method's name, the body's own name, then the types it is nested in (inner to outer): the first generated
        // name ("<Member>b__0", "<Member>g__Local|1_0", "<<Member>b__0>d", "<Member>d__3") names the member it was written in.
        var source = IlCallScan.SourceMethod(caller, callerMethod);
        var names = new List<string>();
        if (source is not null)
            names.Add(source.Name);
        names.Add(callerMethod.Name);
        for (var t = caller; t != IlCallScan.Outermost(caller); t = t.DeclaringType!)
            names.Add(t.Name);

        foreach (var name in names)
        {
            var match = GeneratedName.Match(name);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return IlCallScan.SourceMethodName(caller, callerMethod);
    }

    private static bool IsGenericUpdate(MethodBase target)
    {
        var declaring = target.DeclaringType;
        if (declaring is null)
            return false;

        return (target.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                && typeof(IFieldMappingDataverseService).IsAssignableFrom(declaring))
               || (target.Name == nameof(IDataverseUserClient.PatchAsync)
                   && typeof(IDataverseUserClient).IsAssignableFrom(declaring));
    }

    private static bool IsTheCheck(MethodBase target) =>
        target.DeclaringType == typeof(RecordTableRules) && target.Name == nameof(RecordTableRules.RefuseFixedAfterCreateColumns);

    private static bool IsTheApiItself(Type type) =>
        typeof(IFieldMappingDataverseService).IsAssignableFrom(IlCallScan.Outermost(type))
        || typeof(IDataverseUserClient).IsAssignableFrom(IlCallScan.Outermost(type));

    private static List<((Type Type, string Method) Key, MethodBase Target)> References(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.Where(t => !IsTheApiItself(t)))
            .Select(r => (Key: (IlCallScan.Outermost(r.Caller), SourceMember(r.Caller, r.CallerMethod)), r.Target))
            .ToList();

    /// <summary>Each (outermost type, source member) that references the check, whether or not it writes.</summary>
    internal static IReadOnlySet<(Type Type, string Method)> Askers(IEnumerable<Type> types) =>
        References(types).Where(r => IsTheCheck(r.Target)).Select(r => r.Key).ToHashSet();

    /// <summary>The detector: each (outermost type, source member) that sends a generic update, and whether it asks the check.</summary>
    internal static IReadOnlyDictionary<(Type Type, string Method), bool> Classify(IEnumerable<Type> types)
    {
        var references = References(types);

        var asks = references.Where(r => IsTheCheck(r.Target)).Select(r => r.Key).ToHashSet();
        return references.Where(r => IsGenericUpdate(r.Target))
            .Select(r => r.Key)
            .Distinct()
            .ToDictionary(k => k, asks.Contains);
    }
}

// Compiled control fixtures for CoreRecordPairUpdateCheckGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: a field-dictionary update with no check.</summary>
internal static class CoreRecordPairUpdateControlFieldsWithoutCheck
{
    internal static Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields) =>
        writes.UpdateRecordFieldsAsync(table, id, fields);
}

/// <summary>MUST FIRE: the caller's PATCH inside a lambda, with no check.</summary>
internal static class CoreRecordPairUpdateControlPatchInLambdaWithoutCheck
{
    internal static Task Write(IDataverseUserClient user, string path, string body)
    {
        Func<Task> send = async () => await user.PatchAsync(path, body, CancellationToken.None);
        return send();
    }
}

/// <summary>MUST FIRE (Write): the check sits in another member of the same type, not in the one that writes.</summary>
internal static class CoreRecordPairUpdateControlCheckInAnotherMember
{
    internal static bool Allowed(string table, IEnumerable<string> keys) =>
        RecordTableRules.RefuseFixedAfterCreateColumns(table, keys) is null;

    internal static Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields) =>
        writes.UpdateRecordFieldsAsync(table, id, fields);
}

/// <summary>MUST NOT FIRE: asks the check, then writes in an async local function (the UpdateRecordActionCore shape).</summary>
internal static class CoreRecordPairUpdateControlCheckThenLocalFunctionWrite
{
    internal static async Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields)
    {
        if (RecordTableRules.RefuseFixedAfterCreateColumns(table, fields.Keys) is not null)
            return;

        async Task Patch() => await writes.UpdateRecordFieldsAsync(table, id, fields);
        await Patch();
    }
}

/// <summary>MUST NOT FIRE: only names the API (this comment calls UpdateRecordFieldsAsync; the code does not).</summary>
internal static class CoreRecordPairUpdateControlNameOnly
{
    internal static string Name() => nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync) + nameof(IDataverseUserClient.PatchAsync);
}

/// <summary>MUST NOT FIRE: a PatchAsync of another API (as the Graph SDK's) is not the caller's Dataverse PATCH.</summary>
internal static class CoreRecordPairUpdateControlOtherPatchApi
{
    internal sealed class OtherClient
    {
        internal Task PatchAsync(string path, string body, CancellationToken ct) => Task.CompletedTask;
    }

    internal static Task Write(OtherClient client) => client.PatchAsync("x", "{}", CancellationToken.None);
}
