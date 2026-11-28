using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 028 (FR-17): an EMAIL send filed under one <c>sprk_analysis</c> whose subject or body is
/// empty gets it from the analysis to communication mapping profile (fill-only), through the REAL
/// <see cref="CommunicationService.SendAsync"/> and the REAL <see cref="EmailChannelSender"/> over a fake Graph transport. The
/// analysis is read AS THE CALLER (the production <see cref="CallerRecordAccessProbe"/> with its rights seam substituted, and the
/// caller's own Dataverse read answered by <see cref="MappingSourceCallerReadDouble"/>); a denied, missing or secure source leaves
/// the request as it was (the send then fails its own "Subject is required" validation, so no value and no existence leaks).
/// An analysis whose source document the external document narrowing hides from the caller maps nothing either, decided by the REAL
/// <see cref="AnalysisAuthorizationFilter.IsAnalysisDocumentHiddenFromCallerAsync"/> over a stated caller and narrowing.
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
    private static readonly Guid CallerUserId = Guid.Parse("00000028-1111-4000-8000-000000000004");
    private static readonly Guid SourceDocumentId = Guid.Parse("00000028-1111-4000-8000-000000000005");
    private static readonly Guid SecureTeamId = Guid.Parse("00000028-1111-4000-8000-000000000006");
    private static readonly Guid SecureBusinessUnitId = Guid.Parse("00000028-1111-4000-8000-000000000007");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IFieldMappingDataverseService> _profiles = new();
    private readonly GraphRecorder _graph = new();
    private readonly RightsProbe _probe = new();
    private Entity? _stored;
    private Entity? _analysis;
    private MappingSourceCallerReadDouble? _callerRead;
    private bool _callerExternal;
    private bool _appOnlyReturnsNull;
    private Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.CapturingLog<CommunicationService>? _log;
    private Func<IReadOnlyCollection<Guid>, Task<DocumentNarrowingResult>> _narrowing =
        ids => Task.FromResult(DocumentNarrowingResult.NotNarrowed(ids.Select(id => new DocumentNarrowingRow(id, default, Guid.Empty, Guid.Empty))));

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
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                _appOnlyReturnsNull ? null! : _analysis ?? throw new InvalidOperationException("no analysis"));
        _entities
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => new EntityCollection(
                [new Entity(query.EntityName, query.EntityName == "team" ? SecureTeamId : SecureBusinessUnitId)]));
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
            (ILogger<CommunicationService>?)_log ?? NullLogger<CommunicationService>.Instance);
    }

    /// <summary>The caller's request context: an authenticated principal, a bearer token and the services the step reads.</summary>
    private DefaultHttpContext Http()
    {
        _callerRead ??= MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        var caller = new Mock<ICallerSystemUserResolver>();
        caller
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerUserId.ToString("D")));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity
            .Setup(r => r.IsExternalAsync(CallerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_callerExternal);
        var services = new ServiceCollection()
            .AddSingleton(_profiles.Object)
            .AddSingleton(caller.Object)
            .AddSingleton(identity.Object)
            .AddSingleton(_entities.Object)
            .AddSingleton<InternalRouteDocumentNarrowing>((ids, _) => _narrowing(ids))
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

        var problem = (await send.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.Detail.Should().Contain("Subject is required");
        AssertNoAnalysisValueIn(problem.Detail, problem.Message);
        _graph.Requests.Should().BeEmpty();
        _stored.Should().BeNull("nothing is stored for a send that fails its validation");
        _probe.Asked.Should().Contain(AnalysisId);
        _entities.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Task 067, must not leak: a caller who cannot read the analysis gets none of its values, whichever field was left for the
    /// mapping. Nothing is sent (no Graph request carries a subject, a body or any other field), nothing is stored, and the
    /// response (the validation problem: its detail, title, code and extensions) carries no value of the analysis.
    /// MUTATION: reading the analysis app-only for this caller sends the message with its title or summary.
    /// </summary>
    [Theory]
    [InlineData(AccessRights.None, "", "<p>b</p>", "Subject is required")]
    [InlineData(AccessRights.None, "Typed subject", "", "Body is required")]
    [InlineData(AccessRights.None, "", "", "Subject is required")]
    [InlineData(AccessRights.Write | AccessRights.Append, "Typed subject", "", "Body is required")]
    public async Task ACallerWhoCannotReadTheAnalysis_NoAnalysisValueIsSentStoredOrReturned(
        AccessRights rights, string subject, string body, string validation)
    {
        SubjectAndBodyProfile();
        _probe.Rights[AnalysisId] = rights;

        var send = () => Sut().SendAsync(Email(subject: subject, body: body, Analysed()), Http());

        var problem = (await send.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.Detail.Should().Contain(validation);
        AssertNoAnalysisValueIn(problem.Detail, problem.Title, problem.Code, problem.Message, JsonSerializer.Serialize(problem.Extensions));
        _graph.Requests.Should().BeEmpty("no message is sent, so no field of one carries a value");
        AssertNoAnalysisValueIn(_graph.Requests.Select(r => r.Body).ToArray());
        _stored.Should().BeNull("nothing is stored");
        _entities.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertNoAnalysisValueIn(params string?[] texts)
    {
        foreach (var text in texts.Where(t => t is not null))
        {
            text.Should().NotContain(AnalysisTitle).And.NotContain(AnalysisSummary)
                .And.NotContain("Master services agreement").And.NotContain("three open risks");
        }
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

    /// <summary>
    /// Task 067 (owner 2026-11-27), must fire: the analysis is the email's filing parent (its primary association, table and id),
    /// so a column the caller cannot read (field-level security) is copied, no caller column read is made, and one Information
    /// line names the source and target columns, never the value. MUTATION: masking the filing parent sends no subject.
    /// </summary>
    [Fact]
    public async Task AColumnTheCallerCannotRead_FromTheFilingParent_IsCopied_AndLoggedWithoutItsValue()
    {
        SubjectAndBodyProfile();
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        _callerRead.Secured.Add("sprk_name");
        _log = new Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.CapturingLog<CommunicationService>();

        await Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        _stored.Should().NotBeNull();
        _stored!["sprk_subject"].Should().Be(AnalysisTitle, "the filing parent's mapped value is copied whatever the caller can read on it");
        _callerRead.Paths.Should().BeEmpty("no caller column read is made for the filing parent (no access-model column is mapped)");
        var copied = _log.Entries.Where(e => e.Level == LogLevel.Information && e.Message.Contains("without the caller's column read")).ToList();
        copied.Should().Contain(e => e.Message.Contains("sprk_analysis.sprk_name") && e.Message.Contains("sprk_subject"));
        _log.Messages.Should().NotContain(m => m.Contains(AnalysisTitle) || m.Contains(AnalysisSummary), "a log line never carries the value");
    }

    /// <summary>
    /// Task 067 C2, must not fire: an analysis that is an association but not the PRIMARY one is not the email's filing parent, so
    /// the read is masked as before (#2188) and a column the caller cannot read is not copied.
    /// </summary>
    [Fact]
    public async Task AColumnTheCallerCannotRead_FromAnAnalysisThatIsNotTheFilingParent_IsNotCopied()
    {
        SubjectAndBodyProfile();
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        _callerRead.Secured.Add("sprk_name");

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Matter(), Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        _callerRead.Paths.Should().ContainSingle("the source is masked to the caller's column view");
    }

    /// <summary>Task 067 C2 (table AND id), must not fire: a primary association with the analysis' id but another table is not the
    /// filing parent; the analysis keeps the mask.</summary>
    [Fact]
    public async Task AColumnTheCallerCannotRead_WhenThePrimaryAssociationHasTheAnalysisIdButAnotherTable_IsNotCopied()
    {
        SubjectAndBodyProfile();
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        _callerRead.Secured.Add("sprk_name");

        var send = () => Sut().SendAsync(
            Email(subject: "", body: "<p>b</p>", new CommunicationAssociation { EntityType = "sprk_matter", EntityId = AnalysisId }, Analysed()),
            Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _callerRead.Paths.Should().ContainSingle();
    }

    [Theory]
    [InlineData("sprk_analysis", true)]
    [InlineData("SPRK_Analysis", true)]
    [InlineData(" sprk_analysis", false)] // the send's regarding map does not trim, so it would not file under it
    [InlineData("sprk_matter", false)]
    public void SourceIsTheCommunicationsFilingParent_IsTheTableAndIdOfThePrimaryAssociation(string table, bool expected)
    {
        var request = Email("", "", new CommunicationAssociation { EntityType = table, EntityId = AnalysisId });
        SendTimeFieldMapping.SourceIsTheCommunicationsFilingParent(request, AnalysisId).Should().Be(expected);
        SendTimeFieldMapping.SourceIsTheCommunicationsFilingParent(request, OtherAnalysisId).Should().BeFalse("another id");
        SendTimeFieldMapping.SourceIsTheCommunicationsFilingParent(Email("", "", Matter(), Analysed()), AnalysisId)
            .Should().BeFalse("a second association is not the filing parent");
        SendTimeFieldMapping.SourceIsTheCommunicationsFilingParent(Email("", ""), AnalysisId).Should().BeFalse("no association");
    }

    /// <summary>
    /// Task 067 C3, must fire: from the filing parent, a rule that reads an access-model column keeps the masked read, so a value the
    /// caller cannot read there is not copied, and a warning names the target column.
    /// </summary>
    [Fact]
    public async Task ARuleReadingAnAccessModelColumn_FromTheFilingParent_KeepsTheMask_AndIsNamed()
    {
        _profiles
            .Setup(p => p.GetFieldMappingProfileWithRulesAsync("sprk_analysis", "sprk_communication", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Name = "Analysis to Communication",
                SourceEntity = "sprk_analysis",
                TargetEntity = "sprk_communication",
                IsActive = true,
                Rules = [CopyRule("adx_identity_passwordhash", "sprk_subject", 0), CopyRule("sprk_description", "sprk_body", 1)],
            });
        _analysis = Analysis(AnalysisId);
        _analysis["adx_identity_passwordhash"] = "not-to-be-copied";
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_entities);
        _callerRead.Secured.Add("adx_identity_passwordhash");
        _log = new Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.CapturingLog<CommunicationService>();

        var send = () => Sut().SendAsync(Email(subject: "", body: "", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _callerRead.Paths.Should().ContainSingle("the access-model column is judged by the caller's read");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning
                                           && e.Message.Contains(AccessModelSourceColumns.ReadAsTheCallerWarning("sprk_subject")));
        _log.Messages.Should().NotContain(m => m.Contains("not-to-be-copied"));
    }

    /// <summary>Task 067 C4: the app-only read is of the one analysis, for exactly its mapped columns (and the gate columns it was
    /// always read with, which are never copied).</summary>
    [Fact]
    public async Task TheAppOnlyRead_AsksForExactlyTheMappedColumnsOfTheOneAnalysis()
    {
        SubjectAndBodyProfile();
        var asked = new List<(Guid Id, string[] Columns)>();
        _entities
            .Setup(s => s.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid id, string[] columns, CancellationToken _) => asked.Add((id, columns)))
            .ReturnsAsync(() => _analysis!);

        await Sut().SendAsync(Email(subject: "", body: "", Analysed()), Http());

        var read = asked.Should().ContainSingle().Subject;
        read.Id.Should().Be(AnalysisId);
        read.Columns.Should().BeEquivalentTo(
            new[] { "sprk_name", "sprk_description" }.Concat(Sprk.Bff.Api.Services.RecordCreation.MappingSourceCopyGate.SourceGateColumns("sprk_analysis")).Distinct());
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

    /// <summary>
    /// The external document narrowing hides the analysis' source document from the caller: nothing is filled, the send fails its
    /// own validation with no Graph call, and the analysis is never read app-only beyond its two document links.
    /// MUTATION: not asking the narrowing (or asking after the app-only read) fills the subject from a withheld analysis.
    /// </summary>
    [Fact]
    public async Task AnAnalysisWhoseSourceDocumentIsHiddenFromTheCaller_FillsNothing()
    {
        SubjectAndBodyProfile();
        ExternalCallerWithSourceDocument(DocumentNarrowingReason.OwnRestricted);

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        NothingOfTheAnalysisWasReadBeyondItsDocumentLinks();
    }

    /// <summary>Must not fire: the same external caller, the narrowing shows the source document, so the analysis maps.</summary>
    [Fact]
    public async Task AnAnalysisWhoseSourceDocumentIsShownToTheCaller_IsFilled()
    {
        SubjectAndBodyProfile();
        ExternalCallerWithSourceDocument(DocumentNarrowingReason.Visible);

        await Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        _graph.SendMailBody().Prop("message").Prop("subject").GetString().Should().Be(AnalysisTitle);
    }

    /// <summary>Must not fire: an analysis that derives from no document is unaffected for an external caller.</summary>
    [Fact]
    public async Task AnAnalysisDerivedFromNoDocument_IsFilled_ForAnExternalCaller()
    {
        SubjectAndBodyProfile();
        _callerExternal = true;
        _narrowing = _ => throw new InvalidOperationException("the narrowing is not asked: there is no document");

        await Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        _graph.SendMailBody().Prop("message").Prop("subject").GetString().Should().Be(AnalysisTitle);
    }

    /// <summary>The narrowing faulting hides the document (fail closed): nothing is filled.</summary>
    [Fact]
    public async Task AnAnalysisDecisionThatThrows_FillsNothing()
    {
        SubjectAndBodyProfile();
        ExternalCallerWithSourceDocument(DocumentNarrowingReason.Visible);
        _narrowing = _ => throw new HttpRequestException("narrowing read failed: upstream-secret");

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        NothingOfTheAnalysisWasReadBeyondItsDocumentLinks();
    }

    /// <summary>The analysis' document links cannot be read for an external caller: undecidable, so nothing is filled.</summary>
    [Fact]
    public async Task AnAnalysisWhoseDocumentLinksCannotBeRead_FillsNothing_ForAnExternalCaller()
    {
        SubjectAndBodyProfile();
        _callerExternal = true;
        _entities
            .Setup(s => s.RetrieveAsync(
                "sprk_analysis", AnalysisId, It.Is<string[]>(c => c.SequenceEqual(AnalysisAuthorizationFilter.AnalysisDocumentColumns)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("link read failed: upstream-secret"));

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        NothingOfTheAnalysisWasReadBeyondItsDocumentLinks();
    }

    /// <summary>The caller holds Read, but the app-only read finds no analysis (it was deleted between the two): nothing is filled.</summary>
    [Fact]
    public async Task AnAnalysisTheCallerMayReadButTheAppOnlyReadDoesNotFind_FillsNothing()
    {
        SubjectAndBodyProfile();
        _appOnlyReturnsNull = true;

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        _probe.Asked.Should().Contain(AnalysisId, "the caller's Read was allowed first");
    }

    /// <summary>
    /// The mapping-source rule: an analysis owned by the Secure Record owner team is secure, and the email row's owner is not
    /// decided yet, so nothing is filled and the send fails validation with no Graph call.
    /// </summary>
    [Fact]
    public async Task AnAnalysisOwnedByTheSecureRecordOwnerTeam_FillsNothing()
    {
        SubjectAndBodyProfile();
        _analysis = new Entity("sprk_analysis", AnalysisId)
        {
            ["sprk_name"] = AnalysisTitle,
            ["sprk_description"] = AnalysisSummary,
            ["ownerid"] = new EntityReference("team", SecureTeamId),
        };

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
    }

    /// <summary>Must not fire: an analysis owned by an ordinary team (not the Secure team) is not secure and maps.</summary>
    [Fact]
    public async Task AnAnalysisOwnedByAnOrdinaryTeam_IsFilled()
    {
        SubjectAndBodyProfile();
        _analysis = new Entity("sprk_analysis", AnalysisId)
        {
            ["sprk_name"] = AnalysisTitle,
            ["sprk_description"] = AnalysisSummary,
            ["ownerid"] = new EntityReference("team", Guid.Parse("00000028-1111-4000-8000-0000000000ff")),
        };

        await Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        _graph.SendMailBody().Prop("message").Prop("subject").GetString().Should().Be(AnalysisTitle);
    }

    // ── A mapped value longer than its column is not used ────────────────────────────────────────────────────

    /// <summary>
    /// A subject longer than the subject column would send the email and then fail to create its row, so it is not used: the send
    /// answers its own empty-subject validation and nothing is delivered. MUTATION: no length check delivers an unrecorded email.
    /// </summary>
    [Fact]
    public async Task AMappedSubjectLongerThanItsColumn_IsNotUsed()
    {
        SubjectAndBodyProfile();
        _analysis!["sprk_name"] = new string('s', SendTimeFieldMapping.MaxSubjectLength + 1);

        var send = () => Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Subject is required");
        _graph.Requests.Should().BeEmpty();
        _stored.Should().BeNull();
    }

    /// <summary>Must not fire: a subject exactly as long as the column holds is used whole.</summary>
    [Fact]
    public async Task AMappedSubjectExactlyAsLongAsItsColumn_IsUsed()
    {
        SubjectAndBodyProfile();
        var subject = new string('s', SendTimeFieldMapping.MaxSubjectLength);
        _analysis!["sprk_name"] = subject;

        await Sut().SendAsync(Email(subject: "", body: "<p>b</p>", Analysed()), Http());

        _graph.SendMailBody().Prop("message").Prop("subject").GetString().Should().Be(subject);
        _stored!.GetAttributeValue<string>("sprk_subject").Should().Be(subject);
    }

    [Fact]
    public async Task AMappedBodyLongerThanItsColumn_IsNotUsed_AndOneExactlyAtTheLimitIs()
    {
        SubjectAndBodyProfile();
        _analysis!["sprk_description"] = new string('b', SendTimeFieldMapping.MaxBodyLength + 1);

        var send = () => Sut().SendAsync(Email(subject: "Typed subject", body: "", Analysed()), Http());

        (await send.Should().ThrowAsync<SdapProblemException>()).Which.Detail.Should().Contain("Body is required");
        _graph.Requests.Should().BeEmpty();

        var body = new string('b', SendTimeFieldMapping.MaxBodyLength);
        _analysis["sprk_description"] = body;

        await Sut().SendAsync(Email(subject: "Typed subject", body: "", Analysed()), Http());

        _graph.SendMailBody().Prop("message").Prop("body").Prop("content").GetString().Should().Be(body);
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

    /// <summary>An external caller, and an analysis derived from <see cref="SourceDocumentId"/> that the narrowing decides as stated.</summary>
    private void ExternalCallerWithSourceDocument(DocumentNarrowingReason reason)
    {
        _callerExternal = true;
        _analysis!["sprk_documentid"] = new EntityReference("sprk_document", SourceDocumentId);
        _narrowing = _ => Task.FromResult(new DocumentNarrowingResult(
            new Dictionary<DocumentNarrowingRow, DocumentNarrowingReason>
            {
                [new DocumentNarrowingRow(SourceDocumentId, default, Guid.Empty, Guid.Empty)] = reason,
            },
            faulted: false));
    }

    /// <summary>The analysis was asked for only its document links (the withholding decision), never its mapped columns.</summary>
    private void NothingOfTheAnalysisWasReadBeyondItsDocumentLinks() => _entities.Verify(
        e => e.RetrieveAsync(
            "sprk_analysis", It.IsAny<Guid>(),
            It.Is<string[]>(c => !c.SequenceEqual(AnalysisAuthorizationFilter.AnalysisDocumentColumns)), It.IsAny<CancellationToken>()),
        Times.Never);

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
