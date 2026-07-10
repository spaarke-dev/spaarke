using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Memory;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Memory;

/// <summary>
/// Handler tests for <see cref="MemoryGovernanceEndpoints"/> (task AIR2-052, FR-B-03). Exercises the
/// authorization + audit BEHAVIOR of the minimal governance surface against fake collaborators:
/// record-authorization-aligned read (allow / DENY-no-leak), structural user-scope ownership,
/// GDPR delete/erase with identifiers-only audit, and the INERT-field / no-litigation-hold guarantees.
/// </summary>
public class MemoryGovernanceEndpointsTests
{
    private const string CallerOid = "11111111-1111-1111-1111-111111111111";
    private const string TenantId = "tenant-1";
    private static readonly Guid SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string SecretValue = "privileged settlement figure $1.2M";

    private static HttpContext Ctx(string? oid = CallerOid, string? tid = TenantId)
    {
        var claims = new List<Claim>();
        if (oid is not null) claims.Add(new Claim("oid", oid));
        if (tid is not null) claims.Add(new Claim("tid", tid));
        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }

    private static MemoryItem UserItem(string key = "Drafting Style", string value = SecretValue,
        string? sensitivity = null, string? deletionPolicy = null) => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.User,
        UserId = SystemUserId.ToString(),
        Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = key, Value = value, ConfirmedByUser = true },
        Source = MemoryOrigin.User,
        Sensitivity = sensitivity,
        DeletionPolicy = deletionPolicy,
    };

    private static MemoryItem RecordItem() => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.Record,
        SubjectType = "sprk_matter",
        SubjectId = "matter-guid-1",
        Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = "Trial Strategy", Value = "Bifurcate", ConfirmedByUser = true },
        Source = MemoryOrigin.AiDerived,
    };

    // =====================================================================
    // Record-authorization-aligned read (FR-B-03 core)
    // =====================================================================

    [Fact]
    public async Task ReadRecordMemory_WhenCallerCannotReadRecord_Returns403AndDoesNotQueryMemory()
    {
        // Arrange — the authorizer denies (caller lacks record read access)
        var store = new Mock<IMemoryItemStore>();
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer
            .Setup(a => a.CanCallerReadRecordAsync(It.IsAny<ClaimsPrincipal>(), "sprk_matter", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await MemoryGovernanceEndpoints.ReadRecordMemoryAsync(
            "sprk_matter", Guid.NewGuid(), Ctx(), store.Object, authorizer.Object, NullLogger<MemoryListResponse>.Instance, default);

        // Assert — denied (403) and NO memory was read (no leak of a record's memory the caller can't see)
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        store.Verify(s => s.GetForRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadRecordMemory_WhenCallerCanReadRecord_ReturnsRecordMemory()
    {
        // Arrange — the authorizer allows
        var recordId = Guid.NewGuid();
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.GetForRecordAsync("sprk_matter", recordId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { RecordItem() });
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer
            .Setup(a => a.CanCallerReadRecordAsync(It.IsAny<ClaimsPrincipal>(), "sprk_matter", recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await MemoryGovernanceEndpoints.ReadRecordMemoryAsync(
            "sprk_matter", recordId, Ctx(), store.Object, authorizer.Object, NullLogger<MemoryListResponse>.Instance, default);

        // Assert
        var ok = result.Should().BeOfType<Ok<MemoryListResponse>>().Subject;
        ok.Value!.Count.Should().Be(1);
        ok.Value.Items[0].SubjectType.Should().Be("sprk_matter");
    }

    [Fact]
    public async Task ReadRecordMemory_WhenNoOidClaim_Returns401()
    {
        var result = await MemoryGovernanceEndpoints.ReadRecordMemoryAsync(
            "sprk_matter", Guid.NewGuid(), Ctx(oid: null), Mock.Of<IMemoryItemStore>(),
            Mock.Of<IMemoryAccessAuthorizer>(), NullLogger<MemoryListResponse>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // =====================================================================
    // User review + delete + erase (GDPR, structural ownership, audit)
    // =====================================================================

    [Fact]
    public async Task ListUserMemory_ReturnsCallersMemory_IncludingInertGovernanceFields()
    {
        // Arrange — a confidential, "locked" item is still returned (sensitivity/deletionPolicy INERT)
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.GetForUserAsync(SystemUserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { UserItem(sensitivity: "confidential", deletionPolicy: "locked") });
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer.Setup(a => a.ResolveCallerUserSubjectAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemUserId);

        // Act
        var result = await MemoryGovernanceEndpoints.ListUserMemoryAsync(
            Ctx(), store.Object, authorizer.Object, NullLogger<MemoryListResponse>.Instance, default);

        // Assert — the user sees their own memory; the INERT field is surfaced, not used to filter/deny
        var ok = result.Should().BeOfType<Ok<MemoryListResponse>>().Subject;
        ok.Value!.Count.Should().Be(1);
        ok.Value.Items[0].Sensitivity.Should().Be("confidential");
        ok.Value.Items[0].Value.Should().Be(SecretValue);
    }

    [Fact]
    public async Task ListUserMemory_WhenCallerIsNotADataverseUser_Returns403()
    {
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer.Setup(a => a.ResolveCallerUserSubjectAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await MemoryGovernanceEndpoints.ListUserMemoryAsync(
            Ctx(), Mock.Of<IMemoryItemStore>(), authorizer.Object, NullLogger<MemoryListResponse>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task DeleteUserMemoryItem_DeletesFromCallerPartition_AndEmitsIdentifiersOnlyAudit()
    {
        // Arrange
        const string itemId = "mem_user_3_abc123";
        var store = new Mock<IMemoryItemStore>();
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer.Setup(a => a.ResolveCallerUserSubjectAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemUserId);

        var audit = new Mock<IAuditLogService>();
        AuditEntry? captured = null;
        audit.Setup(a => a.LogInteractionAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, CancellationToken>((e, _) => captured = e)
            .Returns(ValueTask.CompletedTask);

        // Act
        var result = await MemoryGovernanceEndpoints.DeleteUserMemoryItemAsync(
            itemId, Ctx(), store.Object, authorizer.Object, audit.Object, NullLogger<MemoryListResponse>.Instance, default);

        // Assert — deleted from the CALLER's own subject partition (structural ownership), audited by id only
        result.Should().BeOfType<NoContent>();
        store.Verify(s => s.DeleteItemAsync(SystemUserId.ToString(), itemId, It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(MemoryAuditEvents.ActionDelete);
        captured.UserId.Should().Be(CallerOid);
        captured.DocumentsAccessed.Should().ContainSingle().Which.Should().Be(itemId);
        JsonSerializer.Serialize(captured).Should().NotContain(SecretValue);
    }

    [Fact]
    public async Task DeleteUserMemoryItem_ProceedsRegardlessOfDeletionPolicy_NoLitigationHold()
    {
        // The delete path never reads the item, so no deletionPolicy / litigation-hold can block it
        // (both DEFERRED / INERT per operator ruling 2026-07-09 + 2026-07-08). Erasability is absolute.
        var store = new Mock<IMemoryItemStore>();
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer.Setup(a => a.ResolveCallerUserSubjectAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemUserId);

        var result = await MemoryGovernanceEndpoints.DeleteUserMemoryItemAsync(
            "mem_user_3_locked", Ctx(), store.Object, authorizer.Object, Mock.Of<IAuditLogService>(),
            NullLogger<MemoryListResponse>.Instance, default);

        result.Should().BeOfType<NoContent>();
        store.Verify(s => s.DeleteItemAsync(SystemUserId.ToString(), "mem_user_3_locked", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EraseUserMemory_ErasesEntireSubject_AndAuditsCount()
    {
        // Arrange — two items exist; erase-all is the GDPR Art. 17 path
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.GetForUserAsync(SystemUserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { UserItem(key: "A"), UserItem(key: "B") });
        var authorizer = new Mock<IMemoryAccessAuthorizer>();
        authorizer.Setup(a => a.ResolveCallerUserSubjectAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemUserId);

        var audit = new Mock<IAuditLogService>();
        AuditEntry? captured = null;
        audit.Setup(a => a.LogInteractionAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, CancellationToken>((e, _) => captured = e)
            .Returns(ValueTask.CompletedTask);

        // Act
        var result = await MemoryGovernanceEndpoints.EraseUserMemoryAsync(
            Ctx(), store.Object, authorizer.Object, audit.Object, NullLogger<MemoryListResponse>.Instance, default);

        // Assert
        result.Should().BeOfType<NoContent>();
        store.Verify(s => s.EraseSubjectAsync(SystemUserId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(MemoryAuditEvents.ActionErase);
        captured.DocumentsAccessed.Should().HaveCount(2);
        JsonSerializer.Serialize(captured).Should().NotContain(SecretValue);
    }
}
