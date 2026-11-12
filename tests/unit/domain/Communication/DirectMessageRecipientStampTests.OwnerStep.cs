using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3a, through the real entry points: every join path (the send with no thread
/// id, the respond into a thread, the inbound capture) runs the one owner step, and a 1:1 message send is refused before anything
/// is sent when the Direct message owner team is configured but unusable (409) or cannot be read (503). No Direct setting at all
/// sends as before.
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    private readonly DirectMessageOwnerStepTests.RecordingLogger _stepLog = new();

    private DirectMessageOwnerStep OwnerStep(DirectMessageOwnerTeam lookup) => new(_directAccess.Object, _entities.Object, lookup, _stepLog);

    // ── The pre-send refusal (1:1 only) ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task OneToOneSend_WithOnlyOneDirectSettingSet_Is409_AndSendsNothing(string onlySet)
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(onlySet));

        var act = () => Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(409);
        refusal.Code.Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        refusal.Extensions!["reasonCode"].Should().Be(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        refusal.Detail.Should().Contain("not sent");
        _sender.Sends.Should().Be(0);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communication");
    }

    [Fact]
    public async Task OneToOneSend_WhenTheDirectTeamCannotBeRead_Is503_AndSendsNothing()
    {
        var world = new DirectMessageOwnerTeamDouble().WithDirectTeam(Guid.NewGuid());
        world.Faults = true;
        var step = OwnerStep(world.Lookup());

        var act = () => Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.StatusCode.Should().Be(503);
        refusal.Detail.Should().Contain("Try again");
        _sender.Sends.Should().Be(0);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communication");
    }

    [Fact]
    public async Task OneToOneSend_WithNoDirectSetting_Sends()
    {
        var step = OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured());

        await Service(Join(DirectThread), ownerStep: step).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1);
        SentMessageCreate().Should().NotBeNull();
    }

    [Fact]
    public async Task SendToTwoPeople_WithOnlyOneDirectSettingSet_IsNotRefused()
    {
        var step = OwnerStep(DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey));

        await Service(Join(DirectThread), ownerStep: step).SendAsync(
            MessageRequest() with { To = new[] { "someone@contoso.com", "another@contoso.com" } }, Caller(), CancellationToken.None);

        _sender.Sends.Should().Be(1);
    }

    // ── Every join path runs the owner step ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendJoin_RunsTheOwnerStep()
    {
        await Service(Join(DirectThread), ownerStep: OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    [Fact]
    public async Task RespondIntoADirectThread_RunsTheOwnerStep()
    {
        await Service(resolver: null, ownerStep: OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    [Fact]
    public async Task InboundJoin_RunsTheOwnerStep()
    {
        await InboundHandler(Join(DirectThread), OwnerStep(DirectMessageOwnerTeamDouble.Unconfigured()))
            .ProcessAsync(InboundJob(SenderChatId), CancellationToken.None);

        StepRanFor(DirectThread);
    }

    private void StepRanFor(Guid threadId)
    {
        _stepLog.Entries.Should().Contain(e => e.Message.Contains("DirectParticipants") && e.Message.Contains(threadId.ToString()),
            "the join's grant goes through the owner step");
        _directAccess.Verify(a => a.GrantMessageAccessAsync(MessageId, It.Is<ThreadAssignment>(x => x.ThreadId == threadId),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
