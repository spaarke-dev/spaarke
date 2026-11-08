using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CommunicationReplyRegarding;

/// <summary>
/// field-mapping-server-write-path-r1 task 045c (owner decision Q6 (c), 2026-11-07; design gaps G4 and G5, writer W15): a
/// reply or forward copies its source communication's regarding. The copy carries the record TYPE and the reference
/// NUMBER along with the id, name and url, so the copy is as whole as its source, and it never carries a half pair or a
/// type that describes another record. Driven through the real <see cref="CommunicationService"/> send; the row it creates is
/// captured at the Dataverse boundary.
/// </summary>
[Trait("status", "new")]
public class ReplyRegardingCopyTests
{
    private static readonly Guid SourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MatterId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OtherMatterId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid MatterTypeRow = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static Directory World() => Directory.Standard()
        .WithOrdinaryRoot("sprk_matter", MatterId)
        .WithOrdinaryRoot("sprk_matter", OtherMatterId);

    /// <summary>A source communication filed under <see cref="MatterId"/> with the whole pair and its display columns.</summary>
    private static DataverseEntity WholeSource()
    {
        var source = new DataverseEntity("sprk_communication", SourceId);
        source["sprk_isinternalonly"] = false;
        source["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        source["sprk_regardingrecordid"] = MatterId.ToString("D");
        source["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", MatterTypeRow);
        source["sprk_regardingrecordname"] = "Acme Corp";
        source["sprk_regardingrecordurl"] = "/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=" + MatterId.ToString("D");
        source["sprk_regardingrecordnumber"] = "MAT-000123";
        return source;
    }

    [Fact]
    public async Task Reply_CopiesTheSourcesRecordType_AndNumber_WithTheIdNameAndUrl()
    {
        var harness = new Harness(WholeSource());

        await harness.Service.SendAsync(Reply(), harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D"));
        var type = row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype");
        type.Should().NotBeNull("a reply copies the pair's type, not only its id (Q6 (c))");
        type.LogicalName.Should().Be("sprk_recordtype_ref");
        type.Id.Should().Be(MatterTypeRow);
        row.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("MAT-000123");
        row.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp");
        row.GetAttributeValue<string>("sprk_regardingrecordurl").Should().Contain(MatterId.ToString("D"));
    }

    [Fact]
    public async Task Reply_ToAnIdOnlySource_CopiesNoTypeAndNoNumber()
    {
        // An id-only pair is valid (the agreement rule; a communication has no catalog row of its own): the copy stays
        // id-only. It invents neither a type nor a number.
        var source = WholeSource();
        source.Attributes.Remove("sprk_regardingrecordtype");
        source.Attributes.Remove("sprk_regardingrecordnumber");
        var harness = new Harness(source);

        await harness.Service.SendAsync(Reply(), harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D"));
        row.Contains("sprk_regardingrecordtype").Should().BeFalse();
        row.Contains("sprk_regardingrecordnumber").Should().BeFalse();
    }

    [Fact]
    public async Task Reply_ToATypeOnlySource_CopiesNoType_SoNoHalfPairIsPropagated()
    {
        // A type with no id is a half pair. The copy never makes a second one: with no id to copy, the type stays behind.
        var source = WholeSource();
        source.Attributes.Remove("sprk_regardingrecordid");
        var harness = new Harness(source);

        await harness.Service.SendAsync(Reply(), harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.Contains("sprk_regardingrecordid").Should().BeFalse();
        row.Contains("sprk_regardingrecordtype").Should().BeFalse("a type is copied only beside the id it describes");
    }

    [Fact]
    public async Task Reply_WithAnExplicitAssociationToAnotherRecord_KeepsNeitherTheSourcesTypeNorItsNumber()
    {
        // The explicit association wins on the id, name and url. The source's type and number described the source's record,
        // so they must not survive beside another record's id.
        var harness = new Harness(WholeSource());
        var request = Reply() with
        {
            Associations = new[]
            {
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = OtherMatterId, EntityName = "Beta LLC" },
            },
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(OtherMatterId.ToString());
        row.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Beta LLC");
        row.Contains("sprk_regardingrecordtype").Should().BeFalse("it described the source's record, not Beta LLC");
        row.Contains("sprk_regardingrecordnumber").Should().BeFalse("it described the source's record, not Beta LLC");
    }

    [Fact]
    public async Task Reply_WithAnExplicitAssociationToTheSameRecord_KeepsTheCopiedTypeAndNumber()
    {
        // The explicit association names the record the source already names: nothing the copy carried is stale.
        var harness = new Harness(WholeSource());
        var request = Reply() with
        {
            Associations = new[]
            {
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId, EntityName = "Acme Corp" },
            },
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype")!.Id.Should().Be(MatterTypeRow);
        row.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("MAT-000123");
    }

    [Fact]
    public async Task Reply_WithAnExplicitAssociationToAnotherRecord_ThatCarriesNoNameOrUrl_KeepsNoSourceNameOrUrl()
    {
        // The association names another record but gives no display values. The source's name and url belong to the source's
        // record, so none survives beside the new id (nothing is better than another record's).
        var harness = new Harness(WholeSource());
        var request = Reply() with
        {
            Associations = new[] { new CommunicationAssociation { EntityType = "sprk_matter", EntityId = OtherMatterId } },
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(OtherMatterId.ToString());
        row.Contains("sprk_regardingrecordname").Should().BeFalse("it was the source record's name");
        row.Contains("sprk_regardingrecordurl").Should().BeFalse("it was the source record's url");
        row.Contains("sprk_regardingrecordtype").Should().BeFalse();
        row.Contains("sprk_regardingrecordnumber").Should().BeFalse();
    }

    [Fact]
    public async Task Reply_WithAnExplicitAssociationToTheSameRecord_ThatCarriesNoNameOrUrl_KeepsTheSourcesNameAndUrl()
    {
        // Must not fire: the association names the record the source names, so the copied display values still describe it.
        var harness = new Harness(WholeSource());
        var request = Reply() with
        {
            Associations = new[] { new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId } },
        };

        await harness.Service.SendAsync(request, harness.SignedInUser());

        var row = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        row.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp");
        row.GetAttributeValue<string>("sprk_regardingrecordurl").Should().Contain(MatterId.ToString("D"));
    }

    private static SendCommunicationRequest Reply() => new()
    {
        To = new[] { "counsel@other.example" },
        Subject = "RE: Settlement",
        Body = "<p>Noted.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        InheritRegardingFromCommunicationId = SourceId,
        CorrelationId = "corr-045c",
    };

    private sealed class Harness
    {
        public Harness(DataverseEntity source)
        {
            var options = new CommunicationOptions
            {
                ApprovedSenders = new[] { new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true } },
                DefaultMailbox = "noreply@contoso.com",
            };
            var accounts = new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance);
            var validator = new ApprovedSenderValidator(
                Options.Create(options), accounts, Mock.Of<IDistributedCache>(), NullLogger<ApprovedSenderValidator>.Instance);

            var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
            entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseEntity, CancellationToken>((e, _) => Created.Add(e))
                .ReturnsAsync(Guid.NewGuid);
            entities.Setup(e => e.RetrieveAsync("sprk_communication", SourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
            communications.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Directory.CallerUserId);

            var dispatcher = new CommunicationChannelDispatcher(
                new ICommunicationChannelSender[] { new RecordingSender(CommunicationType.Email) },
                Array.Empty<ICommunicationArchiver>());

            Service = new CommunicationService(
                dispatcher, validator, communications.Object, entities.Object, Mock.Of<IDocumentDataverseService>(),
                accountService: null!, jobSubmissionService: null!, Mock.Of<ICommunicationEnrichmentService>(),
                Options.Create(options), CoreAncestorResolverFixtures.Inert(), World().Resolver(),
                NullLogger<CommunicationService>.Instance);
        }

        public CommunicationService Service { get; }

        public List<DataverseEntity> Created { get; } = new();

        public Microsoft.AspNetCore.Http.HttpContext SignedInUser() => new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim("oid", Directory.CallerOid.ToString()),
                    new System.Security.Claims.Claim("preferred_username", "user@contoso.com"),
                },
                "test")),
        };
    }

    private sealed class RecordingSender(CommunicationType type) : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => type;

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress });
    }
}
