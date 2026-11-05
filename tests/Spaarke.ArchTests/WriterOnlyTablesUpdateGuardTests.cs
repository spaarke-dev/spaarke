using System.Reflection;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 048 (owner decision 2026-11-04, D-204 extended to updates): a Signal, a Decision Record
/// and a Budget Revision are changed only by their writers, so every generic update path refuses those tables through
/// <see cref="RecordTableRules.RefuseWriterOnlyTableUpdate"/>, which reads the same registry flag the create refusals read
/// (<see cref="RecordTableRule.CreatedOnlyByItsWriter"/>). This guard keeps the update paths and the registry in agreement.
/// </summary>
/// <remarks>
/// <para><b>Which paths.</b> The generic update paths are the members that ask the core record pair check
/// (<see cref="RecordTableRules.RefuseFixedAfterCreateColumns"/>): <c>CoreRecordPairUpdateCheckGuardTests</c> already makes every BFF
/// method that sends a generic update ask it, or be listed there because its table or columns are fixed. So each member that asks
/// that check must also ask the writer-only check. There is no exemption list (fix round 1, K1): the shared cores
/// (<c>ChildRecordEndpoints.UpdateAsync</c>, <c>OwnedChildWrite.RefileAsync</c>, the push's child writer) ask it themselves, whatever
/// their callers pass. A new generic update path is therefore caught twice: by that guard, then by this one. The chat tool's
/// <c>ValidateChat</c> and the field-mapping push's request check ask the writer-only check on their own (they send nothing) and are
/// pinned by name. <b>Limit (K1):</b> the scan sees only paths that ask the core record pair check, which the other guard finds
/// through <c>UpdateRecordFieldsAsync</c>, <c>UpdateExistingRecordFieldsAsync</c> and <c>IDataverseUserClient.PatchAsync</c>; a future
/// generic path on <c>IGenericEntityService.UpdateAsync</c>/<c>BulkUpdateAsync</c> or <c>DataverseWebApiClient.UpdateAsync</c> would not
/// be seen.</para>
/// <para><b>One list.</b> <see cref="RecordTableRules.RefuseWriterOnlyTableUpdate"/> refuses exactly the flagged tables, agrees
/// with the chat create tool's check, and its compiled body reads the flag (so it cannot become a hand-written list).</para>
/// <para><b>Per source member</b>, as <c>CoreRecordPairUpdateCheckGuardTests</c> attributes it (<see cref="CoreRecordPairUpdateCheckGuardTests.SourceMember"/>):
/// a lambda, local function or state machine counts as the member it is written in. <b>No list, no baseline</b>: a member that asks
/// the core record pair check and not the writer-only check fails the test. Enforced by: this guard (ArchTests
/// in CI and the Stop hook) and the behaviour tests (<c>WriterOnlyTablesUpdateRefusalTests</c>). <b>Controls</b>:
/// <see cref="Detector_MustFireAndMustNotFire"/> runs the detectors over the <c>WriterOnlyUpdateControl*</c> fixtures below.</para>
/// </remarks>
public class WriterOnlyTablesUpdateGuardTests
{
    /// <summary>The update paths that must ask the writer-only check (non-vacuity: the scan sees each one).</summary>
    private static readonly IReadOnlyList<(Type Type, string Method)> UpdateTools = new[]
    {
        (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler), "ValidateChat"),
        (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler), "ExecuteChatAsync"),
        (typeof(Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore), "UpdateAsync"),
        (typeof(Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler), "UpdateAsync"),
        (typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints), "ValidatePushRequest"),
        (typeof(Sprk.Bff.Api.Services.Ai.TopicRegistryWorkProductPersister), "PersistAsync"),
        // Fix round 1 (K1): the shared cores ask it themselves, whatever their callers pass.
        (typeof(Sprk.Bff.Api.Api.ChildRecordEndpoints), "UpdateAsync"),
        (typeof(Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite), "RefileAsync"),
        (typeof(Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints), "ApplyMappingsToChildRecordsAsync"),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "048 (D-204): every generic update path that asks the core record pair check also refuses the writer-only tables")]
    public void EveryGenericUpdatePath_RefusesTheWriterOnlyTables_OrIsListed()
    {
        var fixedAskers = AskersOf(BffTypes.Value, nameof(RecordTableRules.RefuseFixedAfterCreateColumns));
        var writerOnlyAskers = AskersOf(BffTypes.Value, nameof(RecordTableRules.RefuseWriterOnlyTableUpdate));

        var problems = MissingWriterOnlyCheck(fixedAskers, writerOnlyAskers)
            .Select(m => $"{m.Type.FullName}.{m.Method}: asks RecordTableRules.RefuseFixedAfterCreateColumns but not RefuseWriterOnlyTableUpdate")
            .ToList();

        foreach (var (type, method) in UpdateTools)
        {
            if (!writerOnlyAskers.Contains((type, method)))
                problems.Add($"{type.FullName}.{method}: expected to ask RecordTableRules.RefuseWriterOnlyTableUpdate; the scan lost its signal or the check was removed.");
        }

        Assert.True(problems.Count == 0,
            "A Signal, a Decision Record and a Budget Revision are changed only by their writers (owner decision D-204, extended to "
            + "updates). A generic update path asks RecordTableRules.RefuseWriterOnlyTableUpdate itself, before it reads or writes a "
            + "row; there is no exemption list:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "048 (D-204): the update refusal and the create refusals read one list, the registry flag")]
    public void TheUpdateRefusal_IsTheRegistryFlag_TheListTheCreateRefuses()
    {
        var flagged = RecordTableRules.All.Where(r => r.CreatedOnlyByItsWriter).Select(r => r.Table).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "sprk_budgetrevision", "sprk_decisionrecord", "sprk_signal" }, flagged);

        foreach (var rule in RecordTableRules.All)
        {
            var refused = RecordTableRules.RefuseWriterOnlyTableUpdate(rule.Table);
            Assert.True((refused is not null) == rule.CreatedOnlyByItsWriter, $"{rule.Table}: the update refusal disagrees with the registry flag.");
            Assert.True((refused is not null) == Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter(rule.Table),
                $"{rule.Table}: the update refusal disagrees with the chat create tool's refusal.");
            if (refused is not null)
            {
                Assert.Equal(RecordTableRules.ChangedOnlyByItsWriterReasonCode, refused.RefusalCode);
                Assert.Equal(RecordTableRules.ChangedOnlyByItsWriterMessage(rule.Table), refused.Reason);
                // Fix round 1: every path but the chat tool answers in a plain sentence; the model's wording is the chat tool's own.
                Assert.DoesNotContain("Do NOT", refused.Reason);
                Assert.Contains("Do NOT retry this update",
                    Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage(rule.Table));
            }
        }

        // Matched trimmed and in any case; an unregistered table is not refused here.
        Assert.NotNull(RecordTableRules.RefuseWriterOnlyTableUpdate(" SPRK_Signal "));
        Assert.Null(RecordTableRules.RefuseWriterOnlyTableUpdate("sprk_notregisteredfixture"));
        Assert.Null(RecordTableRules.RefuseWriterOnlyTableUpdate(null));

        Assert.True(ReadsTheWriterOnlyFlag(typeof(RecordTableRules).GetMethod(
                nameof(RecordTableRules.RefuseWriterOnlyTableUpdate), BindingFlags.Static | BindingFlags.NonPublic)!),
            "RecordTableRules.RefuseWriterOnlyTableUpdate must read RecordTableRule.CreatedOnlyByItsWriter, so create and update refuse one list.");
    }

    [Fact(DisplayName = "048 control: a path that asks only the core record pair check, and a check that holds its own list, are found; the clean shapes are not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(WriterOnlyUpdateControlFixedCheckOnly), typeof(WriterOnlyUpdateControlBothChecks),
            typeof(WriterOnlyUpdateControlBothChecksInLocalFunction),
        };
        var types = fixtures.SelectMany(IlCallScan.WithNested).ToList();
        var fixedAskers = AskersOf(types, nameof(RecordTableRules.RefuseFixedAfterCreateColumns));
        var writerOnlyAskers = AskersOf(types, nameof(RecordTableRules.RefuseWriterOnlyTableUpdate));

        // Must fire: the core record pair check without the writer-only check (the two clean fixtures are not named).
        Assert.Equal(
            new[] { $"{nameof(WriterOnlyUpdateControlFixedCheckOnly)}.Update" },
            MissingWriterOnlyCheck(fixedAskers, writerOnlyAskers).Select(m => $"{m.Type.Name}.{m.Method}").ToArray());

        // The flag read: a hand-written list is found; the flag read (the production shape) is not.
        Assert.False(ReadsTheWriterOnlyFlag(typeof(WriterOnlyUpdateControlFlagReads).GetMethod(
            nameof(WriterOnlyUpdateControlFlagReads.HandWrittenList), BindingFlags.Static | BindingFlags.NonPublic)!));
        Assert.True(ReadsTheWriterOnlyFlag(typeof(WriterOnlyUpdateControlFlagReads).GetMethod(
            nameof(WriterOnlyUpdateControlFlagReads.ReadsTheFlag), BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    /// <summary>The detector: each member that asks the core record pair check and does not ask the writer-only check.</summary>
    internal static IReadOnlyList<(Type Type, string Method)> MissingWriterOnlyCheck(
        IReadOnlySet<(Type Type, string Method)> fixedAskers,
        IReadOnlySet<(Type Type, string Method)> writerOnlyAskers)
    {
        return fixedAskers.Where(m => !writerOnlyAskers.Contains(m))
            .OrderBy(m => $"{m.Type.FullName}.{m.Method}", StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Each (outermost type, source member) whose compiled body references <c>RecordTableRules.{check}</c>.</summary>
    internal static IReadOnlySet<(Type Type, string Method)> AskersOf(IEnumerable<Type> types, string check) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(RecordTableRules) && r.Target.Name == check)
            .Select(r => (IlCallScan.Outermost(r.Caller), CoreRecordPairUpdateCheckGuardTests.SourceMember(r.Caller, r.CallerMethod)))
            .ToHashSet();

    /// <summary>Whether <paramref name="method"/>'s compiled body reads <see cref="RecordTableRule.CreatedOnlyByItsWriter"/>.</summary>
    internal static bool ReadsTheWriterOnlyFlag(MethodInfo method) =>
        IlCallScan.ReferencedMethods(IlCallScan.CompiledBody(method)).Any(m =>
            m.DeclaringType == typeof(RecordTableRule) && m.Name == "get_" + nameof(RecordTableRule.CreatedOnlyByItsWriter));
}

// Compiled control fixtures for WriterOnlyTablesUpdateGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: asks the core record pair check only.</summary>
internal static class WriterOnlyUpdateControlFixedCheckOnly
{
    internal static bool Update(string table, IEnumerable<string> keys) =>
        RecordTableRules.RefuseFixedAfterCreateColumns(table, keys) is null;
}

/// <summary>MUST NOT FIRE: asks both checks.</summary>
internal static class WriterOnlyUpdateControlBothChecks
{
    internal static bool Update(string table, IEnumerable<string> keys) =>
        RecordTableRules.RefuseFixedAfterCreateColumns(table, keys) is null && RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null;
}

/// <summary>MUST NOT FIRE: asks both checks, the second inside an async local function of the same member.</summary>
internal static class WriterOnlyUpdateControlBothChecksInLocalFunction
{
    internal static async Task<bool> Update(string table, IEnumerable<string> keys)
    {
        if (RecordTableRules.RefuseFixedAfterCreateColumns(table, keys) is not null)
            return false;

        async Task<bool> Allowed()
        {
            await Task.Yield();
            return RecordTableRules.RefuseWriterOnlyTableUpdate(table) is null;
        }

        return await Allowed();
    }
}

/// <summary>The flag-read controls: a hand-written list (must fire) and the registry read (must not fire).</summary>
internal static class WriterOnlyUpdateControlFlagReads
{
    internal static bool HandWrittenList(string table) => table is "sprk_signal" or "sprk_decisionrecord" or "sprk_budgetrevision";

    internal static bool ReadsTheFlag(string table) => RecordTableRules.Find(table) is { CreatedOnlyByItsWriter: true };
}
