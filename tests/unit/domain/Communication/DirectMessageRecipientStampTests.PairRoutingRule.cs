using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 054a piece 2b: which sends the outbound Direct pair routing considers at all
/// (<see cref="CommunicationService.IsOutboundDirectSend"/>). Only a Message send naming no thread whose prepared row carries
/// no typed regarding lookup; an email send, a respond and a record-filed send never reach the routing.
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    [Fact]
    public void IsOutboundDirectSend_OnlyForAMessage_NamingNoThread_FiledUnderNoRecord()
    {
        var unfiled = new Entity("sprk_communication");
        var filed = new Entity("sprk_communication") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()) };
        var filedToNothing = new Entity("sprk_communication") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.Empty) };

        CommunicationService.IsOutboundDirectSend(MessageRequest(), unfiled).Should().BeTrue();
        CommunicationService.IsOutboundDirectSend(MessageRequest(), filedToNothing).Should().BeTrue("an empty id files under nothing");
        CommunicationService.IsOutboundDirectSend(MessageRequest(), filed).Should().BeFalse("a record-filed message is not Direct");
        CommunicationService.IsOutboundDirectSend(MessageRequest() with { ThreadId = DirectThread }, unfiled)
            .Should().BeFalse("a respond names its thread");
        CommunicationService.IsOutboundDirectSend(MessageRequest() with { CommunicationType = CommunicationType.Email }, unfiled)
            .Should().BeFalse("an email is never Direct");
    }
}
