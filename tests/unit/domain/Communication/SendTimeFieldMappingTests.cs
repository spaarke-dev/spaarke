using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 028 (FR-17): an EMAIL send filed under one <c>sprk_analysis</c> whose subject or body is
/// empty gets it from the analysis to communication mapping profile (fill-only), through the REAL
/// <see cref="CommunicationService.SendAsync"/> and the REAL <see cref="EmailChannelSender"/> over a fake Graph transport. The
/// analysis is read AS THE CALLER (the production <see cref="CallerRecordAccessProbe"/> with its rights seam substituted, and the
/// caller's own Dataverse read answered by <see cref="MappingSourceCallerReadDouble"/>); a denied, missing or secure source leaves
/// the request as it was (the send then fails its own "Subject is required" validation, so no value and no existence leaks).
/// </summary>
public class SendTimeFieldMappingTests
{
    private const string Mailbox = "noreply@contoso.com";
    private const string AnalysisTitle = "Analysis: Master services agreement";
    private const string AnalysisSummary = "The analysis found three open risks.";
    private const string AnalysisSet = "sprk_analysises";

    private static readonly Guid AnalysisId = Guid.Parse("00000028-1111-4000-8000-000000000001");
    private static readonly Guid OtherAnalysisId = Guid.Parse("00000028-1111-4000-8000-000000000002");
    private static readonly Guid MatterId = Guid.Parse("00000028-1111-4000-8000-000000000003");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IFieldMappingDataverseService> _profiles = new();
    private readonly GraphRecorder _graph = new();
    private readonly RightsProbe _probe = new();
    private Entity? _stored;
    private Entity? _analysis;
    private MappingSourceCallerReadDouble? _callerRead;

