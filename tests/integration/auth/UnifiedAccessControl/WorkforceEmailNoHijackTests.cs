using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Workforce contact-by-email resolution — finding A-18 (spec FR-12).
///
/// <para><b>What is wrong.</b> The workforce contact-only branch falls back to a raw
/// <c>emailaddress1</c> match and returns whatever contact it finds, asking nothing about whether
/// that contact already belongs to somebody else. The CIAM plane refuses exactly this
/// (<c>ExternalParticipationService.ResolveExternalContactAsync</c> — "no email hijack of a bound
/// Contact"); the workforce plane does not. Under the multitenant workforce model a token's email
/// claim is not domain-verified across a foreign tenant, so a caller carrying
/// <c>email=victim@firm.com</c> inherits the victim's contact and every grant hanging off it.</para>
///
/// <para><b>Why task 001 could not pin it.</b> The decision was four lines welded to a Dataverse
/// query, observable only by standing up Dataverse. Task 013 extracted
/// <see cref="IdentityNormalizationService.DecideWorkforceEmailMatch"/> as a PURE function — which is
/// what makes the assertions below possible at all. <c>internal</c> + <c>InternalsVisibleTo</c>, no
/// reflection into privates (ADR-038 §7 ban B8), no transport double (ban B1).</para>
///
/// <para><b>Read this before trusting a green run here.</b> The pure-decision tests use no double at
/// all, so nothing can be vacuous in them. The service-level tests use a <b>strict</b>
/// <see cref="IDataverseService"/> mock: an unmodelled query THROWS rather than returning an empty
/// collection, so a query that quietly stops selecting the binding column fails loudly instead of
/// resolving to "unbound". What none of this can prove is stated on
/// <see cref="WhatTheseTestsCannotFalsify"/> — read it.</para>
/// </summary>
public class WorkforceEmailNoHijackTests
{
    private static readonly Guid CallerOid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VictimOid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VictimContactId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherContactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string SharedEmail = "victim@firm.example";

    // ─────────────────────────────────────────────────────────────────────────────
    // The decision itself — pure, no doubles, nothing to be vacuous about
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ CHARACTERIZATION — pins A-18. A contact firmly bound to somebody else's oid is resolved to
    /// anyway. FLIPPED BY TASK 013 (FR-12): this MUST become
    /// <c>DenyBoundToDifferentOid</c>.
    /// </summary>
    [Fact]
    public void Characterization_DecideWorkforceEmailMatch_ResolvesContactBoundToADifferentOid()
    {
        var matches = new[]
        {
            new IdentityNormalizationService.WorkforceContactEmailMatch(VictimContactId, VictimOid)
        };

        var decision = IdentityNormalizationService.DecideWorkforceEmailMatch(matches, CallerOid);

        decision.Should().Be(
            IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
            "A-18: the workforce path resolves an email match without ever reading the binding — " +
            "this is the account-takeover vector, pinned here so the flip is visible");
    }

    /// <summary>
    /// ⚠️ CHARACTERIZATION — pins the ambiguity half of A-18: more than one contact carries the
    /// email and the code picks one. FLIPPED BY TASK 013 (FR-12): MUST become
    /// <c>DenyAmbiguousEmail</c>.
    /// </summary>
    [Fact]
    public void Characterization_DecideWorkforceEmailMatch_PicksOneWhenSeveralContactsShareTheEmail()
    {
        var matches = new[]
        {
            new IdentityNormalizationService.WorkforceContactEmailMatch(VictimContactId, null),
            new IdentityNormalizationService.WorkforceContactEmailMatch(OtherContactId, null)
        };

        var decision = IdentityNormalizationService.DecideWorkforceEmailMatch(matches, CallerOid);

        decision.Should().Be(
            IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve,
            "pre-fix the count is never consulted — whichever row Dataverse returned first wins");
    }

