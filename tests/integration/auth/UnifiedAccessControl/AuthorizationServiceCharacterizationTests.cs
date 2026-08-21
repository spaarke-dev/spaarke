using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization suite for <see cref="AuthorizationService"/> — the entry point every
/// endpoint filter calls to answer "may this caller do this to this record?".
///
/// Pins finding A-2 (unified-access-control-r2 spec NFR-07): the service hard-codes
/// <c>userAccessToken: null</c> (AuthorizationService.cs:48-52), so the "user permission check"
/// actually answers *can the application see this record* rather than *can the caller see it*.
/// On the SPA/Teams surface — where reads are app-only and Dataverse row-level security is inert —
/// that makes this check structurally incapable of isolating one caller from another.
///
/// The seam used here is <see cref="IAccessDataSource"/>, the module boundary ADR-038 §4 names as
/// the correct place to substitute a test double. No transport-level mocking.
/// </summary>
public class AuthorizationServiceCharacterizationTests
{
    /// <summary>
    /// Records exactly what <see cref="AuthorizationService"/> passes down to the data source, so the
    /// test can assert on the caller-scoping argument rather than on a log line or a wire format.
    /// </summary>
    private sealed class RecordingAccessDataSource : IAccessDataSource
    {
        public List<(string UserId, string ResourceId, string? UserAccessToken)> Calls { get; } = new();

        public AccessRights RightsToReturn { get; set; } = AccessRights.Read;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId,
            string resourceId,
            string? userAccessToken = null,
            CancellationToken ct = default)
        {
            Calls.Add((userId, resourceId, userAccessToken));

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = RightsToReturn
            });
        }
    }

    private static AuthorizationService ServiceWith(RecordingAccessDataSource source) =>
        new(
            source,
            new IAuthorizationRule[] { new OperationAccessRule(NullLogger<OperationAccessRule>.Instance) },
            NullLogger<AuthorizationService>.Instance);

    private static AuthorizationContext Context(string operation = "read_metadata") => new()
    {
        UserId = "caller-oid-1",
        ResourceId = "document-1",
        Operation = operation
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-2. Flipped by task 004 (FR-02).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-2 — CURRENT (BROKEN) BEHAVIOR. AuthorizationService.cs:48-52 passes
    /// <c>userAccessToken: null</c> unconditionally, selecting service-principal (app-only) mode in
    /// DataverseAccessDataSource. There is no overload, no context field, and no call site that can
    /// supply a caller token — so the answer is never caller-scoped.
    ///
    /// FLIPPED BY: task 004 (FR-02) — after that task the caller's token (or an impersonation
    /// identity) MUST reach the data source, and this assertion inverts to NotBeNull.
    /// </summary>
    [Fact]
    public async Task Characterization_AuthorizeAsync_PassesNullUserAccessToken_AppScopedNotCallerScoped()
    {
        // Arrange
        var source = new RecordingAccessDataSource();

        // Act
        await ServiceWith(source).AuthorizeAsync(Context());

        // Assert — the load-bearing detail: no caller token reaches the data source.
        source.Calls.Should().ContainSingle();
        source.Calls[0].UserAccessToken.Should().BeNull(
            "A-2 pins the CURRENT broken state: AuthorizationService.cs:48-52 hard-codes " +
            "userAccessToken: null, so the permission check answers 'can the app see this' rather " +
            "than 'can this caller see this'. Task 004 makes it caller-scoped and flips this.");
    }

    /// <summary>
    /// A-2 — the consequence. Two DIFFERENT callers asking about the SAME resource produce identical
    /// authorization outcomes, because the only input that varies (the caller identity) never reaches
    /// the privilege evaluation — the snapshot is app-scoped.
    ///
    /// This is the disclosure shape the project exists to close: on the SPA surface the BFF filter is
    /// the entire security boundary, so "every caller gets the same answer" means one client can be
    /// handed another client's matter.
    ///
    /// FLIPPED BY: task 004 (FR-02) — the two callers MUST then be able to differ.
    /// </summary>
    [Fact]
    public async Task Characterization_AuthorizeAsync_ForDifferentCallersOnSameResource_ReturnsSameDecision()
    {
        // Arrange — the data source is app-only, so it cannot distinguish callers.
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.Read };
        var service = ServiceWith(source);

        var callerA = new AuthorizationContext
        {
            UserId = "caller-with-access",
            ResourceId = "document-1",
            Operation = "read_metadata"
        };
        var callerB = new AuthorizationContext
        {
            UserId = "caller-without-access",
            ResourceId = "document-1",
            Operation = "read_metadata"
        };

        // Act
        var resultA = await service.AuthorizeAsync(callerA);
        var resultB = await service.AuthorizeAsync(callerB);

        // Assert — identical outcome for both callers, and neither call carried a caller token.
        resultA.IsAllowed.Should().Be(resultB.IsAllowed);
        source.Calls.Should().HaveCount(2);
        source.Calls.Should().OnlyContain(c => c.UserAccessToken == null);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. These pin fail-closed behavior that tasks 004/005 MUST preserve.
    // ADR-003: pin the deny, never relax it to make a test pass.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_WhenSnapshotCarriesNoRights_Denies()
    {
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.None };

        var result = await ServiceWith(source).AuthorizeAsync(Context());

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizeAsync_WhenOperationIsUnknown_DeniesRegardlessOfRights()
    {
        var source = new RecordingAccessDataSource
        {
            RightsToReturn = AccessRights.Read | AccessRights.Write | AccessRights.Delete
        };

        var result = await ServiceWith(source).AuthorizeAsync(Context("no.such.operation"));

        result.IsAllowed.Should().BeFalse();
        result.ReasonCode.Should().Be("sdap.access.deny.unknown_operation");
    }
}