    public SendTimeFieldMappingTests()
    {
        _entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) =>
            {
                if (entity.LogicalName == "sprk_communication")
                {
                    _stored = entity;
                }
            })
            .ReturnsAsync(Guid.NewGuid());
        _entities
            .Setup(s => s.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => _analysis ?? throw new InvalidOperationException("no analysis"));
        _probe.Rights[AnalysisId] = AccessRights.Read;
        _analysis = Analysis(AnalysisId);
    }

    // ── The analysis → communication profile ─────────────────────────────────────────────────────────────────

    private static Entity Analysis(Guid id) => new Entity("sprk_analysis", id)
    {
        ["sprk_name"] = AnalysisTitle,
        ["sprk_description"] = AnalysisSummary,
    }.WithGate();

    private static FieldMappingRuleEntity CopyRule(string source, string target, int order) => new()
    {
        Name = $"{source} to {target}",
        SourceField = source,
        TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeText,
        MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order,
        IsActive = true,
    };

    /// <summary>The active analysis to communication profile: <c>sprk_name</c> to subject, <c>sprk_description</c> to body.</summary>
    private void SubjectAndBodyProfile() => _profiles
        .Setup(p => p.GetFieldMappingProfileWithRulesAsync("sprk_analysis", "sprk_communication", true, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new FieldMappingProfileEntity
        {
            Name = "Analysis to Communication",
            SourceEntity = "sprk_analysis",
            TargetEntity = "sprk_communication",
            IsActive = true,
            Rules =
            [
                CopyRule("sprk_name", "sprk_subject", 0),
                CopyRule("sprk_description", "sprk_body", 1),
            ],
        });

    // ── The send ─────────────────────────────────────────────────────────────────────────────────────────────

    private CommunicationService Sut()
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = Mailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = Mailbox,
        };
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(_graph)));

        return new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(factory.Object),
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService: not reached by a shared-mailbox send without attachments
            null!, // JobSubmissionService: not reached
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<CommunicationService>.Instance);
    }

    /// <summary>The caller's request context: an authenticated principal, a bearer token and the services the step reads.</summary>
    private DefaultHttpContext Http()
    {
        _callerRead ??= MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        var services = new ServiceCollection()
            .AddSingleton(_profiles.Object)
            .AddSingleton<CallerRecordAccessProbe>(_probe)
            .AddSingleton<IDataverseUserClient>(_callerRead)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .BuildServiceProvider();
        var context = TestHttpContexts.Authenticated();
        context.RequestServices = services;
        context.Request.Headers.Authorization = "Bearer caller-token";
        return context;
    }

    private static SendCommunicationRequest Email(string subject, string body, params CommunicationAssociation[] associations) => new()
    {
        To = ["client@example.com"],
        Subject = subject,
        Body = body,
        BodyFormat = BodyFormat.HTML,
        FromMailbox = Mailbox,
        SendMode = SendMode.SharedMailbox,
        CorrelationId = "task-028",
        Associations = associations,
    };

    private static CommunicationAssociation Analysed(Guid? id = null) =>
        new() { EntityType = "sprk_analysis", EntityId = id ?? AnalysisId };

    private static CommunicationAssociation Matter() =>
        new() { EntityType = "sprk_matter", EntityId = MatterId };

    // ── Cases ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUTATIONS: dropping the step leaves the empty subject (the send fails "Subject is required"); mapping the wrong column or
    /// reading the profile for the wrong tables sends another subject.
    /// </summary>
    [Fact]
    public async Task AnEmptySubject_IsFilledFromTheAnalysisMapping()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "", body: "<p>Typed body</p>", Analysed()), Http());

        var message = _graph.SendMailBody().Prop("message");
        message.Prop("subject").GetString().Should().Be(AnalysisTitle);
        message.Prop("body").Prop("content").GetString().Should().Be("<p>Typed body</p>", "a typed body is not touched");
        _stored!.GetAttributeValue<string>("sprk_subject").Should().Be(AnalysisTitle);
    }

    /// <summary>MUTATION: filling without checking the request's own value replaces what the sender typed.</summary>
    [Fact]
    public async Task ATypedSubject_Survives_EvenWhenTheBodyIsFilledFromTheSameAnalysis()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "My own subject", body: "", Analysed()), Http());

        var message = _graph.SendMailBody().Prop("message");
        message.Prop("subject").GetString().Should().Be("My own subject");
        message.Prop("body").Prop("content").GetString().Should().Be(AnalysisSummary, "the empty body is filled");
        _stored!.GetAttributeValue<string>("sprk_subject").Should().Be("My own subject");
    }

    [Fact]
    public async Task AnEmptyBody_IsFilledFromTheAnalysisMapping_AsPlainText()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "Typed subject", body: "  ", Analysed()), Http());

        var message = _graph.SendMailBody().Prop("message");
        message.Prop("subject").GetString().Should().Be("Typed subject");
        message.Prop("body").Prop("content").GetString().Should().Be(AnalysisSummary);
        message.Prop("body").Prop("contentType").GetString().Should().BeEquivalentTo("text", "mapped text is never sent as markup");
    }

    [Fact]
    public async Task ATypedBody_Survives()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "", body: "<p>Typed body</p>", Analysed()), Http());

        var message = _graph.SendMailBody().Prop("message");
        message.Prop("body").Prop("content").GetString().Should().Be("<p>Typed body</p>");
        message.Prop("body").Prop("contentType").GetString().Should().BeEquivalentTo("html", "the typed body keeps its format");
    }

    [Fact]
    public async Task BothEmpty_AreBothFilled()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "", body: "", Analysed()), Http());

        var message = _graph.SendMailBody().Prop("message");
        message.Prop("subject").GetString().Should().Be(AnalysisTitle);
        message.Prop("body").Prop("content").GetString().Should().Be(AnalysisSummary);
    }

    [Fact]
    public async Task NoProfile_LeavesTheRequestUnchanged_AndTheSendFailsItsOwnValidation()
    {
        _profiles
            .Setup(p => p.GetFieldMappingProfileWithRulesAsync("sprk_analysis", "sprk_communication", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldMappingProfileEntity?)null);

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        _probe.Asked.Should().BeEmpty("no rule needs the analysis, so the caller's rights are not even asked");
        _entities.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The caller cannot read the analysis: nothing is mapped, and nothing is read app-only (no existence or value leak). The
    /// send fails its own validation exactly as if no profile existed.
    /// </summary>
    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Write | AccessRights.Append)]
    public async Task ACallerWhoCannotReadTheAnalysis_GetsNoMapping_AndNothingIsReadAppOnly(AccessRights rights)
    {
        SubjectAndBodyProfile();
        _probe.Rights[AnalysisId] = rights;

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        _probe.Asked.Should().Contain(AnalysisId);
        _entities.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAnalysisThatIsMissing_GetsNoMapping()
    {
        SubjectAndBodyProfile();
        _probe.Rights.Remove(AnalysisId);

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _entities.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A column the caller cannot read (field-level security) is not copied: the app-only read is masked to the caller.</summary>
    [Fact]
    public async Task AColumnTheCallerCannotRead_IsNotCopied()
    {
        SubjectAndBodyProfile();
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        _callerRead.Secured.Add("sprk_name");

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
    }

    /// <summary>The mapping-source rule: a source whose owner cannot be told is not copied from (fail closed).</summary>
    [Fact]
    public async Task ASourceWhoseOwnerCannotBeTold_IsNotCopiedFrom()
    {
        SubjectAndBodyProfile();
        _analysis = new Entity("sprk_analysis", AnalysisId) { ["sprk_name"] = AnalysisTitle, ["sprk_description"] = AnalysisSummary };

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
    }

    [Fact]
    public async Task AFaultingProfileRead_LeavesTheRequestUnchanged()
    {
        _profiles
            .Setup(p => p.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse said: upstream-secret"));

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
    }

    // ── What the step does not touch ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithNoAnalysisAssociation_NoProfileIsLookedUp()
    {
        SubjectAndBodyProfile();

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Matter()), Http());

        await send.Should().ThrowAsync<SdapProblemException>();
        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithTwoAnalysisAssociations_NoProfileIsLookedUp()
    {
        SubjectAndBodyProfile();

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed(), Analysed(OtherAnalysisId)), Http());

        await send.Should().ThrowAsync<SdapProblemException>();
        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASendWithASubjectAndBody_LooksUpNothing()
    {
        SubjectAndBodyProfile();

        await Sut().SendAsync(Email(subject: "S", body: "<p>B</p>", Analysed()), Http());

        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _probe.Asked.Should().BeEmpty();
    }

    /// <summary>A Message (ACS chat) send is not an email send: the step never runs for it.</summary>
    [Fact]
    public async Task AMessageSend_IsUntouched()
    {
        SubjectAndBodyProfile();
        var request = Email(subject: "", body: "", Analysed()) with { CommunicationType = CommunicationType.Message };

        try
        {
            await Sut().SendAsync(request, Http());
        }
        catch (Exception)
        {
            // The message path has its own requirements (a thread, a participant); only the mapping step is under test here.
        }

        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _probe.Asked.Should().BeEmpty();
    }

    /// <summary>A system-mail send (secret-bearing, server-built) is not a caller's email: the step never runs for it.</summary>
    [Fact]
    public async Task ASystemMailSend_IsUntouched()
    {
        SubjectAndBodyProfile();
        var request = Email(subject: "", body: "<p>b</p>", Analysed()) with { SystemMail = new SystemMailOptions { StoredBody = "<p>b</p>" } };

        var send = () => Sut().SendAsync(request, Http());

        await send.Should().ThrowAsync<SdapProblemException>();
        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A send with no HttpContext (a server-side caller) has no caller to read as: nothing is looked up.</summary>
    [Fact]
    public async Task WithNoHttpContext_NoProfileIsLookedUp()
    {
        SubjectAndBodyProfile();

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), httpContext: null);

        await send.Should().ThrowAsync<SdapProblemException>();
        _profiles.Verify(p => p.GetFieldMappingProfileWithRulesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Doubles ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The production probe with only its rights answer substituted: rights per record id (none unless set).</summary>
    private sealed class RightsProbe()
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public Dictionary<Guid, AccessRights> Rights { get; } = new();

        public List<Guid> Asked { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Asked.Add(recordId);
            entitySet.Should().Be(AnalysisSet);
            callerBearerToken.Should().Be("caller-token");
            return Task.FromResult(Rights.TryGetValue(recordId, out var rights) ? rights : AccessRights.None);
        }
    }

    /// <summary>A fake Graph transport: records each request; <c>sendMail</c> answers 202, anything else 200 with no messages.</summary>
    private sealed class GraphRecorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = new();

        public JsonElement SendMailBody()
        {
            var send = Requests.Should().ContainSingle(r => r.Uri.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)).Subject;
            return JsonDocument.Parse(send.Body!).RootElement;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
