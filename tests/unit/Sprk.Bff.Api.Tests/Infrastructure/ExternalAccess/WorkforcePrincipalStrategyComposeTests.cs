using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Ontology task 168 fix round 3: <see cref="WorkforcePrincipalStrategy.ResolveAsync"/>'s composition was lifted into the
/// internal static <see cref="WorkforcePrincipalStrategy.ComposeAsync"/> (shared with the Assign Task recipient gate). The
/// resolver must build exactly the principal it built before: the same three evaluator calls in the same order, the same
/// Read-bearing mapping and derived fields, and the <c>[WF-AUTH]</c> resolved line with its sources, still logged by the strategy.
/// </summary>
public sealed class WorkforcePrincipalStrategyComposeTests
{
    private static readonly Guid Contact = Guid.Parse("c1680003-0000-4000-8000-0000000000c1");
    private static readonly Guid SystemUser = Guid.Parse("c1680003-0000-4000-8000-0000000000c2");
    private static readonly Guid ReadProject = Guid.Parse("c1680003-0000-4000-8000-0000000000c3");
    private static readonly Guid NoneProject = Guid.Parse("c1680003-0000-4000-8000-0000000000c4");
    private static readonly Guid ReadMatter = Guid.Parse("c1680003-0000-4000-8000-0000000000c5");
    private static readonly Guid WriteOnlyWa = Guid.Parse("c1680003-0000-4000-8000-0000000000c6");
    private const string Oid = "c1680003-0000-4000-8000-0000000000c7";
    private const string Tenant = "c1680003-0000-4000-8000-0000000000c8";
    private const string TokenEmail = "pat@customer.example";

    private static AccessibleRecordSet Set(
        string entity, AccessibleRecordSetSources sources, bool capped, params (Guid Id, AccessRights Rights)[] rights) => new()
    {
        PrincipalKind = WorkforcePrincipalKind.ContactOnly,
        EntityType = entity,
        Rights = rights.ToDictionary(r => r.Id, r => r.Rights),
        Sources = sources,
        Capped = capped,
    };

    private sealed class CapturingLogger : ILogger<WorkforcePrincipalStrategy>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    public static TheoryData<string> Kinds => new() { "contact-only", "systemuser" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ResolveAsync_BuildsTheSamePrincipal_FromTheThreeCompositionsInOrder_AndLogsTheResolvedLine(string kind)
    {
        var principal = kind == "contact-only"
            ? WorkforcePrincipalResolution.ForContact(Contact, Oid, Tenant)
            : WorkforcePrincipalResolution.ForSystemUser(SystemUser, Contact, Oid, Tenant, TokenEmail);
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(principal);

        var calls = new List<string>();
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        sets.Setup(s => s.ComposeAsync(principal.Principal!, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) =>
            {
                calls.Add(entity);
                return entity switch
                {
                    "sprk_project" => Set(entity, new AccessibleRecordSetSources(false, true, StandingGrantMembership: true), capped: false,
                        (ReadProject, AccessRights.Read | AccessRights.Write), (NoneProject, AccessRights.None)),
                    "sprk_matter" => Set(entity, new AccessibleRecordSetSources(false, true, false, ImpersonatedRootSet: true), capped: true,
                        (ReadMatter, AccessRights.Read)),
                    _ => Set(entity, new AccessibleRecordSetSources(false, true, false, OrgExpansionMembership: true), capped: false,
                        (WriteOnlyWa, AccessRights.Write)),
                };
            });

        var log = new CapturingLogger();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", TokenEmail) }, "Bearer")) };

        var resolution = await new WorkforcePrincipalStrategy(resolver.Object, sets.Object, log).ResolveAsync(http, CancellationToken.None);

        calls.Should().Equal("sprk_project", "sprk_matter", "sprk_workassignment");
        resolution.IsResolved.Should().BeTrue();
        var p = resolution.Principal!;
        p.Plane.Should().Be(CallerPrincipalPlane.Workforce);
        p.ContactId.Should().Be(Contact);
        p.SystemUserId.Should().Be(kind == "systemuser" ? SystemUser : null);
        p.ContactUnreadable.Should().BeFalse();
        p.Email.Should().Be(TokenEmail, "the token's email, as before");
        p.Oid.Should().Be(Oid);
        p.ProjectAccess.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CallerProjectAccess { ProjectId = ReadProject, Rights = AccessRights.Read | AccessRights.Write });
        p.MatterAccess.Should().Equal(new Dictionary<Guid, AccessRights> { [ReadMatter] = AccessRights.Read });
        p.WorkAssignmentAccess.Should().BeEmpty("only Read-bearing entries reach the principal");
        p.UndatedAccessTermEntityTypes.Should().BeEquivalentTo("sprk_project", "sprk_workassignment");
        p.DataverseConfirmsWrites.Should().BeTrue();
        p.TruncatedRootEntityTypes.Should().BeEquivalentTo("sprk_matter");

        log.Messages.Should().ContainSingle(m => m.StartsWith("[WF-AUTH] Workforce ", StringComparison.Ordinal)).Which.Should()
            .Contain(kind == "contact-only" ? "ContactOnly" : "SystemUser")
            .And.Contain("1 project / 1 matter / 0 work-assignment accessible roots")
            .And.Contain("project sources: AccessibleRecordSetSources { SystemUserMembership = False, ContactGrants = True, StandingGrantMembership = True");
    }

    [Fact]
    public async Task ComposeAsync_TheLiftedStatic_IsWhatResolveAsyncReturns()
    {
        var principal = WorkforcePrincipalResolution.ForContact(Contact, Oid, Tenant).Principal!;
        var sets = new Mock<IAccessibleRecordSetService>();
        sets.Setup(s => s.ComposeAsync(principal, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) =>
                Set(entity, new AccessibleRecordSetSources(false, true, true), capped: entity == "sprk_project", (ReadMatter, AccessRights.Read)));

        var composed = await WorkforcePrincipalStrategy.ComposeAsync(sets.Object, principal, TokenEmail, CancellationToken.None);

        composed.Capped.Should().BeTrue();
        composed.SetFor("sprk_matter")!.RightsFor(ReadMatter).Should().Be(AccessRights.Read);
        composed.SetFor("sprk_event").Should().BeNull();
        composed.Principal.Plane.Should().Be(CallerPrincipalPlane.Workforce);
        composed.Principal.Email.Should().Be(TokenEmail);
        composed.Principal.TruncatedRootEntityTypes.Should().BeEquivalentTo("sprk_project");
    }
}
