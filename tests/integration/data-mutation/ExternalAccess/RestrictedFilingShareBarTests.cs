using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 198 Part D: what a message's participants keep after it is filed to a record, driven over the
/// REAL <see cref="SecureChildReconciler"/> (its <c>AfterRefileAsync</c>, the step every re-file writer runs), the REAL
/// synchronizer and one in-memory Dataverse (<see cref="SecureChildShareWorld"/>).
/// <list type="bullet">
/// <item>An ORDINARY target: every participant keeps their shares on the message and its content rows.</item>
/// <item>A SECURE target: the message's shares follow the synchronizer (the root's set), as before.</item>
/// <item>A RESTRICTED target (its own value, or inherited through its filing): every user flagged external loses their shares on
/// the message and its content rows (attachment, participant, email review log, document); an internal user, and a user whose
/// flag is blank, keeps them.</item>
/// <item>Fail closed: a user-flag read that faults removes nothing; a user the read does not return is left as is.</item>
/// </list>
/// </summary>
public class RestrictedFilingShareBarTests
{
    private const int Read = 1;

    private static readonly Guid External = Guid.Parse("e0000198-0000-4000-8000-0000000000e1");
    private static readonly Guid Internal = Guid.Parse("e0000198-0000-4000-8000-0000000000a1");
    private static readonly Guid BlankFlag = Guid.Parse("e0000198-0000-4000-8000-0000000000b1");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserDirectory _users = new();

    public RestrictedFilingShareBarTests()
    {
        _users.Flags[External] = true;
        _users.Flags[Internal] = false;
        _users.Flags[BlankFlag] = null;
    }

