using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 144 (owner decision F2 = a) — the read-only scheduled census of the Secure Record
/// business unit. Between provisioning calls an administrator can Change-BU a user into the Secure Record BU, add a
/// member to its owner team, or widen a role's depth, and no BFF code can block it (no plugins). This job is what
/// notices: one CRITICAL line per finding, naming the principal.
/// </summary>
/// <remarks>
/// <para><b>What is pinned.</b> A clean directory reports isolated at Information; each way a secure record becomes
/// readable — a user in the BU, a member of the named team, a human reaching the BU by depth — is a CRITICAL line and an
/// unsuccessful run; a principal on a SECOND page is still found; a directory without the BU is inert (Warning, never a
/// pass); and an unreadable census THROWS after its heartbeat, so the scheduler retries rather than recording "clean".</para>
///
/// <para><b>The one seam</b> is <see cref="IGenericEntityService"/>, mocked strict: a small directory answers each
/// query by entity name, the way the live SDK does, with the same attribute TYPES (EntityReference lookups, Guid
/// intersect columns, OptionSetValue choices) — a reader that misread a type would fail here, not in production.</para>
/// </remarks>
public class SecureRecordIsolationCensusJobTests
{
    private static readonly Guid RootBu = Guid.Parse("00000000-0000-0000-0000-0000000001a0");
    private static readonly Guid SecureBu = Guid.Parse("00000000-0000-0000-0000-0000000001b0");
    private static readonly Guid SiblingBu = Guid.Parse("00000000-0000-0000-0000-0000000001c0");

    private static readonly Guid PrivProject = Guid.Parse("00000000-0000-0000-0000-0000000002a1");
    private static readonly Guid PrivMatter = Guid.Parse("00000000-0000-0000-0000-0000000002a2");
    private static readonly Guid PrivWorkAssignment = Guid.Parse("00000000-0000-0000-0000-0000000002a3");

    private static readonly Guid BasicUserRootRole = Guid.Parse("00000000-0000-0000-0000-0000000003a1");
    private static readonly Guid BasicUserRootCopy = BasicUserRootRole;
    private static readonly Guid BasicUserSiblingCopy = Guid.Parse("00000000-0000-0000-0000-0000000003a2");
    private static readonly Guid SysAdminRole = Guid.Parse("00000000-0000-0000-0000-0000000003b1");
    private static readonly Guid OwnerRole = Guid.Parse("00000000-0000-0000-0000-0000000003c1");

    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-0000000004a1");
    private static readonly Guid TestUser = Guid.Parse("00000000-0000-0000-0000-0000000004a2");
    private static readonly Guid AppUser = Guid.Parse("00000000-0000-0000-0000-0000000004a3");

    private static readonly Guid SecureDefaultTeam = Guid.Parse("00000000-0000-0000-0000-0000000005a1");
    private static readonly Guid NamedTeam = Guid.Parse("00000000-0000-0000-0000-0000000005a2");
    private static readonly Guid SiblingDefaultTeam = Guid.Parse("00000000-0000-0000-0000-0000000005a3");

    private readonly Directory _directory = new();
    private readonly CapturingLogger _log = new();
    private readonly Dictionary<string, Guid> _codifiedChildPrivileges = new(StringComparer.Ordinal);

