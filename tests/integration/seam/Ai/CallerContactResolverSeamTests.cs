using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// FR-B-06 (task AIR2-055) — caller-contact self-assignment resolution: "assign it to me" / "my
/// tasks" must resolve to a concrete Dataverse contact deterministically, server-side, never a model
/// guess (ADR-039). Covers (a) <see cref="CallerContactResolver"/>'s claims→contact mapping in
/// isolation, and (b) the vertical-slice seam through <see cref="ContextBinder"/>: a known caller's
/// contact lands on the <c>ContextEnvelope</c> User slice, an unresolvable caller yields an honest
/// no-contact result, and a client/model-supplied value in dispatch <c>Args</c> can never substitute
/// for server-side resolution.
/// </summary>
public sealed class CallerContactResolverSeamTests
{
    private static readonly Guid CallerOid = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid ResolvedContactId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    private static ClaimsPrincipal PrincipalWithOid(Guid oid) =>
        new(new ClaimsIdentity(new[] { new Claim("oid", oid.ToString()) }, "TestAuth"));

    /// <summary>
    /// The contacts BOUND to the caller's oid (unified-access-control-r2 task 141): every contact whose
    /// <c>sprk_externalobjectid</c> is the oid in "D" format, in ANY state (the binder's own question — verifier
    /// finding 6). The resolver used to query <c>contact.azureactivedirectoryobjectid</c>, which does not exist in
    /// dev — every "assign it to me" there came back lookup-failed. The strict double answers ONLY the binding
    /// query without a statecode filter, so a regression to the old column, or back to an active-only read that
    /// hides an inactive second contact, throws instead of passing.
    /// </summary>
    private static Mock<IDataverseService> DataverseReturning(Guid oidQueried, params (Guid Id, int State)[] contacts)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        var collection = new EntityCollection();
        foreach (var (cid, state) in contacts)
        {
            collection.Entities.Add(new Entity("contact")
            {
                Id = cid,
                ["statecode"] = new OptionSetValue(state),
                ["sprk_externalobjectid"] = oidQueried.ToString("D"),
            });
        }

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "contact" &&
                    q.Criteria.Conditions.Any(c =>
                        c.AttributeName == "sprk_externalobjectid" &&
                        c.Values.Count == 1 &&
                        Equals(c.Values[0], oidQueried.ToString("D"))) &&
                    q.Criteria.Conditions.All(c => c.AttributeName != "statecode")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        return dataverse;
    }

    private static Mock<IDataverseService> DataverseReturning(Guid oidQueried, params Guid[] activeContactIds)
        => DataverseReturning(oidQueried, activeContactIds.Select(id => (id, 0)).ToArray());

    private static Mock<IDataverseService> DataverseReturning(Guid oidQueried, Guid? contactId)
        => DataverseReturning(oidQueried, contactId is { } c ? new[] { c } : Array.Empty<Guid>());

    private static ContextBinder NewBinder(ICallerContactResolver resolver) =>
        new(new ChatSessionManager(new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(), Mock.Of<ILogger<ChatSessionManager>>()),
            Mock.Of<ILogger<ContextBinder>>(),
            callerContactResolver: resolver);

    // ─── CallerContactResolver — isolated resolution correctness (FR-B-06) ───

    [Fact]
    public async Task ResolveAsync_KnownCallerOid_ReturnsResolvedContactId()
    {
        var dataverse = DataverseReturning(CallerOid, ResolvedContactId);
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.ContactId.Should().Be(ResolvedContactId.ToString("D"));
    }

    [Fact]
    public async Task ResolveAsync_NoMatchingContact_ReturnsUnresolvedNeverGuesses()
    {
        // NEGATIVE (acceptance criterion): a caller with no matching contact must NOT resolve to a
        // nearest/first contact — the resolver returns an explicit unresolved result.
        var dataverse = DataverseReturning(CallerOid, contactId: null);
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.ContactId.Should().BeNull();
        result.UnresolvedReason.Should().Be("no-matching-contact");
    }

    [Fact]
    public async Task ResolveAsync_TwoContactsBoundToTheOid_ReturnsUnresolvedNeverPicksOne()
    {
        // NEGATIVE (task 141): an ambiguous binding is an identity collision, not a choice.
        var dataverse = DataverseReturning(CallerOid, ResolvedContactId, Guid.NewGuid());
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("ambiguous-binding");
    }

    [Fact]
    public async Task ResolveAsync_AnActiveAndAnInactiveContactOnTheOid_IsAmbiguous_NeverTheActiveOne()
    {
        // NEGATIVE (verifier finding 6): the binder reads the oid over every state and denies + flags this data
        // as OidOnMultipleContacts. "Assign it to me" must give the same answer, not quietly pick the active row.
        var dataverse = DataverseReturning(CallerOid, (ResolvedContactId, 0), (Guid.NewGuid(), 1));
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.ContactId.Should().BeNull();
        result.UnresolvedReason.Should().Be("ambiguous-binding");
    }

    [Fact]
    public async Task ResolveAsync_TheOnlyContactOnTheOidIsInactive_ReturnsItsOwnUnresolvedReason()
    {
        // Deactivating a contact is how an operator removes a person (ADR-003); it is never "me".
        var dataverse = DataverseReturning(CallerOid, (ResolvedContactId, 1));
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("inactive-contact");
    }

    [Fact]
    public async Task ResolveAsync_LookupFails_ReturnsAnHonestUnresolved()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(PrincipalWithOid(CallerOid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("lookup-failed");
    }

    [Fact]
    public async Task ResolveAsync_NoClaimsPrincipal_ReturnsUnresolved()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict); // no Dataverse call expected
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());

        var result = await sut.ResolveAsync(caller: null, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("no-claims-principal");
    }

    [Fact]
    public async Task ResolveAsync_NoOidClaim_ReturnsUnresolvedWithoutDataverseRoundTrip()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict); // no round-trip without an oid
        var sut = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());
        var principalWithoutOid = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "Ada") }, "TestAuth"));

        var result = await sut.ResolveAsync(principalWithoutOid, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("no-oid-claim");
    }

    // ─── ContextBinder seam — the resolved contact reaches the ContextEnvelope User slice ───

    [Fact]
    public async Task BindAsync_KnownCallerViaClaims_PopulatesUserSliceCallerContactId()
    {
        var dataverse = DataverseReturning(CallerOid, ResolvedContactId);
        var resolver = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());
        var binder = NewBinder(resolver);

        var bound = await binder.BindAsync(
            new ContextBindingRequest { Caller = PrincipalWithOid(CallerOid) }, CancellationToken.None);

        bound.Context.User!.CallerContactId.Should().Be(ResolvedContactId.ToString("D"));
    }

    [Fact]
    public async Task BindAsync_UnresolvableCaller_UserSliceCallerContactIdStaysNull()
    {
        // NEGATIVE (acceptance criterion): unresolvable caller → honest no-contact on the envelope,
        // never a guessed/nearest contact.
        var dataverse = DataverseReturning(CallerOid, contactId: null);
        var resolver = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());
        var binder = NewBinder(resolver);

        var bound = await binder.BindAsync(
            new ContextBindingRequest { Caller = PrincipalWithOid(CallerOid) }, CancellationToken.None);

        bound.Context.User!.CallerContactId.Should().BeNull();
    }

    [Fact]
    public async Task BindAsync_NoCallerAndNoAmbientHttpContext_UserSliceCallerContactIdStaysNull()
    {
        // No Caller supplied + no IHttpContextAccessor wired (background/pre-session bind shape) →
        // the resolver runs against a null principal → honest no-contact, no Dataverse round-trip.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        var resolver = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());
        var binder = NewBinder(resolver);

        var bound = await binder.BindAsync(new ContextBindingRequest(), CancellationToken.None);

        bound.Context.User!.CallerContactId.Should().BeNull();
    }

    [Fact]
    public async Task BindAsync_ArgsCarryingSpoofedContactId_NeverInfluencesResolution()
    {
        // Acceptance criterion: "no code path accepts a model/client-supplied contact id for 'me'".
        // Dispatch Args carrying a callerContactId-shaped field must NOT influence the resolved User
        // slice — ContextBinder never reads Args for contact resolution, only Caller / ambient
        // HttpContext (both exclusively server-populated).
        var dataverse = DataverseReturning(CallerOid, ResolvedContactId);
        var resolver = new CallerContactResolver(dataverse.Object, Mock.Of<ILogger<CallerContactResolver>>());
        var binder = NewBinder(resolver);

        var spoofedArgs = JsonSerializer.SerializeToElement(
            new { callerContactId = "ffffffff-ffff-ffff-ffff-ffffffffffff" });

        var bound = await binder.BindAsync(
            new ContextBindingRequest { Caller = PrincipalWithOid(CallerOid), Args = spoofedArgs },
            CancellationToken.None);

        // The envelope's CallerContactId is the resolver's deterministic output — never the spoofed
        // Args value the client attempted to smuggle in.
        bound.Context.User!.CallerContactId.Should().Be(ResolvedContactId.ToString("D"));
        bound.Context.User!.CallerContactId.Should().NotBe("ffffffff-ffff-ffff-ffff-ffffffffffff");
    }
}