    /// <summary>
    /// ⚠️ CHARACTERIZATION — a caller with no usable oid of their own still inherits an unbound
    /// contact by email alone. FLIPPED BY TASK 013 (FR-12): MUST become
    /// <c>DenyUnidentifiableCaller</c>.
    /// </summary>
    [Fact]
    public void Characterization_DecideWorkforceEmailMatch_ResolvesForACallerWithNoUsableOid()
    {
        var matches = new[]
        {
            new IdentityNormalizationService.WorkforceContactEmailMatch(VictimContactId, null)
        };

        var decision = IdentityNormalizationService.DecideWorkforceEmailMatch(matches, Guid.Empty);

        decision.Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.Resolve);
    }

    /// <summary>
    /// Not a characterization — this already holds and must keep holding after the flip. No contact
    /// carries the email, so there is nothing to resolve to.
    /// </summary>
    [Fact]
    public void DecideWorkforceEmailMatch_WithNoMatches_ReturnsNoMatch()
    {
        var decision = IdentityNormalizationService.DecideWorkforceEmailMatch(
            Array.Empty<IdentityNormalizationService.WorkforceContactEmailMatch>(), CallerOid);

        decision.Should().Be(IdentityNormalizationService.WorkforceEmailMatchDecision.NoMatch);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The query — does it actually READ what the decision needs?
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The guard cannot be load-bearing if the query never fetches the binding column: every match
    /// would arrive looking unbound, and the no-hijack check would pass everything while reading as
    /// though it were enforcing something. That is the precise shape of a vacuous control, so it is
    /// asserted directly on the emitted <see cref="QueryExpression"/> rather than inferred.
    /// </summary>
    [Fact]
    public async Task ContactByEmailQuery_SelectsTheOidBindingColumn()
    {
        var (sut, captured) = CreateSutCapturingContactQuery(rows: Array.Empty<Entity>());

        await sut.TryResolveContactByWorkforceIdentityAsync(CallerOid, SharedEmail, CancellationToken.None);

        var emailQuery = captured.Single(q => HasEmailCondition(q));
        emailQuery.ColumnSet.Columns.Should().Contain(
            "azureactivedirectoryobjectid",
            "without the binding column every match looks unbound and the no-hijack check is a no-op");
    }

    /// <summary>
    /// <c>TopCount = 1</c> makes an ambiguous email indistinguishable from an unambiguous one — the
    /// second row simply never arrives. Reading two is the cheapest query that can tell them apart.
    /// </summary>
    [Fact]
    public async Task ContactByEmailQuery_ReadsMoreThanOneRowSoAmbiguityIsVisible()
    {
        var (sut, captured) = CreateSutCapturingContactQuery(rows: Array.Empty<Entity>());

        await sut.TryResolveContactByWorkforceIdentityAsync(CallerOid, SharedEmail, CancellationToken.None);

        var emailQuery = captured.Single(q => HasEmailCondition(q));
        emailQuery.TopCount.Should().BeGreaterThan(
            1, "a single-row read cannot distinguish 'exactly one contact' from 'several'");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // End to end through the service, with a STRICT Dataverse double
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ CHARACTERIZATION — the whole finding, end to end: a caller presenting the victim's email
    /// receives the victim's contact. FLIPPED BY TASK 013 (FR-12): MUST return <c>null</c>.
    /// </summary>
    [Fact]
    public async Task Characterization_TryResolveContactByWorkforceIdentity_ReturnsAContactBoundToAnotherOid()
    {
        var sut = CreateSutWithContacts(ContactRow(VictimContactId, VictimOid));

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, SharedEmail, CancellationToken.None);

        resolved.Should().Be(
            VictimContactId,
            "A-18: the caller's email matched a contact bound to a DIFFERENT oid and was handed it anyway");
    }

    /// <summary>
    /// Already correct and must stay correct after the flip: an UNBOUND contact matched by verified
    /// email still resolves. This is the legitimate Type-2 onboarding path — a customer employee
    /// with no <c>azureactivedirectoryobjectid</c> yet — and over-denying it would break the feature
    /// the guard is meant to protect.
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
    /// The caller's OWN oid-bound contact resolves — and does so on the oid cross-reference, never
    /// reaching the email fallback at all.
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
    /// A contact-by-email query that cannot be read must not resolve. This one is genuinely
    /// unchanged by task 013 — it is asserted because the refactor moves the failure path, and a
    /// refactor that turned "unreadable" into "no match" would look identical from the outside.
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
    /// A null/blank email never reaches the fallback — only the oid cross-reference runs. Pinned so
    /// a future change cannot start matching on an empty string, which in OData would be a filter
    /// that matches every contact with no email.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryResolveContactByWorkforceIdentity_WithNoEmail_RunsOnlyTheOidCrossReference(string? email)
    {
        var (sut, captured) = CreateSutCapturingContactQuery(rows: Array.Empty<Entity>());

        var resolved = await sut.TryResolveContactByWorkforceIdentityAsync(
            CallerOid, email, CancellationToken.None);

        resolved.Should().BeNull();
        captured.Should().NotContain(q => HasEmailCondition(q), "there is no verified email to match on");
    }

    /// <summary>
    /// <b>What these tests cannot falsify.</b> Kept as executable documentation so the limits travel
    /// with the suite rather than living in a PR description nobody re-reads.
    ///
    /// <list type="number">
    /// <item><b>Dataverse's own email matching.</b> Every double here decides for itself which rows
    /// come back for <c>emailaddress1 eq '...'</c>. Real Dataverse matches strings under a
    /// case-INSENSITIVE SQL collation, so <c>Victim@Firm.example</c> and <c>victim@firm.example</c>
    /// are the same row there and could be different rows in a naive double. Nothing in this file
    /// proves which. The guard is deliberately built not to care: it compares parsed
    /// <see cref="Guid"/>s, never strings, so oid comparison carries no case or normalization
    /// assumption — but the *matching* that produces the candidate set is the platform's, untested
    /// here. Only a live-Dataverse test can close this.</item>
    ///
    /// <item><b>That the email claim means anything.</b> The whole finding turns on
    /// <c>email</c>/<c>preferred_username</c>/<c>upn</c> not being domain-verified across a foreign
    /// tenant under the multitenant workforce model. That is a property of Entra and of the app
    /// registration (prerequisite E-6), not of this code, and no unit test can observe it.</item>
    ///
    /// <item><b>That an absent attribute means UNBOUND.</b> Dataverse omits null attributes from a
    /// returned <see cref="Entity"/>, so the query helper reads "no
    /// <c>azureactivedirectoryobjectid</c> on the row" as "this contact is unbound". If the platform
    /// ever returned the row WITHOUT that column for some other reason — a column-level security
    /// mask, a field-permission trim — a bound contact would read as unbound and the guard would
    /// pass it. The doubles here reproduce the documented shape, which is not the same as verifying
    /// it.</item>
    ///
    /// <item><b>The CIAM binding column.</b> This guard reads only the workforce binding
    /// (<c>azureactivedirectoryobjectid</c>). A contact bound to a CIAM <c>oid</c>
    /// (<c>sprk_externalobjectid</c>) and NOT to a workforce oid is still reachable by workforce
    /// email match. That is the exact mirror A-18 and FR-12 scope, and the cross-plane case is
    /// reported separately — it is not covered here.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void WhatTheseTestsCannotFalsify() => true.Should().BeTrue();

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers — deliberately STRICT: an unmodelled query throws, it does not match-everything
    // ─────────────────────────────────────────────────────────────────────────────

    private static Entity ContactRow(Guid contactId, Guid? boundOid)
    {
        var row = new Entity("contact") { Id = contactId };
        row["contactid"] = contactId;
        // Only set when bound — Dataverse omits null attributes, and the guard reads absence as
        // "unbound". Writing an explicit null here would model a shape the platform never sends.
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
    /// Strict double modelling exactly two contact queries — the oid cross-reference (always empty,
    /// forcing the email fallback under test) and the email match. Any OTHER query throws
    /// <c>MockException</c> rather than returning an empty collection: a permissive fallback here is
    /// how a test passes while asserting nothing.
    /// </summary>
    private static IdentityNormalizationService CreateSutWithContacts(params Entity[] emailMatches)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.Is<QueryExpression>(q => HasOidCondition(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                // The oid cross-reference resolves only a contact actually bound to THIS oid.
                var wanted = (Guid)q.Criteria.Conditions.First(c => c.AttributeName == "azureactivedirectoryobjectid").Values[0];
                var collection = new EntityCollection();
                foreach (var row in emailMatches)
                {
                    if (row.Contains("azureactivedirectoryobjectid") &&
                        Equals(row["azureactivedirectoryobjectid"], wanted))
                    {
                        collection.Entities.Add(row);
                    }
                }
                return collection;
            });

        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.Is<QueryExpression>(q => HasEmailCondition(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var collection = new EntityCollection();
                foreach (var row in emailMatches.Take(Math.Max(q.TopCount ?? int.MaxValue, 0)))
                {
                    collection.Entities.Add(row);
                }
                return collection;
            });

        return CreateSut(dataverse.Object);
    }

    private static (IdentityNormalizationService Sut, List<QueryExpression> Captured) CreateSutCapturingContactQuery(
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
    /// Never serves a hit. The workforce contact path is deliberately uncached (ADR-003: authorization
    /// decisions are not cached), so a cache that could answer would be modelling something the
    /// production path does not do.
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
