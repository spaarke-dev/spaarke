using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 018b (UAC-r2 condition (b) item i): an Office quick-created matter's owner membership event is
/// published EXACTLY ONCE — by the unified create's post-create step, with the owner team its write bound — and the
/// endpoint's own publish is gone. A project's create publishes none (<c>MembershipPublishingTables</c> names no project).
/// </summary>
/// <remarks>
/// The publish is fire-and-forget (ADR-034 Q2), so the test waits for the first event and then a settle window for a
/// second. The owner read-back the old endpoint publish made (<c>knownOwner: null</c>) is answered here, so a re-added
/// endpoint publish would be seen as a second event, not lost to an unanswered read.
/// </remarks>
[Trait("status", "task-018b-field-mapping")]
public class OfficeQuickCreateMembershipPublishTests
{
    private static readonly Guid CallerId = OfficeQuickCreateWorld.ContractCaller;
    private static readonly Guid Team = OfficeQuickCreateWorld.DefaultTeam;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Post_Matter_PublishesOneOwnerAddedEvent_CarryingTheOwnerTeamTheWriteBound()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        var publisher = new SignallingPublisher();
        using var host = Arrange(factory, publisher, "sprk_matter", "sprk_matternumber");
        var created = factory.CaptureRecordCreationWrite();

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Published Once" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await publisher.WaitForFirstAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the matter's owner event is published");
        await Task.Delay(Settle);

        var write = created.Entity!;
        var boundTeam = write.GetAttributeValue<EntityReference>("ownerid");
        boundTeam.Should().BeEquivalentTo(new EntityReference("team", Team), "the precondition: the write bound the team owner");

        var evt = publisher.Snapshot().Should().ContainSingle("exactly one publish per Office matter create").Subject;
        evt.EntityLogicalName.Should().Be("sprk_matter");
        evt.EntityRecordId.Should().Be(write.Id);
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(boundTeam.Id, "the owner team the write bound");
        evt.SourceField.Should().Be(MembershipChangedEvent.OwnerSourceField);
    }

    [Fact]
    public async Task Post_Project_PublishesNoOwnerEvent()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        var publisher = new SignallingPublisher();
        using var host = Arrange(factory, publisher, "sprk_project", "sprk_projectnumber");
        var created = factory.CaptureRecordCreationWrite();

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/project", new QuickCreateRequest { Name = "Not Published" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Writes.Should().ContainSingle("the precondition: the project was created");
        (await publisher.WaitForFirstAsync(Settle)).Should().BeFalse("a project's create publishes no owner event");
        publisher.Snapshot().Should().BeEmpty();
    }

    /// <summary>
    /// A resolved caller, the owning team's business unit, the created row's number and owner read-backs, and a host whose
    /// <see cref="IMembershipEventPublisher"/> is <paramref name="publisher"/>.
    /// </summary>
    private static WebApplicationFactory<Program> Arrange(
        OfficeQuickCreateTestWebAppFactory factory, SignallingPublisher publisher, string table, string numberColumn)
    {
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerId.ToString("D")));
        factory.Entities
            .Setup(e => e.RetrieveAsync("team", Team, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", Team)
            {
                ["businessunitid"] = new EntityReference("businessunit", OfficeQuickCreateWorld.ContractBusinessUnit),
            });
        factory.Entities
            .Setup(e => e.RetrieveAsync(table, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity(table, id)
            {
                [numberColumn] = "NUM-0001",
                ["ownerid"] = new EntityReference("team", Team),
            });

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipEventPublisher>();
            services.AddSingleton<IMembershipEventPublisher>(publisher);
        }));
    }

    /// <summary>Records every event (thread-safe: the publish runs off the request) and signals the first.</summary>
    private sealed class SignallingPublisher : IMembershipEventPublisher
    {
        private readonly List<MembershipChangedEvent> _published = new();
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
        {
            lock (_published)
            {
                _published.Add(evt);
            }

            _first.TrySetResult();
            return Task.CompletedTask;
        }

        public IReadOnlyList<MembershipChangedEvent> Snapshot()
        {
            lock (_published)
            {
                return _published.ToList();
            }
        }

        public async Task<bool> WaitForFirstAsync(TimeSpan timeout) =>
            await Task.WhenAny(_first.Task, Task.Delay(timeout)).ConfigureAwait(false) == _first.Task;
    }
}
