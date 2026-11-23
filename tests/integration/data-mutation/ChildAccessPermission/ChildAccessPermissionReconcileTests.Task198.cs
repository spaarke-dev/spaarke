using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 198 Part D: when the record a message is filed under BECOMES Restricted, the job that keeps a
/// communication's Access Permission equal to what it inherits (this reconciler, inside <see cref="SecureChildReconciliationJob"/>)
/// also removes the shares of users flagged external on the message and its content rows — the same bar a re-file runs
/// (<see cref="RestrictedFilingShareBar"/>). Through the job exactly as the scheduler runs it.
/// </summary>
public partial class ChildAccessPermissionReconcileTests
{
    private static readonly Guid ExternalUser = Guid.Parse("e0000198-0000-4000-8000-0000000000e2");
    private static readonly Guid InternalUser = Guid.Parse("e0000198-0000-4000-8000-0000000000a2");

    /// <summary>A Standard matter with a message filed under it and one attachment, both shared with an external and an internal user.</summary>
    private static (Guid Matter, Guid Message, Guid Attachment) SharedMessageUnderAStandardMatter(Harness job)
    {
        job.LinkColumns.Users[ExternalUser] = true;
        job.LinkColumns.Users[InternalUser] = false;
        var matter = job.Root("sprk_matter", Standard);
        var message = job.Child("sprk_communication", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        var attachment = Guid.NewGuid();
        job.World.OrdinaryChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", message));
        foreach (var (table, id) in new[] { ("sprk_communication", message), ("sprk_communicationattachment", attachment) })
        {
            job.Shares.Seed(table, id, DataversePrincipalRef.User(ExternalUser), 1);
            job.Shares.Seed(table, id, DataversePrincipalRef.User(InternalUser), 1);
        }

        return (matter, message, attachment);
    }

    private static bool Holds(Harness job, string table, Guid id, Guid user) =>
        job.Shares.MaskOf(table, id, DataversePrincipalRef.User(user)) is > 0;

    [Fact(DisplayName = "198 D must-fire (job): a matter turning Restricted takes the external user's shares off its messages and their content rows")]
    public async Task Run_WhenAMatterTurnsRestricted_TheExternalUsersSharesOnItsMessagesAreRemoved()
    {
        var job = new Harness();
        var (matter, message, attachment) = SharedMessageUnderAStandardMatter(job);
        await job.RunAsync(); // settles under Standard: nothing removed
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeTrue("the matter is not Restricted yet");

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().Contain(("sprk_communication", message, Restricted));
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeFalse("the message now inherits Restricted");
        Holds(job, "sprk_communicationattachment", attachment, ExternalUser).Should().BeFalse("content rows included");
        Holds(job, "sprk_communication", message, InternalUser).Should().BeTrue("an internal user keeps it");
        Holds(job, "sprk_communicationattachment", attachment, InternalUser).Should().BeTrue();
        job.LastResult!.Success.Should().BeTrue();
    }

    [Fact(DisplayName = "198 D must-not-fire (job): a matter that stays Standard leaves every share on its messages")]
    public async Task Run_WhenAMatterStaysStandard_EveryShareOnItsMessagesIsKept()
    {
        var job = new Harness();
        var (matter, message, attachment) = SharedMessageUnderAStandardMatter(job);
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Limited));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().Contain(("sprk_communication", message, Limited));
        foreach (var user in new[] { ExternalUser, InternalUser })
        {
            Holds(job, "sprk_communication", message, user).Should().BeTrue("Limited is not Restricted");
            Holds(job, "sprk_communicationattachment", attachment, user).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 D fail closed (job): a user that cannot be read is left, and the message is carried to the next run")]
    public async Task Run_WhenAShareHoldersFlagCannotBeRead_TheShareIsLeft_AndTheMessageIsCarried()
    {
        var job = new Harness();
        var (matter, message, _) = SharedMessageUnderAStandardMatter(job);
        job.LinkColumns.Users.Remove(ExternalUser); // the read returns no row for them
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        Holds(job, "sprk_communication", message, ExternalUser).Should().BeTrue("unknown never removes");

        // The user becomes readable: the carried message is visited again and the bar completes, without a new change.
        job.LinkColumns.Users[ExternalUser] = true;
        await job.RunAsync();
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeFalse("the carried row is barred on the next run");
    }

    // ── Fix round 1, R3: the bar runs on EVERY Restricted communication the job examines ──────────────────────────────────

    [Fact(DisplayName = "198 R3 must-fire (finding 9): a message filed under nothing whose OWN value is Restricted loses its external participant's share in the sweep")]
    public async Task Run_AParentlessMessageWhoseOwnValueIsRestricted_LosesTheExternalShare_InTheSweep()
    {
        var job = new Harness();
        job.LinkColumns.Users[ExternalUser] = true;
        job.LinkColumns.Users[InternalUser] = false;
        var message = job.Child("sprk_communication", Restricted); // no filing: its own value is the user's (round 81)
        foreach (var user in new[] { ExternalUser, InternalUser })
            job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(user), 1);

        var report = (await job.RunAsync()).GetProperty("accessPermission");

        report.GetProperty("rowsChanged").GetInt32().Should().Be(0, "nothing marks it changed: only the sweep finds it");
        report.GetProperty("sweep").GetProperty("rows").GetInt32().Should().BeGreaterThan(0);
        report.GetProperty("parentless").GetInt32().Should().Be(1);
        job.World.AccessPermissionWrites.Should().BeEmpty("a parentless row's own value is never written");
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeFalse("its own value is Restricted");
        Holds(job, "sprk_communication", message, InternalUser).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R3 must-not-fire: a message filed under nothing whose own value is Standard keeps its external participant")]
    public async Task Run_AParentlessStandardMessage_KeepsTheExternalShare()
    {
        var job = new Harness();
        job.LinkColumns.Users[ExternalUser] = true;
        var message = job.Child("sprk_communication", Standard);
        job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(ExternalUser), 1);

        var report = (await job.RunAsync()).GetProperty("accessPermission");

        report.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(0);
        Holds(job, "sprk_communication", message, ExternalUser).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R3 bounds: many Restricted messages — each run bars at most the write cap, logs the capped run, and carries the rest until all are barred")]
    public async Task Run_WithMoreRestrictedMessagesThanTheBudget_IsCapped_Carried_AndConverges()
    {
        var job = new Harness(maxWritesPerRun: 2);
        job.LinkColumns.Users[ExternalUser] = true;
        var matter = job.Root("sprk_matter", Restricted);
        var messages = Enumerable.Range(0, 5)
            .Select(_ => job.Child("sprk_communication", Restricted, ("sprk_regardingmatter", "sprk_matter", matter)))
            .ToArray();
        foreach (var message in messages)
            job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(ExternalUser), 1);

        var first = (await job.RunAsync()).GetProperty("accessPermission");
        var heldAfterFirst = messages.Count(m => Holds(job, "sprk_communication", m, ExternalUser));
        var second = (await job.RunAsync()).GetProperty("accessPermission");
        var third = (await job.RunAsync()).GetProperty("accessPermission");

        var bar = first.GetProperty("restrictedBar");
        bar.GetProperty("ran").GetInt32().Should().Be(2, "at most the write cap per run");
        bar.GetProperty("capped").GetInt32().Should().Be(3);
        bar.GetProperty("capReached").GetBoolean().Should().BeTrue();
        bar.GetProperty("left").GetInt32().Should().Be(0, "sweep rows past the cap are carried, not dropped");
        first.GetProperty("carriedRows").GetInt32().Should().Be(3);
        first.GetProperty("problems").EnumerateArray().Select(p => p.GetString())
            .Should().Contain(p => p!.Contains("reached its cap of 2", StringComparison.Ordinal), "a capped run says so");
        heldAfterFirst.Should().Be(3);

        second.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(2, "the carried rows, still within the cap");
        second.GetProperty("restrictedBar").GetProperty("capped").GetInt32().Should().Be(1);
        third.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(1);
        third.GetProperty("restrictedBar").GetProperty("capReached").GetBoolean().Should().BeFalse();
        third.GetProperty("carriedRows").GetInt32().Should().Be(0);
        messages.Should().OnlyContain(m => !Holds(job, "sprk_communication", m, ExternalUser), "every one is barred in the end");
        job.World.AccessPermissionWrites.Should().BeEmpty("every value was already right; the bar is bounded on its own");
    }

