using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// Unit tests for per-boundary ACS + Event Grid provisioning (task 012, FR-18): the orchestrator
/// builds a plan from options (per-boundary data location, chat-event subscription → BFF webhook +
/// dead-letter), invokes the provisioning seam as an EXTENSION of the existing flow, and the R1
/// Null-Object provisioner defers rather than making a live Azure call. Config/shape level only —
/// no live Azure resource is created.
/// </summary>
public class AcsBoundaryProvisioningTests
{
    private static AcsProvisioningOptions Options(
        string dataLocation = "unitedstates",
        string webhook = "https://bff.example.com/api/communication/acs/eventgrid",
        string[]? eventTypes = null,
        string deadLetterStorageId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sprkacme",
        string deadLetterContainer = "acs-eventgrid-deadletter") => new()
        {
            DataLocation = dataLocation,
            EventGrid = new AcsEventGridOptions
            {
                WebhookEndpointUrl = webhook,
                IncludedEventTypes = eventTypes ?? AcsChatEventTypes.Default,
                DeadLetter = new AcsDeadLetterOptions
                {
                    StorageAccountResourceId = deadLetterStorageId,
                    ContainerName = deadLetterContainer,
                },
            },
        };

    private static AcsBoundaryProvisioningService Service(
        AcsProvisioningOptions options,
        IAcsBoundaryProvisioner provisioner) =>
        new(provisioner, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AcsBoundaryProvisioningService>.Instance);

    // ── Plan building ───────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_UsesConfiguredDataLocation_WhenNoOverride()
    {
        var svc = Service(Options(dataLocation: "europe"), new CapturingProvisioner());

        var plan = svc.BuildPlan("acme");

        plan.DataLocation.Should().Be("europe");
        plan.BoundaryId.Should().Be("acme");
        plan.AcsResourceName.Should().Be("sprk-acme-acs");
    }

    [Fact]
    public void BuildPlan_PerBoundaryOverride_WinsOverConfiguredDefault()
    {
        // D-01: data location is immutable at create — a boundary needing a different residency
        // MUST choose it here at create time.
        var svc = Service(Options(dataLocation: "unitedstates"), new CapturingProvisioner());

        var plan = svc.BuildPlan("contoso", dataLocationOverride: "australia");

        plan.DataLocation.Should().Be("australia");
    }

    [Fact]
    public void BuildPlan_SubscribesFiveChatEvents_ToConfiguredWebhook_WithDeadLetter()
    {
        var svc = Service(Options(), new CapturingProvisioner());

        var sub = svc.BuildPlan("acme").EventSubscription;

        sub.WebhookEndpointUrl.Should().Be("https://bff.example.com/api/communication/acs/eventgrid");
        sub.IncludedEventTypes.Should().BeEquivalentTo(new[]
        {
            "Microsoft.Communication.ChatMessageReceivedInThread",
            "Microsoft.Communication.ChatMessageEditedInThread",
            "Microsoft.Communication.ChatMessageDeletedInThread",
            "Microsoft.Communication.ParticipantAddedToThread",
            "Microsoft.Communication.ParticipantRemovedFromThread",
        });
        sub.DeadLetterStorageAccountResourceId.Should().NotBeEmpty();
        sub.DeadLetterContainerName.Should().Be("acs-eventgrid-deadletter");
        sub.SystemTopicName.Should().Be("sprk-acme-acs-egt");
    }

    [Fact]
    public void BuildPlan_EmptyEventTypes_FallsBackToDefaultChatEvents()
    {
        var svc = Service(Options(eventTypes: System.Array.Empty<string>()), new CapturingProvisioner());

        svc.BuildPlan("acme").EventSubscription.IncludedEventTypes
            .Should().BeEquivalentTo(AcsChatEventTypes.Default);
    }

    [Fact]
    public void BuildPlan_BlankBoundaryId_Throws()
    {
        var svc = Service(Options(), new CapturingProvisioner());

        var act = () => svc.BuildPlan("  ");

        act.Should().Throw<System.ArgumentException>();
    }

    // ── Orchestration invokes the provisioning seam (extension, not parallel path) ──

    [Fact]
    public async Task ProvisionBoundaryAsync_InvokesProvisioner_WithBuiltPlan()
    {
        var capture = new CapturingProvisioner();
        var svc = Service(Options(dataLocation: "uk"), capture);

        await svc.ProvisionBoundaryAsync("acme");

        capture.LastPlan.Should().NotBeNull();
        capture.LastPlan!.BoundaryId.Should().Be("acme");
        capture.LastPlan.DataLocation.Should().Be("uk");
        capture.LastPlan.EventSubscription.IncludedEventTypes.Should().HaveCount(5);
    }

    [Fact]
    public async Task ProvisionBoundaryAsync_ReturnsProvisionerResult()
    {
        var svc = Service(Options(), new CapturingProvisioner());

        var result = await svc.ProvisionBoundaryAsync("acme");

        result.BoundaryId.Should().Be("acme");
        result.Status.Should().Be(AcsProvisioningStatus.Deferred);
    }

    // ── R1 Null-Object provisioner: defers, uses injected credential, no live call ──

    [Fact]
    public async Task DeferredProvisioner_ReturnsDeferred_WithoutInvokingCredential()
    {
        var credential = new StubTokenCredential();
        var provisioner = new DeferredAcsBoundaryProvisioner(
            credential, NullLogger<DeferredAcsBoundaryProvisioner>.Instance);
        var plan = Service(Options(), provisioner).BuildPlan("acme");

        var result = await provisioner.ProvisionAsync(plan);

        result.Status.Should().Be(AcsProvisioningStatus.Deferred);
        result.Detail.Should().NotBeNullOrEmpty();
        // R1 makes no management-plane token request — provisioning is Bicep/operator-driven.
        credential.GetTokenCallCount.Should().Be(0);
    }

    // ── Test doubles (hand-written; no Moq of transport/credential per ADR-038) ──

    private sealed class CapturingProvisioner : IAcsBoundaryProvisioner
    {
        public AcsProvisioningPlan? LastPlan { get; private set; }

        public Task<AcsProvisioningResult> ProvisionAsync(AcsProvisioningPlan plan, CancellationToken ct = default)
        {
            LastPlan = plan;
            return Task.FromResult(AcsProvisioningResult.Deferred(plan.BoundaryId, "captured"));
        }
    }

    private sealed class StubTokenCredential : TokenCredential
    {
        public int GetTokenCallCount { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            GetTokenCallCount++;
            return new AccessToken("stub", System.DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            GetTokenCallCount++;
            return new ValueTask<AccessToken>(new AccessToken("stub", System.DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
