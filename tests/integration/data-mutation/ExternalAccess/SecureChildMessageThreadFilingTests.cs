using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// #2143 round 6: the backstop applies the ONE rule for a message's thread parents (<c>RecordThreadFiling</c>) whether or not its
/// record thread is a row of the pass. Driven over the REAL <see cref="SecureChildReconciler"/>, the REAL ownership resolver and
/// one in-memory Dataverse (<see cref="SecureChildShareWorld"/>): a thread regarding a secure root OUTSIDE the pass is read and
/// still files the message under that root; a thread that cannot be read leaves the message undecided (failed, not written,
/// left for the next pass); a thread the pass already loaded is never read again.
/// </summary>
public class SecureChildMessageThreadFilingTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();

    /// <summary>A matter root's pass reads its Assign cascade (document locations) over the Web API; this one has none.</summary>
    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    /// <summary>
    /// Matter S1 mid-unsecure; secure matter S2 with a record thread; an isolated message regarding S1 that sits in S2's thread
    /// (<paramref name="threadId"/>, seeded only when <paramref name="seedThread"/>).
    /// </summary>
    private (Guid S1, Guid Message) MessageOnAnUnsecuredMatterInASecureMattersThread(Guid threadId, bool seedThread = true)
    {
        var (s1, s2, message) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", s1)
            .SecureRoot("sprk_matter", s2)
            .SecureChild("sprk_communication", message,
                ("sprk_regardingmatter", "sprk_matter", s1), ("sprk_communicationthread", "sprk_communicationthread", threadId));
        if (seedThread)
        {
            _world.Add("sprk_communicationthread", threadId,
                ("sprk_threadtype", new OptionSetValue(100000000)), // #2143 r7d: a record thread
                ("owningteam", new EntityReference("team", SecureTeam)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", s2)));
        }

        return (s1, message);
    }

    [Fact(DisplayName = "#2143 r6 must-fire: a message whose record thread regards a secure root OUTSIDE the pass is decided Secure")]
    public async Task AMessageInTheThreadOfASecureRootOutsideThePass_StaysSecure_WhenItsOtherMatterIsUnsecured()
    {
        // Before round 6 the thread was not a row of S1's pass, so it added nothing: the message was released to S1's ordinary
        // team although it sits in a secure record's thread.
        var thread = Guid.NewGuid();
        var (s1, message) = MessageOnAnUnsecuredMatterInASecureMattersThread(thread);

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_communication", message).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "it is still filed under S2 through its record thread");
        _world.OwnerWrites.Should().NotContain(w => w.Id == message, "it stays where it is");
    }

    [Fact(DisplayName = "#2143 r6 must-fire: a message whose record thread cannot be read is left unwritten and reported failed")]
    public async Task AMessageWhoseRecordThreadCannotBeRead_IsLeftUnwritten_AndReportedFailed()
    {
        var thread = Guid.NewGuid();
        var (s1, message) = MessageOnAnUnsecuredMatterInASecureMattersThread(thread);
        _world.FailingRowReadsOf("sprk_communicationthread", thread)
            .FailingIdListReadsOf("sprk_communicationthread", thread); // #2143 r7e: threads are read by id list

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().NotBe(SecureChildReconcileStatus.Completed, "one row was left undecided");
        report.Changes.Should().ContainSingle(c => c.Id == message)
            .Which.Should().Match<SecureChildRowChange>(c =>
                c.Outcome == SecureChildRowOutcome.Failed && c.Detail == "its record thread could not be read");
        _world.OwnerWrites.Should().NotContain(w => w.Id == message, "never decided as if the thread filed nothing");
        _world.OwnerOf("sprk_communication", message).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    [Fact(DisplayName = "#2143 r6 must-not-fire: a record thread that is a row of the pass is not read again")]
    public async Task AMessageInARecordThreadOfThePass_UsesThePreloadedThread_AndReadsItNoMore()
    {
        // The thread regards the root, so the pass loads it (already isolated, so the pass writes and reads back nothing of it).
        // A by-id read of it fails here, and the message is still decided from the preloaded row.
        var (matter, thread, message) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", matter)
            .SecureChild("sprk_communicationthread", thread, ("sprk_regardingmatter", "sprk_matter", matter))
            .UserOwnedChild("sprk_communication", message, ("sprk_communicationthread", "sprk_communicationthread", thread))
            .FailingRowReadsOf("sprk_communicationthread", thread)
            .FailingIdListReadsOf("sprk_communicationthread", thread); // #2143 r7e: threads are read by id list
        _world.Set("sprk_communicationthread", thread, "sprk_threadtype", new OptionSetValue(100000000)); // #2143 r7d

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_communication", message).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "filed under the matter through its preloaded record thread");
    }

    // ---- #2143 round 7 (R7e): the threads outside the pass are read in batches ----

    private const string ThreadTable = "sprk_communicationthread";

    /// <summary>The id-list reads of record threads this world answered (one per page).</summary>
    private IReadOnlyList<Microsoft.Xrm.Sdk.Query.ConditionExpression> ThreadIdListReads() => _world.Queries
        .Where(q => q.EntityName == ThreadTable)
        .SelectMany(q => q.Criteria.Conditions)
        .Where(c => c.AttributeName == ThreadTable + "id" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.In)
        .ToList();

    /// <summary>The by-id reads of ONE record thread this world answered.</summary>
    private int ThreadRowReads() => _world.Queries
        .Where(q => q.EntityName == ThreadTable)
        .SelectMany(q => q.Criteria.Conditions)
        .Count(c => c.AttributeName == ThreadTable + "id" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal);

    /// <summary>
    /// Matter S1 mid-unsecure and secure matter S2; <paramref name="count"/> isolated messages regarding S1, each in a record
    /// thread of its own regarding S2 (none of them a row of S1's pass).
    /// </summary>
    private (Guid S1, Guid[] Threads, Guid[] Messages) MessagesInThreadsOfASecureRootOutsideThePass(int count)
    {
        var (s1, s2) = (Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", s1).SecureRoot("sprk_matter", s2);
        var threads = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var messages = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < count; i++)
        {
            _world.Add(ThreadTable, threads[i],
                ("sprk_threadtype", new OptionSetValue(100000000)),
                ("owningteam", new EntityReference("team", SecureTeam)),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", s2)));
            _world.SecureChild("sprk_communication", messages[i],
                ("sprk_regardingmatter", "sprk_matter", s1), ("sprk_communicationthread", ThreadTable, threads[i]));
        }

        return (s1, threads, messages);
    }

    [Fact(DisplayName = "#2143 r7e: N record threads outside the pass are read in ceil(N/chunk) id-list queries")]
    public async Task TheRecordThreadsOutsideThePass_AreReadInBatches_OneQueryPerChunk()
    {
        // Before R7e every thread was read by its own query: N reads for N threads.
        var chunk = SecureChildShareSynchronizer.DescendantConditionsPerQuery;
        var count = chunk + 1;
        var (s1, threads, messages) = MessagesInThreadsOfASecureRootOutsideThePass(count);

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        var reads = ThreadIdListReads();
        reads.Should().HaveCount((count + chunk - 1) / chunk, "one id-list query per chunk of threads");
        reads.Should().OnlyContain(c => c.Values.Count <= chunk);
        reads.SelectMany(c => c.Values).Should().BeEquivalentTo(threads.Cast<object>(), "every thread once");
        ThreadRowReads().Should().Be(0, "no thread is read on its own");
        _world.OwnerWrites.Should().NotContain(w => messages.Contains(w.Id), "each is still filed under S2 through its thread");
    }

    [Fact(DisplayName = "#2143 r8: the batched record-thread read is committed (NoLock = false), as the re-file path reads a thread")]
    public async Task TheBatchedRecordThreadRead_IsCommitted_NeverADirtyRead()
    {
        // A dirty read could see a thread re-filed by a transaction that then rolls back, and decide its messages from parents
        // the thread never committed. The single read it replaced (RecordThreadFiling.ReadThreadAsync) is committed too.
        var (s1, _, _) = MessagesInThreadsOfASecureRootOutsideThePass(2);

        await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        var batched = _world.Queries
            .Where(q => q.EntityName == ThreadTable && q.Criteria.Conditions.Any(c =>
                c.AttributeName == ThreadTable + "id" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.In))
            .ToList();
        batched.Should().NotBeEmpty();
        batched.Should().OnlyContain(q => !q.NoLock, "a committed read");
    }

    [Fact(DisplayName = "#2143 r7e must-fire: a thread chunk that faults leaves every message of that chunk failed and unwritten")]
    public async Task AFaultedThreadChunk_LeavesEveryMessageInItFailed_AndUnwritten()
    {
        // Only ONE thread's id makes the id-list read fault; the read is the chunk's, so no thread of it is known and every
        // message in one of its threads is left undecided. Before R7e only the faulted thread's message was failed.
        var (s1, threads, messages) = MessagesInThreadsOfASecureRootOutsideThePass(2);
        _world.FailingIdListReadsOf(ThreadTable, threads[0]);

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().NotBe(SecureChildReconcileStatus.Completed, "rows were left undecided");
        foreach (var message in messages)
        {
            report.Changes.Should().ContainSingle(c => c.Id == message)
                .Which.Should().Match<SecureChildRowChange>(c =>
                    c.Outcome == SecureChildRowOutcome.Failed && c.Detail == "its record thread could not be read");
            _world.OwnerOf("sprk_communication", message).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        }

        _world.OwnerWrites.Should().NotContain(w => messages.Contains(w.Id), "never decided as if the threads filed nothing");
    }

    [Fact(DisplayName = "#2143 r7e must-fire: a thread the batch does not return leaves its message failed; the others are decided")]
    public async Task AThreadTheBatchDoesNotReturn_LeavesItsMessageFailed_AndTheOthersAreDecided()
    {
        // A message whose thread is gone (deleted, or not readable) is not decided as if its thread filed nothing; a message in
        // a thread the same chunk returned is decided as usual.
        var (s1, threads, messages) = MessagesInThreadsOfASecureRootOutsideThePass(2);
        _world.Delete(ThreadTable, threads[0]);

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", s1, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Changes.Should().ContainSingle(c => c.Id == messages[0])
            .Which.Should().Match<SecureChildRowChange>(c =>
                c.Outcome == SecureChildRowOutcome.Failed && c.Detail == "its record thread could not be read");
        report.Changes.Should().NotContain(c => c.Id == messages[1] && c.Outcome == SecureChildRowOutcome.Failed);
        _world.OwnerWrites.Should().NotContain(w => messages.Contains(w.Id));
    }
}
