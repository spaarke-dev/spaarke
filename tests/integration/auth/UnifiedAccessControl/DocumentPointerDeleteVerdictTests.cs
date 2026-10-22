using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// R3 ISS-004 review (#1650) F1 — the document-pointer check as the DELETE needs it
/// (<see cref="RecordContainerResolver.VerifyDocumentPointerForDeleteAsync"/>): the rule in force, but a fault told apart
/// from a refusal, and an item that is gone from an owned container, under a pointer the BFF bound, reported as such.
/// </summary>
/// <remarks>
/// <para>Seam: the REAL <see cref="RecordContainerResolver"/> over a substituted registry, entity service and SPE item
/// reader (<see cref="TestRecordContainerResolver.DocumentPointerWorld"/>), run under BOTH rules. The boolean contract that
/// download and every other caller rely on is pinned at the end: still <see langword="false"/>, never a throw.</para>
/// </remarks>
public class DocumentPointerDeleteVerdictTests
{
    private const string Drive = "b!owned-root-container";
    private const string ForeignDrive = "b!a-container-no-unit-stamps";
    private const string Item = "01BOUNDITEMOFTHEDOCUMENT";
    private static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-0000000d1e7e");
    private static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;

    /// <summary>A root unit stamping <see cref="Drive"/>; one document owned by it, bound to <see cref="Item"/>.</summary>
    private static World Environment(bool strict)
    {
        var world = new World { Strict = strict, ItemIdBoundBackfillComplete = strict };
        world.BusinessUnits[Root] = (null, Drive);
        world.Rows[("sprk_document", DocumentId)] = BoundDocument(Item);
        return world;
    }

    private static Entity BoundDocument(string? boundItem)
    {
        var row = World.Document(DocumentId);
        if (boundItem is not null)
        {
            row[DocumentPointerBinding.BoundItemIdColumn] = boundItem;
        }

        return row;
    }

