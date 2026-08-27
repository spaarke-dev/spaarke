using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Workforce contact-by-email resolution must apply the CIAM no-hijack oid check —
/// finding A-18 (spec FR-12), closed by task 013.
///
/// <para><b>What was wrong.</b> The workforce contact-only branch fell back to a raw
/// <c>emailaddress1</c> match and returned whatever contact it found, asking nothing about whether
/// that contact already belonged to somebody else. The CIAM plane has refused exactly this since
/// ADR-028 Amendment A1 (<c>ExternalParticipationService.ResolveExternalContactAsync</c> — "no email
/// hijack of a bound Contact"); the workforce plane never had the rule. Under the multitenant
/// workforce model a token's email claim is not domain-verified across a foreign tenant, so a caller
/// carrying <c>email=victim@firm.example</c> inherited the victim's contact and every grant hanging
/// off it. A <c>contact</c> is not a security principal and cannot be impersonated in Dataverse —
/// which is why nothing downstream would have caught this: the contact plane COMPUTES access, so a
/// wrongly-resolved contact is silently a wrongly-computed access set.</para>
///
/// <para><b>Why task 001 could not pin it.</b> The decision was four lines welded to a Dataverse
/// query, observable only by standing up Dataverse. Task 013 extracted
/// <see cref="IdentityNormalizationService.DecideWorkforceEmailMatch"/> as a PURE function first, in
/// its own commit, with the pre-fix semantics intact — that commit is what these assertions were
/// written against before the flip. <c>internal</c> + <c>InternalsVisibleTo</c>, no reflection into
/// privates (ADR-038 §7 ban B8), no transport double (ban B1).</para>
///
/// <para><b>Read this before trusting a green run here.</b> The decision tests use no double at all,
/// so there is nothing in them that can pass vacuously. Every guard is perturbed INDIVIDUALLY —
/// one input changed, outcome flips — so none of them is merely along for the ride. The
/// service-level tests use a <b>strict</b> <see cref="IDataverseService"/> mock: an unmodelled query
/// THROWS rather than returning an empty collection, because a double that falls back to
/// match-nothing turns "the query changed shape" into "the contact is unbound", which is a green
/// test for a broken guard. What none of this can prove is enumerated on
/// <see cref="WhatTheseTestsCannotFalsify"/> — read it.</para>
/// </summary>
public class WorkforceEmailNoHijackTests
{
    private static readonly Guid CallerOid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VictimOid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VictimContactId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherContactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string SharedEmail = "victim@firm.example";
    private const string TestTenantId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-12 acceptance — the decision itself. Pure: no doubles, nothing to be vacuous about.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 013 (FR-12) — was
    /// <c>Characterization_DecideWorkforceEmailMatch_ResolvesContactBoundToADifferentOid</c>, which
    /// pinned A-18: a contact firmly bound to somebody else's oid was resolved to anyway.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_ContactBoundToADifferentOid_Denies()
    {
        var decision = Decide(Match(VictimContactId, VictimOid), CallerOid);

        decision.Should().Be(
            IdentityNormalizationService.WorkforceEmailMatchDecision.DenyBoundToDifferentOid,
            "FR-12 acceptance: an email matching a contact bound to a different oid is denied");
    }

    /// <summary>
    /// The guard's comparison is load-bearing, not just its existence. ONE input moves — the
    /// contact's bound oid becomes the caller's own — and the outcome flips to Resolve. Without this,
    /// a guard that denied on "has any binding at all" would be indistinguishable from the correct
    /// one, and it would break every returning caller.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_PerturbBoundOidToTheCallersOwn_Resolves()
    {
        Decide(Match(VictimContactId, VictimOid), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.DenyBoundToDifferentOid);

        Decide(Match(VictimContactId, CallerOid), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
                "only the bound oid changed");
    }

