using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message is never filed; UAC-r2: fail closed wherever a
/// filing is read). The secure-child share mirror (<see cref="SecureChildShareSynchronizer"/>) selects children by owner and places each
/// under a root by the row's own filing. A private <c>sprk_communication</c> (a reference column holds a record) is placed under no
/// root, so none of a root's shares is mirrored onto it; an ordinary Secure-team-owned message is mirrored as before. The reference
/// columns are read only where metadata has them; a metadata read that faults faults the run.
/// </summary>
/// <remarks>The REAL synchronizer over <see cref="SecureChildShareWorld"/> and <see cref="FakeRecordShareTable"/>, as
/// <see cref="SecureChildShareMirrorTests"/>. Placement: <c>tests/integration/data-mutation/**</c>.</remarks>
public class SecureChildShareSynchronizerPrivateMessageTests
{
    private const int Collaborate = 262167;
    private const string Communication = "sprk_communication";

    private static readonly Guid Project = Guid.Parse("a1560000-0000-4000-8000-000000000001");
    private static readonly Guid Matter = Guid.Parse("a1560000-0000-4000-8000-000000000002");
    private static readonly Guid Ordinary = Guid.Parse("c0560000-0000-4000-8000-000000000001");
    private static readonly Guid Private = Guid.Parse("c0560000-0000-4000-8000-000000000002");
    private static readonly Guid Attachment = Guid.Parse("a7560000-0000-4000-8000-000000000001");
    private static readonly Guid Sharee = Guid.Parse("11560000-0000-4000-8000-00000000000a");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly CapturingLogger _log = new();

    public SecureChildShareSynchronizerPrivateMessageTests()
    {
        // A secure project with two Secure-team-owned messages filed under it: an ordinary one, and one that names a matter it is
        // about (private) — filed under the project outside the BFF — and an attachment of the private one.
        _world.SecureRoot("sprk_project", Project)
            .SecureRoot("sprk_matter", Matter)
            .SecureChild(Communication, Ordinary, ("sprk_regardingproject", "sprk_project", Project))
            .SecureChild(Communication, Private, ("sprk_regardingproject", "sprk_project", Project))
            .SecureChild("sprk_communicationattachment", Attachment, (Communication, Communication, Private));
        _world.Set(Communication, Private, PrivateContextColumns.Matter, new EntityReference("sprk_matter", Matter));
        _world.CommunicationColumnMetadata = () => Columns(PrivateContextColumns.All);
        _shares.Seed("sprk_project", Project, DataversePrincipalRef.User(Sharee), Collaborate);
    }

    private SecureChildShareSynchronizer Synchronizer() => _world.Synchronizer(_shares, logger: _log);

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(IEnumerable<string> names) =>
        names.Append("sprk_communicationid").Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);

    private int Mask(string table, Guid id) => _shares.MaskOf(table, id, DataversePrincipalRef.User(Sharee)) ?? 0;

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under a secure root gets no mirrored share; an ordinary message does")]
    public async Task ReconcileAll_APrivateMessageUnderASecureRoot_IsNotMirrored_AnOrdinaryOneIs()
    {
        var result = await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        Mask(Communication, Ordinary).Should().Be(Collaborate, "an ordinary Secure-owned message is mirrored as before");
        Mask(Communication, Private).Should().Be(0, "a private message is filed under nothing");
        _shares.WriteLog.Should().NotContain(w => w.RecordId == Private);
    }

    [Fact(DisplayName = "fm 056: a row that sits below a private message is not placed under the root through it")]
    public async Task ReconcileAll_ARowBelowAPrivateMessage_IsNotPlacedUnderTheRootThroughIt()
    {
        await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        Mask("sprk_communicationattachment", Attachment).Should().Be(0);
    }

    [Fact(DisplayName = "fm 056: the scoped run of the root skips a private message below it")]
    public async Task SyncRoot_APrivateMessageUnderTheRoot_IsNotMirrored()
    {
        var result = await Synchronizer().SyncRootAsync("sprk_project", Project, CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        Mask(Communication, Ordinary).Should().Be(Collaborate);
        Mask(Communication, Private).Should().Be(0);
    }

    [Fact(DisplayName = "fm 056: the one-row sync of a private message mirrors nothing onto it")]
    public async Task SyncChild_APrivateMessage_IsNotMirrored()
    {
        await Synchronizer().SyncChildAsync(Communication, Private, CancellationToken.None);

        Mask(Communication, Private).Should().Be(0);
        _shares.WriteLog.Should().NotContain(w => w.RecordId == Private);
    }

    [Fact(DisplayName = "fm 056: the warning names the row and no value")]
    public async Task ThePrivateMessageWarning_NamesTheRowId_NeverAValue()
    {
        await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        _log.Warnings.Should().Contain(m => m.Contains(Private.ToString(), StringComparison.OrdinalIgnoreCase));
        _log.Warnings.Should().NotContain(m => m.Contains(Matter.ToString(), StringComparison.OrdinalIgnoreCase),
            "the record the message is about is never logged");
    }

    [Fact(DisplayName = "fm 056 must-not-fire: with the reference columns absent from metadata the run reads and mirrors as before")]
    public async Task WithTheColumnsAbsentFromMetadata_BehaviourIsAsBefore()
    {
        _world.CommunicationColumnMetadata = () => Columns(Array.Empty<string>());

        var result = await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        Mask(Communication, Ordinary).Should().Be(Collaborate);
        Mask(Communication, Private).Should().Be(Collaborate, "no reference column exists to read, so the row is filed as it states");
        _world.Queries.Where(q => q.EntityName == Communication)
            .SelectMany(q => q.ColumnSet.Columns)
            .Should().NotContain(PrivateContextColumns.All, "a column absent from metadata is never selected");
    }

    [Fact(DisplayName = "fm 056: only the columns metadata has are selected")]
    public async Task OnlyTheReferenceColumnsMetadataHas_AreRead()
    {
        _world.CommunicationColumnMetadata = () => Columns(new[] { PrivateContextColumns.Matter });

        await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        var read = _world.Queries.Where(q => q.EntityName == Communication).SelectMany(q => q.ColumnSet.Columns).ToList();
        read.Should().Contain(PrivateContextColumns.Matter);
        read.Should().NotContain(PrivateContextColumns.Project).And.NotContain(PrivateContextColumns.WorkAssignment);
        Mask(Communication, Private).Should().Be(0);
    }

    [Fact(DisplayName = "fm 056: a metadata read that faults faults the run, as any other read fault does")]
    public async Task APresenceReadFault_FailsTheRun()
    {
        _world.CommunicationColumnMetadata = () => throw new InvalidOperationException("metadata unavailable");

        var result = await Synchronizer().ReconcileAllAsync(CancellationToken.None);

        result.Status.Should().Be(SecureChildShareSyncStatus.Failed, "a fault fails the run, as a fault of any other secure-child read does");
        _shares.WriteLog.Should().BeEmpty("nothing is mirrored on a run that could not read the filing");
    }

    private sealed class CapturingLogger : ILogger<SecureChildShareSynchronizer>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
