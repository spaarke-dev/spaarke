using System.Net;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b F3b fix round 1, step 2 (F1), the REST plane: a row with its own invoice link (an event, a to-do, a document)
/// whose own link is null but which is filed under an invoice-linked row follows that row's invoice, through the REAL routes. An event
/// under an event, a to-do under an event, a to-do under a service request and a document under a communication
/// (<c>sprk_relatedcommunication</c>) are hidden on the list (and refused on the single-record write routes) when the row above links to
/// another firm's invoice, and shown when it links to the caller's own firm's invoice or to none. The walk's bound is read from the
/// constant the walk uses (<see cref="WalkBound"/>), never written as a number. The assistant plane: the <c>F3b_F1_</c> tests in
/// <c>DataverseReadQueryHandlerTests</c> and <c>DataverseSearchDataHandlerTests</c>. An analysis under a document has no REST route.
/// </summary>
/// <remarks>The REAL routes and <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/> (every read
/// projected through its <c>$select</c>; a row above is seeded in the filing world, which the by-key read answers too), with the REAL
/// caller-aware invoice seam over <see cref="InvoiceWorld"/>.</remarks>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedFamilyRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000003a");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b4");
    private static readonly Guid OpenThread = Guid.Parse("13000000-0000-4000-8000-0000000000c4");

    /// <summary>The hop bound the invoice walk reads (<see cref="InvoiceLinks"/>). Every bound test builds its chain from this, so a change
    /// to the walk's constant is one line here.</summary>
    private static int WalkBound => InvoiceLinks.MaxWalkHops;

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedFamilyRouteTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = CallerOn(Matter);
        _own = _world.OwnedInvoice();
        _other = _world.OwnedInvoice();
        _world.InvoiceFirm(_other, OtherFirm);
        _fixture.Data.AddInvoice(_own, Matter, null);
        _fixture.Data.AddInvoice(_other, Matter, null);
        _fixture.Data.AddThread(OpenThread, 100000000);
        // The live-parent walk reads a communication's thread app-only (fail closed when it does not come back): filed under nothing.
        _fixture.Data.AddFilingRow("sprk_communicationthread", OpenThread);
        _fixture.InvoiceHiding = InvoiceHidingStub.Real(_world);
    }

    /// <summary>Each family: the listed child table and the edge to the row above it.</summary>
    public static TheoryData<string> Families => new() { "event-event", "todo-event", "todo-servicerequest", "document-communication" };

    private static (string ChildTable, string ParentTable, string Column) Shape(string family) => family switch
    {
        "event-event" => ("sprk_event", "sprk_event", "sprk_regardingevent"),
        "todo-event" => ("sprk_todo", "sprk_event", "sprk_regardingevent"),
        "todo-servicerequest" => ("sprk_todo", "sprk_servicerequest", "sprk_regardingservicerequest"),
        "document-communication" => ("sprk_document", "sprk_communication", "sprk_relatedcommunication"),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown family (test)."),
    };

    // =====================================================================
    // Seeding
    // =====================================================================

    /// <summary>
    /// A row above a child, in the filing world (the by-key read answers it as a Web API row): filed under the matter through its own core
    /// lookup (a service request is filed under none), optionally under <paramref name="above"/>, and linked to <paramref name="invoice"/>.
    /// A communication is a shareable message on an Open thread, so the document rules let its documents through.
    /// </summary>
    private Guid RowAbove(string table, Guid? invoice, (string Column, string Target, Guid Id)? above = null)
    {
        var id = Guid.NewGuid();
        var lookups = new List<(string Column, string Target, Guid Id)>();
        if (table != "sprk_servicerequest")
            lookups.Add((table == "sprk_document" ? "sprk_matter" : "sprk_regardingmatter", "sprk_matter", Matter));
        if (above is { } a)
            lookups.Add(a);
        if (invoice is { } i)
            lookups.Add((InvoiceLinks.LinkColumnsOf(table)[0], "sprk_invoice", i));
        _fixture.Data.AddFilingRow(table, id, lookups.ToArray());
        if (table == "sprk_communication")
        {
            _fixture.Data.AddCommunication(id, internalOnly: false, thread: OpenThread);
            _fixture.Data.AddFilingRow("sprk_communication", id, lookups.Append(("sprk_communicationthread", "sprk_communicationthread", OpenThread)).ToArray());
            if (invoice is { } link)
                _fixture.Data.SetColumn("sprk_communications", "sprk_communicationid", id.ToString(), "_sprk_regardinginvoice_value", link.ToString());
        }

        return id;
    }

    /// <summary>A chain of <paramref name="length"/> events (each regarding the next); the TOP one links to <paramref name="topInvoice"/>.
    /// Returns the first (hop 1).</summary>
    private Guid EventChain(int length, Guid? topInvoice)
    {
        Guid? above = null;
        for (var i = length; i >= 1; i--)
            above = RowAbove("sprk_event", i == length ? topInvoice : null,
                above is { } a ? ("sprk_regardingevent", "sprk_event", a) : null);
        return above!.Value;
    }

    /// <summary>A child of <paramref name="family"/> filed to the matter, its own invoice link null, under <paramref name="parent"/>.</summary>
    private string Child(string family, string name, Guid? parent)
    {
        var (table, _, column) = Shape(family);
        var id = table switch
        {
            "sprk_event" => _fixture.Data.Add("matters", Matter, name),
            "sprk_todo" => _fixture.Data.AddTodo("matters", Matter, name),
            _ => _fixture.Data.AddDocument("matters", Matter, name),
        };
        if (parent is { } p)
            _fixture.Data.SetColumn(SetOf(table), $"{table}id", id, $"_{column}_value", p.ToString());
        return id;
    }

    private static string SetOf(string table) => table switch
    {
        "sprk_event" => "sprk_events",
        "sprk_todo" => "sprk_todos",
        _ => "sprk_documents",
    };

    private static string Segment(string table) => table switch
    {
        "sprk_event" => "events",
        "sprk_todo" => "todos",
        _ => "documents",
    };

    private async Task<(IReadOnlyList<string?> Names, bool Truncated)> ListAsync(string childTable)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/{Segment(childTable)}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        var names = doc.RootElement.GetProperty("value").EnumerateArray().Select(v => v.GetProperty("sprk_name").GetString()).ToList();
        var truncated = doc.RootElement.TryGetProperty("truncated", out var t) && t.ValueKind == JsonValueKind.True;
        return (names, truncated);
    }

    private async Task<string> DeleteAsync(string childTable, string id)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.DeleteAsync($"/api/v1/external/{Segment(childTable)}/{id}");
        return await Summarise(response);
    }

    /// <summary>A response as a caller sees it: status, and for a refusal its problem fields (no per-request fields).</summary>
    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode < 400 || string.IsNullOrWhiteSpace(body))
            return $"{(int)response.StatusCode}";
        using var doc = JsonDocument.Parse(body);
        return string.Join("|", new[] { $"{(int)response.StatusCode}" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    // =====================================================================
    // Lists: must fire / must not fire, per family
    // =====================================================================

    [Theory(DisplayName = "F3b fix round 1 REST list (must fire / must not fire): a child whose own invoice link is null is hidden when the row above links to another firm's invoice; shown when it links to the caller's own firm's invoice or to none")]
    [MemberData(nameof(Families))]
    public async Task List_AChildFollowsTheInvoiceOfTheRowAbove(string family)
    {
        var (childTable, parentTable, _) = Shape(family);
        Child(family, "Under other firm's", RowAbove(parentTable, _other));
        Child(family, "Under own firm's", RowAbove(parentTable, _own));
        Child(family, "Under unlinked", RowAbove(parentTable, null));
        Child(family, "Not filed under anything", null);

        var (names, truncated) = await ListAsync(childTable);

        names.Should().BeEquivalentTo("Under own firm's", "Under unlinked", "Not filed under anything");
        truncated.Should().BeFalse("a row the firm rule hides is an answer, not a read failure");
    }

    // =====================================================================
    // Single-record routes (delete-own): the uniform refusal
    // =====================================================================

    public static TheoryData<string> DeletableFamilies => new() { "event-event", "todo-event", "todo-servicerequest" };

    [Theory(DisplayName = "F3b fix round 1 REST single record (must fire): delete-own of a child under a row linked to another firm's invoice is the uniform refusal (the answer for an id that does not exist), never a 5xx")]
    [MemberData(nameof(DeletableFamilies))]
    public async Task Delete_AChildUnderAnotherFirmsRow_IsTheUniformRefusal(string family)
    {
        var (childTable, parentTable, _) = Shape(family);
        var missing = await DeleteAsync(childTable, Guid.NewGuid().ToString());
        var id = Child(family, "Under other firm's", RowAbove(parentTable, _other));

        var answer = await DeleteAsync(childTable, id);

        answer.Should().Be(missing);
        _fixture.Data.Requests.Should().NotContain(r => r.StartsWith("DELETE", StringComparison.Ordinal), "nothing is deleted");
    }

    [Theory(DisplayName = "F3b fix round 1 REST single record (must not fire): delete-own of a child under a row linked to the caller's own firm's invoice, or to none, is allowed")]
    [MemberData(nameof(DeletableFamilies))]
    public async Task Delete_AChildUnderAVisibleOrUnlinkedRow_IsAllowed(string family)
    {
        var (childTable, parentTable, _) = Shape(family);
        var underOwn = Child(family, "Under own firm's", RowAbove(parentTable, _own));
        var underUnlinked = Child(family, "Under unlinked", RowAbove(parentTable, null));

        (await DeleteAsync(childTable, underOwn)).Should().StartWith("2");
        (await DeleteAsync(childTable, underUnlinked)).Should().StartWith("2");
    }

    // =====================================================================
    // The bound (built from the walk's constant)
    // =====================================================================

    [Theory(DisplayName = "F3b fix round 1 REST list (bound): a to-do under a chain of events as long as the bound is decided by the top; one hop past the bound is hidden, even for the caller's own firm's invoice")]
    [InlineData(0, true, false)]  // at the bound, top is another firm's: hidden
    [InlineData(0, false, true)]  // at the bound, top is the caller's own firm's: shown
    [InlineData(1, false, false)] // one past the bound: hidden although the top invoice is visible
    public async Task List_TheChainIsBounded(int pastTheBound, bool topIsOtherFirms, bool shown)
    {
        Child("todo-event", "Chained", EventChain(WalkBound + pastTheBound, topIsOtherFirms ? _other : _own));
        Child("todo-event", "Not filed under anything", null);

        var (names, truncated) = await ListAsync("sprk_todo");

        names.Contains("Chained").Should().Be(shown);
        names.Should().Contain("Not filed under anything");
        // EB-D-215 (ruling D-70): a row hidden because its chain is past the bound is a fault on a list, so the list says truncated;
        // a row decided within the bound (shown, or hidden by another firm's invoice) is an answer and does not.
        truncated.Should().Be(pastTheBound > 0);
    }

    // =====================================================================
    // EB-D-215: a row hidden because its chain is undecidable says truncated
    // =====================================================================

    [Theory(DisplayName = "EB-D-215 REST list (must fire): a to-do hidden because its chain is undecidable (a cycle, an unreadable link, a row above that does not come back) makes the list say truncated; a decided sibling is listed")]
    [InlineData("cycle")]
    [InlineData("unreadable-link")]
    [InlineData("row-above-missing")]
    public async Task List_AnUndecidableChain_SaysTruncated(string kind)
    {
        switch (kind)
        {
            case "cycle":
                // Two events, each regarding the other, both filed under the matter.
                var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
                _fixture.Data.AddFilingRow("sprk_event", first,
                    ("sprk_regardingmatter", "sprk_matter", Matter), ("sprk_regardingevent", "sprk_event", second));
                _fixture.Data.AddFilingRow("sprk_event", second,
                    ("sprk_regardingmatter", "sprk_matter", Matter), ("sprk_regardingevent", "sprk_event", first));
                Child("todo-event", "Undecidable", first);
                break;
            case "unreadable-link":
                // The service request edge: only the invoice walk reads it (a service request is outside the Secure lineage map, so
                // the task 187 live-parent rule does not decide this row first).
                var garbled = Child("todo-servicerequest", "Undecidable", null);
                _fixture.Data.SetColumn("sprk_todos", "sprk_todoid", garbled, "_sprk_regardingservicerequest_value", "not-a-guid");
                break;
            default:
                Child("todo-event", "Undecidable", Guid.NewGuid());
                break;
        }

        Child("todo-event", "Under own firm's", RowAbove("sprk_event", _own));

        var (names, truncated) = await ListAsync("sprk_todo");

        names.Should().Equal("Under own firm's");
        truncated.Should().BeTrue("a row the walk could not decide is a fault on a list, not an answer");
    }

    [Fact(DisplayName = "EB-D-215 REST list (must not fire): a list whose rows are all decided - hidden by another firm's invoice, shown, or filed under nothing - does not say truncated")]
    public async Task List_WithNoUndecidableRow_DoesNotSayTruncated()
    {
        Child("todo-event", "Under other firm's", EventChain(WalkBound, _other));
        Child("todo-event", "Under own firm's", EventChain(WalkBound, _own));
        Child("todo-event", "Not filed under anything", null);

        var (names, truncated) = await ListAsync("sprk_todo");

        names.Should().BeEquivalentTo("Under own firm's", "Not filed under anything");
        truncated.Should().BeFalse();
    }

    [Fact(DisplayName = "F3b fix round 1 REST single record (bound): delete-own of a to-do one hop past the bound is the uniform refusal, not a 5xx")]
    public async Task Delete_OneHopPastTheBound_IsTheUniformRefusal()
    {
        var missing = await DeleteAsync("sprk_todo", Guid.NewGuid().ToString());
        var id = Child("todo-event", "Chained", EventChain(WalkBound + 1, _own));

        (await DeleteAsync("sprk_todo", id)).Should().Be(missing);
        _fixture.Data.Requests.Should().NotContain(r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    // =====================================================================
    // C2: a hop to a core record is not walked and does not count toward the bound
    // =====================================================================

    [Fact(DisplayName = "F3b fix round 1 C2 pin (REST): a to-do under a chain of events as long as the bound, each filed under the matter, with no invoice anywhere, is decided 'no invoice' and shown - the matter above is not a hop")]
    public async Task C2_ACoreRecordAboveTheChain_IsNotAHop()
    {
        Child("todo-event", "Chained", EventChain(WalkBound, topInvoice: null));

        var (names, truncated) = await ListAsync("sprk_todo");

        names.Should().Equal("Chained");
        truncated.Should().BeFalse();
    }

    private static CallerPrincipal CallerOn(Guid matter) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read | AccessRights.Write | AccessRights.Delete },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}