    /// <summary>
    /// The other half of the same perturbation: drop the binding entirely and it resolves. This is
    /// the legitimate Type-2 onboarding path (a customer employee with no
    /// <c>azureactivedirectoryobjectid</c> yet), and it is the reason the escalation trigger on this
    /// task existed — the security property lives in <i>bound to a DIFFERENT oid</i>, never in
    /// <i>bound at all</i>.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_PerturbBoundOidToUnbound_Resolves()
    {
        Decide(Match(VictimContactId, boundOid: null), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
                "an unbound contact is nobody else's yet — denying it would break onboarding");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 013 (FR-12) — was
    /// <c>Characterization_DecideWorkforceEmailMatch_PicksOneWhenSeveralContactsShareTheEmail</c>.
    /// Picking one was a coin-flip over whose grants the caller inherited.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_SeveralContactsShareTheEmail_DeniesRatherThanPicking()
    {
        var decision = Decide(
            new[] { Match1(VictimContactId, null), Match1(OtherContactId, null) },
            CallerOid);

        decision.Should().Be(
            IdentityNormalizationService.WorkforceEmailMatchDecision.DenyAmbiguousEmail,
            "FR-12 negative acceptance: ambiguity denies, it does not pick one");
    }

    /// <summary>
    /// Perturbs the ambiguity guard alone: same first row, second row removed → Resolve. Proves the
    /// deny above came from the COUNT and not from anything about the row that happens to be first.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_PerturbAmbiguityToASingleMatch_Resolves()
    {
        Decide(new[] { Match1(VictimContactId, null), Match1(OtherContactId, null) }, CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.DenyAmbiguousEmail);

        Decide(new[] { Match1(VictimContactId, null) }, CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
                "only the second row was removed");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 013 (FR-12) — was
    /// <c>Characterization_DecideWorkforceEmailMatch_ResolvesForACallerWithNoUsableOid</c>. A caller
    /// we cannot name cannot be shown to own anything; resolving them by email alone is the "silent
    /// fallback to an unscoped principal" the resolver's own contract forbids.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_CallerWithNoUsableOid_Denies()
    {
        Decide(Match(VictimContactId, boundOid: null), Guid.Empty)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.DenyUnidentifiableCaller);
    }

    /// <summary>Perturbs the caller-oid guard alone: same match, real caller oid → Resolve.</summary>
    [Fact]
    public void DecideWorkforceEmailMatch_PerturbCallerOidToAUsableValue_Resolves()
    {
        Decide(Match(VictimContactId, boundOid: null), Guid.Empty)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.DenyUnidentifiableCaller);

        Decide(Match(VictimContactId, boundOid: null), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
                "only the caller oid changed");
    }

    /// <summary>
    /// No contact carries the email — a plain miss, distinct from every deny above. Kept separate
    /// because collapsing "nobody matched" into "denied" would hide the deny signal in the noise of
    /// ordinary non-contacts.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_WithNoMatches_ReturnsNoMatch()
    {
        Decide(Array.Empty<IdentityNormalizationService.WorkforceContactEmailMatch>(), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.NoMatch);
    }

