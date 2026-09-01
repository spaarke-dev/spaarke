using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <c>POST /api/office/save</c> — the entity-association gate on the Office add-in save path.
///
/// <para><b>The defect (found while executing task 008; no Phase 0 finding covered it).</b>
/// <c>EntityAccessFilter</c> builds a resource id of the form <c>"{entityType}:{entityId}"</c>
/// (EntityAccessFilter.cs:143) and hands it to <c>AuthorizationService</c>. That bottoms out in
/// <c>DataverseAccessDataSource</c>, which substitutes the value into <c>sprk_documents({resourceId})</c>
/// in BOTH its <c>RetrievePrincipalAccess</c> target (:387) AND its fallback read probe (:461). The
/// emitted URL is therefore <c>sprk_documents(sprk_matter:8f3a…)</c> — not a document id, not even a
/// GUID. Dataverse rejects it, the code fails closed to <see cref="AccessRights.None"/>, and
/// <c>entity.associate_document</c> requires <see cref="AccessRights.AppendTo"/>
/// (OperationAccessPolicy.cs:186) — so the save is refused for EVERY caller, however privileged.</para>
///
/// <para><b>Why it went unnoticed.</b> The only test that exercised this route with a
/// <c>TargetEntity</c> is <c>[Fact(Skip = "Requires fully mocked Office services…")]</c>
/// (OfficeEndpointsContractTests.cs:47), and every e2e spec intercepts <c>/office/save</c> at the
/// network layer with <c>page.route(...)</c> so it never reaches the BFF. The filter had no coverage
/// of any kind. This file is that coverage.</para>
///
/// <para>Same defect CLASS as A-20 (an authorization path structurally unable to return the right it
/// requires), on a surface no Phase 0 task owned.</para>
/// </summary>
public class EntityAccessFilterCharacterizationTests : IClassFixture<OfficeSaveTestFixture>
{
    private readonly OfficeSaveTestFixture _fixture;

    private static readonly Guid MatterId = Guid.Parse("5f5f5f5f-0000-0000-0000-00000000000a");

    /// <summary>What Dataverse reports for a caller who may associate documents with a record.</summary>
    private const string CanAssociate = "ReadAccess,WriteAccess,AppendToAccess";

    /// <summary>A caller who can see the record but not attach anything to it.</summary>
    private const string ReadOnly = "ReadAccess";

