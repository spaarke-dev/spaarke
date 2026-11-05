using System.Reflection;
using Spaarke.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// <see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/> is a Web API PATCH without <c>If-Match</c>: for an id
/// that names no row, Dataverse creates one (an upsert). A path that only updates existing records calls
/// <see cref="IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync(string, Guid, Dictionary{string, object?}, Guid?, CancellationToken)"/>
/// instead, which sends <c>If-Match: *</c> and refuses an unknown id with nothing written. Only the types listed below, each
/// with its reason, may call <c>UpdateRecordFieldsAsync</c>.
/// </summary>
/// <remarks>
/// <para><b>Why IL.</b> <see cref="IlCallScan"/> reads the compiled method references, so a comment, a string or a <c>nameof</c>
/// is not a call, and a call from a lambda, a local function or an async state machine is attributed to the type it is written
/// in (<see cref="IlCallScan.Outermost"/>). A call through the interface or through a type that implements it counts; the
/// implementations themselves are the API and are not callers.</para>
/// <para><b>Closed list, no baseline</b>: a listed type that no longer calls the method fails the test, so the list only
/// shrinks. <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the
/// <c>UpdateRecordFieldsControl*</c> fixtures below. Enforced by: this guard (ArchTests in CI and the Stop hook) and the
/// behaviour tests of the update-only paths (<c>InvoiceFamilyGenericUpdatePathTests.UpdateOnly</c>, every table on all three).</para>
/// </remarks>
public class UpdateRecordFieldsCallerGuardTests
{
    /// <summary>The closed list: the types that may call <c>UpdateRecordFieldsAsync</c>, each with its reason.</summary>
    private static readonly IReadOnlyList<(Type Type, string Reason)> AllowedCallers = new[]
    {
        (typeof(Sprk.Bff.Api.Services.RecordCreation.RecordCreationService),
            "Relies on create-if-missing: the unified create (step 11) PATCHes a fresh Guid.NewGuid() id to create the row."),
        (typeof(Sprk.Bff.Api.Services.Finance.SignalEvaluationService),
            "Relies on create-if-missing: the spend signal is created or re-asserted by an upsert on its deterministic id."),
    };

    /// <summary>The generic update paths that update existing records only; none may call <c>UpdateRecordFieldsAsync</c>.</summary>
    private static readonly Type[] UpdateOnlyPaths =
    {
        typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler),
        typeof(Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore),
        typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "UpdateRecordFieldsAsync (create-if-missing): only the listed types call it")]
    public void OnlyListedTypes_CallUpdateRecordFieldsAsync()
    {
        var callers = Callers(BffTypes.Value);

        var problems = new List<string>();
        foreach (var caller in callers.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!AllowedCallers.Any(a => a.Type == caller))
                problems.Add($"{caller.FullName}: calls IFieldMappingDataverseService.UpdateRecordFieldsAsync, which creates the row when "
                    + "the id names none. Call UpdateExistingRecordFieldsAsync (update-only, If-Match: *; it throws KeyNotFoundException "
                    + "for an unknown id) unless the path must create, and then list the type here with its reason.");
        }

        foreach (var (type, _) in AllowedCallers)
        {
            if (!callers.Contains(type))
                problems.Add($"STALE entry {type.FullName}: it no longer calls UpdateRecordFieldsAsync; remove it from the list.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact(DisplayName = "UpdateRecordFieldsAsync guard: the update-only generic update paths do not call it, and do call UpdateExistingRecordFieldsAsync")]
    public void TheUpdateOnlyPaths_DoNotCallUpdateRecordFieldsAsync()
    {
        var callers = Callers(BffTypes.Value);
        var updateOnly = UpdateExistingCallers(BffTypes.Value);

        foreach (var type in UpdateOnlyPaths)
        {
            Assert.DoesNotContain(type, callers);
            // Non-vacuity: the scan sees the path's write, through the update-only method.
            Assert.Contains(type, updateOnly);
        }
    }

    [Fact(DisplayName = "UpdateRecordFieldsAsync guard control: a call is found (direct, local function); UpdateExistingRecordFieldsAsync and a nameof are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(UpdateRecordFieldsControlDirectCall), typeof(UpdateRecordFieldsControlInLocalFunction),
            typeof(UpdateRecordFieldsControlUpdateExisting), typeof(UpdateRecordFieldsControlNameOnly),
        };
        var callers = Callers(fixtures.SelectMany(IlCallScan.WithNested));

        // Must fire: a direct call and a call in an async local function.
        Assert.Equal(
            new[] { nameof(UpdateRecordFieldsControlDirectCall), nameof(UpdateRecordFieldsControlInLocalFunction) },
            callers.Select(t => t.Name).Order(StringComparer.Ordinal).ToArray());

        // Must not fire: the update-only method (both overloads) and a nameof of the API.
        Assert.DoesNotContain(typeof(UpdateRecordFieldsControlUpdateExisting), callers);
        Assert.DoesNotContain(typeof(UpdateRecordFieldsControlNameOnly), callers);
        Assert.Contains(typeof(UpdateRecordFieldsControlUpdateExisting), UpdateExistingCallers(fixtures.SelectMany(IlCallScan.WithNested)));
    }

    private static bool IsTheApiItself(Type type) => typeof(IFieldMappingDataverseService).IsAssignableFrom(IlCallScan.Outermost(type));

    private static bool IsCall(MethodBase target, string name) =>
        target.Name == name
        && target.DeclaringType is { } declaring
        && typeof(IFieldMappingDataverseService).IsAssignableFrom(declaring);

    private static IReadOnlySet<Type> CallersOf(IEnumerable<Type> types, string name) =>
        IlCallScan.MethodReferences(types.Where(t => !IsTheApiItself(t)))
            .Where(r => IsCall(r.Target, name))
            .Select(r => IlCallScan.Outermost(r.Caller))
            .ToHashSet();

    /// <summary>The detector: each outermost type that calls <c>UpdateRecordFieldsAsync</c>.</summary>
    internal static IReadOnlySet<Type> Callers(IEnumerable<Type> types) =>
        CallersOf(types, nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync));

    private static IReadOnlySet<Type> UpdateExistingCallers(IEnumerable<Type> types) =>
        CallersOf(types, nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
}

// Compiled control fixtures for UpdateRecordFieldsCallerGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: a direct call.</summary>
internal static class UpdateRecordFieldsControlDirectCall
{
    internal static Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields) =>
        writes.UpdateRecordFieldsAsync(table, id, fields);
}

/// <summary>MUST FIRE: a call in an async local function.</summary>
internal static class UpdateRecordFieldsControlInLocalFunction
{
    internal static async Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields)
    {
        async Task Patch() => await writes.UpdateRecordFieldsAsync(table, id, fields, CancellationToken.None, Guid.NewGuid());
        await Patch();
    }
}

/// <summary>MUST NOT FIRE: the update-only method, both overloads.</summary>
internal static class UpdateRecordFieldsControlUpdateExisting
{
    internal static async Task Write(IFieldMappingDataverseService writes, string table, Guid id, Dictionary<string, object?> fields)
    {
        await writes.UpdateExistingRecordFieldsAsync(table, id, fields);
        await writes.UpdateExistingRecordFieldsAsync(table, id, fields, Guid.NewGuid(), CancellationToken.None);
    }
}

/// <summary>MUST NOT FIRE: only names the API (this comment calls UpdateRecordFieldsAsync; the code does not).</summary>
internal static class UpdateRecordFieldsControlNameOnly
{
    internal static string Name() => nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync);
}
