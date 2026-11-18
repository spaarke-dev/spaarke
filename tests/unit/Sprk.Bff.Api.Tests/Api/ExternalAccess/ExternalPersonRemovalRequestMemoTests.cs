// GitHub #2845 — the removed-marker writes of ExternalPersonRemovalService forget the request's access memo.
//
// The composition reads a contact's removed marker once per request (RequestAccessMemo). A removal or a reinstatement that
// writes the marker inside the same request must make the next read of it go back to Dataverse — whatever the write
// answered, and also when the write THREW (it may still have landed). One test per write site, on the real service, with
// only the identity store and the Dataverse grant read substituted.

using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class ExternalPersonRemovalRequestMemoTests
{
    private static readonly Guid Contact = Guid.Parse("28450000-0000-4000-8000-0000000003c1");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_AfterTheMarkerWrite_TheNextRemovedMarkerReadInTheRequestGoesToDataverse(bool writeThrows)
    {
        var world = new World(removedOn: null);
        await world.ReadMarkerInTheRequestAsync();
        await world.ReadMarkerInTheRequestAsync();
        world.MarkerReads.Should().Be(1, "precondition: the second read was answered from the request's memo");

        world.Store.Setup(s => s.WriteRemovedMarkerAsync(Contact, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(writeThrows
                ? Task.FromException<StoreWriteResult>(new HttpRequestException("the write timed out"))
                : Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed)));

        var remove = () => world.Service().RemoveAsync(Contact, correlationId: null, CancellationToken.None);
        if (writeThrows)
        {
            await remove.Should().ThrowAsync<HttpRequestException>();
        }
        else
        {
            (await remove()).Status.Should().Be(PersonAdministrationStatus.Incomplete);
        }

        await world.ReadMarkerInTheRequestAsync();
        world.MarkerReads.Should().Be(2, "the marker write made the request forget the marker it had read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reinstatement_AfterTheMarkerClear_TheNextRemovedMarkerReadInTheRequestGoesToDataverse(bool writeThrows)
    {
        var world = new World(removedOn: DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        await world.ReadMarkerInTheRequestAsync();
        await world.ReadMarkerInTheRequestAsync();
        world.MarkerReads.Should().Be(1, "precondition: the second read was answered from the request's memo");

        world.Store.Setup(s => s.WriteRemovedMarkerIfUnchangedAsync(Contact, "W/\"7\"", null, It.IsAny<CancellationToken>()))
            .Returns(writeThrows
                ? Task.FromException<StoreWriteResult>(new HttpRequestException("the write timed out"))
                : Task.FromResult(StoreWriteResult.Written));

        var reinstate = () => world.Service().ReinstateAsync(Contact, CancellationToken.None);
        if (writeThrows)
        {
            await reinstate.Should().ThrowAsync<HttpRequestException>();
        }
        else
        {
            (await reinstate()).Status.Should().Be(PersonAdministrationStatus.Completed);
        }

        await world.ReadMarkerInTheRequestAsync();
        world.MarkerReads.Should().Be(2, "the marker clear made the request forget the marker it had read");
    }

    private sealed class World
    {
        private readonly HttpContext _request = new DefaultHttpContext();
        private readonly Mock<DataverseWebApiClient> _dataverse;
        private int _markerReads;

        public World(DateTimeOffset? removedOn)
        {
            Store.Setup(s => s.GetContactAsync(Contact, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ContactLookup.Of(new ContactBindingRow(Contact, StateCode: 0, RawOid: null, RawPlane: null, ETag: "W/\"7\"")
                {
                    RemovedOn = removedOn,
                }));
            Store.Setup(s => s.ReadRemovedMarkerAsync(Contact, It.IsAny<CancellationToken>()))
                .ReturnsAsync(removedOn is null ? RemovedMarkerRead.NotRemoved : RemovedMarkerRead.Removed);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build();
            _dataverse = new Mock<DataverseWebApiClient>(config, NullLogger<DataverseWebApiClient>.Instance, new StaticTokenCredential(), null!);
            // The reinstatement's live "holds an active grant" read: none.
            _dataverse.Setup(d => d.QueryAsync<ExternalGrantRow>(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExternalGrantRow>());
        }

        public Mock<IContactIdentityStore> Store { get; } = new(MockBehavior.Strict);

        /// <summary>How many times the removed marker was read from Dataverse through the request's memo.</summary>
        public int MarkerReads => Volatile.Read(ref _markerReads);

        /// <summary>The composition's read of the marker: once per request through the request's memo.</summary>
        public Task<RemovedMarkerRead> ReadMarkerInTheRequestAsync()
            => RequestAccessMemo.For(_request)!.GetOrReadAsync(
                RequestAccessMemo.RemovedMarkerKey(Contact),
                _ =>
                {
                    Interlocked.Increment(ref _markerReads);
                    return Task.FromResult(RemovedMarkerRead.NotRemoved);
                },
                marker => marker is RemovedMarkerRead.NotRemoved or RemovedMarkerRead.Removed,
                CancellationToken.None);

        public ExternalPersonRemovalService Service()
        {
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(_request);
            var participations = new ExternalParticipationService(
                new HttpClient(), Mock.Of<ITenantCache>(), configuration: null!, credential: null!, accessor.Object,
                NullLogger<ExternalParticipationService>.Instance, NoFilingEntities());

            // The removal needs the shared revoke to exist; these paths end at the marker write, before any revoke.
            var revocation = new ExternalGrantRevocationService(
                _dataverse.Object, speContainerMembership: null!, participations, assignedAccess: null!, containerResolver: null!,
                TimeProvider.System, Store.Object, NullLogger.Instance);

            return new ExternalPersonRemovalService(
                _dataverse.Object, revocation, participations, Store.Object, Mock.Of<IMembershipCacheInvalidator>(),
                TimeProvider.System, NullLogger.Instance);
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
