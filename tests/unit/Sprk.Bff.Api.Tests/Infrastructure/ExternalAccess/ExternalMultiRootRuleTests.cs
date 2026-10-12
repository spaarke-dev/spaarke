using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// The multi-root rule itself (spaarke-SPA-external-access-platform-r3 task 039 notes section 12; owner decision 2026-10-12,
/// option B): a linked root the caller cannot read withholds a row only when it is SECURE or its secure state is unknown.
/// The paths that call it are tested end to end in the list, document-route, message, feed and grid test classes.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalMultiRootRuleTests
{
    private static readonly Guid Readable = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid Other = Guid.Parse("a0390000-0000-4000-8000-0000000000dd");

    private static readonly CallerPrincipal Caller = new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(Readable, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static Task<SecureRootFlags> Read(
        NonSecureRootReader reader, params (ExternalGrantRootType, string?)[][] rows) =>
        ExternalMultiRootRule.ReadFlagsAsync(reader, Caller, rows, NullLogger.Instance, CancellationToken.None);

    private static NonSecureRootReader Answering(
        List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>> calls, params (ExternalGrantRootType, Guid)[] notSecure) =>
        (roots, _) =>
        {
            calls.Add(roots);
            return Task.FromResult(new NonSecureRootsRead(notSecure.ToHashSet()));
        };

    [Fact]
    public async Task ANonSecureUnreadableRoot_DoesNotWithhold_ASecureOne_Does()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var flags = await Read(Answering(calls, (ExternalGrantRootType.Matter, Other)),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeTrue();
        ExternalMultiRootRule.SecureRootsReadable(Caller, SecureRootFlags.None, Readable.ToString(), Other.ToString(), null)
            .Should().BeFalse("nothing known: the unreadable root counts as secure");
        calls.Should().ContainSingle().Which.Should().Equal((ExternalGrantRootType.Matter, Other));
    }

    [Fact]
    public async Task AReadableRoot_IsNeverLookedUp_AndARowWithNoUnreadableRoot_CausesNoRead()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();

        var flags = await Read(Answering(calls), ExternalMultiRootRule.Stamps(Readable.ToString(), null, null));

        calls.Should().BeEmpty();
        flags.Should().BeSameAs(SecureRootFlags.None);
    }

    [Fact]
    public async Task ManyRows_TheSameUnreadableRoot_IsAskedOnce()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var rows = Enumerable.Range(0, 20).Select(_ => ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null)).ToArray();

        await Read(Answering(calls), rows);

        calls.Should().ContainSingle().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task AReaderThatThrows_FailsClosed()
    {
        var flags = await Read((_, _) => throw new InvalidOperationException("boom"),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerForARootThatWasNotAsked_OpensNothing()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        // The reader claims the PROJECT with Other's id is not secure; the row links Other as a MATTER.
        var flags = await Read(Answering(calls, (ExternalGrantRootType.Project, Other)),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeFalse();
    }

    [Fact]
    public void AMalformedLookup_IsWithheld_AndNeverLookedUp()
    {
        ExternalMultiRootRule.UnreadableRoots(Caller, ExternalMultiRootRule.Stamps(Readable.ToString(), "not-a-guid", null))
            .Should().BeEmpty();
        ExternalMultiRootRule.SecureRootsReadable(Caller, SecureRootFlags.None, Readable.ToString(), "not-a-guid", null)
            .Should().BeFalse();
    }

    [Fact]
    public async Task PastTheLookupBound_TheRestCountAsSecure()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var ids = Enumerable.Range(0, ExternalMultiRootRule.MaxFlagLookups + 5).Select(_ => Guid.NewGuid()).ToList();
        NonSecureRootReader everyAskedIsOpen = (roots, _) =>
        {
            calls.Add(roots);
            return Task.FromResult(new NonSecureRootsRead(roots.ToHashSet()));
        };

        var flags = await Read(everyAskedIsOpen,
            ids.Select(id => ExternalMultiRootRule.Stamps(null, id.ToString(), null)).ToArray());

        calls.Should().ContainSingle().Which.Should().HaveCount(ExternalMultiRootRule.MaxFlagLookups);
        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, null, ids[0].ToString(), null).Should().BeTrue();
        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, null, ids[^1].ToString(), null).Should().BeFalse();
    }

    // =====================================================================
    // Main-session decision 2026-10-12 (stricter reading of option B): "secure" means EFFECTIVELY secure (UAC-r2 task 174).
    // ExternalDataService.GetNonSecureRootsAsync, REAL, over FakeDataverse: each root's own flag over the Web API, and the
    // ONE filing walk (EffectiveRootFlags) over a fake IGenericEntityService.
    // =====================================================================

    private static readonly Guid WorkAssignment = Guid.Parse("a0390000-0000-4000-8000-0000000000e1");
    private static readonly Guid Matter = Guid.Parse("a0390000-0000-4000-8000-0000000000e2");

    private static (ExternalGrantRootType Root, Guid Id) Wa(Guid id) => (ExternalGrantRootType.WorkAssignment, id);

    private static async Task<IReadOnlySet<(ExternalGrantRootType Root, Guid Id)>> NonSecure(
        ExternalDataService service, params (ExternalGrantRootType Root, Guid Id)[] roots) =>
        (await service.GetNonSecureRootsAsync(roots, CancellationToken.None)).NotSecure;

    [Fact]
    public async Task EffectiveStatus_AWorkAssignmentWithAFalseOwnFlag_UnderASecureMatter_IsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddSecureRoot(ExternalGrantRootType.Matter, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), Wa(WorkAssignment))).Should().BeEmpty(
            "the own flag has not caught up with the secure matter it is filed under; the rule asks the effective status");
    }

    [Fact]
    public async Task EffectiveStatus_AWorkAssignmentWithAFalseOwnFlag_UnderANonSecureMatter_IsNotSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), Wa(WorkAssignment))).Should().Equal(Wa(WorkAssignment));
        data.FilingReadLog.Should().Equal("sprk_workassignment", "sprk_matter");
    }

    [Fact]
    public async Task EffectiveStatus_AWorkAssignmentUnderASecureProject_IsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddSecureRoot(ExternalGrantRootType.Project, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Project, Matter);

        (await NonSecure(data.NewService(), Wa(WorkAssignment))).Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveStatus_AWorkAssignmentWhoseOwnFlagIsTrue_IsSecure_WithoutAWalk()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddSecureRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);

        (await NonSecure(data.NewService(), Wa(WorkAssignment))).Should().BeEmpty();
        data.FilingReadLog.Should().BeEmpty("a secure record is secure already; no ancestor can make it less so");
    }

    [Fact]
    public async Task EffectiveStatus_AMatterFilesUnderNothing_ItsOwnFlagIsItsEffectiveFlag_WithoutAWalk()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), (ExternalGrantRootType.Matter, Matter))).Should().Equal((ExternalGrantRootType.Matter, Matter));
        data.FilingReadLog.Should().BeEmpty();
    }

    [Theory]
    [InlineData("every filing read throws")]
    [InlineData("the ancestor read throws")]
    [InlineData("the work assignment's filing read throws")]
    [InlineData("the ancestor's flag is empty")]
    [InlineData("no filing reader is configured")]
    public async Task EffectiveStatus_WhenTheAncestryCannotBeDecided_TheRootCountsAsSecure_FailClosed(string failure)
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddRoot(ExternalGrantRootType.Matter, Matter, isSecure: failure == "the ancestor's flag is empty" ? null : false);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);
        switch (failure)
        {
            case "every filing read throws": data.FilingReadThrows = true; break;
            case "the ancestor read throws": data.FilingFaultEntity = "sprk_matter"; break;
            case "the work assignment's filing read throws": data.FilingFaultEntity = "sprk_workassignment"; break;
        }

        var service = data.NewService(withFilingReader: failure != "no filing reader is configured");

        (await NonSecure(service, Wa(WorkAssignment))).Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveStatus_ManyRoots_CostOneOwnFlagQueryAndOneWalkReadPerLevelAndTable_NotOnePerRoot()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var secureMatter = Guid.Parse("a0390000-0000-4000-8000-0000000000f1");
        var openMatter = Guid.Parse("a0390000-0000-4000-8000-0000000000f2");
        data.AddSecureRoot(ExternalGrantRootType.Matter, secureMatter);
        data.AddOpenRoot(ExternalGrantRootType.Matter, openMatter);
        var underSecure = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var underOpen = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in underSecure) { data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, id); data.FileWorkAssignmentUnder(id, ExternalGrantRootType.Matter, secureMatter); }
        foreach (var id in underOpen) { data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, id); data.FileWorkAssignmentUnder(id, ExternalGrantRootType.Matter, openMatter); }

        var result = await NonSecure(data.NewService(), underSecure.Concat(underOpen).Select(Wa).ToArray());

        result.Should().BeEquivalentTo(underOpen.Select(Wa));
        data.FlagReads.Should().ContainSingle("20 work assignments are one own-flag query");
        data.FilingReadLog.Should().Equal(new[] { "sprk_workassignment", "sprk_matter" }, "one batched read of the 20 rows, one of their 2 matters");
    }

    [Fact]
    public async Task EffectiveStatus_OneRootAskedByManyRows_IsWalkedOnce()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var service = data.NewService();
        NonSecureRootReader reader = (roots, ct) => { calls.Add(roots); return service.GetNonSecureRootsAsync(roots, ct); };

        var rows = Enumerable.Range(0, 25).Select(_ => ExternalMultiRootRule.Stamps(Readable.ToString(), null, WorkAssignment.ToString())).ToArray();
        var flags = await Read(reader, rows);

        calls.Should().ContainSingle().Which.Should().ContainSingle();
        data.FilingReadLog.Should().HaveCount(2, "the work assignment's row and its matter, whatever the row count");
        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), null, WorkAssignment.ToString()).Should().BeTrue();
    }

    // =====================================================================
    // Review of 039 option B, K1 (owner policy): a NAMED filing parent the read does not return makes the root's status
    // Unknown, so it folds to secure. The shared walk's default (write-time, UAC-r2) is unchanged: absent confers nothing.
    // =====================================================================

    [Fact]
    public async Task Effective_AWorkAssignmentWithAFalseOwnFlag_WhoseNamedParentMatterIsNotReturned_IsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        // The matter is named by the work assignment's lookup but there is no such record in the filing world.
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);

        var read = await data.NewService().GetNonSecureRootsAsync(new[] { Wa(WorkAssignment) }, CancellationToken.None);

        read.NotSecure.Should().BeEmpty("a parent the read cannot find is unknown, and unknown counts as secure");
        read.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task Effective_AWorkAssignmentWithAFalseOwnFlag_UnderAnExistingNonSecureMatter_IsNotSecure_AndTheReadIsComplete()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);

        var read = await data.NewService().GetNonSecureRootsAsync(new[] { Wa(WorkAssignment) }, CancellationToken.None);

        read.NotSecure.Should().Equal(Wa(WorkAssignment));
        read.Complete.Should().BeTrue();
    }

    [Fact]
    public async Task TheSharedWalk_ByDefault_StillTreatsANamedButAbsentParentAsConferringNothing_ButOptsInToUnknown()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter); // named, never created
        var ids = new[] { WorkAssignment };

        foreach (var single in new[] { true, false })
        {
            // Both shapes of the walk: one record (point reads) and many (batched). A second id makes it batched.
            var asked = single ? ids : ids.Append(Guid.NewGuid()).ToArray();

            var legacy = await EffectiveRootFlags.ReadAncestryAsync(
                data.FilingReader, NullLogger.Instance, "sprk_workassignment", asked, CancellationToken.None);
            legacy![WorkAssignment].IsKnown.Should().BeTrue("the write-time walk (UAC-r2) is unchanged: a missing parent confers nothing");
            legacy[WorkAssignment].HasSecureParent.Should().BeFalse();

            var strict = await EffectiveRootFlags.ReadAncestryAsync(
                data.FilingReader, NullLogger.Instance, "sprk_workassignment", asked, CancellationToken.None, absentParentIsUnknown: true);
            strict![WorkAssignment].IsKnown.Should().BeFalse("the read path opts in: a named parent that is not returned is unknown");
        }
    }

    // =====================================================================
    // Review of 039 option B, K3: a PROJECT files under a matter only through the polymorphic pair
    // (sprk_regardingrecordid + sprk_regardingrecordtype -> sprk_recordtype_ref), not a typed lookup.
    // =====================================================================

    private static readonly Guid Project = Guid.Parse("a0390000-0000-4000-8000-0000000000e3");

    private static (ExternalGrantRootType Root, Guid Id) Proj(Guid id) => (ExternalGrantRootType.Project, id);

    [Fact]
    public async Task Effective_AProjectWithAFalseOwnFlag_UnderASecureMatter_IsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.Project, Project);
        data.AddSecureRoot(ExternalGrantRootType.Matter, Matter);
        data.FileProjectUnderByPair(Project, ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), Proj(Project))).Should().BeEmpty(
            "the project's own flag has not caught up with the secure matter its pair names");
        data.FilingReadLog.Should().Contain("sprk_recordtype_ref", "the pair's type is what says the id is a matter");
    }

    [Fact]
    public async Task Effective_AProjectWithAFalseOwnFlag_UnderANonSecureMatter_IsNotSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.Project, Project);
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);
        data.FileProjectUnderByPair(Project, ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), Proj(Project))).Should().Equal(Proj(Project));
        data.FilingReadLog.Should().Contain("sprk_recordtype_ref");
    }

    [Fact]
    public async Task Effective_AProjectWithAFalseOwnFlag_WhoseNamedMatterIsNotReturned_IsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.Project, Project);
        data.FileProjectUnderByPair(Project, ExternalGrantRootType.Matter, Matter);

        (await NonSecure(data.NewService(), Proj(Project))).Should().BeEmpty();
    }

    // =====================================================================
    // Review of 039 option B, K2: the read says whether it COMPLETED.
    // =====================================================================

    [Fact]
    public async Task TheRead_IsComplete_WhenEveryChunkAndWalkSucceeded_EvenWhenARootIsSecure()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddSecureRoot(ExternalGrantRootType.Matter, Matter);
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);

        var read = await data.NewService().GetNonSecureRootsAsync(
            new[] { (ExternalGrantRootType.Matter, Matter), Wa(WorkAssignment) }, CancellationToken.None);

        read.Complete.Should().BeTrue("a root that is genuinely secure is an answer, not a failure");
    }

    [Theory]
    [InlineData("a flag chunk faults")]
    [InlineData("a flag chunk throws")]
    [InlineData("a walk read throws")]
    public async Task TheRead_IsIncomplete_WhenAChunkOrAWalkFailed(string failure)
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, WorkAssignment);
        data.AddOpenRoot(ExternalGrantRootType.Matter, Matter);
        data.FileWorkAssignmentUnder(WorkAssignment, ExternalGrantRootType.Matter, Matter);
        switch (failure)
        {
            case "a flag chunk faults": data.FlagFault = System.Net.HttpStatusCode.InternalServerError; break;
            case "a flag chunk throws": data.FlagReadThrows = true; break;
            case "a walk read throws": data.FilingFaultEntity = "sprk_matter"; break;
        }

        var read = await data.NewService().GetNonSecureRootsAsync(new[] { Wa(WorkAssignment) }, CancellationToken.None);

        read.Complete.Should().BeFalse();
        read.NotSecure.Should().BeEmpty("fail closed, as before");
    }

    [Fact]
    public async Task APastTheLookupBoundRequest_IsIncomplete()
    {
        var ids = Enumerable.Range(0, ExternalMultiRootRule.MaxFlagLookups + 5).Select(_ => Guid.NewGuid()).ToList();
        NonSecureRootReader everyAskedIsOpen = (roots, _) => Task.FromResult(new NonSecureRootsRead(roots.ToHashSet()));

        var flags = await Read(everyAskedIsOpen, ids.Select(id => ExternalMultiRootRule.Stamps(null, id.ToString(), null)).ToArray());

        flags.IsComplete.Should().BeFalse("the roots past the bound were never asked");
    }

    [Fact]
    public async Task AReaderThatThrows_IsIncomplete_AndAReadOfNothing_IsComplete()
    {
        var thrown = await Read((_, _) => throw new InvalidOperationException("boom"),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));
        var nothing = await Read(Answering(new()), ExternalMultiRootRule.Stamps(Readable.ToString(), null, null));

        thrown.IsComplete.Should().BeFalse();
        nothing.IsComplete.Should().BeTrue("no unreadable root means no read, and nothing to lose");
    }
}
