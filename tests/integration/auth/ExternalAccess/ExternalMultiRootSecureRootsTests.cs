// spaarke-SPA-external-access-platform-r3 task 039, owner decision 2026-10-12 (option B), and its review fixes.
//
// KEEP-path classification (ADR-038 section 2 path #1, security-auth): every test asserts an AUTHORIZATION decision - what an
// external caller may READ from a row that is filed to more than one core root. The file is compiled into
// Sprk.Bff.Api.Tests by the existing tests/integration/auth glob (tests/CLAUDE.md), so it needs no project change.
//
// THE RULE. A row linked to several roots is visible only with Read on every EFFECTIVELY secure linked root. Unknown status
// counts as secure. "Effectively" is task 174's rule: a record filed under a secure record is secure before its own flag
// catches up (the ONE filing walk, EffectiveRootFlags).
//
// WHAT IS UNDER TEST, AND HOW. The REAL routes (/api/v1/external/...), the REAL ExternalDataService, the REAL secure-flag read and
// the REAL filing walk, over ExternalRootEventReadTests.FakeDataverse - a Web API double that projects every response
// through the request's $select and answers the walk's reads from a filing world. Nothing about the rule is re-implemented
// here; the double sits only at the Dataverse wire.
//
// Every must-fire (a row is withheld) has its must-not-fire (the same shape, with a non-secure root, is shown). A rule that
// only ever withholds would pass the first and be caught by the second.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration assertions, no constructor
// null-checks.

using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

