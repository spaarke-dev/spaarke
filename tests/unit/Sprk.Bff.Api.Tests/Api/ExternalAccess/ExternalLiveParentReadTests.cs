using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 187 (owner round 99 "Live-parent check (precise)", D-123; #1857): an external child-row read
/// decides from {stamped roots} ∪ {LIVE roots of every parent the row is filed under}, not from the copied stamp alone.
/// </summary>
/// <remarks>
/// <para>Driven through the REAL routes and the REAL <see cref="ExternalDataService"/> over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>: the HTTP side projects through <c>$select</c> (a list that does not
/// select a parent lookup reads it as "not filed" and the withholding tests fail), and the filing world answers the
/// live-parent walk (<c>ParentLineageWalk.TopsOfManyAsync</c>) by the id conditions it sends.</para>
/// <para>World: the caller holds Read on ordinary matter <see cref="M"/> (the route) and nothing on secure matter
/// <see cref="X"/>. A communication <see cref="C"/>, an invoice <see cref="I"/> or an event <see cref="E"/> is a parent.</para>
/// </remarks>
[Trait("category", "external-access")]
[Trait("status", "repaired")]
public sealed class ExternalLiveParentReadTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999187");
    private static readonly Guid M = Guid.Parse("a1870000-0000-4000-8000-00000000000a");
    private static readonly Guid X = Guid.Parse("a1870000-0000-4000-8000-0000000000dd");
    private static readonly Guid O = Guid.Parse("a1870000-0000-4000-8000-0000000000ee");
    private static readonly Guid C = Guid.Parse("c1870000-0000-4000-8000-000000000001");
    private static readonly Guid I = Guid.Parse("c1870000-0000-4000-8000-000000000002");
    private static readonly Guid E = Guid.Parse("c1870000-0000-4000-8000-000000000003");

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalLiveParentReadTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private ExternalRootEventReadTests.FakeDataverse Data => _fixture.Data;

    // =====================================================================
    // AC1 — carrier (permanent): a to-do stamped ordinary M whose sprk_regardingcommunication is a communication under
    // secure X. SecureChildReconciler put it behind X; the external list must too.
    // =====================================================================

    [Fact]
    public async Task CarrierTodo_UnderASecureRootThroughItsCommunication_IsWithheld_FromAnMOnlyReader()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var plain = Data.AddTodo("matters", M, "Plain");
        var carrier = Data.AddTodo("matters", M, "Carrier");
        Data.SetColumn("sprk_todos", "sprk_todoid", carrier, "_sprk_regardingcommunication_value", C.ToString());

        var body = await Todos(M);

        body.Value.Select(t => t.SprkTodoid).Should().Equal(plain);
        body.Truncated.Should().BeFalse("a to-do withheld for its secure live root is not a truncated list");
    }

    [Fact]
    public async Task CarrierTodo_IsListed_ToACallerWhoReadsMAndX()
    {
        _fixture.Principal = Holding(matters: new[] { M, X });
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var carrier = Data.AddTodo("matters", M, "Carrier");
        Data.SetColumn("sprk_todos", "sprk_todoid", carrier, "_sprk_regardingcommunication_value", C.ToString());

        var body = await Todos(M);

        body.Value.Select(t => t.SprkTodoid).Should().Equal(carrier);
        Data.FlagReads.Should().BeEmpty("a live root the caller can read never needs its secure flag");
    }

    // =====================================================================
    // AC2 — stale copy (transient): the stamp says M while the source intermediate's LIVE root is secure X.
    // =====================================================================

    [Fact]
    public async Task StaleTodo_WhoseInvoiceNowBelongsToSecureX_IsWithheld()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_invoice", I, ("sprk_matter", "sprk_matter", X)); // re-filed outside the BFF; the copy says M
        var stale = Data.AddTodo("matters", M, "Stale");
        Data.SetColumn("sprk_todos", "sprk_todoid", stale, "_sprk_regardinginvoice_value", I.ToString());

        var body = await Todos(M);

        body.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleEvent_WhoseInvoiceNowBelongsToSecureX_IsWithheld()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_invoice", I, ("sprk_matter", "sprk_matter", X));
        var plain = Data.Add("matters", M, "Plain");
        var stale = Data.Add("matters", M, "Stale");
        Data.SetColumn("sprk_events", "sprk_eventid", stale, "_sprk_regardinginvoice_value", I.ToString());

        var body = await Events(M);

        body.Value.Select(e => e.SprkEventid).Should().Equal(plain);
    }

    [Fact]
    public async Task CarrierEvent_ThroughItsCommunication_IsListed_OnlyToACallerWhoReadsX()
    {
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var carrier = Data.Add("matters", M, "Carrier");
        Data.SetColumn("sprk_events", "sprk_eventid", carrier, "_sprk_regardingcommunication_value", C.ToString());

        _fixture.Principal = Holding(matters: new[] { M });
        (await Events(M)).Value.Should().BeEmpty();

        _fixture.Principal = Holding(matters: new[] { M, X });
        (await Events(M)).Value.Select(e => e.SprkEventid).Should().Equal(carrier);
    }

    // =====================================================================
    // AC3 — no over-hiding: ordinary parents list exactly as before.
    // =====================================================================

    [Fact]
    public async Task ATodoAndEvent_WhoseLiveParentsAreOrdinary_ListAsBefore_EvenUnderARootTheCallerCannotRead()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddOpenRoot(ExternalGrantRootType.Matter, O); // ordinary, and the caller holds nothing on it
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", O));
        Data.AddFilingRow("sprk_invoice", I, ("sprk_matter", "sprk_matter", M));
        var underOrdinary = Data.AddTodo("matters", M, "Under ordinary O");
        Data.SetColumn("sprk_todos", "sprk_todoid", underOrdinary, "_sprk_regardingcommunication_value", C.ToString());
        var underRoute = Data.AddTodo("matters", M, "Under M itself");
        Data.SetColumn("sprk_todos", "sprk_todoid", underRoute, "_sprk_regardinginvoice_value", I.ToString());
        var eventUnderOrdinary = Data.Add("matters", M, "Event under ordinary O");
        Data.SetColumn("sprk_events", "sprk_eventid", eventUnderOrdinary, "_sprk_regardingcommunication_value", C.ToString());

        (await Todos(M)).Value.Select(t => t.SprkTodoid).Should().BeEquivalentTo(new[] { underOrdinary, underRoute });
        (await Events(M)).Value.Select(e => e.SprkEventid).Should().Equal(eventUnderOrdinary);
    }

    [Fact]
    public async Task APageWithNoParentLookups_ReadsNoFilingAtAll()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddTodo("matters", M, "One");
        Data.Add("matters", M, "Event");

        (await Todos(M)).Value.Should().HaveCount(1);
        (await Events(M)).Value.Should().HaveCount(1);
        Data.FilingReadLog.Should().BeEmpty("a row naming no parent needs no live-parent read");
    }

    // =====================================================================
    // Query cost: ONE batched read per parent table per walk level for the whole page; the roots are never read by the walk.
    // =====================================================================

    [Fact]
    public async Task ThreeTodosUnderThreeCommunications_CostOneCommunicationQuery_AndNoRootRead()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        for (var n = 0; n < 3; n++)
        {
            var communication = Guid.NewGuid();
            Data.AddFilingRow("sprk_communication", communication, ("sprk_regardingmatter", "sprk_matter", O));
            var todo = Data.AddTodo("matters", M, $"To-do {n}");
            Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", communication.ToString());
        }

        (await Todos(M)).Value.Should().HaveCount(3);

        Data.FilingReadLog.Should().Equal(new[] { "sprk_communication" },
            "the live-parent walk reads each parent table once per level, and a root ends the climb without a read");
        Data.FlagReads.Should().ContainSingle("the live roots join the ONE secure-flag read");
    }

    [Fact]
    public async Task ATwoLevelFiling_IsFollowed_CommunicationThenInvoice()
    {
        // The communication's own stamp says ordinary O, but it regards invoice I, which belongs to secure X: the walk climbs.
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", O), ("sprk_regardinginvoice", "sprk_invoice", I));
        Data.AddFilingRow("sprk_invoice", I, ("sprk_matter", "sprk_matter", X));
        var todo = Data.AddTodo("matters", M, "Two levels up");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());

        (await Todos(M)).Value.Should().BeEmpty();
        Data.FilingReadLog.Should().Equal("sprk_communication", "sprk_invoice");
    }

    // =====================================================================
    // AC7 — fail closed: a parent read that throws, a parent that cannot be resolved, and the walk bound withhold the row.
    // =====================================================================

    [Theory]
    [InlineData("throws")]
    [InlineData("missing")]
    [InlineData("too-deep")]
    public async Task ATodoWhoseParentCannotBeResolved_IsWithheld_AndTheListSaysTruncated(string failure)
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        var plain = Data.AddTodo("matters", M, "Plain");
        var todo = Data.AddTodo("matters", M, "Unresolvable parent");
        switch (failure)
        {
            case "throws":
                Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", O));
                Data.FilingFaultEntity = "sprk_communication";
                Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());
                break;
            case "missing":
                // C is named by the to-do but the walk's read does not return it (deleted, or invisible to the app identity).
                Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());
                break;
            case "too-deep":
                // event -> event -> ... seven levels before a root: past ParentLineageWalk.MaxDepth (6).
                var chain = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
                for (var n = 0; n < chain.Length; n++)
                {
                    if (n + 1 < chain.Length)
                        Data.AddFilingRow("sprk_event", chain[n], ("sprk_regardingevent", "sprk_event", chain[n + 1]));
                    else
                        Data.AddFilingRow("sprk_event", chain[n], ("sprk_regardingmatter", "sprk_matter", O));
                }

                Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingevent_value", chain[0].ToString());
                break;
        }

        var body = await Todos(M);

        body.Value.Select(t => t.SprkTodoid).Should().Equal(plain);
        body.Truncated.Should().BeTrue("the row was withheld only because its live parent could not be decided");
    }

    [Fact]
    public async Task AParentLookupThatIsNotAGuid_IsWithheld()
    {
        _fixture.Principal = Holding(matters: new[] { M });
        var todo = Data.AddTodo("matters", M, "Garbled parent");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", "not-a-guid");

        (await Todos(M)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task WithNoFilingReader_ATodoNamingAParent_IsWithheld_AndOneNamingNone_IsListed()
    {
        var service = Data.NewService(withFilingReader: false);
        var caller = Holding(matters: new[] { M });
        var plain = Data.AddTodo("matters", M, "Plain");
        var todo = Data.AddTodo("matters", M, "Parent");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());

        var body = await service.GetTodosAsync(ExternalDataService.TodoRootKind.Matter, M, caller);

        body.Value.Select(t => t.SprkTodoid).Should().Equal(plain);
        body.Truncated.Should().BeTrue();
    }

    // =====================================================================
    // AC4 — PATCH and DELETE on a to-do (and DELETE on an event) whose live parent is a secure root the caller cannot read:
    // refused with the existing deny shape, and nothing is written.
    // =====================================================================

    [Fact]
    public async Task PatchTodo_WhoseLiveParentIsUnderSecureX_Is403_AndWritesNothing()
    {
        _fixture.Principal = Holding(matters: new[] { M }, rights: AccessRights.Read | AccessRights.Write);
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var todo = Data.AddTodo("matters", M, "Carrier");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.insufficient_rights");
        Data.Requests.Should().NotContain(r => r.StartsWith("PATCH"), "nothing is written");
    }

    [Fact]
    public async Task PatchTodo_WhoseLiveParentsAreReadable_IsApplied()
    {
        _fixture.Principal = Holding(matters: new[] { M, X }, rights: AccessRights.Read | AccessRights.Write);
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var todo = Data.AddTodo("matters", M, "Carrier");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Data.Requests.Should().Contain(r => r.StartsWith("PATCH"));
    }

    [Fact]
    public async Task PatchTodo_WhoseLiveParentCannotBeRead_Is403_FailClosed()
    {
        _fixture.Principal = Holding(matters: new[] { M }, rights: AccessRights.Read | AccessRights.Write);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", M));
        Data.FilingReadThrows = true;
        var todo = Data.AddTodo("matters", M, "Unreadable parent");
        Data.SetColumn("sprk_todos", "sprk_todoid", todo, "_sprk_regardingcommunication_value", C.ToString());

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Data.Requests.Should().NotContain(r => r.StartsWith("PATCH"));
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("events")]
    public async Task DeleteOwn_WhoseLiveParentIsUnderSecureX_Is403_AndDeletesNothing(string kind)
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", X));
        var id = SeedOwnRow(kind, "Carrier");
        SetParent(kind, id, "_sprk_regardingcommunication_value", C);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.DeleteAsync($"/api/v1/external/{kind}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.delete_own");
        Data.Requests.Should().NotContain(r => r.StartsWith("DELETE"), "nothing is deleted");
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("events")]
    public async Task DeleteOwn_WhoseLiveParentCannotBeRead_Is503_AndDeletesNothing(string kind)
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", M));
        Data.FilingReadThrows = true;
        var id = SeedOwnRow(kind, "Unreadable parent");
        SetParent(kind, id, "_sprk_regardingcommunication_value", C);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.DeleteAsync($"/api/v1/external/{kind}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Data.Requests.Should().NotContain(r => r.StartsWith("DELETE"));
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("events")]
    public async Task DeleteOwn_WhoseLiveParentsAreOrdinary_Deletes(string kind)
    {
        _fixture.Principal = Holding(matters: new[] { M });
        Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        Data.AddFilingRow("sprk_communication", C, ("sprk_regardingmatter", "sprk_matter", O));
        var id = SeedOwnRow(kind, "Ordinary parent");
        SetParent(kind, id, "_sprk_regardingcommunication_value", C);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.DeleteAsync($"/api/v1/external/{kind}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Data.Requests.Should().Contain(r => r.StartsWith("DELETE"));
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<ExternalCollectionResponse<ExternalTodoDto>> Todos(Guid matter)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/matters/{matter}/todos"))!;
    }

    private async Task<ExternalCollectionResponse<ExternalEventDto>> Events(Guid matter)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>($"/api/v1/external/matters/{matter}/events"))!;
    }

    /// <summary>A to-do or event under M, stamped as created by the caller (the delete-own stamp half).</summary>
    private string SeedOwnRow(string kind, string name)
    {
        var id = kind == "todos" ? Data.AddTodo("matters", M, name) : Data.Add("matters", M, name);
        Data.SetColumn(SetOf(kind), IdOf(kind), id, "_sprk_createdbycontact_value", CallerContact.ToString());
        return id;
    }

    private void SetParent(string kind, string id, string column, Guid parent) =>
        Data.SetColumn(SetOf(kind), IdOf(kind), id, column, parent.ToString());

    private static string SetOf(string kind) => kind == "todos" ? "sprk_todos" : "sprk_events";

    private static string IdOf(string kind) => kind == "todos" ? "sprk_todoid" : "sprk_eventid";

    private static CallerPrincipal Holding(Guid[] matters, AccessRights rights = AccessRights.Read) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters.ToDictionary(m => m, _ => rights),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}
