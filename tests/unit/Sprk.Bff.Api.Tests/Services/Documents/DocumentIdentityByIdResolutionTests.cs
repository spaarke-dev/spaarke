using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// spaarkeai-word-add-in-r1 task 112: <see cref="DocumentUrlIdentityResolution.ResolveByIdAsync"/> — the by-ID entry
/// point behind <c>GET /api/documents/{documentId}/identity</c>. The contracts pinned here:
/// <list type="bullet">
/// <item>the row becomes the SAME <see cref="DocumentUrlIdentityResolution.Resolution"/> the URL path builds — names,
/// and the first populated direct slot in matter &gt; project &gt; invoice &gt; work-assignment order;</item>
/// <item>the retrieve asks for the four direct slots (a missing column would silently read as "not filed");</item>
/// <item>absent (by-id <c>0x80040217</c>) is <see langword="null"/>; anything else Dataverse cannot answer is 503 —
/// including the ALTERNATE-key not-found code, which a by-id read never legitimately produces;</item>
/// <item>the caller's cancellation propagates as cancellation, not as 503.</item>
/// </list>
/// Dataverse is <see cref="IGenericEntityService"/>, STRICT, so any call a test does not expect fails it.
/// </summary>
public class DocumentIdentityByIdResolutionTests
{
    private const int ObjectDoesNotExist = -2147220969;           // 0x80040217 — by-id miss
    private const int AlternateKeyRecordDoesNotExist = -2147088239; // 0x80060891 — alt-key miss (not a by-id answer)

    private static readonly Guid DocumentId = Guid.Parse("7b1f0c2e-1111-4222-8333-944455556666");

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);

    // ── Resolved ──────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> DirectSlots => new()
    {
        "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment",
    };

    [Theory]
    [MemberData(nameof(DirectSlots))]
    public async Task RowFiledToADirectSlot_ResolvesWithThatSlotAsRelatedRecord(string slot)
    {
        var reference = new EntityReference(slot, Guid.NewGuid()) { Name = "Record name" };
        ByIdReturns(Row(DocumentId, (slot, reference)));

        var identity = await Resolve();

        identity.Should().NotBeNull();
        identity!.DocumentId.Should().Be(DocumentId);
        identity.DocumentName.Should().Be("Quick-saved brief");
        identity.FileName.Should().Be("Quick-saved brief.docx");
        identity.RelatedRecord.Should().BeSameAs(reference);
    }

    [Fact]
    public async Task RowWithSeveralSlots_ReturnsTheHighestPrioritySlot_AsTheUrlPathDoes()
    {
        var project = new EntityReference("sprk_project", Guid.NewGuid());
        var workAssignment = new EntityReference("sprk_workassignment", Guid.NewGuid());
        ByIdReturns(Row(DocumentId, ("sprk_workassignment", workAssignment), ("sprk_project", project)));

        var identity = await Resolve();

        identity!.RelatedRecord.Should().BeSameAs(project, "project outranks work assignment in the regarding priority");
    }

    [Fact]
    public async Task UnfiledRow_ResolvesWithNoRelatedRecord()
    {
        // The Quick Save case: a document with no record yet — resolved, but not filed.
        ByIdReturns(Row(DocumentId));

        var identity = await Resolve();

        identity!.DocumentId.Should().Be(DocumentId);
        identity.RelatedRecord.Should().BeNull();
    }

    [Fact]
    public async Task Retrieve_IsByIdOnSprkDocument_AndAsksForTheNamesAndAllFourDirectSlots()
    {
        ByIdReturns(Row(DocumentId));

        await Resolve();

        _dataverse.Verify(d => d.RetrieveAsync(
            "sprk_document",
            DocumentId,
            It.Is<string[]>(c =>
                c.Contains("sprk_documentname") && c.Contains("sprk_filename")
                && c.Contains("sprk_matter") && c.Contains("sprk_project")
                && c.Contains("sprk_invoice") && c.Contains("sprk_workassignment")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Absent vs. indeterminate ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AbsentRow_ObjectDoesNotExistFault_IsNull()
    {
        ByIdThrows(Fault(ObjectDoesNotExist, "sprk_document With Id = ... Does Not Exist"));

        (await Resolve()).Should().BeNull();
    }

    [Fact]
    public async Task AbsentRow_ObjectDoesNotExistFault_WrappedByTheClient_IsNull()
    {
        ByIdThrows(new InvalidOperationException("Retrieve failed",
            Fault(ObjectDoesNotExist, "sprk_document With Id = ... Does Not Exist")));

        (await Resolve()).Should().BeNull();
    }

    public static TheoryData<Exception> IndeterminateFailures => new()
    {
        new TimeoutException("The request channel timed out while waiting for a reply."),
        new InvalidOperationException("Retrieve failed", new HttpRequestException("No such host is known.")),
        // A by-id read never returns the ALTERNATE-key miss; seeing it means something else is wrong. Not "absent".
        Fault(AlternateKeyRecordDoesNotExist, "A record with the specified key values does not exist in sprk_document entity"),
        // Message says "does not exist" but the code is a schema error: classification is by code, never by text.
        Fault(-2147217149, "'sprk_document' entity doesn't contain attribute with Name = 'sprk_x' — does not exist"),
    };

    [Theory]
    [MemberData(nameof(IndeterminateFailures))]
    public async Task DataverseFailure_Is503_NeverAbsent(Exception failure)
    {
        ByIdThrows(failure);

        var act = () => Resolve();

        var thrown = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        thrown.StatusCode.Should().Be(503);
        thrown.Code.Should().Be("identity_resolution_unavailable");
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation_NotAs503()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ByIdThrows(new InvalidOperationException("Retrieve failed", new TaskCanceledException("A task was canceled.")));

        var act = () => Resolve(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private Task<DocumentUrlIdentityResolution.Resolution?> Resolve(CancellationToken ct = default)
        => DocumentUrlIdentityResolution.ResolveByIdAsync(DocumentId, _dataverse.Object, NullLogger.Instance, ct);

    private void ByIdReturns(Entity row)
        => _dataverse
            .Setup(d => d.RetrieveAsync("sprk_document", DocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

    private void ByIdThrows(Exception ex)
        => _dataverse
            .Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private static FaultException<OrganizationServiceFault> Fault(int errorCode, string message)
        => new(new OrganizationServiceFault { ErrorCode = errorCode, Message = message }, new FaultReason(message));

    private static Entity Row(Guid id, params (string Attribute, EntityReference Reference)[] lookups)
    {
        var row = new Entity("sprk_document", id)
        {
            ["sprk_documentname"] = "Quick-saved brief",
            ["sprk_filename"] = "Quick-saved brief.docx",
        };
        foreach (var (attribute, reference) in lookups)
            row[attribute] = reference;
        return row;
    }
}