    // ── Pass 2, F2: the carry cap. Every path into the carry respects it; a row neither handled nor carried is counted, and the
    // sweep cursor stays before it (or the sweep starts again), so no row is lost ────────────────────────────────────────────

    private static async Task<List<System.Text.Json.JsonElement>> RunsAsync(Harness job, int count)
    {
        var reports = new List<System.Text.Json.JsonElement>();
        for (var i = 0; i < count; i++)
            reports.Add((await job.RunAsync()).GetProperty("accessPermission"));
        return reports;
    }

    [Fact(DisplayName = "198 pass 2 F2 must-fire: sweep rows the capped bar cannot carry are counted left, the cursor stays before them, and every one is barred in the end")]
    public async Task F2_BarRowsPastTheCarryCap_AreCounted_TheSweepCursorHolds_AndNoneIsLost()
    {
        var job = new Harness(maxWritesPerRun: 1, maxCarriedRows: 2);
        job.LinkColumns.Users[ExternalUser] = true;
        var messages = Enumerable.Range(0, 5).Select(_ => job.Child("sprk_communication", Restricted)).ToArray(); // parentless: only the sweep finds them
        foreach (var message in messages)
            job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(ExternalUser), 1);

        var reports = await RunsAsync(job, 6);

        var first = reports[0];
        first.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(1);
        first.GetProperty("restrictedBar").GetProperty("capped").GetInt32().Should().Be(4);
        first.GetProperty("restrictedBar").GetProperty("left").GetInt32().Should().Be(2, "two capped rows did not fit the carry: counted");
        first.GetProperty("carriedRows").GetInt32().Should().Be(2);
        first.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeFalse(
            "the sweep cursor does not pass a row that was neither barred nor carried");
        reports.Should().OnlyContain(r => r.GetProperty("carriedRows").GetInt32() <= 2, "the carry never exceeds its cap");
        messages.Should().OnlyContain(m => !Holds(job, "sprk_communication", m, ExternalUser), "no row is lost: every one is barred");
        reports[^1].GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue("the sweep finishes once nothing is left");
    }

    [Fact(DisplayName = "198 pass 2 F2 must-not-fire: capped rows that all fit the carry are carried, nothing is left, and the sweep completes in one run")]
    public async Task F2_CappedRowsThatFitTheCarry_LeaveNothing_AndTheSweepCompletes()
    {
        var job = new Harness(maxWritesPerRun: 1, maxCarriedRows: 4);
        job.LinkColumns.Users[ExternalUser] = true;
        var messages = Enumerable.Range(0, 5).Select(_ => job.Child("sprk_communication", Restricted)).ToArray();
        foreach (var message in messages)
            job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(ExternalUser), 1);

        var first = (await job.RunAsync()).GetProperty("accessPermission");

        first.GetProperty("restrictedBar").GetProperty("capped").GetInt32().Should().Be(4);
        first.GetProperty("restrictedBar").GetProperty("left").GetInt32().Should().Be(0);
        first.GetProperty("carriedRows").GetInt32().Should().Be(4, "exactly at the cap");
        first.GetProperty("carryOverflow").GetInt32().Should().Be(0);
        first.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue("nothing was lost, so the cursor is not held");
    }

    [Fact(DisplayName = "198 pass 2 F2 must-fire: undecidable sweep rows past the carry cap are not dropped: the cursor stays before them, and they are written once their parent reads")]
    public async Task F2_UndecidableRowsPastTheCarryCap_AreNotDropped_AndAreWrittenLater()
    {
        var job = new Harness(maxCarriedRows: 2);
        var matter = job.Root("sprk_matter", Restricted);
        var todos = Enumerable.Range(0, 3)
            .Select(_ => job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter)))
            .ToArray(); // never modified: only the sweep finds them
        job.World.FailingIdListReadsOf("sprk_matter", matter);

        var first = (await job.RunAsync()).GetProperty("accessPermission");
        job.World.ClearQueryFaults();
        var later = await RunsAsync(job, 3);

        first.GetProperty("undetermined").GetInt32().Should().Be(3);
        first.GetProperty("carriedRows").GetInt32().Should().Be(2, "the carry never exceeds its cap");
        job.World.AccessPermissionWrites.Select(w => w.Id).Should().BeEquivalentTo(todos, "no row is lost");
        first.GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeFalse(
            "the sweep cursor does not pass a row that was neither decided nor carried");
        later[^1].GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();
        first.GetProperty("carryOverflow").GetInt32().Should().Be(1, "the row past the cap is counted, never silently dropped");
    }

    // ── Pass 2, item 4: projection pins. The world honours a query's column set, so a column the reconciler stops asking for
    // reads as null. These pin the columns the reconciler's listing reads ask for (ColumnSetAsync): the document's value and
    // own-narrowing record, and the communication's value, which R3's parentless own-value read depends on ─────────────────

    /// <summary>The reconciler alone (no other pass) over a world: every query in <see cref="SecureChildShareWorld.Queries"/> is its own.</summary>
    private static async Task<(SecureChildShareWorld World, List<Guid> Barred)> ReconcilerRunAsync(
        Action<SecureChildShareWorld>? arrange = null, bool writes = false, params string[] tablesWithoutAccessPermission)
    {
        var world = SecureChildShareWorld.Standard();
        arrange?.Invoke(world);
        var probe = SecureChildShareWorld.CoreAncestorsOver(() => world, tablesWithoutAccessPermission);
        var barred = new List<Guid>();
        var reconciler = new ChildAccessPermissionReconciler(
            SecureChildShareWorld.EntitiesOver(() => world).Object, (table, token) => probe.ProbeColumnsAsync(table, token),
            NullLogger.Instance, _ => Task.FromResult(true),
            (communication, _) => { barred.Add(communication); return Task.FromResult(true); });
        world.Queries.Clear();
        await reconciler.RunAsync(Start.AddHours(-1),
            [("sprk_document", Guid.NewGuid()), ("sprk_communication", Guid.NewGuid())], // the by-id read of carried rows
            ChildAccessPermissionReconciler.SweepFromStart, 100, writes, maxWrites: 10, CancellationToken.None);
        return (world, barred);
    }

    /// <summary>The reconciler's three listing reads of a table: changed since the window, carried rows by id, the sweep.</summary>
    private static List<QueryExpression> ListingReadsOf(SecureChildShareWorld world, string table)
    {
        var reads = world.Queries.Where(q => q.EntityName == table).ToList();
        var changed = reads.Where(q => q.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon")).ToList();
        var byId = reads.Where(q => q.Criteria.Conditions.Any(c => c.AttributeName == table + "id" && c.Operator == ConditionOperator.In)).ToList();
        var sweep = reads.Where(q => q.Orders.Any(o => o.AttributeName == table + "id")).ToList();
        changed.Should().NotBeEmpty($"the reconciler lists {table} rows changed since the window");
        byId.Should().NotBeEmpty($"the reconciler reads carried {table} rows by id");
        sweep.Should().NotBeEmpty($"the reconciler sweeps {table}");
        return [.. changed, .. byId, .. sweep];
    }

    private static bool Asks(QueryExpression query, string column) =>
        query.ColumnSet.AllColumns || query.ColumnSet.Columns.Contains(column, StringComparer.OrdinalIgnoreCase);

    [Fact(DisplayName = "198 pass 2 pin (must-fire): every listing read of sprk_document asks for its value and its own-narrowing record")]
    public async Task Pin_TheDocumentListingReads_AskForTheValueAndTheRecord()
    {
        var (world, _) = await ReconcilerRunAsync();

        ListingReadsOf(world, "sprk_document").Should().OnlyContain(
            q => Asks(q, InheritedAccessPermission.Column) && Asks(q, AccessInheritance.Column),
            "DecideDocument reads both from the listed row; a column not asked for reads as null");
    }

    [Fact(DisplayName = "198 pass 2 pin (must-not-fire): documents without the record column are listed with the value only")]
    public async Task Pin_DocumentsWithoutTheRecordColumn_AreListedWithTheValueOnly()
    {
        var (world, _) = await ReconcilerRunAsync(w => w.DocumentsLackAccessRecordColumn = true);

        ListingReadsOf(world, "sprk_document").Should().OnlyContain(
            q => Asks(q, InheritedAccessPermission.Column) && !Asks(q, AccessInheritance.Column));
    }

    [Fact(DisplayName = "198 pass 2 pin (must-fire): every listing read of sprk_communication asks for its value, and a parentless Restricted message is barred")]
    public async Task Pin_TheCommunicationListingReads_AskForTheValue_AndTheParentlessOwnValueIsRead()
    {
        var message = Guid.NewGuid();
        var (world, barred) = await ReconcilerRunAsync(
            w => w.OrdinaryChild("sprk_communication", message).Set("sprk_communication", message, Column, new OptionSetValue(Restricted)),
            writes: true);

        ListingReadsOf(world, "sprk_communication").Should().OnlyContain(q => Asks(q, InheritedAccessPermission.Column),
            "R3 reads a parentless message's own value from the listed row");
        barred.Should().Equal([message], "its own value, read from the listing, is Restricted");
    }

    [Fact(DisplayName = "198 pass 2 pin (must-not-fire): a table without the column is listed without it, and a parentless Standard message is not barred")]
    public async Task Pin_ACommunicationTableWithoutTheColumn_IsListedWithoutIt_AndAStandardMessageIsNotBarred()
    {
        var standard = Guid.NewGuid();
        var (_, barredStandard) = await ReconcilerRunAsync(
            w => w.OrdinaryChild("sprk_communication", standard).Set("sprk_communication", standard, Column, new OptionSetValue(Standard)),
            writes: true);
        barredStandard.Should().BeEmpty("its own value is Standard");

        var restricted = Guid.NewGuid();
        var (world, barred) = await ReconcilerRunAsync(
            w => w.OrdinaryChild("sprk_communication", restricted).Set("sprk_communication", restricted, Column, new OptionSetValue(Restricted)),
            writes: true, "sprk_communication");
        world.Queries.Where(q => q.EntityName == "sprk_communication")
            .Should().OnlyContain(q => !Asks(q, InheritedAccessPermission.Column), "the column does not exist here");
        barred.Should().BeEmpty("a table without the column is not reconciled");
    }

    [Fact(DisplayName = "198 pass 2 F2 must-fire: rows the capped bar leaves (listed only under a change) are found again by a restarted sweep, without holding the watermark")]
    public async Task F2_RowsTheCappedBarLeaves_AreFoundAgainByARestartedSweep()
    {
        var job = new Harness(maxWritesPerRun: 1);
        job.LinkColumns.Users[ExternalUser] = true;
        var matter = job.Root("sprk_matter", Restricted);
        var messages = Enumerable.Range(0, 3)
            .Select(_ => job.Child("sprk_communication", Restricted, ("sprk_regardingmatter", "sprk_matter", matter)))
            .ToArray();
        var settle = await RunsAsync(job, 4); // the sweep finishes and its carried rows drain (nothing to remove yet)
        settle[^1].GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();
        settle[^1].GetProperty("carriedRows").GetInt32().Should().Be(0);

        foreach (var message in messages)
            job.Shares.Seed("sprk_communication", message, DataversePrincipalRef.User(ExternalUser), 1);
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime); // its messages are listed only under this change
        var changed = (await job.RunAsync()).GetProperty("accessPermission");
        var after = await RunsAsync(job, 5);

        changed.GetProperty("restrictedBar").GetProperty("ran").GetInt32().Should().Be(1);
        changed.GetProperty("restrictedBar").GetProperty("left").GetInt32().Should().Be(2, "rows listed only under a change are not carried");
        after[0].GetProperty("rowsChanged").GetInt32().Should().Be(0, "the watermark moved past the change: it is not held for left rows");
        messages.Should().OnlyContain(m => !Holds(job, "sprk_communication", m, ExternalUser),
            "the sweep starts again and finds the rows the bar left, so none is lost");
    }
}