    public SecureRecordIsolationCensusJobTests()
    {
        // The dev shape, isolated: the secure BU and an ordinary BU are siblings under root; the ordinary user holds
        // Deep from the SIBLING (reaches nothing secure); the administrator holds System Administrator directly (the
        // allow-listed exception); the owner role sits on the named team alone; nobody is in the secure BU.
        _directory.BusinessUnit(RootBu, "Spaarke", null);
        _directory.BusinessUnit(SecureBu, SecureRecordOwnerTeam.DefaultBusinessUnitName, RootBu);
        _directory.BusinessUnit(SiblingBu, "Spaarke Business Unit 1", RootBu);

        _directory.Privilege(PrivProject, "prvReadsprk_Project");
        _directory.Privilege(PrivMatter, "prvReadsprk_Matter");
        _directory.Privilege(PrivWorkAssignment, "prvReadsprk_WorkAssignment");

        _directory.Role(BasicUserRootCopy, "Spaarke Basic User", RootBu, BasicUserRootRole);
        _directory.Role(BasicUserSiblingCopy, "Spaarke Basic User", SiblingBu, BasicUserRootRole);
        _directory.Role(SysAdminRole, "System Administrator", RootBu, SysAdminRole);
        _directory.Role(OwnerRole, SecureBuRoleDepthAssertion.SecureOwnerRoleName, SecureBu, OwnerRole);

        _directory.Depth(BasicUserRootRole, PrivProject, PrivilegeDepth.Deep);
        _directory.Depth(BasicUserRootRole, PrivMatter, PrivilegeDepth.Deep);
        _directory.Depth(BasicUserRootRole, PrivWorkAssignment, PrivilegeDepth.Deep);
        _directory.Depth(SysAdminRole, PrivProject, PrivilegeDepth.Global);

        // Task 145 (clause 5): the owner role holds Read at Basic on every table of the codified set — iterated FROM
        // the embedded set, so this fixture never carries a second copy of the list.
        var rootPrivileges = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["prvReadsprk_Project"] = PrivProject,
            ["prvReadsprk_Matter"] = PrivMatter,
            ["prvReadsprk_WorkAssignment"] = PrivWorkAssignment
        };
        foreach (var table in SecureRecordOwnerRoleSet.Embedded.Tables)
        {
            if (!rootPrivileges.TryGetValue(table.PrivilegeName, out var privilegeId))
            {
                privilegeId = Guid.NewGuid();
                _directory.Privilege(privilegeId, table.PrivilegeName);
                _codifiedChildPrivileges[table.LogicalName] = privilegeId;
            }

            _directory.Depth(OwnerRole, privilegeId, PrivilegeDepth.Basic);
        }

        _directory.User(Admin, "Ralph Admin", RootBu, accessMode: 0, isDisabled: false, isApplication: false, SysAdminRole);
        _directory.User(TestUser, "Test User 1", SiblingBu, accessMode: 0, isDisabled: false, isApplication: false, BasicUserSiblingCopy);
        _directory.User(AppUser, "# mi-bff-api-dev", RootBu, accessMode: 4, isDisabled: false, isApplication: true);

