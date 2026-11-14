using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 (F1): a content row of a message whose owner is a team. When that team is the
/// resolved Direct message owner team, the row is the Direct team's. While neither Direct setting is configured the answer is
/// today's, from configuration alone. While the lookup cannot answer, a team that may be the Direct team gives a refusal with a
/// stable event, never a business unit's default team. A Secure-owned message, a message owned by another team, and a row that is
/// not message content keep today's answer.
/// </summary>
/// <remarks>The REAL <see cref="RecordOwnershipResolver"/> over <see cref="FakeDirectory"/>, with the REAL
/// <see cref="DirectMessageOwnerTeam"/> over <see cref="DirectMessageOwnerTeamDouble"/>.</remarks>
public class RecordOwnershipResolverDirectParentTests
{
    private static readonly Guid DirectUnit = DirectMessageOwnerTeamDouble.DirectUnitId;
    private static readonly Guid DirectTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000001");
    private static readonly Guid DirectUnitDefaultTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000002");
    private static readonly Guid OtherNamedTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000003");
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000010");

    private readonly RecordingLogger _log = new();

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContentRow_OfAMessageTheResolvedDirectTeamOwns_IsTheDirectTeams()
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), Resolved()).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Owned);
        resolution.OwningTeamId.Should().Be(DirectTeam, "never the Direct business unit's default team");
    }

    [Fact]
    public async Task ParticipantRow_AskedForItsOwnRow_OfAMessageTheResolvedDirectTeamOwns_IsTheDirectTeams()
    {
        var row = new Entity("sprk_communicationparticipant", Guid.NewGuid())
        {
            ["sprk_communication"] = new EntityReference("sprk_communication", MessageId),
        };

        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), Resolved()).ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(row), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(DirectTeam);
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task OnlyOneSettingSet_AMessageOwnedByATeamThatMayBeTheDirectTeam_Refuses_NeverTheDefaultTeam(string onlySet)
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ResolveOwnerAsync(RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.OwningTeamId.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Event.Id == 5418 && e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task LookupFaults_AMessageOwnedByATeamThatMayBeTheDirectTeam_TheFaultPropagates_NotARefusal()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        world.Faults = true;

        var act = () => Resolver(World().WithMessage(DirectUnit, DirectTeam), world.Lookup()).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("a faulted read is retried by the caller, never a permanent refusal");
        _log.Entries.Should().NotContain(e => e.Event.Id == 5418);
    }

    [Fact]
    public async Task GateRecord_OfAMessageTheResolvedDirectTeamOwns_IsTheDirectTeams()
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), Resolved()).ResolveOwnerAsync(
            GateContext(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Owned);
        resolution.OwningTeamId.Should().Be(DirectTeam, "a gate record of a Direct-team message is the Direct team's");
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task GateRecord_WhenTheDirectTeamCannotBeIdentified_RefusesWithEvent5418(string onlySet)
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ResolveOwnerAsync(GateContext(), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        resolution.OwningTeamId.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Event.Id == 5418 && e.Level == LogLevel.Error);
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotConfigured_KeepsTodaysAnswer_AndReadsNothingMore()
    {
        var withoutLookup = World().WithMessage(DirectUnit, DirectTeam);
        var baseline = await withoutLookup.Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);
        var directory = World().WithMessage(DirectUnit, DirectTeam);
        var client = new CountingClient();

        var resolution = await Resolver(directory, Unconfigured(client)).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.Should().BeEquivalentTo(baseline);
        resolution.OwningTeamId.Should().Be(DirectUnitDefaultTeam, "the rule is off while both Direct settings are unset");
        client.Reads.Should().Be(0);
        directory.QueriedEntities.Should().Equal(withoutLookup.QueriedEntities);
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task OnlyOneSettingSet_AMessageOwnedByItsUnitsDefaultTeam_KeepsTodaysAnswer()
    {
        var resolution = await Resolver(
                World().WithMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam),
                DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey))
            .ResolveOwnerAsync(RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.ChildTeam, "a default team is never the Direct team");
        _log.Entries.Should().NotContain(e => e.Event.Id == 5418);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecureOwnedMessage_KeepsTheSecureAnswer(bool resolved)
    {
        var lookup = resolved ? Resolved() : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey);

        var resolution = await Resolver(World().WithMessage(FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam), lookup)
            .ResolveOwnerAsync(RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam);
        _log.Entries.Should().NotContain(e => e.Event.Id == 5418);
    }

    [Fact]
    public async Task MessageOwnedByAnotherTeam_WithAResolvedLookup_KeepsTodaysAnswer()
    {
        var resolution = await Resolver(World().WithMessage(FakeDirectory.ChildBu, OtherNamedTeam), Resolved()).ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", MessageId), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.ChildTeam);
    }

    [Fact]
    public async Task ARowThatIsNotMessageContent_FiledUnderADirectTeamMessage_KeepsTodaysAnswer()
    {
        var row = new Entity("sprk_event", Guid.NewGuid())
        {
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", MessageId),
        };

        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), Resolved()).ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(row), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(DirectUnitDefaultTeam, "the rule reads message content rows only");
    }

    // ── uac-r2 197a-2 (#2728, owner option a): a document of an UNFILED message the Direct team owns ─────────────────────────
    // The #2468 demotion (an unfiled communication counts for secure-if-any only) is skipped ONLY when the message is positively
    // owned by the resolved Direct team; every other case demotes as before. Asked as the reconciler asks (KeepCreator), so a
    // demoted answer is "unchanged".

    [Fact(DisplayName = "#2728 must-fire: a document linked only to an unfiled message the resolved Direct team owns is the Direct team's")]
    [Trait("status", "issue-2728")]
    public async Task Document_OfAnUnfiledMessageTheResolvedDirectTeamOwns_IsTheDirectTeams()
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), Resolved()).ResolveOwnerAsync(
            DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Owned, resolution.Reason);
        resolution.OwningTeamId.Should().Be(DirectTeam, "the document follows its Direct message, never the Direct unit's default team");
    }

    [Fact(DisplayName = "#2728 must-not-fire: a document of an unfiled message owned by ANOTHER team is demoted as before")]
    [Trait("status", "issue-2728")]
    public async Task Document_OfAnUnfiledMessageOwnedByAnotherTeam_IsDemotedAsBefore()
    {
        var resolution = await Resolver(World().WithMessage(FakeDirectory.ChildBu, OtherNamedTeam), Resolved()).ResolveOwnerAsync(
            DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "an unfiled message that is not the Direct team's never names it");
    }

    [Fact(DisplayName = "#2728 must-not-fire: with no Direct setting configured the document is demoted, and the lookup reads nothing")]
    [Trait("status", "issue-2728")]
    public async Task Document_WhenNoDirectSettingIsConfigured_IsDemotedAsBefore_AndNothingMoreIsRead()
    {
        var withoutLookup = World().WithMessage(DirectUnit, DirectTeam);
        await withoutLookup.Resolver().ResolveOwnerAsync(DocumentOfMessage(), CancellationToken.None);
        var directory = World().WithMessage(DirectUnit, DirectTeam);
        var client = new CountingClient();

        var resolution = await Resolver(directory, Unconfigured(client)).ResolveOwnerAsync(DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        client.Reads.Should().Be(0);
        directory.QueriedEntities.Should().Equal(withoutLookup.QueriedEntities, "the rule off reads exactly what it read before");
    }

    [Fact(DisplayName = "#2728 must-not-fire: without a Direct lookup composed the document is demoted as before")]
    [Trait("status", "issue-2728")]
    public async Task Document_WithoutADirectLookup_IsDemotedAsBefore()
    {
        var resolution = await World().WithMessage(DirectUnit, DirectTeam).Resolver().ResolveOwnerAsync(
            DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
    }

    [Theory(DisplayName = "#2728 must-not-fire: while the Direct team cannot be identified the document is demoted, never refused or the Direct team")]
    [Trait("status", "issue-2728")]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task Document_WhileTheDirectTeamCannotBeIdentified_IsDemotedAsBefore(string onlySet)
    {
        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ResolveOwnerAsync(DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "only a POSITIVE Direct answer skips the demotion");
        _log.Entries.Should().NotContain(e => e.Event.Id == 5418);
    }

    [Fact(DisplayName = "#2728 must-not-fire: when the Direct lookup faults the document is demoted, never the Direct team")]
    [Trait("status", "issue-2728")]
    public async Task Document_WhenTheDirectLookupFaults_IsDemotedAsBefore()
    {
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;

        var resolution = await Resolver(World().WithMessage(DirectUnit, DirectTeam), lookup.Lookup()).ResolveOwnerAsync(
            DocumentOfMessage(), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.OwningTeamId.Should().BeNull();
    }

    [Fact(DisplayName = "#2728 must-not-fire: when the message's owner read faults the fault propagates, as before; never the Direct team")]
    [Trait("status", "issue-2728")]
    public async Task Document_WhenTheMessagesOwnerReadFaults_TheFaultPropagates()
    {
        // The message's owner-facts read alone faults (its filing read, the whole row, answers). Before #2728 the demoted path
        // reads the same row's owner facts for secure-if-any, so the same fault propagated there.
        var directory = World().WithMessage(DirectUnit, DirectTeam);
        var resolver = ResolverFaulting(directory, Resolved(),
            q => q.EntityName == "sprk_communication" && !q.ColumnSet.AllColumns ? new TimeoutException("the owner read timed out") : null);

        var act = () => resolver.ResolveOwnerAsync(DocumentOfMessage(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>("a fault is not an answer: never the Direct team on a guess");
    }

    [Fact(DisplayName = "#2728 must-not-fire: a message that cannot be found still refuses with its own code")]
    [Trait("status", "issue-2728")]
    public async Task Document_OfAMessageThatCannotBeFound_StillRefuses()
    {
        var resolution = await Resolver(World(), Resolved()).ResolveOwnerAsync(DocumentOfMessage(), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.CommunicationFilingUnresolved);
    }

    [Fact(DisplayName = "#2728 never loosens: a Direct-owned unfiled message plus a Secure second parent gives the Secure team")]
    [Trait("status", "issue-2728")]
    public async Task Document_OfADirectOwnedUnfiledMessage_AndASecureSecondParent_IsTheSecureTeams()
    {
        var secureEvent = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000020");
        var question = DocumentOfMessage(("sprk_relatedevent", new EntityReference("sprk_event", secureEvent)));
        question.Parents[0].Should().Be(new RecordOwnershipParent("sprk_communication", MessageId),
            "precondition: the message is the document's primary parent, so the #2468 branch is the one asked");

        var resolution = await Resolver(
                World().WithMessage(DirectUnit, DirectTeam)
                    .WithRecord("sprk_event", secureEvent, FakeDirectory.SecureBu, owningTeam: FakeDirectory.SecureNamedTeam),
                Resolved())
            .ResolveOwnerAsync(question, CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam, "secure-if-any over the other parents still holds");
        resolution.IsSecureOwner.Should().BeTrue();
    }

    [Fact(DisplayName = "#2728 never loosens: a Direct-owned unfiled message plus a Secure canonical (Secure-only) gives the Secure team")]
    [Trait("status", "issue-2728")]
    public async Task Document_OfADirectOwnedUnfiledMessage_AndASecureCanonical_IsTheSecureTeams()
    {
        var canonical = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000021");
        var question = DocumentOfMessage(("sprk_canonicaldocument", new EntityReference("sprk_document", canonical)));

        var resolution = await Resolver(
                World().WithMessage(DirectUnit, DirectTeam)
                    .WithRecord("sprk_document", canonical, FakeDirectory.SecureBu, owningTeam: FakeDirectory.SecureNamedTeam),
                Resolved())
            .ResolveOwnerAsync(question, CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam);
    }

    [Fact(DisplayName = "#2728 must-fire: on the re-file path the document follows its Direct message, its owner read committed")]
    [Trait("status", "issue-2728")]
    public async Task Refile_LeavingADocumentLinkedOnlyToADirectOwnedUnfiledMessage_IsTheDirectTeams_ItsOwnerReadCommitted()
    {
        var matter = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000030");
        var document = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000031");
        var directory = World().WithMessage(DirectUnit, DirectTeam)
            .WithRecord("sprk_matter", matter, FakeDirectory.ChildBu, owningTeam: FakeDirectory.ChildTeam)
            .WithRecord("sprk_document", document, FakeDirectory.ChildBu, owningTeam: FakeDirectory.ChildTeam, extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", matter),
                ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", MessageId),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = document,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = null },
        };

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_document", document, new Dictionary<string, object?> { ["sprk_matter"] = null });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(DirectTeam, resolution.Reason);
        var ownerReads = directory.RecordQueries
            .Where(q => q.EntityName == "sprk_communication" && !q.ColumnSet.AllColumns)
            .ToList();
        ownerReads.Should().NotBeEmpty().And.OnlyContain(q => !q.NoLock, "every parent fact on the re-file path is a committed read");
    }

    /// <summary>A document linked to the message (<c>sprk_relatedcommunication</c>) and to <paramref name="others"/>, asked as the
    /// reconciler asks for an existing row (<c>SecureChildReconciler.ContextFor</c>: the row's own question, KeepCreator).</summary>
    private static RecordOwnershipContext DocumentOfMessage(params (string Column, EntityReference Target)[] others)
    {
        var row = new Entity("sprk_document", Guid.Parse("d1d1d1d1-0054-4054-8054-0000000000d1"))
        {
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", MessageId),
        };
        foreach (var (column, target) in others)
            row[column] = target;
        return RecordOwnershipContext.ForChild(row) with { WhenUnfiled = UnfiledOwnership.KeepCreator };
    }

    /// <summary>The REAL resolver over <paramref name="directory"/> with <paramref name="lookup"/>; <paramref name="faultFor"/> may
    /// fault one query.</summary>
    private RecordOwnershipResolver ResolverFaulting(
        FakeDirectory directory, DirectMessageOwnerTeam lookup, Func<Microsoft.Xrm.Sdk.Query.QueryExpression, Exception?> faultFor)
    {
        var entities = new Moq.Mock<IGenericEntityService>(Moq.MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), Moq.It.IsAny<CancellationToken>()))
            .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) =>
                faultFor(query) is { } fault
                    ? Task.FromException<EntityCollection>(fault)
                    : Task.FromResult(directory.Answer(query)));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureRecord:BusinessUnitName"] = FakeDirectory.SecureBuName,
                ["SecureRecord:OwnerTeamName"] = FakeDirectory.SecureOwnerTeamName,
            })
            .Build();
        return new RecordOwnershipResolver(entities.Object, configuration, _log, lookup);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The standard directory plus the Direct business unit, its default team and its named Direct team, and another
    /// named (non-default) Owner team in the child unit.</summary>
    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithBusinessUnit(DirectUnit, DirectMessageOwnerTeamDouble.BusinessUnitName, parent: FakeDirectory.GeneralBu)
        .WithTeam(DirectUnitDefaultTeam, DirectUnit, isDefault: true, teamType: 0, DirectMessageOwnerTeamDouble.BusinessUnitName)
        .WithTeam(DirectTeam, DirectUnit, isDefault: false, teamType: 0, DirectMessageOwnerTeamDouble.OwnerTeamName)
        .WithTeam(OtherNamedTeam, FakeDirectory.ChildBu, isDefault: false, teamType: 0, "Another owner team");

    /// <summary>The gate writer's question: the communication first, as content of it (DecisionRecordWriter, GateDecisionRecordSharing).</summary>
    private static RecordOwnershipContext GateContext() =>
        RecordOwnershipContext.ForParents([new RecordOwnershipParent("sprk_communication", MessageId)]) with { KeepCreatorUnlessTargetIsTeamOwned = true };

    private RecordOwnershipResolver Resolver(FakeDirectory directory, DirectMessageOwnerTeam lookup) =>
        directory.Resolver(logger: _log, directMessageOwnerTeam: lookup);

    private static DirectMessageOwnerTeam Resolved() => new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).Lookup();

    /// <summary>The lookup with no Direct setting, over a client that counts every read.</summary>
    private static DirectMessageOwnerTeam Unconfigured(CountingClient client) => new(
        client, new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<DirectMessageOwnerTeam>.Instance);

    private sealed class CountingClient : DataverseWebApiClient
    {
        public CountingClient()
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new NoCredential())
        {
        }

        public int Reads { get; private set; }

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null, int? top = null,
            int? skip = null, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new List<T>());
        }
    }

    private sealed class NoCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This client issues no HTTP.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This client issues no HTTP.");
    }

    private sealed class RecordingLogger : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}

/// <summary>Seeds the one message the tests ask about.</summary>
internal static class DirectParentWorldExtensions
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-000000000010");

    public static FakeDirectory WithMessage(this FakeDirectory directory, Guid owningBusinessUnit, Guid owningTeam) =>
        directory.WithRecord("sprk_communication", MessageId, owningBusinessUnit, owningTeam: owningTeam);
}
