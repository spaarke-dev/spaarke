using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Ontology task 170 (D-196): <see cref="DataverseServiceClientImpl.GetEntitySetNameAsync"/> asks Dataverse once per table for
/// the life of the instance (the BFF's singleton, so once per process). One decision commit asked <c>RetrieveEntityRequest</c>
/// for <c>sprk_matter</c> six times. A failure, or an empty name, is never remembered.
/// </summary>
/// <remarks>The shipped class with only its metadata read (<c>RetrieveEntitySetNameAsync</c>, <c>protected virtual</c>) answered
/// in memory and counted; nothing connects (the connection is deferred to first use, and no test reaches it).</remarks>
[Trait("status", "new")]
public sealed class EntitySetNameCacheTests
{
    [Fact]
    public async Task ATableResolvedOnce_IsNotAskedAgain_AndEachTableIsAskedOnce()
    {
        var sut = new CountingClient();

        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");
        (await sut.GetEntitySetNameAsync("contact")).Should().Be("contacts");
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");

        sut.Asked.Should().Equal("sprk_matter", "contact");
    }

    [Fact]
    public async Task AFailedLookup_IsNotRemembered_TheNextCallAsksAgain()
    {
        var sut = new CountingClient { FailNext = new InvalidOperationException("Failed to query metadata for entity 'sprk_matter'") };

        var first = () => sut.GetEntitySetNameAsync("sprk_matter");
        await first.Should().ThrowAsync<InvalidOperationException>("the failure still surfaces, as before");
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");

        sut.Asked.Should().Equal("sprk_matter", "sprk_matter");
    }

    [Fact]
    public async Task AnEmptyName_IsNotRemembered()
    {
        var sut = new CountingClient();
        sut.Names["sprk_noweb"] = "";

        (await sut.GetEntitySetNameAsync("sprk_noweb")).Should().BeEmpty();
        sut.Names["sprk_noweb"] = "sprk_nowebs";
        (await sut.GetEntitySetNameAsync("sprk_noweb")).Should().Be("sprk_nowebs");

        sut.Asked.Should().Equal("sprk_noweb", "sprk_noweb");
    }

    [Fact]
    public async Task AnEntryOlderThanTheAdr009Lifetime_IsAskedAgain_AFresherOneIsNot()
    {
        var clock = new StepClock();
        var sut = new CountingClient { EntitySetNameClock = clock };

        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");
        clock.Advance(TimeSpan.FromMinutes(14));
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters");
        sut.Asked.Should().Equal("sprk_matter");

        // Dataverse now says the table was renamed; the cached entry is only trusted for 15 minutes in all.
        sut.Names["sprk_matter"] = "sprk_matters2";
        clock.Advance(TimeSpan.FromMinutes(1));
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters2", "an entry is never older than 15 minutes (ADR-009 metadata row)");
        sut.Asked.Should().Equal("sprk_matter", "sprk_matter");

        // and the refreshed entry gets a fresh lifetime
        clock.Advance(TimeSpan.FromMinutes(14));
        (await sut.GetEntitySetNameAsync("sprk_matter")).Should().Be("sprk_matters2");
        sut.Asked.Should().HaveCount(2);
    }

    [Fact]
    public void TheLifetime_IsNoMoreThanTheAdr009MetadataCeiling() =>
        DataverseServiceClientImpl.EntitySetNameLifetime.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));

    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>The shipped client with its metadata read answered in memory.</summary>
    private sealed class CountingClient : DataverseServiceClientImpl
    {
        public CountingClient()
            : base(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                    ["Graph:ManagedIdentity:Enabled"] = "true",
                }).Build(),
                NullLogger<DataverseServiceClientImpl>.Instance,
                managedIdentityCredential: new NoCredential())
        {
        }

        public ConcurrentDictionary<string, string> Names { get; } = new(StringComparer.Ordinal)
        {
            ["sprk_matter"] = "sprk_matters",
            ["contact"] = "contacts",
        };

        public ConcurrentQueue<string> AskedQueue { get; } = new();

        public IReadOnlyList<string> Asked => AskedQueue.ToList();

        public Exception? FailNext { get; set; }

        protected override Task<string> RetrieveEntitySetNameAsync(string entityLogicalName, CancellationToken ct)
        {
            AskedQueue.Enqueue(entityLogicalName);
            if (FailNext is { } failure)
            {
                FailNext = null;
                return Task.FromException<string>(failure);
            }

            return Task.FromResult(Names[entityLogicalName]);
        }
    }

    /// <summary>Never asked: no test connects.</summary>
    private sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no test connects to Dataverse");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no test connects to Dataverse");
    }
}
