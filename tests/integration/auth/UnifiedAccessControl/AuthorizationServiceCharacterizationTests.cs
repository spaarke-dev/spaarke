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

    /// <summary>
    /// Caller-scoped test double: grants <paramref name="rightsWhenMatched"/> only to the holder of
    /// <paramref name="tokenWithAccess"/>, and nothing to anyone else. This is the behaviour the real
    /// OBO path has (Dataverse answers as the impersonated user), and it is what makes
    /// "two callers can differ" a meaningful assertion rather than a tautology.
    /// </summary>
    private sealed class PerCallerAccessDataSource(string tokenWithAccess, AccessRights rightsWhenMatched)
        : IAccessDataSource
    {
        public List<string?> TokensReceived { get; } = new();

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId,
            string resourceId,
            string? userAccessToken = null,
            CancellationToken ct = default)
        {
            TokensReceived.Add(userAccessToken);

            var rights = string.Equals(userAccessToken, tokenWithAccess, StringComparison.Ordinal)
                ? rightsWhenMatched
                : AccessRights.None;

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = rights
            });
        }
    }

    private static AuthorizationService ServiceWith(IAccessDataSource source) =>
        new(
            source,
            new IAuthorizationRule[] { new OperationAccessRule(NullLogger<OperationAccessRule>.Instance) },
            NullLogger<AuthorizationService>.Instance);

    private const string CallerToken = "caller-bearer-token";

    private static AuthorizationContext Context(
        string operation = "read_metadata", string? token = CallerToken) => new()
        {
            UserId = "caller-oid-1",
            ResourceId = "document-1",
            Operation = operation,
            UserAccessToken = token
        };

    // ─────────────────────────────────────────────────────────────────────────────
    // A-2 — Flipped by task 004 (FR-02).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 004 (FR-02) — was
    /// <c>Characterization_AuthorizeAsync_PassesNullUserAccessToken_AppScopedNotCallerScoped</c>.
    ///
    /// AuthorizationService.cs:48-52 used to hard-code <c>userAccessToken: null</c>, selecting
    /// service-principal (app-only) mode in DataverseAccessDataSource. There was no overload, no
    /// context field, and no call-site that could supply a caller token — so the answer was never
    /// caller-scoped, and on the SPA/Teams surface (where reads are app-only and Dataverse row-level
    /// security is inert) it was structurally incapable of isolating one caller from another.
    ///
    /// The token now rides on <c>AuthorizationContext.UserAccessToken</c> and is forwarded verbatim.
    /// </summary>
    [Fact]
    public async Task AuthorizeAsync_ForwardsCallerTokenToDataSource_EvaluatingAsTheCaller()
    {
        // Arrange
        var source = new RecordingAccessDataSource();

        // Act
        await ServiceWith(source).AuthorizeAsync(Context());

        // Assert — the load-bearing detail: the CALLER's token reaches the data source, so the
        // snapshot is computed for the caller rather than for the application.
        source.Calls.Should().ContainSingle();
        source.Calls[0].UserAccessToken.Should().Be(CallerToken,
            "A-2 is closed: AuthorizationService forwards the caller's token so DataverseAccessDataSource " +
            "takes its OBO path and answers 'can THIS CALLER see this record'");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 004 (FR-02) — was
    /// <c>Characterization_AuthorizeAsync_ForDifferentCallersOnSameResource_ReturnsSameDecision</c>.
    ///
    /// The consequence half of A-2: two DIFFERENT callers asking about the SAME resource used to
    /// produce identical outcomes, because the only input that varied (caller identity) never reached
    /// the privilege evaluation. That is the disclosure shape this project exists to close — on the
    /// SPA surface the BFF filter is the entire security boundary, so "every caller gets the same
    /// answer" means one client can be handed another client's matter.
    ///
    /// Two callers can now genuinely differ. The data-source double answers per-caller (keyed on the
    /// token it receives), which is exactly what the real OBO path does.
    /// </summary>
    [Fact]
    public async Task AuthorizeAsync_ForDifferentCallersOnSameResource_CanReachDifferentDecisions()
    {
        // Arrange — a caller-scoped double: it grants Read only to the holder of the "with-access"
        // token, mirroring how Dataverse answers an impersonated/OBO read.
        var source = new PerCallerAccessDataSource(
            tokenWithAccess: "token-with-access", rightsWhenMatched: AccessRights.Read);
        var service = ServiceWith(source);

        var callerA = new AuthorizationContext
        {
            UserId = "caller-with-access",
            ResourceId = "document-1",
            Operation = "read_metadata",
            UserAccessToken = "token-with-access"
        };
        var callerB = new AuthorizationContext
        {
            UserId = "caller-without-access",
            ResourceId = "document-1",
            Operation = "read_metadata",
            UserAccessToken = "token-without-access"
        };

        // Act
        var resultA = await service.AuthorizeAsync(callerA);
        var resultB = await service.AuthorizeAsync(callerB);

        // Assert — the two callers now reach DIFFERENT decisions on the same record. Before task 004
        // this was impossible: the snapshot was app-scoped, so both answers were identical.
        resultA.IsAllowed.Should().BeTrue("this caller's token grants Read on the record");
        resultB.IsAllowed.Should().BeFalse("this caller's token grants nothing on the record");

        // Anti-vacuity: prove the distinction came from the token reaching the data source, not from
        // some unrelated short-circuit.
        source.TokensReceived.Should().HaveCount(2);
        source.TokensReceived.Should().BeEquivalentTo(
            new[] { "token-with-access", "token-without-access" });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FAIL CLOSED — task 004's core guarantee (FR-02). A caller-scoped evaluation with no
    // obtainable token DENIES; it must never degrade to app-only.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The guard that makes A-2 non-recurrable: an absent caller token is a DENY, not a silent
    /// fallback to app-only evaluation. Without this, forgetting the token at a new call-site would
    /// reintroduce the original defect invisibly — the request would succeed, just app-scoped.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthorizeAsync_WithNoCallerToken_DeniesAndNeverConsultsDataSource(string? token)
    {
        // Arrange — a data source that would GRANT if consulted, so a pass here could only come from
        // the app-only fallback this task exists to remove.
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.Read };

        // Act
        var result = await ServiceWith(source).AuthorizeAsync(Context(token: token));

        // Assert
        result.IsAllowed.Should().BeFalse("a caller-scoped evaluation without a caller token must deny");
        result.ReasonCode.Should().Be("sdap.access.deny.no_caller_token",
            "the denial must be attributable to the missing credential — not to unknown_operation or " +
            "insufficient_rights, which would hide the real cause");

        source.Calls.Should().BeEmpty(
            "the data source must NOT be consulted at all: reaching it with a null token is exactly " +
            "the app-only evaluation A-2 describes (FR-02 acceptance — no caller-scoped path reaches " +
            "IAccessDataSource with userAccessToken: null)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GetCallerAccessAsync — task 006 (FR-05). The snapshot accessor capability consumers use.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The guard that makes A-4 non-recurrable, and the sibling of
    /// <see cref="AuthorizeAsync_WithNoCallerToken_DeniesAndNeverConsultsDataSource"/>: a snapshot
    /// request with no caller token yields <see cref="AccessRights.None"/> WITHOUT consulting the data
    /// source. Passing the null through would be strictly worse than returning None — a null selects
    /// app-only evaluation, which on the SPA/Teams surface always answers yes.
    ///
    /// The double would GRANT if reached, so this assertion cannot succeed vacuously.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCallerAccessAsync_WithNoCallerToken_ReturnsNoRightsAndNeverConsultsDataSource(string? token)
    {
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.Read };

        var snapshot = await ServiceWith(source)
            .GetCallerAccessAsync("caller-oid-1", "document-1", token);

        snapshot.AccessRights.Should().Be(AccessRights.None,
            "a caller-scoped snapshot without a caller token must carry no rights");
        source.Calls.Should().BeEmpty(
            "reaching the data source with a null token IS the app-only evaluation finding A-4 " +
            "describes — it must not happen at all");
    }

    /// <summary>
    /// The forwarding half: the caller's token reaches the data source verbatim, so capability
    /// consumers compute from the caller's own rights rather than the application's.
    /// </summary>
    [Fact]
    public async Task GetCallerAccessAsync_WithCallerToken_ForwardsItAndReturnsTheCallersRights()
    {
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.Read | AccessRights.Write };

        var snapshot = await ServiceWith(source)
            .GetCallerAccessAsync("caller-oid-1", "document-1", CallerToken);

        source.Calls.Should().ContainSingle();
        source.Calls[0].UserAccessToken.Should().Be(CallerToken);
        source.Calls[0].UserId.Should().Be("caller-oid-1");
        source.Calls[0].ResourceId.Should().Be("document-1");

        snapshot.AccessRights.Should().Be(AccessRights.Read | AccessRights.Write);
    }

    /// <summary>
    /// Enforcement and capability reporting must read the SAME snapshot (FR-05 acceptance: "capabilities
    /// derive from the same snapshot as enforcement"). <c>AuthorizeAsync</c> routes through
    /// <c>GetCallerAccessAsync</c>, so both present identical arguments to the data source. If a future
    /// change gave <c>AuthorizeAsync</c> its own path, the two argument tuples would diverge here.
    /// </summary>
    [Fact]
    public async Task AuthorizeAsync_AndGetCallerAccessAsync_PresentIdenticalArgumentsToTheDataSource()
    {
        var source = new RecordingAccessDataSource { RightsToReturn = AccessRights.Read };
        var service = ServiceWith(source);

        await service.AuthorizeAsync(Context());
        await service.GetCallerAccessAsync("caller-oid-1", "document-1", CallerToken);

        source.Calls.Should().HaveCount(2);
        source.Calls[0].Should().Be(source.Calls[1],
            "both the enforcement path and the capability path must resolve the caller's rights the " +
            "same way — a second, divergent access calculus is what FR-05 forbids");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. These pin fail-closed behavior that task 005 MUST preserve.
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