    public static TheoryData<bool> BothRules => new() { false, true };

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task APointerThatPassesTheRule_IsVerified(bool strict)
    {
        var verdict = await Environment(strict).Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Verified);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task ABoundItemGoneFromAnOwnedContainer_IsFileAlreadyGone(bool strict)
    {
        var world = Environment(strict);
        world.Items[(Drive, Item)] = null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.FileAlreadyGone);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AnItemGone_ButBoundToAnotherItem_IsRefused(bool strict)
    {
        var world = Environment(strict);
        world.Rows[("sprk_document", DocumentId)] = BoundDocument("01SOMEOTHERITEM");
        world.Items[(Drive, Item)] = null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Refused, "a re-pointed row proves nothing about the file");
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AnItemGone_WithNoBoundCopy_IsRefused_EvenBeforeTheBackfillIsComplete(bool strict)
    {
        // Unlike the rule (which, mid-transition, lets an empty copy fall back to the rule in force), 'the file is gone'
        // needs the copy: an unbound pointer might be one a Write holder set.
        var world = Environment(strict);
        world.Rows[("sprk_document", DocumentId)] = BoundDocument(null);
        world.ItemIdBoundBackfillComplete = false;
        world.Items[(Drive, Item)] = null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Refused);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AnItemGone_FromAContainerThisStampDoesNotOwn_IsRefused(bool strict)
    {
        var world = Environment(strict);
        world.UnownedDrives.Add(Drive);
        world.Items[(Drive, Item)] = null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Refused, "the unowned-container refusal is deliberate, not a 404");
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AnItemThatIsStillThere_InAContainerTheRuleRefuses_IsRefused_NeverGone(bool strict)
    {
        // Must-not-fire: the bound id matches, but Graph does not answer 404, so nothing is 'gone'.
        var world = Environment(strict);

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, ForeignDrive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Refused);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AGraphFault_IsAFault_NotARefusal(bool strict)
    {
        var world = Environment(strict);
        world.ItemReadFault = new HttpRequestException("Graph 429 Too Many Requests");

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Fault);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task ADataverseFault_IsAFault_NotARefusal(bool strict)
    {
        var world = Environment(strict);
        world.RetrieveFault = (entity, _) => entity == "sprk_document" ? new TimeoutException("Dataverse timed out") : null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Fault);
    }

    [Fact]
    public async Task AFaultOnlyInTheStrictDerivationRead_IsAFault_NotARefusal()
    {
        // R3 ISS-004 re-check F3-1. The strict rule's one-column binding read succeeds; only the WIDE sprk_document read of
        // the derivation faults. (ADataverseFault_IsAFault above hits the binding read first, which is the other half.)
        var world = Environment(strict: true);
        world.RetrieveFault = (entity, columns) =>
            entity == "sprk_document" && columns.Length > 1 ? new TimeoutException("Dataverse timed out") : null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Fault);
    }

    [Fact]
    public async Task TheBooleanCheck_UnderAFaultOnlyInTheStrictDerivationRead_StillReturnsFalse_AndTheDerivationStillFoldsItIntoUndecided()
    {
        var world = Environment(strict: true);
        world.RetrieveFault = (entity, columns) =>
            entity == "sprk_document" && columns.Length > 1 ? new TimeoutException("Dataverse timed out") : null;
        var resolver = world.Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, Drive, Item)).Should().BeFalse();
        (await resolver.DeriveDocumentContainersAsync(DocumentId)).Decided.Should().BeFalse(
            "every other caller of the public derivation keeps its Undecided-on-fault contract");
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task APermanentGraphStatusOnTheItemRead_IsARefusal_AndATransientOneIsAFault(bool strict)
    {
        // R3 ISS-004 re-check F3-2: 400 (a malformed item id) and 403 are final for this pointer; 429 and 5xx are not.
        // 404 is not an exception at all (GetItemCreatorAsync maps it to null: FileAlreadyGone / Refused above).
        foreach (var (status, expected) in new[]
                 {
                     (400, DocumentPointerDeleteVerdict.Refused), (403, DocumentPointerDeleteVerdict.Refused),
                     (429, DocumentPointerDeleteVerdict.Fault), (500, DocumentPointerDeleteVerdict.Fault),
                     (503, DocumentPointerDeleteVerdict.Fault),
                 })
        {
            var world = Environment(strict);
            world.ItemReadFault = new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = status };

            (await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item))
                .Should().Be(expected, $"Graph {status}");
        }
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task AFaultAskingWhetherTheFileIsGone_IsAFault(bool strict)
    {
        // The item is gone, so the rule refuses and the 'is it gone?' question is asked; its row read then faults.
        var world = Environment(strict);
        world.Items[(Drive, Item)] = null;
        world.RetrieveFault = (entity, columns) =>
            entity == "sprk_document" && columns.Length == 1 && columns[0] == DocumentPointerBinding.BoundItemIdColumn
                ? new TimeoutException("Dataverse timed out")
                : null;

        var verdict = await world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item);

        verdict.Should().Be(DocumentPointerDeleteVerdict.Fault);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task ATypedRefusalBelow500_IsARefusal_AndOneAt503IsAFault(bool strict)
    {
        var refused = Environment(strict);
        refused.ItemReadFault = new SdapProblemException("container_ownership_indeterminate", "Indeterminate", statusCode: 409);
        var unavailable = Environment(strict);
        unavailable.ItemReadFault = new SdapProblemException("spe_unavailable", "Unavailable", statusCode: 503);

        (await refused.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item))
            .Should().Be(DocumentPointerDeleteVerdict.Refused);
        (await unavailable.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item))
            .Should().Be(DocumentPointerDeleteVerdict.Fault);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task ABlankPointerOrAnEmptyId_IsRefused(bool strict)
    {
        var resolver = Environment(strict).Build();

        (await resolver.VerifyDocumentPointerForDeleteAsync(DocumentId, " ", Item)).Should().Be(DocumentPointerDeleteVerdict.Refused);
        (await resolver.VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, null)).Should().Be(DocumentPointerDeleteVerdict.Refused);
        (await resolver.VerifyDocumentPointerForDeleteAsync(Guid.Empty, Drive, Item)).Should().Be(DocumentPointerDeleteVerdict.Refused);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task ACancelledCaller_IsRethrown_NotAFault(bool strict)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var world = Environment(strict);
        world.ItemReadFault = new OperationCanceledException(cts.Token);

        var act = () => world.Build().VerifyDocumentPointerForDeleteAsync(DocumentId, Drive, Item, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The boolean contract every other caller relies on is untouched ──

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task TheBooleanCheck_StillFoldsAFaultIntoFalse_AndEnsureStillThrowsThe409(bool strict)
    {
        var world = Environment(strict);
        world.ItemReadFault = new HttpRequestException("Graph 429 Too Many Requests");
        var resolver = world.Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, Drive, Item)).Should().BeFalse();
        var ensure = () => resolver.EnsureDocumentPointerContainerAsync(DocumentId, Drive, Item);
        (await ensure.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(409);
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task TheBooleanCheck_StillRefusesAnItemGone_EvenWhenBound(bool strict)
    {
        // A download of a gone file stays refused: only the delete reads 'gone' as 'nothing left to delete'.
        var world = Environment(strict);
        world.Items[(Drive, Item)] = null;

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, Drive, Item)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothRules))]
    public async Task TheBooleanCheck_StillAllowsAPassingPointer(bool strict)
    {
        (await Environment(strict).Build().IsDocumentPointerContainerAllowedAsync(DocumentId, Drive, Item)).Should().BeTrue();
    }
}