    public EntityAccessFilterCharacterizationTests(OfficeSaveTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// ✅ FLIPPED BY TASK 008 follow-up (owner-authorised 2026-08-23) — was
    /// <c>Characterization_PostOfficeSave_WithTargetEntity_IsDeniedForEveryCaller</c>.
    ///
    /// <para>A caller Dataverse says holds <c>AppendToAccess</c> on the target matter must be allowed to
    /// file a document against it. Before the fix this returned 403 for everyone, because the rights
    /// lookup asked <c>sprk_documents</c> about a matter id and got nothing back.</para>
    /// </summary>
    [Fact]
    public async Task PostOfficeSave_ForCallerWithAppendToOnTargetEntity_IsNotDeniedByEntityAccess()
    {
        // Arrange
        using var client = _fixture.CreateClientWithRights(CanAssociate);

        // Act
        var response = await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting("sprk_matter", MatterId));

        // Assert — the gate must not be what stops this caller. (The handler may still fail downstream
        // on the unavailable test-host Office/SPE services; that is not this filter's concern.)
        (await ReasonCodeOf(response)).Should().NotBe("insufficient_rights",
            "a caller Dataverse reports as holding AppendToAccess on the matter may file a document against it");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "the association gate must consult the TARGET ENTITY's rights, not sprk_documents");
    }

    /// <summary>
    /// The negative twin — without it the test above proves only "something changed". A caller who can
    /// read the matter but holds no <c>AppendToAccess</c> is still refused, so the fix did not simply
    /// remove the gate.
    /// </summary>
    [Fact]
    public async Task PostOfficeSave_ForCallerWithoutAppendToOnTargetEntity_IsDenied()
    {
        // Arrange
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        // Act
        var response = await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting("sprk_matter", MatterId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "entity.associate_document requires AppendTo (OperationAccessPolicy.cs:186); Read alone is not enough");
    }

    /// <summary>
    /// A refusal the USER can act on.
    ///
    /// <para><b>The defect this pins</b> (found + fixed 2026-08-31, owner-directed). The filter passed the
    /// literal reason <c>"insufficient_rights"</c> into a five-arm <c>MapAuthorizationDenial</c> switch that
    /// had no arm for that string — so every real denial fell to the default and the user was told
    /// <i>"Access denied to association target"</i>. That names no record, no missing capability and no
    /// remedy, and "association target" is internal vocabulary. The switch's own better sentence (for
    /// <c>"permission_denied"</c>) was unreachable from the only call site.</para>
    ///
    /// <para>The Office task pane renders the SERVER's <c>detail</c> in preference to its own static
    /// per-code string (<c>errorMessages.ts</c> <c>mapProblemDetailsToMessage</c>:
    /// <c>message: problem.detail || mapped.message</c>), so this text is what the user actually reads —
    /// which is why asserting it is asserting behaviour, not wording for its own sake.</para>
    ///
    /// <para>Third instance of this class in this project: task 092 found a live 404 surfaced as
    /// <i>"Failed to create sharing link."</i>, and task 072 found every handler exception on nine routes
    /// masked as <c>500 "Authorization Error"</c>. A correct status code with an uninformative body is a
    /// support call, not a fix.</para>
    /// </summary>
    [Fact]
    public async Task PostOfficeSave_WhenDeniedForInsufficientRights_ExplainsTheMissingPermissionAndTheRemedy()
    {
        // Arrange — a caller who can see the matter but cannot attach anything to it.
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        // Act
        var response = await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting("sprk_matter", MatterId));

        // Assert — the machine-readable half is unchanged (other tests + the client's code map rely on it).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be("insufficient_rights");

        var detail = await DetailOf(response);

        detail.Should().NotBeNullOrWhiteSpace("a 403 with no detail leaves the client's generic string to explain a specific failure");

        detail.Should().Contain("Append To",
            "the user cannot act on 'access denied'; naming the Dataverse permission is the one word an "
            + "administrator can search for. Safe to name: the filter answers 403 whether or not the record "
            + "exists (CallerRecordAccessProbe conflates not-found with no-access on purpose), so this "
            + "discloses no record existence — see task 022's enumeration-oracle finding");

        detail.Should().ContainAny("administrator", "owner",
            "a denial that states no remedy is a dead end for the user");

        detail.Should().NotContain("association target",
            "the pre-fix default arm's wording — internal vocabulary that named neither the record nor the "
            + "missing capability. Its return would mean the reason-code fall-through has come back");
    }

    /// <summary>
    /// The rights check must be aimed at the TARGET ENTITY's own collection. This is the assertion that
    /// actually pins the defect: before the fix the probe was never consulted at all, and the lookup
    /// that WAS performed named <c>sprk_documents</c>.
    /// </summary>
    [Fact]
    public async Task PostOfficeSave_ChecksTheTargetEntitysOwnCollection_NotDocuments()
    {
        // Arrange
        using var client = _fixture.CreateClientWithRights(CanAssociate);

        // Act
        await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting("sprk_matter", MatterId));

        // Assert
        _fixture.ProbedTargets.Should().Contain(("sprk_matters", MatterId),
            "associating a document with a matter is authorized by the caller's rights ON THAT MATTER");
        _fixture.ProbedTargets.Should().NotContain(t => t.EntitySet == "sprk_documents",
            "asking sprk_documents about a matter id is the defect — it can only ever answer 'no rights'");
    }

    /// <summary>
    /// Every supported association target resolves to its own Dataverse collection. A missing or wrong
    /// entry here reintroduces the defect for that one entity type only — which is the shape that
    /// survives review, because the other four keep working.
    /// </summary>
    [Theory]
    [InlineData("account", "accounts")]
    [InlineData("contact", "contacts")]
    [InlineData("sprk_matter", "sprk_matters")]
    [InlineData("sprk_project", "sprk_projects")]
    [InlineData("sprk_invoice", "sprk_invoices")]
    public async Task PostOfficeSave_ForEachSupportedTargetType_ProbesThatTypesCollection(
        string entityType, string expectedEntitySet)
    {
        // Arrange — a distinct id per case so the assertion cannot pass on another case's recording.
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(CanAssociate);

        // Act
        await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting(entityType, recordId));

        // Assert
        _fixture.ProbedTargets.Should().Contain((expectedEntitySet, recordId));
    }

    /// <summary>
    /// Fail-closed: an association target the filter does not recognise is refused rather than waved
    /// through. Pins that adding a new supported entity type is a deliberate act.
    /// </summary>
    [Fact]
    public async Task PostOfficeSave_ForAnUnsupportedTargetType_IsRejected()
    {
        using var client = _fixture.CreateClientWithRights(CanAssociate);

        var response = await client.PostAsJsonAsync(
            "/api/office/save", SaveRequestTargeting("sprk_notathing", Guid.NewGuid()));

        response.IsSuccessStatusCode.Should().BeFalse(
            "an unrecognised association target must never reach the save handler");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static object SaveRequestTargeting(string entityType, Guid entityId) => new
    {
        contentType = 0,                       // SaveContentType.Email
        email = new
        {
            subject = "Filing note",
            senderEmail = "counsel@example.com",
            senderName = "Counsel"
        },
        targetEntity = new { entityType, entityId }
    };

    /// <summary>The ProblemDetails <c>reasonCode</c>, or <c>null</c> when the response carries none.</summary>
    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The ProblemDetails <c>detail</c> — the text the Office task pane actually shows the user, since
    /// <c>mapProblemDetailsToMessage</c> prefers it over its own static per-error-code string.
    /// </summary>
    private static async Task<string?> DetailOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
