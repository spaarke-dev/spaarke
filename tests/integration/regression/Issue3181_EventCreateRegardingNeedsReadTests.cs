// Regression test for GitHub #3181 (found by spaarkeai-mcp-bff-api-r2 task 006's tenant-isolation review; owner decision D-3181,
// 2026-11-27: option (b)).
//
// Creating an event filed under a record asked only AppendTo on that record ("event.attach_regarding"), then read the record's
// name and number app-only and copied them onto the new event, which the caller then reads back. AppendTo does not imply Read,
// so a caller whose role grants Append To widely but Read narrowly learned the name and number of a matter they cannot read.
// The check now asks Read AND AppendTo, for POST /api/v1/events and for IEventsCapability.CreateAsync (the MCP create tool).
//
// Must fire: AppendTo without Read on the regarding record is refused by both paths, with the same 403 rights answer as before,
// and no event is written. Must not fire: Read + AppendTo still creates and copies the name and number.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Tests.Auth.Events;
using Xunit;
using EventsAuthHost = Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests.EventsAuthHost;
using CreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3181_EventCreateRegardingNeedsReadTests
{
    private const string MattersSet = "sprk_matters";
    private const string CreateEventPrivilege = "prvCreatesprk_Event";
    private const string MatterName = "Acme Holdings";
    private const string MatterNumber = "MAT-3181";

    /// <summary>The caller holds the Create privilege; the matter exists with a name and a number; the caller's rights on it are <paramref name="rights"/>.</summary>
    private static void MatterWithNameAndNumber(EventsAuthHost host, Guid matter, AccessRights rights)
    {
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(MattersSet, matter, rights);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(host.InCallerCatalog("sprk_matter", new Entity("sprk_recordtype_ref", Guid.NewGuid())
            {
                [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber",
            }));
        host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter) { ["sprk_mattername"] = MatterName, ["sprk_matternumber"] = MatterNumber });
    }

    private static HttpRequestMessage RestCreate(Guid matter) =>
        new(HttpMethod.Post, "/api/v1/events")
        {
            Content = JsonContent.Create(new { subject = "Review", regardingRecordType = 1, regardingRecordId = matter }),
        };

    /// <summary>The capability over the same seams as the route (the MCP create tool's path), and a signed-in caller's request.</summary>
    private static (EventsCapability Capability, HttpContext Request) CapabilityOver(EventsAuthHost host)
    {
        var capability = new EventsCapability(
            host.Events.Object,
            host.RecordTypes.Object,
            host.Caller.Object,
            host.Identity.Object,
            host.Entities.Object,
            host.RecordCreation,
            host.CallerClient,
            host.Ownership,
            host.Probe,
            NullLogger<EventsCapability>.Instance);

        var request = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", "6f0c1a52-0000-4000-8000-000000000159")], "Issue3181")),
        };
        request.Request.Headers.Authorization = "Bearer caller-token";
        return (capability, request);
    }

    private static CreateEventRequest CapabilityRequest(Guid matter) =>
        new("Review", RegardingRecordId: matter, RegardingRecordType: Spaarke.Dataverse.RegardingRecordType.Matter);

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rest_Create_WithAppendToButNoReadOnTheRegarding_Is403InsufficientRights_AndNothingIsWritten()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNameAndNumber(host, matter, AccessRights.AppendTo);
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(RestCreate(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>()
            .Should().Be("sdap.access.deny.insufficient_rights");
        creates.Should().BeEmpty("no event row is written");
        host.AssertServiceNotReached();
        host.Entities.Verify(
            e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "the matter's name and number are never read for a caller who may not read them");
    }

    [Fact]
    public async Task Capability_Create_WithAppendToButNoReadOnTheRegarding_IsForbiddenWithRestsReason_AndNothingIsWritten()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNameAndNumber(host, matter, AccessRights.AppendTo);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);

        var result = await capability.CreateAsync(request, CapabilityRequest(matter), CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Forbidden);
        result.ReasonCode.Should().Be("sdap.access.deny.insufficient_rights", "the same reason as POST /api/v1/events");
        result.Value.Should().BeNull();
        creates.Should().BeEmpty("no event row is written");
        host.AssertServiceNotReached();
        host.Entities.Verify(
            e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "the matter's name and number are never read for a caller who may not read them");
    }

    /// <summary>The refusal bytes are unchanged by the policy change: the AppendTo-only caller gets exactly the answer a caller with no rights at all got before.</summary>
    [Fact]
    public async Task Rest_Create_TheDenyBodyForAppendToWithoutRead_IsByteIdenticalToTheDenyBodyForNoRights()
    {
        string[] bodies = new string[2];
        for (var i = 0; i < 2; i++)
        {
            await using var host = await EventsAuthHost.StartAsync();
            var matter = Guid.NewGuid();
            MatterWithNameAndNumber(host, matter, i == 0 ? AccessRights.AppendTo : AccessRights.None);

            var response = await host.SendAsync(RestCreate(matter));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            bodies[i] = EventCallerScopeTests.Normalize(await response.Content.ReadAsStringAsync());
        }

        bodies[0].Should().Be(bodies[1]);
        bodies[0].Should().NotContain("Read", "the 403 names no right");
        bodies[0].Should().NotContain("AppendTo", "the 403 names no right");
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rest_Create_WithReadAndAppendToOnTheRegarding_Succeeds_AndCopiesTheNameAndNumber()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNameAndNumber(host, matter, AccessRights.Read | AccessRights.AppendTo);
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(RestCreate(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = creates.Should().ContainSingle().Subject;
        created.RegardingRecordName.Should().Be(MatterName);
        created.RegardingRecordNumber.Should().Be(MatterNumber);
    }

    [Fact]
    public async Task Capability_Create_WithReadAndAppendToOnTheRegarding_Succeeds_AndCopiesTheNameAndNumber()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNameAndNumber(host, matter, AccessRights.Read | AccessRights.AppendTo);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);

        var result = await capability.CreateAsync(request, CapabilityRequest(matter), CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Ok);
        var created = creates.Should().ContainSingle().Subject;
        created.RegardingRecordName.Should().Be(MatterName);
        created.RegardingRecordNumber.Should().Be(MatterNumber);
    }
}