    /// <summary>An empty contact id is not a principal either — perturbed against the real id.</summary>
    [Fact]
    public void DecideWorkforceEmailMatch_PerturbContactIdToEmpty_ReturnsNoMatch()
    {
        Decide(Match(Guid.Empty, boundOid: null), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.NoMatch);

        Decide(Match(VictimContactId, boundOid: null), CallerOid)
            .Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The query — does it actually READ what the decision needs?
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The guard cannot be load-bearing if the query never fetches the binding column: every match
    /// would arrive looking unbound and the check would pass everything while reading as though it
    /// enforced something. That is the precise shape of a vacuous control, so it is asserted directly
    /// on the emitted <see cref="QueryExpression"/> rather than inferred from an outcome.
    /// </summary>
    [Fact]
    public async Task ContactByEmailQuery_SelectsTheOidBindingColumn()
    {
        var (sut, captured) = CreateSutCapturingQueries(rows: Array.Empty<Entity>());

        await sut.TryResolveContactByWorkforceIdentityAsync(CallerOid, SharedEmail, CancellationToken.None);

        captured.Single(HasEmailCondition).ColumnSet.Columns.Should().Contain(
            "azureactivedirectoryobjectid",
            "without the binding column every match looks unbound and the no-hijack check is a no-op");
    }

    /// <summary>
    /// <c>TopCount = 1</c> makes an ambiguous email indistinguishable from an unambiguous one — the
    /// second row simply never arrives, so <c>DenyAmbiguousEmail</c> becomes unreachable no matter
    /// what the decision function says.
    /// </summary>
    [Fact]
    public async Task ContactByEmailQuery_ReadsMoreThanOneRowSoAmbiguityIsReachable()
    {
        var (sut, captured) = CreateSutCapturingQueries(rows: Array.Empty<Entity>());

        await sut.TryResolveContactByWorkforceIdentityAsync(CallerOid, SharedEmail, CancellationToken.None);

        captured.Single(HasEmailCondition).TopCount.Should().BeGreaterThan(
            1, "a single-row read cannot distinguish 'exactly one contact' from 'several'");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // End to end through the service, with a STRICT Dataverse double
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 013 (FR-12) — was
    /// <c>Characterization_TryResolveContactByWorkforceIdentity_ReturnsAContactBoundToAnotherOid</c>,
    /// the whole finding end to end: a caller presenting the victim's email received the victim's
    /// contact.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_EmailMatchesAContactBoundToAnotherOid_ResolvesNothing()
    {
        var sut = CreateSutWithContacts(ContactRow(VictimContactId, VictimOid));

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().BeNull(
            "FR-12: no principal resolved, so no grants inherited — the contact plane computes access " +
            "from the resolved contact, so resolving the wrong one IS the disclosure");
    }

    /// <summary>
    /// The legitimate onboarding path survives: an UNBOUND contact matched by verified email still
    /// resolves. Paired with the test above, this is the whole security property — same email, same
    /// query, outcome decided solely by whether the contact was already someone's.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_WithAnUnboundContact_StillResolves()
    {
        var sut = CreateSutWithContacts(ContactRow(VictimContactId, boundOid: null));

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().Be(VictimContactId, "an unbound contact is nobody else's yet");
    }

    /// <summary>
    /// The caller's OWN oid-bound contact resolves — on the oid cross-reference, before the email
    /// fallback is consulted at all.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_WithTheCallersOwnBoundContact_Resolves()
    {
        var sut = CreateSutWithContacts(ContactRow(VictimContactId, CallerOid));

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().Be(VictimContactId);
    }

    /// <summary>
    /// Two contacts carry the email → nothing resolves. Note the strict double hands back BOTH rows
    /// only because the query asks for two; this test is downstream of
    /// <see cref="ContactByEmailQuery_ReadsMoreThanOneRowSoAmbiguityIsReachable"/> and would silently
    /// become a single-match test without it.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_WithSeveralContactsSharingTheEmail_ResolvesNothing()
    {
        var sut = CreateSutWithContacts(
            ContactRow(VictimContactId, boundOid: null),
            ContactRow(OtherContactId, boundOid: null));

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().BeNull("FR-12 negative acceptance: ambiguity denies rather than picking one");
    }

    /// <summary>
    /// <b>The oid comparison carries no textual assumption.</b> Dataverse returns
    /// <c>azureactivedirectoryobjectid</c> as a <see cref="Guid"/> via the SDK and as a STRING via the
    /// Web API, and the string casing is not ours to choose. The guard parses before comparing, so an
    /// upper-case string form of the caller's own oid resolves rather than being read as "a different
    /// oid" — which is what a naive <c>string.Equals</c> would have done, denying every returning
    /// caller on one of the two transports. This is the one normalization assumption in the guard that
    /// a self-authored double CAN falsify, so it is asserted rather than asserted-about.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_BindingStoredAsAnUpperCaseString_IsNotTreatedAsADifferentOid()
    {
        var row = new Entity("contact") { Id = VictimContactId };
        row["contactid"] = VictimContactId;
        row["azureactivedirectoryobjectid"] = CallerOid.ToString("D").ToUpperInvariant();

        // Forced down the EMAIL path: the oid cross-reference in the double matches on the Guid-typed
        // attribute only, so this string-bound row is invisible to it — exactly the shape that made
        // the fallback necessary in the first place.
        var sut = CreateSutWithContacts(row);

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().Be(
            VictimContactId,
            "the caller's own binding in a different textual form is still the caller's own binding");
    }

    /// <summary>
    /// A contact-by-email query that cannot be READ must not resolve. Genuinely unchanged by task 013,
    /// asserted because the refactor moved this failure path: the helper used to return
    /// <c>Guid?</c> for both "query failed" and "nothing matched", and a change that turned
    /// "unreadable" into "clean miss" would look identical from the outside.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_WhenTheQueryFails_ResolvesNothing()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var resolved = await CreateSut(dataverse.Object).TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().BeNull("ADR-003: an unreadable binding state denies");
    }

    /// <summary>
    /// No usable email → the fallback never runs, so no <c>emailaddress1</c> filter is emitted. Pinned
    /// because an empty-string filter would be an OData predicate matching every contact that has no
    /// email — a worse hijack than the one being fixed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryResolveContactByWorkforceIdentity_WithNoEmail_NeverEmitsAnEmailFilter(string? email)
    {
        var (sut, captured) = CreateSutCapturingQueries(rows: Array.Empty<Entity>());

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, email, CancellationToken.None);

        resolved.Should().BeNull();
        captured.Should().NotContain(q => HasEmailCondition(q), "there is no verified email to match on");
    }