        _directory.Team(SecureDefaultTeam, SecureRecordOwnerTeam.DefaultBusinessUnitName, SecureBu, isDefault: true, teamType: 0);
        _directory.Team(NamedTeam, SecureRecordOwnerTeam.DefaultOwnerTeamName, SecureBu, isDefault: false, teamType: 0, roles: new[] { OwnerRole });
        _directory.Team(SiblingDefaultTeam, "Spaarke Business Unit 1", SiblingBu, isDefault: true, teamType: 0, members: new[] { TestUser });
    }

    [Fact]
    public async Task Run_OnAnIsolatedDirectory_ReportsIsolated_AndLogsNoCriticalLine()
    {
        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical);
        _log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat status=isolated"))
            .Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task Run_WhenAUserHasBeenMovedIntoTheSecureBusinessUnit_LogsCriticalNamingThem()
    {
        _directory.User(Guid.NewGuid(), "Moved Attorney", SecureBu, accessMode: 0, isDisabled: false, isApplication: false);

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(nameof(SecureBuVerdict.SecureBusinessUnitHasUsers));
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical
                                           && e.Message.Contains(nameof(SecureBuVerdict.SecureBusinessUnitHasUsers))
                                           && e.Message.Contains("Moved Attorney"));
    }

    [Fact]
    public async Task Run_WhenTheNamedOwnerTeamHasAnApplicationUserMember_LogsCritical()
    {
        _directory.AddMember(NamedTeam, AppUser);

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical
                                           && e.Message.Contains(nameof(SecureBuVerdict.OwnerTeamHasMembers))
                                           && e.Message.Contains("# mi-bff-api-dev"));
    }

    [Fact]
    public async Task Run_WhenAHumanReachesTheSecureBusinessUnitByDepth_LogsCritical()
    {
        // Spaarke Basic User's ROOT copy, held directly by a user in root: Deep from an ancestor of the secure BU.
        _directory.User(Guid.NewGuid(), "Root Paralegal", RootBu, accessMode: 0, isDisabled: false, isApplication: false,
            BasicUserRootCopy);

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical
                                           && e.Message.Contains(nameof(SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit))
                                           && e.Message.Contains("Root Paralegal"));
    }

    [Fact]
    public async Task Run_WhenTheOffendingUserIsOnTheSecondPage_StillFindsThem()
    {
        _directory.User(Guid.NewGuid(), "Second Page User", SecureBu, accessMode: 0, isDisabled: true, isApplication: false);
        _directory.SplitIntoTwoPages("systemuser");

        var result = await RunAsync();

        result.Success.Should().BeFalse("a census that stopped at page one would omit this user and report isolation");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("Second Page User"));
        _directory.PagesRequested("systemuser").Should().Be(2);
    }

    [Fact]
    public async Task Run_WhenTheSecureBusinessUnitDoesNotExist_IsInert_WarnsAndDoesNotPass()
    {
        _directory.RemoveBusinessUnit(SecureBu);

        var result = await RunAsync();

        result.Success.Should().BeFalse("inert is not a pass");
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical, "there is no secure BU whose records could be exposed");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("heartbeat status=inert"));
    }

    /// <summary>
    /// A second business unit carrying the secure name, listed AFTER the real one, holding a user. A job that graded the
    /// first match would report this directory isolated; it must log CRITICAL and not pass (task 144, verifier round 2).
    /// </summary>
    [Fact]
    public async Task Run_WhenTwoBusinessUnitsCarryTheSecureName_LogsCriticalAndNeverReportsIsolated()
    {
        var secondSecureNamed = Guid.NewGuid();
        _directory.BusinessUnit(secondSecureNamed, SecureRecordOwnerTeam.DefaultBusinessUnitName, SiblingBu);
        _directory.User(Guid.NewGuid(), "Second Unit User", secondSecureNamed, accessMode: 0, isDisabled: false, isApplication: false);

        var result = await RunAsync();

        result.Success.Should().BeFalse("which business unit holds the secure records cannot be decided");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical
                                           && e.Message.Contains(nameof(SecureBuVerdict.SecureBusinessUnitAmbiguous))
                                           && e.Message.Contains(secondSecureNamed.ToString()));
        _log.Entries.Should().NotContain(e => e.Message.Contains("heartbeat status=isolated"));
    }

    /// <summary>
    /// Task 145 (clause 5): the owner role has lost Read on a codified child table (the guide's old strip did exactly
    /// this to sprk_document). The run fails and names the table — at ERROR, not CRITICAL: the gap makes secure-child
    /// writes fail closed, it exposes nothing, and the CRITICAL lines stay the ones that mean a disclosure.
    /// </summary>
    [Fact]
    public async Task Run_WhenTheOwnerRoleLacksACodifiedTable_FailsAndLogsAnErrorNamingIt_NotCritical()
    {
        _directory.RemoveDepth(OwnerRole, _codifiedChildPrivileges["sprk_todo"]);

        var result = await RunAsync();

        result.Success.Should().BeFalse("secure To Dos cannot be owned by the team");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error
                                           && e.Message.Contains(nameof(SecureBuVerdict.SecureOwnerRoleLacksCodifiedPrivilege))
                                           && e.Message.Contains("prvReadsprk_Todo"));
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Critical, "a coverage gap is not an exposure");
    }

    /// <summary>
    /// A codified table the environment does not have (not installed there, or renamed) is a clause-5 finding — and
    /// the exposure clauses are STILL graded in the same run: a user moved into the secure BU is still a CRITICAL line.
    /// Throwing instead would blind clauses 1-4 on every run in such an environment.
    /// </summary>
    [Fact]
    public async Task Run_WhenACodifiedPrivilegeDoesNotExistInTheEnvironment_ReportsIt_AndStillGradesTheExposureClauses()
    {
        _directory.RemovePrivilege(_codifiedChildPrivileges["sprk_memo"]);
        _directory.User(Guid.NewGuid(), "Moved Attorney", SecureBu, accessMode: 0, isDisabled: false, isApplication: false);

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("prvReadsprk_Memo"));
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("Moved Attorney"));
    }

    /// <summary>A missing GUARDED privilege still stops the run: clause 1 cannot be graded without it.</summary>
    [Fact]
    public async Task Run_WhenAGuardedPrivilegeDoesNotExistInTheEnvironment_ThrowsRatherThanGrading()
    {
        _directory.RemovePrivilege(PrivMatter);

        var act = () => RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*isolation is unknown*");
        _log.Entries.Should().NotContain(e => e.Message.Contains("heartbeat status=isolated"));
    }

    [Fact]
    public async Task Run_WhenTheCensusCannotBeRead_ThrowsAfterItsHeartbeat_SoTheSchedulerRetries()
    {
        _directory.FailReadsOf("teammembership");

        var act = () => RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*isolation is unknown*");
        _log.Entries.Should().Contain(e => e.Message.Contains("heartbeat status=error"),
            "an attempt that fails still leaves its heartbeat (ADR-036 A1 rule 5)");
        _log.Entries.Should().NotContain(e => e.Message.Contains("heartbeat status=isolated"));
    }

    /// <summary>
    /// Task 260 (ISS-014): the provisioning acceptance route runs <see cref="SecureRecordIsolationCensus"/> directly; the
    /// job runs it through its schedule. Same directory, same status and findings — one census, two runners.
    /// </summary>
    [Theory]
    [InlineData("isolated")]
    [InlineData("findings")]
    [InlineData("inert")]
    public async Task TheJobAndTheAcceptanceRoutesCensus_AgreeOnStatusAndFindings(string shape)
    {
        if (shape == "findings")
            _directory.User(Guid.NewGuid(), "Moved Attorney", SecureBu, accessMode: 0, isDisabled: false, isApplication: false);
        if (shape == "inert")
            _directory.RemoveBusinessUnit(SecureBu);

        var jobResult = await RunAsync();
        var direct = SecureRecordIsolationCensus.ToResult(
            await SecureRecordIsolationCensus.EvaluateAsync(Entities(), new ConfigurationBuilder().Build(), CancellationToken.None));

        using var json = System.Text.Json.JsonDocument.Parse(jobResult.ResultJson!);
        direct.Status.Should().Be(shape);
        json.RootElement.GetProperty("status").GetString().Should().Be(direct.Status);
        json.RootElement.GetProperty("verdict").GetString().Should().Be(direct.Verdict);
        json.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => (f.GetProperty("verdict").GetString(), f.GetProperty("message").GetString()))
            .Should().Equal(direct.Findings.Select(f => ((string?)f.Verdict, (string?)f.Message)));
    }

    // =====================================================================================================
    // Harness
    // =====================================================================================================

    private IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => _directory.Answer(query));
        return entities.Object;
    }

    private Task<JobRunResult> RunAsync()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => _directory.Answer(query));

        // The job resolves its Dataverse seam per run from a scope, as the sibling jobs do.
        var services = new ServiceCollection();
        services.AddSingleton(entities.Object);

        var job = new SecureRecordIsolationCensusJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), // the compiled default names
            new FakeTimeProvider(),
            _log);

        return job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-144", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    /// <summary>
    /// The directory, answering each query by entity name with the SDK's attribute types. It evaluates the one kind of
    /// condition the reader uses to narrow (<c>In</c> on privilege names and privilege ids) and serves paging.
    /// </summary>
    private sealed class Directory
    {
        private readonly Dictionary<string, List<Entity>> _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _twoPages = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _pagesRequested = new(StringComparer.OrdinalIgnoreCase);

        private List<Entity> Rows(string entity) =>
            _rows.TryGetValue(entity, out var rows) ? rows : _rows[entity] = new List<Entity>();

        public void BusinessUnit(Guid id, string name, Guid? parent)
        {
            var row = new Entity("businessunit", id) { ["businessunitid"] = id, ["name"] = name };
            if (parent is { } p) row["parentbusinessunitid"] = new EntityReference("businessunit", p);
            Rows("businessunit").Add(row);
        }

        public void RemoveBusinessUnit(Guid id) => Rows("businessunit").RemoveAll(r => r.Id == id);

        public void Privilege(Guid id, string name) =>
            Rows("privilege").Add(new Entity("privilege", id) { ["privilegeid"] = id, ["name"] = name });

        public void RemovePrivilege(Guid id) => Rows("privilege").RemoveAll(r => r.Id == id);

        public void RemoveDepth(Guid rootRole, Guid privilege) =>
            Rows("roleprivileges").RemoveAll(r => (Guid)r["roleid"] == rootRole && (Guid)r["privilegeid"] == privilege);

        public void Role(Guid id, string name, Guid businessUnit, Guid rootRole) =>
            Rows("role").Add(new Entity("role", id)
            {
                ["roleid"] = id,
                ["name"] = name,
                ["businessunitid"] = new EntityReference("businessunit", businessUnit),
                ["parentrootroleid"] = new EntityReference("role", rootRole)
            });

        public void Depth(Guid rootRole, Guid privilege, PrivilegeDepth depth) =>
            Rows("roleprivileges").Add(new Entity("roleprivileges", Guid.NewGuid())
            {
                ["roleid"] = rootRole,
                ["privilegeid"] = privilege,
                ["privilegedepthmask"] = (int)depth
            });

        public void User(Guid id, string name, Guid businessUnit, int accessMode, bool isDisabled, bool isApplication,
            params Guid[] roles)
        {
            var row = new Entity("systemuser", id)
            {
                ["systemuserid"] = id,
                ["fullname"] = name,
                ["domainname"] = name.Replace(" ", ".", StringComparison.Ordinal) + "@spaarke.com",
                ["accessmode"] = new OptionSetValue(accessMode),
                ["isdisabled"] = isDisabled,
                ["businessunitid"] = new EntityReference("businessunit", businessUnit)
            };
            if (isApplication) row["applicationid"] = Guid.NewGuid();
            Rows("systemuser").Add(row);

            foreach (var role in roles)
            {
                Rows("systemuserroles").Add(new Entity("systemuserroles", Guid.NewGuid())
                {
                    ["systemuserid"] = id,
                    ["roleid"] = role
                });
            }
        }

        public void Team(Guid id, string name, Guid businessUnit, bool isDefault, int teamType,
            Guid[]? roles = null, Guid[]? members = null)
        {
            Rows("team").Add(new Entity("team", id)
            {
                ["teamid"] = id,
                ["name"] = name,
                ["isdefault"] = isDefault,
                ["teamtype"] = new OptionSetValue(teamType),
                ["businessunitid"] = new EntityReference("businessunit", businessUnit)
            });

            foreach (var role in roles ?? Array.Empty<Guid>())
            {
                Rows("teamroles").Add(new Entity("teamroles", Guid.NewGuid()) { ["teamid"] = id, ["roleid"] = role });
            }

            foreach (var member in members ?? Array.Empty<Guid>())
            {
                AddMember(id, member);
            }
        }

        public void AddMember(Guid team, Guid user) =>
            Rows("teammembership").Add(new Entity("teammembership", Guid.NewGuid())
            {
                ["teamid"] = team,
                ["systemuserid"] = user
            });

        public void SplitIntoTwoPages(string entity) => _twoPages.Add(entity);

        public void FailReadsOf(string entity) => _failing.Add(entity);

        public int PagesRequested(string entity) => _pagesRequested.TryGetValue(entity, out var n) ? n : 0;

        public EntityCollection Answer(QueryExpression query)
        {
            var entity = query.EntityName;
            if (_failing.Contains(entity))
            {
                throw new InvalidOperationException($"Test: {entity} is not readable.");
            }

            _pagesRequested[entity] = PagesRequested(entity) + 1;

            IEnumerable<Entity> rows = Rows(entity);
            foreach (var condition in query.Criteria.Conditions.Where(c => c.Operator == ConditionOperator.In))
            {
                var allowed = condition.Values.ToHashSet();
                rows = rows.Where(r => r.Attributes.TryGetValue(condition.AttributeName, out var v) && allowed.Contains(v));
            }

            var all = rows.ToList();
            if (!_twoPages.Contains(entity) || all.Count < 2)
            {
                return new EntityCollection(all) { MoreRecords = false };
            }

            // Two pages: the LAST row arrives only on page 2.
            var firstPage = query.PageInfo?.PageNumber is null or 1;
            return firstPage
                ? new EntityCollection(all.Take(all.Count - 1).ToList()) { MoreRecords = true, PagingCookie = "page-1" }
                : new EntityCollection(all.Skip(all.Count - 1).ToList()) { MoreRecords = false };
        }
    }

    /// <summary>Captures every log line the job writes.</summary>
    private sealed class CapturingLogger : ILogger<SecureRecordIsolationCensusJob>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _entries.Add((logLevel, formatter(state, exception)));
    }
}
