// R3 Part 1 Phase 2 — Task 082 (2026-06-22); rewritten by unified-access-control-r2 task 152 (2026-10-02), and again
// by its verifier round 1 (item 7) to RUN THE SITE instead of the shared helper plus a source grep.
//
// POST /api/v1/documents (DataverseDocumentsEndpoints.CreateDocumentAsync, internal for these tests) publishes the owner
// MembershipChangedEvent for the row it created. Task 152 (ADR-034 A3): the event states the row's REAL owner — the
// business-unit default owner TEAM the endpoint resolved and wrote (task 080) — as PersonIdType=Team, PersonId=teamid,
// the key MembershipReconciliationJob builds for the same row. Before task 152 the endpoint published the caller's AAD
// oid as a User owner: false for a team-owned row, and in an identity space reconciliation never writes.

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

[Trait("status", "task-152-uac-r2")]
public class DataverseDocumentsEndpointsMembershipPublishingTests
{
    private static readonly Guid DocumentId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid ResolvedTeamId = Guid.Parse("cccccccc-9999-9999-9999-cccccccccccc");
    private const string TraceId = "trace-doc";

    [Fact]
    public async Task DocumentCreate_PublishesTheTeamItResolvedAndWrote_NotTheCallersOid()
    {
        var site = new Site(resolvedTeam: ResolvedTeamId);

        var result = await site.RunAsync();

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status201Created);
        site.Written!.OwningTeamId.Should().Be(ResolvedTeamId, "task 080: the row is owned by the resolved team");

        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(ResolvedTeamId, "the event names the SAME team the create wrote as the owner");
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid, "no publisher emits an AAD oid");
        evt.SourceField.Should().Be("ownerid");
        evt.EntityLogicalName.Should().Be("sprk_document");
        evt.EntityRecordId.Should().Be(DocumentId);
        evt.MutationType.Should().Be(MembershipMutationType.Added);
        evt.CorrelationId.Should().Be(TraceId, "NFR-08");

        site.Dataverse.Verify(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the site already holds the owner it wrote — no read-back");
    }

    [Fact]
    public async Task DocumentCreate_NoTeamResolved_RefusesAndPublishesNothing()
    {
        var site = new Site(resolvedTeam: null);

        var result = await site.RunAsync();

        (result as IStatusCodeHttpResult)?.StatusCode.Should().NotBe(StatusCodes.Status201Created);
        site.Written.Should().BeNull("no row is written without an owner team (task 080)");
        site.Publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public void DocumentCreateEndpoint_FalseOwnerCommentIsGone()
    {
        var source = OwnerEventTestKit.ReadSource("Api", "DataverseDocumentsEndpoints.cs");

        source.Should().NotContain("PersonId = callerOid", "the caller's oid is never an event's person");
        source.Should().NotContain("defaulted by Dataverse to the OBO", "the false owner comment is removed");
    }

    /// <summary>The POST /api/v1/documents site with boundary fakes; <see cref="RunAsync"/> executes the real handler.</summary>
    private sealed class Site
    {
        public Site(Guid? resolvedTeam)
        {
            // c1-r1: POST /api/v1/documents asks ResolveOwnerAsync (the caller is also recorded as the creator person).
            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(resolvedTeam is { } team
                    ? RecordOwnerResolution.Owned(team)
                    : RecordOwnerResolution.Refused(RecordOwnerRefusal.ActingUserUnresolved, "no team"));
            Documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<CreateDocumentRequest, CancellationToken>((r, _) => Written = r)
                .ReturnsAsync(DocumentId.ToString("D"));
            Documents.Setup(d => d.GetDocumentAsync(DocumentId.ToString("D"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity { Id = DocumentId.ToString("D"), Name = "Brief.docx", FileName = "Brief.docx", ContainerId = "b!container" });
        }

        public Mock<IDocumentDataverseService> Documents { get; } = new(MockBehavior.Strict);
        public Mock<IRecordOwnershipResolver> Ownership { get; } = new(MockBehavior.Strict);
        public Mock<IGenericEntityService> Dataverse { get; } = OwnerEventTestKit.Dataverse();
        public RecordingMembershipEventPublisher Publisher { get; } = new();
        public CreateDocumentRequest? Written { get; private set; }

        public Task<IResult> RunAsync()
        {
            var context = new DefaultHttpContext
            {
                TraceIdentifier = TraceId,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("oid", OwnerEventTestKit.CallerOid.ToString("D")) }, "test")),
            };
            return DataverseDocumentsEndpoints.CreateDocumentAsync(
                new CreateDocumentRequest { Name = "Brief.docx", ContainerId = "b!container" },
                Documents.Object,
                Publisher,
                Ownership.Object,
                Dataverse.Object,
                NullLogger<Program>.Instance,
                context,
                CancellationToken.None);
        }
    }
}
