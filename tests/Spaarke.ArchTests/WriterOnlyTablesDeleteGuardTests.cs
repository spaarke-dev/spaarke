using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 048 fix round 2 (owner decision 2026-11-05, D-204 extended to the general delete path):
/// a Signal, a Decision Record and a Budget Revision are changed only by their writers, so the general delete path refuses those
/// tables through <see cref="RecordTableRules.RefuseWriterOnlyTableDelete"/>, which reads the same registry flag the create and
/// update refusals read (<see cref="RecordTableRule.CreatedOnlyByItsWriter"/>). Sibling of <see cref="WriterOnlyTablesUpdateGuardTests"/>.
/// </summary>
/// <remarks>
/// <para><b>Which paths.</b> The only delete whose table a caller (or a playbook's configuration) chooses is the chat delete tool
/// (<c>DataverseDeleteRecordHandler</c>), which asks the check in both <c>ValidateChat</c> and <c>ExecuteChatAsync</c> and is pinned by
/// name. Every other BFF member that sends a delete through a scanned API (<c>IDataverseUserClient.DeleteAsync</c>,
/// <c>IGenericEntityService.DeleteAsync</c>, <c>DataverseWebApiClient.DeleteAsync</c>) either asks the check or is listed below with the
/// reason its table is fixed in code. A new delete path is therefore caught here and has to decide. <b>Closed list, no baseline</b>: a
/// listed member that now asks the check, or no longer sends a delete, fails the test.</para>
/// <para><b>Exempt: the method that implements a scanned API's <c>DeleteAsync</c></b> (task 049 fix round 1; narrowed in fix round 3).
/// A decorator of <c>IDataverseUserClient</c> (e.g. <c>CallerMetadataReadMemo</c>) implements <c>DeleteAsync</c> by passing the caller's
/// path on to the client it wraps. It chooses no table: its callers do, and the scan already sees each of them at its own call to that
/// API. So a call to a scanned API's <c>DeleteAsync</c> is not counted when the calling member IS the method that implements it: the
/// target of the type's interface map for an interface API, or an override of the class API's virtual <c>DeleteAsync</c>. The exemption
/// is by that method's identity, not by its name: another member that deletes is still counted, including an extra <c>DeleteAsync</c>
/// overload on the same type and an override of any other virtual that calls <c>base.DeleteAsync</c>.</para>
/// <para><b>One list.</b> The refusal equals the registry flag table by table, agrees with the update refusal and the chat create
/// tool's check, and its compiled body reads the flag (it cannot become a hand-written list).</para>
/// <para><b>Limit.</b> The scan sees only those three APIs; a future delete on another API (the Dataverse SDK client directly, a raw
/// HTTP call) would not be seen. Enforced by: this guard (ArchTests in CI and the Stop hook) and the behaviour tests
/// (<c>DataverseDeleteRecordHandlerTests</c>). <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/>.</para>
/// </remarks>
public class WriterOnlyTablesDeleteGuardTests
{
    /// <summary>Members that send a delete through a scanned API without the check, each with the reason its table is fixed in code.</summary>
    private static readonly IReadOnlyList<(Type Type, string Method, string Reason)> FixedTableDeletes = new[]
    {
        (typeof(Sprk.Bff.Api.Api.Ai.AnalysisEndpoints), "CompensatePromoteAnalysisDeleteAsync",
            "Compensates a promote: removes the sprk_analysis row it just created (AnalysisEntityLogicalName)."),
        (typeof(Sprk.Bff.Api.Api.Reporting.ReportingEndpoints), "CreateReport",
            "Rolls back the sprk_report row this create just wrote; a fixed table."),
        (typeof(Sprk.Bff.Api.Api.Reporting.ReportingEndpoints), "DeleteReport",
            "Removes a sprk_report row by its own route; the path is built from the fixed set name sprk_reports."),
        (typeof(Sprk.Bff.Api.Api.SpeAdmin.ConfigEndpoints), "DeleteConfigAsync",
            "Removes a row of the route's own constant entity set (the SPE admin configuration table)."),
        (typeof(Sprk.Bff.Api.Api.SpeAdmin.EnvironmentEndpoints), "DeleteEnvironmentAsync",
            "Removes a row of the route's own constant entity set (the SPE admin environment table)."),
        (typeof(Sprk.Bff.Api.Services.Access.SecureRootInheritance), "CompleteIsolatedCreateAsync",
            "Removes the work assignment or project this call was just handed after creating it into isolation; its table comes from the create path, which refuses the three tables."),
        (typeof(Sprk.Bff.Api.Services.Ai.Membership.MembershipJunctionUpdater), "HandleAsync",
            "Removes rows of its own junction table (JunctionEntityLogicalName)."),
        (typeof(Sprk.Bff.Api.Services.Finance.Files.StoredFileReconcileJob), "DeleteRowAsync",
            "Removes a stored-file row (StoredFileSchema.Entity)."),
        (typeof(Sprk.Bff.Api.Services.Finance.RateCardImportService), "CompensateAsync",
            "Rolls back rate-card rows this import just created; the tables are the import's own."),
        (typeof(Sprk.Bff.Api.Services.Finance.RuleSetService), "CreateDraftRuleAsync",
            "Rolls back the rule rows it just created; fixed finance tables."),
        (typeof(Sprk.Bff.Api.Services.Finance.RuleSetService), "DeleteIfPresentAsync",
            "Private helper of the compensations above; its callers pass the policy and version entities."),
        (typeof(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService), "CompensateAsync",
            "Rolls back the invoice submission row it just created (SubmissionEntity)."),
        (typeof(Sprk.Bff.Api.Services.Finance.Submission.SubmissionCommitGate), "DeleteIfPresentAsync",
            "Private helper whose callers pass the approval, line and invoice entities of the submission."),
        (typeof(Sprk.Bff.Api.Services.Workspace.WorkspaceLayoutService), "DeleteLayoutAsync",
            "Removes a workspace layout row (EntityName); a fixed table."),
    };