    /// <summary>
    /// Nothing on this path writes a binding (FR-12 negative acceptance: a denied match must not
    /// create or confirm one). The strict double models ONLY reads, so any write — Update, Create,
    /// Upsert — throws <c>MockException</c> and fails this test. Asserting "no write happened" by
    /// making writes impossible is stronger than verifying a specific write method was not called,
    /// which would miss a write issued through a different one.
    /// </summary>
    [Fact]
    public async Task TryResolveContactByWorkforceIdentity_DeniedMatch_WritesNoOidBinding()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var collection = new EntityCollection();
                if (HasEmailCondition(q)) collection.Entities.Add(ContactRow(VictimContactId, VictimOid));
                return collection;
            });

        var resolved = await CreateSut(dataverse.Object).TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().BeNull();
        // No Verify(..., Times.Never) needed: an unmodelled write would already have thrown.
        dataverse.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The resolver — a denied contact is NO principal, never a partial one
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A denied contact resolution must surface as an explicit DENY at the resolver, not as a
    /// resolved-but-contactless principal. The whole point of the guard is lost if the caller still
    /// gets through carrying a principal whose access set is then computed from nothing.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenContactResolutionDenies_ReturnsExplicitDenyWithNoPrincipal()
    {
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(
                CallerOid, SharedEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await CreateResolver(identity.Object).ResolveAsync(
            WorkforceToken(CallerOid, SharedEmail), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull("no partial principal — FR-12 requires no grants inherited");
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
        result.DenyCode.Should().Be(WorkforcePrincipalResolver.DenyPrincipalNotResolved);
    }

    /// <summary>
    /// If contact resolution THROWS, the caller is denied rather than the exception escaping. An
    /// unhandled throw here is a 500, which is fail-closed only by accident of the pipeline; a deny is
    /// fail-closed by construction (ADR-003) and is auditable. Remove the try/catch in
    /// <c>WorkforcePrincipalResolver</c> and this test throws instead of failing an assertion — which
    /// is what makes it load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenContactResolutionThrows_DeniesInsteadOfPropagating()
    {
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("binding state unknown"));

        var result = await CreateResolver(identity.Object).ResolveAsync(
            WorkforceToken(CallerOid, SharedEmail), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.Principal.Should().BeNull();
        result.DenyReason.Should().Be(WorkforceDenyReason.PrincipalNotResolved);
    }

    /// <summary>
    /// Cancellation is NOT a deny — it must propagate. A catch-all that swallowed
    /// <see cref="OperationCanceledException"/> into a deny would turn every client disconnect into a
    /// logged authorization failure, burying the real ones.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenContactResolutionIsCancelled_PropagatesRatherThanDenying()
    {
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity
            .Setup(x => x.TryResolveContactByWorkforceIdentityAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await CreateResolver(identity.Object).ResolveAsync(
            WorkforceToken(CallerOid, SharedEmail), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Limits
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>What these tests cannot falsify.</b> Executable documentation, so the limits travel with the
    /// suite instead of living in a PR description nobody re-reads.
    ///
    /// <list type="number">
    /// <item><b>Dataverse's own email matching — the big one.</b> Every double here decides for itself
    /// which rows come back for <c>emailaddress1 eq '...'</c>. Real Dataverse matches strings under a
    /// case-INSENSITIVE SQL collation, so <c>Victim@Firm.example</c> and <c>victim@firm.example</c> are
    /// the SAME row there and would be different rows in a naive double. Nothing in this file proves
    /// which, and no unit test can: the matching is the platform's. The consequence is bounded but real
    /// — if the real collation were case-SENSITIVE, a caller could dodge the guard by presenting a
    /// differently-cased email that matched no contact, which fails closed (no resolution), not open.
    /// The guard is deliberately built not to depend on this: it compares parsed
    /// <see cref="Guid"/>s, never strings. Only a live-Dataverse test closes it.</item>
    ///
    /// <item><b>That the email claim means anything at all.</b> The entire finding turns on
    /// <c>email</c>/<c>preferred_username</c>/<c>upn</c> NOT being domain-verified across a foreign
    /// tenant under the multitenant workforce model. That is a property of Entra and of the app
    /// registration (prerequisite E-6, environment work), not of this code. These tests assume the
    /// hostile input is reachable; they cannot demonstrate it is.</item>
    ///
    /// <item><b>That an absent attribute means UNBOUND.</b> Dataverse omits null attributes from a
    /// returned <see cref="Entity"/>, so the query helper reads "no
    /// <c>azureactivedirectoryobjectid</c> on the row" as "this contact is unbound". If the platform
    /// ever returned the row WITHOUT that column for another reason — column-level security, a field
    /// permission trim, a projection change — a BOUND contact would read as unbound and the guard
    /// would pass it. This is the guard's single fail-OPEN assumption. The doubles reproduce the
    /// documented shape, which is not the same as verifying it.</item>
    ///
    /// <item><b>The CIAM binding column.</b> This guard reads only the workforce binding
    /// (<c>azureactivedirectoryobjectid</c>) — the exact mirror FR-12 scopes. A contact bound to a
    /// CIAM <c>oid</c> (<c>sprk_externalobjectid</c>) but NOT to a workforce oid is still reachable by
    /// a workforce email match. That cross-plane case is reported separately and is NOT covered
    /// here.</item>
    ///
    /// <item><b>Inactive contacts.</b> The query carries no <c>statecode</c> filter — pre-existing
    /// behaviour, deliberately unchanged by this task. A deactivated contact bound to a different oid
    /// is still denied (the guard runs first), but a deactivated UNBOUND contact still resolves.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void WhatTheseTestsCannotFalsify() => true.Should().BeTrue();

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers — deliberately STRICT: an unmodelled call throws, it does not default permissive
    // ─────────────────────────────────────────────────────────────────────────────

    private static IdentityNormalizationService.WorkforceEmailMatchDecision Decide(
        IReadOnlyList<IdentityNormalizationService.WorkforceContactEmailMatch> matches, Guid callerOid)
        => IdentityNormalizationService.DecideWorkforceEmailMatch(matches, callerOid);

    private static IdentityNormalizationService.WorkforceContactEmailMatch Match1(Guid contactId, Guid? boundOid)
        => new(contactId, boundOid);

    private static IdentityNormalizationService.WorkforceContactEmailMatch[] Match(Guid contactId, Guid? boundOid)
        => new[] { Match1(contactId, boundOid) };

    private static Entity ContactRow(Guid contactId, Guid? boundOid)
    {
        var row = new Entity("contact") { Id = contactId };
        row["contactid"] = contactId;
        // Set ONLY when bound. Dataverse omits null attributes and the guard reads absence as
        // "unbound"; writing an explicit null would model a shape the platform never sends.
        if (boundOid is { } oid)
        {
            row["azureactivedirectoryobjectid"] = oid;
        }
        return row;
    }

    private static bool HasEmailCondition(QueryExpression q)
        => q.Criteria.Conditions.Any(c => c.AttributeName == "emailaddress1");

    private static bool HasOidCondition(QueryExpression q)
        => q.Criteria.Conditions.Any(c => c.AttributeName == "azureactivedirectoryobjectid");

    /// <summary>
    /// Strict double modelling exactly two contact queries — the oid cross-reference and the email
    /// match. Any OTHER interaction (a write, a Retrieve, a FetchExpression) throws
    /// <c>MockException</c> rather than returning something empty and plausible: a permissive fallback
    /// is how a test passes while asserting nothing.
    /// </summary>
    private static IdentityNormalizationService CreateSutWithContacts(params Entity[] contacts)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOidCondition(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                // Models the real cross-reference: matches only a contact whose Guid-typed binding
                // equals the queried oid. A string-typed binding is invisible to it, as in Dataverse
                // when the column is compared against a Guid parameter.
                var wanted = q.Criteria.Conditions
                    .First(c => c.AttributeName == "azureactivedirectoryobjectid").Values[0];
                var collection = new EntityCollection();
                foreach (var row in contacts)
                {
                    if (row.Contains("azureactivedirectoryobjectid") &&
                        row["azureactivedirectoryobjectid"] is Guid g && Equals(g, wanted))
                    {
                        collection.Entities.Add(row);
                    }
                }
                return collection;
            });

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasEmailCondition(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                // Honors TopCount, so a query that narrowed back to one row would show up here as a
                // lost ambiguity signal rather than being silently compensated for.
                var collection = new EntityCollection();
                foreach (var row in contacts.Take(q.TopCount ?? contacts.Length))
                {
                    collection.Entities.Add(row);
                }
                return collection;
            });

        return CreateSut(dataverse.Object);
    }

    private static (IdentityNormalizationService Sut, List<QueryExpression> Captured) CreateSutCapturingQueries(
        Entity[] rows)
    {
        var captured = new List<QueryExpression>();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                captured.Add(q);
                var collection = new EntityCollection();
                foreach (var row in rows) collection.Entities.Add(row);
                return collection;
            });

        return (CreateSut(dataverse.Object), captured);
    }

    private static IdentityNormalizationService CreateSut(IDataverseService dataverse)
        => new(
            dataverse,
            new NoOpTenantCache(),
            Array.Empty<IIdentityOrganizationResolver>(),
            Options.Create(new MembershipOptions()),
            NullLogger<IdentityNormalizationService>.Instance);

    /// <summary>
    /// Resolver over a strict identity double, with a Dataverse double that resolves NO systemuser —
    /// forcing the contact-only branch (b) under test.
    /// </summary>
    private static WorkforcePrincipalResolver CreateResolver(IIdentityNormalizationService identity)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        return new WorkforcePrincipalResolver(
            identity,
            dataverse.Object,
            new NoOpTenantCache(),
            NullLogger<WorkforcePrincipalResolver>.Instance);
    }

    private static ClaimsPrincipal WorkforceToken(Guid oid, string email)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim("oid", oid.ToString("D")),
                new Claim("tid", TestTenantId),
                new Claim("email", email)
            },
            authenticationType: "TestWorkforce"));

    /// <summary>
    /// Never serves a hit. The workforce contact path is deliberately uncached (ADR-003: cache data,
    /// never authorization decisions), so a cache that could answer would model something the
    /// production path does not do — and would let a stale entry, not the guard, decide the outcome.
    /// </summary>
    private sealed class NoOpTenantCache : ITenantCache
    {
        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => factory(ct);
    }
}