[Trait("category", "external-access")]
public sealed class ExternalMultiRootSecureRootsTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");

    /// <summary>The project the route is for and the caller can read.</summary>
    private static readonly Guid P = Guid.Parse("a0390000-0000-4000-8000-0000000000a1");

    /// <summary>Another project (the "related" twin of a document), which the caller cannot read.</summary>
    private static readonly Guid Q = Guid.Parse("a0390000-0000-4000-8000-0000000000a2");

    /// <summary>A matter the caller cannot read.</summary>
    private static readonly Guid M = Guid.Parse("a0390000-0000-4000-8000-0000000000b1");

    /// <summary>A work assignment the caller cannot read.</summary>
    private static readonly Guid W = Guid.Parse("a0390000-0000-4000-8000-0000000000c1");

    private static readonly Guid Invoice = Guid.Parse("a0390000-0000-4000-8000-0000000000d1");
    private static readonly Guid ServiceRequest = Guid.Parse("a0390000-0000-4000-8000-0000000000e1");

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalMultiRootSecureRootsTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = ReaderOfProjectP();
    }

    public static TheoryData<string> Kinds => new() { "events", "todos", "documents" };

    // =====================================================================
    // The headline pair: a row linked to P (readable) and an unreadable matter M
    // =====================================================================

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARowLinkedToPAndASecureUnreadableMatter_IsWithheld(string kind)
    {
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, M);
        var plain = Add(kind, "Project only");
        Add(kind, "Also in a secure matter", matter: M);

        (await ListIds(kind)).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARowLinkedToPAndANonSecureUnreadableMatter_IsShown(string kind)
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, M);
        var plain = Add(kind, "Project only");
        var dual = Add(kind, "Also in a non-secure matter", matter: M);

        (await ListIds(kind)).Should().BeEquivalentTo(new[] { plain, dual });
    }

    // =====================================================================
    // EFFECTIVE status: a work assignment whose own flag is false but which is filed under a secure matter is secure,
    // even though the row carries no stamp for that matter
    // =====================================================================

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARowLinkedToPAndAWorkAssignmentWithAFalseOwnFlagUnderASecureMatter_IsWithheld(string kind)
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, W);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, M);
        _fixture.Data.FileWorkAssignmentUnder(W, ExternalGrantRootType.Matter, M);
        var plain = Add(kind, "Project only");
        Add(kind, "Also in a work assignment under a secure matter", workAssignment: W);

        (await ListIds(kind)).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARowLinkedToPAndAWorkAssignmentWithAFalseOwnFlagUnderANonSecureMatter_IsShown(string kind)
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, W);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, M);
        _fixture.Data.FileWorkAssignmentUnder(W, ExternalGrantRootType.Matter, M);
        var plain = Add(kind, "Project only");
        var dual = Add(kind, "Also in a work assignment under a non-secure matter", workAssignment: W);

        (await ListIds(kind)).Should().BeEquivalentTo(new[] { plain, dual });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ARowLinkedToPAndAWorkAssignmentWhoseNamedParentMatterIsNotReturned_IsWithheld(string kind)
    {
        // Owner policy: a named parent the read cannot find is unknown, and unknown counts as secure.
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, W);
        _fixture.Data.FileWorkAssignmentUnder(W, ExternalGrantRootType.Matter, M); // M was never created
        var plain = Add(kind, "Project only");
        Add(kind, "Also in a work assignment under an unreadable matter", workAssignment: W);

        (await ListIds(kind)).Should().Equal(plain);
    }

    [Fact]
    public async Task ADocumentLinkedToPAndAProjectUnderASecureMatterThroughThePair_IsWithheld()
    {
        // A project files under a matter only through the polymorphic pair (sprk_regardingrecordid / _type).
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Project, Q);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, M);
        _fixture.Data.FileProjectUnderByPair(Q, ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("projects", P, "Project only");
        _fixture.Data.AddDocument("projects", P, "Also in a project under a secure matter", ("_sprk_relatedproject_value", Q));

        (await ListIds("documents")).Should().Equal(plain);
    }

    [Fact]
    public async Task ADocumentLinkedToPAndAProjectUnderANonSecureMatterThroughThePair_IsShown()
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Project, Q);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, M);
        _fixture.Data.FileProjectUnderByPair(Q, ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("projects", P, "Project only");
        var dual = _fixture.Data.AddDocument("projects", P, "Also in a project under a non-secure matter", ("_sprk_relatedproject_value", Q));

        (await ListIds("documents")).Should().BeEquivalentTo(new[] { plain, dual });
    }

    // =====================================================================
    // Review fix 6: GET /invoices/{id}/documents and GET /servicerequests/{id}/documents share GetDocumentsAsync
    // =====================================================================

    [Fact]
    public async Task InvoiceDocuments_ADocumentAlsoFiledToASecureUnreadableMatter_IsWithheld()
    {
        _fixture.Data.AddInvoice(Invoice, matter: null, project: P);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("invoices", Invoice, "Invoice only");
        _fixture.Data.AddDocument("invoices", Invoice, "Invoice and a secure matter", ("_sprk_matter_value", M));

        (await ListDocumentIds($"/api/v1/external/invoices/{Invoice}/documents")).Should().Equal(plain);
    }

    [Fact]
    public async Task InvoiceDocuments_ADocumentAlsoFiledToANonSecureUnreadableMatter_IsShown()
    {
        _fixture.Data.AddInvoice(Invoice, matter: null, project: P);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("invoices", Invoice, "Invoice only");
        var dual = _fixture.Data.AddDocument("invoices", Invoice, "Invoice and a non-secure matter", ("_sprk_matter_value", M));

        (await ListDocumentIds($"/api/v1/external/invoices/{Invoice}/documents")).Should().BeEquivalentTo(new[] { plain, dual });
    }

    [Fact]
    public async Task ServiceRequestDocuments_ADocumentAlsoFiledToASecureUnreadableMatter_IsWithheld()
    {
        _fixture.Principal = RequesterWorkforceContact();
        _fixture.Data.AddServiceRequest(ServiceRequest, CallerContact);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("servicerequests", ServiceRequest, "Request only");
        _fixture.Data.AddDocument("servicerequests", ServiceRequest, "Request and a secure matter", ("_sprk_matter_value", M));

        (await ListDocumentIds($"/api/v1/external/servicerequests/{ServiceRequest}/documents")).Should().Equal(plain);
    }

    [Fact]
    public async Task ServiceRequestDocuments_ADocumentAlsoFiledToANonSecureUnreadableMatter_IsShown()
    {
        _fixture.Principal = RequesterWorkforceContact();
        _fixture.Data.AddServiceRequest(ServiceRequest, CallerContact);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, M);
        var plain = _fixture.Data.AddDocument("servicerequests", ServiceRequest, "Request only");
        var dual = _fixture.Data.AddDocument("servicerequests", ServiceRequest, "Request and a non-secure matter", ("_sprk_matter_value", M));

        (await ListDocumentIds($"/api/v1/external/servicerequests/{ServiceRequest}/documents"))
            .Should().BeEquivalentTo(new[] { plain, dual });
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>Seeds an event, to-do or document filed to project P and, when given, to one more root.</summary>
    private string Add(string kind, string name, Guid? matter = null, Guid? workAssignment = null) => kind switch
    {
        "events" => _fixture.Data.Add("projects", P, name, matter: matter, workAssignment: workAssignment),
        "todos" => _fixture.Data.AddTodo("projects", P, name, matter: matter, workAssignment: workAssignment),
        _ => _fixture.Data.AddDocument("projects", P, name, Links(matter, workAssignment)),
    };

    private static (string Column, Guid Value)[] Links(Guid? matter, Guid? workAssignment)
    {
        var links = new List<(string, Guid)>();
        if (matter is { } m) links.Add(("_sprk_matter_value", m));
        if (workAssignment is { } w) links.Add(("_sprk_workassignment_value", w));
        return links.ToArray();
    }

    private async Task<IReadOnlyList<string>> ListIds(string kind)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var json = await client.GetStringAsync($"/api/v1/external/projects/{P}/{kind}");
        return IdsOf(json, kind switch { "events" => "sprk_eventid", "todos" => "sprk_todoid", _ => "sprk_documentid" });
    }

    private async Task<IReadOnlyList<string>> ListDocumentIds(string path)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(path);
        var json = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(json);
        return IdsOf(json, "sprk_documentid");
    }

    private static IReadOnlyList<string> IdsOf(string json, string idProperty)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value").EnumerateArray().Select(v => v.GetProperty(idProperty).GetString()!).ToList();
    }

    /// <summary>A caller who can read project P (and so an invoice under it), and nothing else.</summary>
    private static CallerPrincipal ReaderOfProjectP() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(P, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    /// <summary>A workforce contact: the requester of a service request, who reads its documents as the submitter.</summary>
    private static CallerPrincipal RequesterWorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = CallerContact,
        Email = "staff@customer.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };
}