    private static readonly IReadOnlyList<(Type Type, string Method)> DeleteTools = new[]
    {
        (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseDeleteRecordHandler), "ValidateChat"),
        (typeof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseDeleteRecordHandler), "ExecuteChatAsync"),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "048 fix 2 (D-204): every BFF member that sends a delete through a scanned API refuses the writer-only tables, or is listed with its fixed table")]
    public void EveryGenericDeletePath_RefusesTheWriterOnlyTables_OrIsListed()
    {
        var deleters = DeletersOf(BffTypes.Value);
        var askers = AskersOf(BffTypes.Value);

        var problems = MissingDeleteCheck(deleters, askers, FixedTableDeletes.Select(p => (p.Type, p.Method)))
            .Select(m => $"{m.Type.FullName}.{m.Method}: sends a delete through a scanned API without asking RecordTableRules.RefuseWriterOnlyTableDelete")
            .ToList();

        foreach (var (type, method, _) in FixedTableDeletes)
        {
            if (askers.Contains((type, method)))
                problems.Add($"STALE entry {type.FullName}.{method}: it now asks the delete check; remove it from the list.");
            if (!deleters.Contains((type, method)))
                problems.Add($"STALE entry {type.FullName}.{method}: it no longer sends a delete through a scanned API; remove it from the list.");
        }

        foreach (var (type, method) in DeleteTools)
        {
            if (!deleters.Contains((type, method)) && method == "ExecuteChatAsync")
                problems.Add($"{type.FullName}.{method}: expected to be seen sending a delete; the scan lost its signal.");
            if (!askers.Contains((type, method)))
                problems.Add($"{type.FullName}.{method}: expected to ask RecordTableRules.RefuseWriterOnlyTableDelete; the check was removed.");
        }

        Assert.True(problems.Count == 0,
            "A Signal, a Decision Record and a Budget Revision are changed only by their writers (owner decision D-204, extended to the "
            + "general delete path). A member that deletes a caller- or configuration-chosen table asks "
            + "RecordTableRules.RefuseWriterOnlyTableDelete before it reads or deletes; one that deletes a table fixed in code is listed in "
            + "FixedTableDeletes with the reason:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "048 fix 2 (D-204): the delete refusal, the update refusal and the create refusals read one list, the registry flag")]
    public void TheDeleteRefusal_IsTheRegistryFlag_TheListTheOtherRefusalsRead()
    {
        foreach (var rule in RecordTableRules.All)
        {
            var refused = RecordTableRules.RefuseWriterOnlyTableDelete(rule.Table);
            Assert.True((refused is not null) == rule.CreatedOnlyByItsWriter, $"{rule.Table}: the delete refusal disagrees with the registry flag.");
            Assert.True((refused is not null) == (RecordTableRules.RefuseWriterOnlyTableUpdate(rule.Table) is not null),
                $"{rule.Table}: the delete refusal disagrees with the update refusal.");
            // ebilling-r1 task 164a-1 (uac-r2 Q6): the chat create refuses every table the delete refuses, and the create-only
            // tables besides (RecordTableRule.GenericCreateRefused; an invoice line's delete is not refused).
            var createRefused = Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter(rule.Table);
            Assert.True(createRefused == rule.GenericCreateRefused, $"{rule.Table}: the chat create tool's refusal disagrees with the registry.");
            Assert.True(refused is null || createRefused,
                $"{rule.Table}: the delete refusal is not matched by the chat create tool's refusal.");
            if (refused is not null)
            {
                Assert.Equal("record_delete.changed_only_by_its_writer", refused.RefusalCode);
                Assert.Equal(RecordTableRules.RemovedOnlyByItsWriterReasonCode, refused.RefusalCode);
                Assert.Equal(RecordTableRules.ChangedOnlyByItsWriterDeleteMessage(rule.Table), refused.Reason);
                Assert.DoesNotContain("Do NOT", refused.Reason);
                Assert.Contains("Do NOT retry this request",
                    Sprk.Bff.Api.Services.Ai.Handlers.DataverseDeleteRecordHandler.ChangedOnlyByItsWriterChatMessage(rule.Table));
            }
        }

        Assert.NotNull(RecordTableRules.RefuseWriterOnlyTableDelete(" SPRK_Signal "));
        Assert.Null(RecordTableRules.RefuseWriterOnlyTableDelete("sprk_notregisteredfixture"));
        Assert.Null(RecordTableRules.RefuseWriterOnlyTableDelete(null));

        Assert.True(WriterOnlyTablesUpdateGuardTests.ReadsTheWriterOnlyFlag(typeof(RecordTableRules).GetMethod(
                nameof(RecordTableRules.RefuseWriterOnlyTableDelete), BindingFlags.Static | BindingFlags.NonPublic)!),
            "RecordTableRules.RefuseWriterOnlyTableDelete must read RecordTableRule.CreatedOnlyByItsWriter, so create, update and delete refuse one list.");
    }

    [Fact(DisplayName = "048 fix 2 control: a member that deletes without the check is found; one that asks it, or is listed, is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(WriterOnlyDeleteControlNoCheck), typeof(WriterOnlyDeleteControlWithCheck), typeof(WriterOnlyDeleteControlWebApiNoCheck),
            typeof(WriterOnlyDeleteControlForwardingDecorator), typeof(WriterOnlyDeleteControlOverloadDecorator),
            typeof(WriterOnlyDeleteControlWebApiSubclass),
        };
        var types = fixtures.SelectMany(IlCallScan.WithNested).ToList();
        var deleters = DeletersOf(types);
        var askers = AskersOf(types);

        // Must fire: a delete through the generic entity service, and one through the Web API client, without the check; a
        // decorator's OTHER member that deletes through the client it wraps; an extra DeleteAsync(Guid) OVERLOAD on a decorator
        // (the same name as the interface method, so only the method's identity tells them apart); and a subclass's override of
        // ANOTHER virtual that calls base.DeleteAsync on a fixed table. Must not fire: a decorator's own DeleteAsync, forwarding
        // to that client, and a subclass's override of the base virtual DeleteAsync (the exemption).
        Assert.Equal(
            new[]
            {
                $"{nameof(WriterOnlyDeleteControlForwardingDecorator)}.Purge",
                $"{nameof(WriterOnlyDeleteControlNoCheck)}.Delete",
                $"{nameof(WriterOnlyDeleteControlOverloadDecorator)}.DeleteAsync",
                $"{nameof(WriterOnlyDeleteControlWebApiNoCheck)}.Delete",
                $"{nameof(WriterOnlyDeleteControlWebApiSubclass)}.UpdateAsync",
            },
            MissingDeleteCheck(deleters, askers, []).Select(m => $"{m.Type.Name}.{m.Method}").Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain((typeof(WriterOnlyDeleteControlForwardingDecorator), "DeleteAsync"), deleters);
        Assert.DoesNotContain((typeof(WriterOnlyDeleteControlWebApiSubclass), "DeleteAsync"), deleters);

        // The real decorator is exempt through its interface map.
        var memo = DeletersOf(IlCallScan.WithNested(typeof(Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient).Assembly
            .GetTypes().Single(t => t.Name == "CallerMetadataReadMemo")).ToList());
        Assert.DoesNotContain(memo, m => m.Method == "DeleteAsync");

        // Must not fire when listed; the member that asks the check is never named.
        Assert.Equal(
            new[] { $"{nameof(WriterOnlyDeleteControlWebApiNoCheck)}.Delete" },
            MissingDeleteCheck(deleters, askers,
                    [
                        (typeof(WriterOnlyDeleteControlNoCheck), "Delete"), (typeof(WriterOnlyDeleteControlForwardingDecorator), "Purge"),
                        (typeof(WriterOnlyDeleteControlOverloadDecorator), "DeleteAsync"),
                        (typeof(WriterOnlyDeleteControlWebApiSubclass), "UpdateAsync"),
                    ])
                .Select(m => $"{m.Type.Name}.{m.Method}").ToArray());
    }

    /// <summary>The detector: each deleting member that does not ask the delete check and is not listed.</summary>
    internal static IReadOnlyList<(Type Type, string Method)> MissingDeleteCheck(
        IReadOnlySet<(Type Type, string Method)> deleters,
        IReadOnlySet<(Type Type, string Method)> askers,
        IEnumerable<(Type Type, string Method)> listed)
    {
        var excused = listed.ToHashSet();
        return deleters.Where(m => !askers.Contains(m) && !excused.Contains(m))
            .OrderBy(m => $"{m.Type.FullName}.{m.Method}", StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The APIs whose <c>DeleteAsync</c> the scan counts.</summary>
    private static readonly IReadOnlyList<Type> ScannedDeleteApis =
        [typeof(IDataverseUserClient), typeof(IGenericEntityService), typeof(DataverseWebApiClient)];

    /// <summary>
    /// Each (outermost type, source member) whose compiled body sends a delete through a scanned API, except a type's own
    /// <c>DeleteAsync</c> forwarding to the API that type implements (see the class remarks).
    /// </summary>
    internal static IReadOnlySet<(Type Type, string Method)> DeletersOf(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.Name == "DeleteAsync" && ScannedDeleteApis.Contains(r.Target.DeclaringType!))
            .Select(r => (Caller: r.Caller, CallerMethod: r.CallerMethod,
                Member: CoreRecordPairUpdateCheckGuardTests.SourceMember(r.Caller, r.CallerMethod), Api: r.Target.DeclaringType!))
            .Where(d => !IsTheApisOwnForwardingDelete(d.Caller, d.CallerMethod, d.Api))
            .Select(d => (IlCallScan.Outermost(d.Caller), d.Member))
            .ToHashSet();

    /// <summary>
    /// Whether the calling body belongs to the method that implements <paramref name="api"/>'s <c>DeleteAsync</c>: for an interface
    /// API the target of the type's interface map for a <c>DeleteAsync</c> of that interface, for a class API an override of its
    /// virtual <c>DeleteAsync</c>. Decided by the method's metadata identity, never by its name alone.
    /// </summary>
    private static bool IsTheApisOwnForwardingDelete(Type caller, MethodBase callerMethod, Type api)
    {
        if (IlCallScan.SourceMethod(caller, callerMethod) is not MethodInfo source || source.Name != "DeleteAsync"
            || source.DeclaringType is not { } type || type == api || !api.IsAssignableFrom(type))
        {
            return false;
        }

        if (api.IsInterface)
        {
            var map = type.GetInterfaceMap(api);
            for (var i = 0; i < map.TargetMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == "DeleteAsync" && map.TargetMethods[i].HasSameMetadataDefinitionAs(source))
                    return true;
            }

            return false;
        }

        var baseDefinition = source.GetBaseDefinition();
        return baseDefinition.Name == "DeleteAsync" && baseDefinition.DeclaringType == api
               && !baseDefinition.HasSameMetadataDefinitionAs(source);
    }

    /// <summary>Each (outermost type, source member) whose compiled body asks <c>RecordTableRules.RefuseWriterOnlyTableDelete</c>.</summary>
    internal static IReadOnlySet<(Type Type, string Method)> AskersOf(IEnumerable<Type> types) =>
        WriterOnlyTablesUpdateGuardTests.AskersOf(types, nameof(RecordTableRules.RefuseWriterOnlyTableDelete));
}

// Compiled control fixtures for WriterOnlyTablesDeleteGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: deletes through the generic entity service without the check.</summary>
internal static class WriterOnlyDeleteControlNoCheck
{
    internal static Task Delete(IGenericEntityService entities, string table, Guid id) => entities.DeleteAsync(table, id);
}

/// <summary>MUST FIRE: deletes through the Web API client without the check.</summary>
internal static class WriterOnlyDeleteControlWebApiNoCheck
{
    internal static Task Delete(DataverseWebApiClient client, string entitySet, Guid id) => client.DeleteAsync(entitySet, id);
}

/// <summary>
/// A decorator of the caller's client (as <c>CallerMetadataReadMemo</c>). MUST NOT FIRE: its own <c>DeleteAsync</c> forwards to the
/// client it wraps. MUST FIRE: <see cref="Purge"/>, another member, deletes through that client.
/// </summary>
internal sealed class WriterOnlyDeleteControlForwardingDecorator(IDataverseUserClient inner) : IDataverseUserClient
{
    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
        inner.GetAsync(relativePath, cancellationToken);

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
        inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation,
        CancellationToken cancellationToken) =>
        inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        inner.PatchAsync(relativePath, jsonBody, cancellationToken);

    public async Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        await inner.DeleteAsync(relativePath, cancellationToken);

    internal Task<DataverseUserResponse> Purge(string relativePath) => inner.DeleteAsync(relativePath, CancellationToken.None);
}

/// <summary>
/// A decorator whose interface <c>DeleteAsync</c> forwards (MUST NOT FIRE) but which also has an extra <c>DeleteAsync(Guid)</c> overload
/// that deletes a fixed writer-only table (MUST FIRE: the overload is not the interface method, whatever its name).
/// </summary>
internal sealed class WriterOnlyDeleteControlOverloadDecorator(IDataverseUserClient inner) : IDataverseUserClient
{
    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
        inner.GetAsync(relativePath, cancellationToken);

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
        inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation,
        CancellationToken cancellationToken) =>
        inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        inner.PatchAsync(relativePath, jsonBody, cancellationToken);

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        inner.DeleteAsync(relativePath, cancellationToken);

    internal Task<DataverseUserResponse> DeleteAsync(Guid signalId) =>
        inner.DeleteAsync($"sprk_signals({signalId:D})", CancellationToken.None);
}

/// <summary>
/// A subclass of the Web API client. Its override of the base virtual <c>DeleteAsync</c> forwards (MUST NOT FIRE); its override of
/// ANOTHER virtual, <c>UpdateAsync</c>, calls <c>base.DeleteAsync</c> on a fixed table (MUST FIRE).
/// </summary>
internal sealed class WriterOnlyDeleteControlWebApiSubclass(
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    Microsoft.Extensions.Logging.ILogger<DataverseWebApiClient> logger) : DataverseWebApiClient(configuration, logger)
{
    public override Task DeleteAsync(string entitySetName, Guid id, CancellationToken cancellationToken = default) =>
        base.DeleteAsync(entitySetName, id, cancellationToken);

    public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default) =>
        base.DeleteAsync("sprk_signals", id, cancellationToken);
}

/// <summary>MUST NOT FIRE: asks the check first.</summary>
internal static class WriterOnlyDeleteControlWithCheck
{
    internal static async Task Delete(IDataverseUserClient user, string table, string path)
    {
        if (RecordTableRules.RefuseWriterOnlyTableDelete(table) is not null)
            return;
        await user.DeleteAsync(path, CancellationToken.None);
    }
}