    private SecureChildReconciler Reconciler() => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _users);

    private Task RefileAsync(Guid message) =>
        Reconciler().AfterRefileAsync("sprk_communication", message, () => Task.FromResult(false), CancellationToken.None);

    /// <summary>A message filed through <paramref name="filing"/> with one attachment, participant, review log and document row.</summary>
    private (Guid Message, IReadOnlyList<(string Table, Guid Id)> Rows) Message(params (string Column, string Target, Guid Id)[] filing)
    {
        var (message, attachment, participant, reviewLog, document) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryChild("sprk_communication", message, filing)
            .OrdinaryChild("sprk_communicationattachment", attachment, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_communicationparticipant", participant, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_emailreviewlog", reviewLog, ("sprk_communication", "sprk_communication", message))
            .OrdinaryChild("sprk_document", document, ("sprk_relatedcommunication", "sprk_communication", message));
        return (message, new[]
        {
            ("sprk_communication", message), ("sprk_communicationattachment", attachment),
            ("sprk_communicationparticipant", participant), ("sprk_emailreviewlog", reviewLog), ("sprk_document", document),
        });
    }

    private void ShareWith(IEnumerable<(string Table, Guid Id)> rows, params Guid[] users)
    {
        foreach (var (table, id) in rows)
        foreach (var user in users)
            _shares.Seed(table, id, DataversePrincipalRef.User(user), Read);
    }

    private Guid Matter(int? accessPermission)
    {
        var matter = Guid.NewGuid();
        _world.OrdinaryRoot("sprk_matter", matter);
        if (accessPermission is { } value)
            _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(value));
        return matter;
    }

    private bool Holds(string table, Guid id, Guid user) => _shares.MaskOf(table, id, DataversePrincipalRef.User(user)) is > 0;

    [Fact(DisplayName = "198 D pin (must-not-fire): filed to an ORDINARY record, every participant keeps the message and content-row shares")]
    public async Task FiledToAnOrdinaryRecord_EveryParticipantKeepsTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, External, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        foreach (var user in new[] { External, Internal, BlankFlag })
            Holds(table, id, user).Should().BeTrue($"{table} is filed under an ordinary record");
        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        _users.Queries.Should().BeEmpty("a target that is not Restricted never reads the users");
    }

    [Fact(DisplayName = "198 D must-fire: filed to a RESTRICTED record, an external participant loses every share, content rows included")]
    public async Task FiledToARestrictedRecord_AnExternalParticipantLosesTheirShares_ContentRowsIncluded()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, External, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"{table} is filed under a Restricted record and the user is flagged external");
            Holds(table, id, Internal).Should().BeTrue($"an internal user keeps {table}");
            Holds(table, id, BlankFlag).Should().BeTrue($"a blank flag is internal (owner round 67), so {table} is kept");
        }
    }

    [Fact(DisplayName = "198 D must-fire: a target Restricted by INHERITANCE (an event under a Restricted matter) bars the external participant too")]
    public async Task FiledToARecordThatInheritsRestricted_AnExternalParticipantLosesTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var evt = Guid.NewGuid();
        _world.OrdinaryChild("sprk_event", evt, ("sprk_regardingmatter", "sprk_matter", matter));
        var (message, rows) = Message(("sprk_regardingevent", "sprk_event", evt));
        ShareWith(rows, External, Internal);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, External).Should().BeFalse($"{table} inherits Restricted through the event");
            Holds(table, id, Internal).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 D must-not-fire: filed to a Restricted record, internal and blank-flag participants keep everything")]
    public async Task FiledToARestrictedRecord_InternalParticipantsKeepTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, Internal, BlankFlag);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, Internal).Should().BeTrue();
            Holds(table, id, BlankFlag).Should().BeTrue();
        }

        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact(DisplayName = "198 D fail closed: when the users cannot be read, nothing is removed")]
    public async Task FiledToARestrictedRecord_WhenTheUserFlagsCannotBeRead_NothingIsRemoved()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        ShareWith(rows, External, Internal);
        _users.Faults = true;

        await RefileAsync(message);

        foreach (var (table, id) in rows)
            Holds(table, id, External).Should().BeTrue("a fault is unknown, and unknown never removes (nor grants)");
        _shares.WriteLog.Where(w => w.Action.StartsWith("Revoke", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact(DisplayName = "198 D fail closed: a user the read does not return is left as is; the external one still loses theirs")]
    public async Task FiledToARestrictedRecord_AnUnreadUserIsLeft_TheExternalOneStillLosesTheirShares()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(("sprk_regardingmatter", "sprk_matter", matter));
        var unread = Guid.NewGuid(); // not in the directory
        ShareWith(rows, External, unread);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, unread).Should().BeTrue("an unreadable user is logged and left for the next pass, never decided");
            Holds(table, id, External).Should().BeFalse();
        }
    }

    [Fact(DisplayName = "198 D pin: filed to a SECURE record, the message's shares follow the synchronizer (the root's set)")]
    public async Task FiledToASecureRecord_TheMessageSharesFollowTheSynchronizer()
    {
        var secure = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", secure);
        _shares.Seed("sprk_matter", secure, DataversePrincipalRef.User(Internal), Read);
        var message = Guid.NewGuid();
        _world.SecureChild("sprk_communication", message, ("sprk_regardingmatter", "sprk_matter", secure));
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Internal), Read);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(BlankFlag), Read);

        await RefileAsync(message);

        Holds("sprk_communication", message, Internal).Should().BeTrue("the root shares it");
        Holds("sprk_communication", message, BlankFlag).Should().BeFalse("the root does not share it: the mirror takes it off");
        _users.Queries.Should().BeEmpty("a Secure target that is not Restricted is the synchronizer's alone");
    }

    /// <summary>The <c>systemusers</c> flag read (<c>sprk_isexternal</c>); every other Web API read answers nothing.</summary>
    private sealed class UserDirectory : DataverseWebApiClient
    {
        private static readonly Regex UserId = new(@"systemuserid eq ([0-9a-fA-F-]{36})");

        public UserDirectory()
            : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(), NullLogger<DataverseWebApiClient>.Instance, new NoCredential())
        {
        }

        /// <summary>Each user's stored flag (<c>null</c> = blank); a user absent here is not returned.</summary>
        public Dictionary<Guid, bool?> Flags { get; } = new();

        public bool Faults { get; set; }

        public List<string> Queries { get; } = new();

        public override Task<List<T>> QueryAsync<T>(
            string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
            CancellationToken cancellationToken = default)
        {
            if (entitySetName != "systemusers")
                return Task.FromResult(new List<T>());

            Queries.Add(filter ?? string.Empty);
            if (Faults)
                throw new HttpRequestException("Test: the users could not be read.");

            var rows = UserId.Matches(filter ?? string.Empty)
                .Select(m => Guid.Parse(m.Groups[1].Value))
                .Where(Flags.ContainsKey)
                .Select(id => new Dictionary<string, object?> { ["systemuserid"] = id, ["sprk_isexternal"] = Flags[id] })
                .ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        private sealed class NoCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");
        }
    }
}
