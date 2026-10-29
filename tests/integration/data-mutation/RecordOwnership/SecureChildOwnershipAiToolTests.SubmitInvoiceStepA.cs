using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 1, A). Step 1 is READ-ONLY (fix round F1-b): the caller's Read on the source document
/// and on its communication, and the communication's match. Step A is handed to the intake, which runs it only after its own read-only refusals;
/// step A decides "unfiled" from the D-140 chain and files to the core record, AS THE CALLER, through the REAL communication re-file core
/// (<c>CommunicationEndpoints.RefileCommunicationAsync</c> over <c>ChildRecordEndpoints.UpdateAsync</c>), over this harness's scripted caller
/// client, ownership directory, restamper and reconciler. Any answer of the re-file core but 204 is the route's answer and nothing is submitted.
/// </summary>
/// <remarks>Maintain-class (ADR-038): A is the one write Submit Invoice makes as the caller, and the only one it does not undo.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid SourceDocument = Guid.Parse("09400000-0000-4000-8000-00000000d0c1");
    private static readonly Guid SourceThread = Guid.Parse("09400000-0000-4000-8000-00000000d0c2");
    private static readonly Guid AnotherMatter = Guid.Parse("09400000-0000-4000-8000-00000000d0c3");

    /// <summary>A record thread's <c>sprk_threadtype</c> (a Direct thread is <see cref="RecordThreadFiling.DirectThreadType"/>).</summary>
    private const int RecordThreadType = 100000000;

    private readonly Sprk.Bff.Api.Tests.Finance.Files.RecordingRightsProbe _sourceRights = new();

    /// <summary>
    /// The app-only reads of step 1 and of A's "unfiled" decision: the document (linked to <see cref="FiledCommunication"/>), the communication's
    /// filing (a regarding matter when <paramref name="communicationFiled"/>; the thread <paramref name="thread"/> when given) and the thread row
    /// (none: the thread cannot be found).
    /// </summary>
    private static Mock<IGenericEntityService> SourceReads(bool communicationFiled, Entity? thread = null, bool inThread = false)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_document", SourceDocument, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", SourceDocument)
            {
                ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", FiledCommunication),
            });
        var communication = new Entity("sprk_communication", FiledCommunication);
        if (communicationFiled)
        {
            communication["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter);
        }

        if (thread is not null || inThread)
        {
            communication["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", SourceThread);
        }

        entities.Setup(e => e.RetrieveAsync("sprk_communication", FiledCommunication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);
        entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => thread is null ? new EntityCollection() : new EntityCollection(new List<Entity> { thread }));
        return entities;
    }

    /// <summary>A thread row: its type and the matter it carries.</summary>
    private static Entity ThreadRow(int threadType, Guid matter) => new("sprk_communicationthread", SourceThread)
    {
        ["sprk_threadtype"] = new OptionSetValue(threadType),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
    };

    private HttpContext SubmitInvoiceHttp(IGenericEntityService entities)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallerRecordAccessProbe>(_sourceRights);
        services.AddSingleton(entities);
        services.AddSingleton<IDataverseUserClient>(_user);
        services.AddSingleton<IRecordOwnershipResolver>(_world.Resolver());
        services.AddSingleton(Restamper());
        services.AddSingleton(Children());
        services.AddSingleton(new Mock<ICommunicationDataverseService>().Object);
        // A's isolation check over the share world (the REAL synchronizer's upward walk and its Secure Record owner team).
        services.AddSingleton(new ExistingDocumentIsolationCheck(
            SecureChildShareWorld.EntitiesOver(() => ShareWorld).Object, Shares(), NullLogger<ExistingDocumentIsolationCheck>.Instance));
        var http = HttpContextOfCaller();
        http.Request.Headers.Authorization = "Bearer caller-token";
        http.RequestServices = services.BuildServiceProvider();
        return http;
    }

    private Task<InvoiceSubmissionEndpoints.SourcePreparation> StepOne(IGenericEntityService entities, Guid? communicationId = null) =>
        InvoiceSubmissionEndpoints.PrepareSourceAsync(
            SourceDocument, communicationId ?? FiledCommunication, new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, OrdinaryMatter),
            SubmitInvoiceHttp(entities), documentReadChecked: false, CancellationToken.None);

    /// <summary>Step 1, then step A as the intake runs it (the hook the route hands over).</summary>
    private async Task<bool> StepOneThenA(IGenericEntityService entities)
    {
        var prepared = await StepOne(entities);
        prepared.Refusal.Should().BeNull("step 1 passes");
        _user.Patches.Should().BeEmpty("step 1 is read-only: nothing is filed before the intake's refusals have run");
        prepared.Hook.Should().NotBeNull("the document has a communication, so there is a step A to hand to the intake");
        return await prepared.Hook!(CancellationToken.None);
    }

    private void CallerCanReadTheSource(AccessRights onCommunication)
    {
        _sourceRights.Grant("sprk_documents", SourceDocument, AccessRights.Read);
        _sourceRights.Grant("sprk_communications", FiledCommunication, onCommunication);
    }

    [Fact(DisplayName = "094 A (must fire): an unfiled source communication is filed to the core record AS THE CALLER through the re-file core, only when the intake runs A")]
    public async Task SubmitInvoice_UnfiledCommunication_IsFiledAsTheCaller_WhenStepARuns()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var filed = await StepOneThenA(SourceReads(communicationFiled: false).Object);

        filed.Should().BeTrue("A filed it (204), so the submission proceeds");
        var patch = _user.Patches.Should().ContainSingle("one PATCH, as the caller, by the re-file core").Subject;
        patch.Path.Should().Contain(FiledCommunication.ToString("D"));
        patch.Body.Should().Contain($"/sprk_matters({OrdinaryMatter:D})");
    }

    [Fact(DisplayName = "094 A (must fire): a communication on a Direct thread is unfiled by the D-140 chain and is filed by A")]
    public async Task SubmitInvoice_CommunicationOnADirectThread_IsUnfiled_AndFiled()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var filed = await StepOneThenA(SourceReads(communicationFiled: false,
            thread: new Entity("sprk_communicationthread", SourceThread) { ["sprk_threadtype"] = new OptionSetValue(RecordThreadFiling.DirectThreadType) }).Object);

        filed.Should().BeTrue();
        _user.Patches.Should().ContainSingle();
    }

    [Fact(DisplayName = "094 A (must fire): the re-file core's refusal (no AppendTo on the core record) is the answer as it is; nothing is written or submitted")]
    public async Task SubmitInvoice_ReFileRefused_IsTheAnswer()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);
        _user.NoAppendTo.Add(OrdinaryMatter);

        var act = () => StepOneThenA(SourceReads(communicationFiled: false).Object);

        var refused = (await act.Should().ThrowAsync<InvoiceSubmissionEndpoints.SourceFilingRefusedException>()).Which.Answer;
        Status(refused).Should().Be(StatusCodes.Status404NotFound, "the re-file core's own answer for a record the caller cannot append to");
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must fire): an unfiled communication the caller may not Write is the re-file route's own 403 (COMMUNICATION_REFILE_FORBIDDEN); the core is never asked")]
    public async Task SubmitInvoice_UnfiledCommunication_WithoutWrite_IsTheReFileForbidden()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read);

        var act = () => StepOneThenA(SourceReads(communicationFiled: false).Object);

        // Changed 2026-10-29 (095 contract fix): the 403 is the intake's answer (a SourceFilingRefusedException carrying the route's
        // Refused result), not an SdapProblemException the problem middleware would render with the code nested under extensions.code.
        // Asserted on the wire body: a client reads errorCode ?? reasonCode (contract section 6).
        var refused = (await act.Should().ThrowAsync<InvoiceSubmissionEndpoints.SourceFilingRefusedException>()).Which.Answer;
        var (status, body) = await Sprk.Bff.Api.Tests.Services.Finance.Submission.WireBody.OfAsync(refused);
        status.Should().Be(StatusCodes.Status403Forbidden);
        body.GetProperty("errorCode").GetString().Should().Be(Sprk.Bff.Api.Api.Filters.CommunicationRecordAuthorizationFilter.RefileDenyCode);
        body.GetProperty("detail").GetString().Should().Be(InvoiceSubmissionEndpoints.RefileForbiddenDetail);
        body.TryGetProperty("filingKept", out _).Should().BeFalse("nothing was filed");
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must not fire): a communication already filed is not re-filed; no write")]
    public async Task SubmitInvoice_FiledCommunication_IsNotReFiled()
    {
        CallerCanReadTheSource(AccessRights.Read);

        (await StepOneThenA(SourceReads(communicationFiled: true).Object)).Should().BeFalse();

        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must not fire): step A decides 'unfiled' from the D-140 chain (case 1); no write")]
    public async Task SubmitInvoice_StepA_UnfiledFromTheD140Chain_Case1_NoWrite()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var filed = await StepOneThenA(SourceReads(communicationFiled: false, thread: ThreadRow(RecordThreadType, AnotherMatter)).Object);

        filed.Should().BeFalse("the D-140 chain is not empty");
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must not fire): step A decides 'unfiled' from the D-140 chain (case 2); no write")]
    public async Task SubmitInvoice_StepA_UnfiledFromTheD140Chain_Case2_NoWrite()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var filed = await StepOneThenA(SourceReads(communicationFiled: false, thread: ThreadRow(RecordThreadType, SecureMatter)).Object);

        filed.Should().BeFalse();
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must not fire): step A decides 'unfiled' from the D-140 chain (case 3, fail closed); no write")]
    public async Task SubmitInvoice_StepA_UnfiledFromTheD140Chain_Case3_NoWrite()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var filed = await StepOneThenA(SourceReads(communicationFiled: false, inThread: true).Object);

        filed.Should().BeFalse();
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must fire): step A refuses an isolated communication (case 1): 409; no write")]
    public async Task SubmitInvoice_StepA_IsolatedCommunication_Case1_Is409_NoWrite()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);
        ShareWorld
            .SecureChild("sprk_communicationthread", SourceThread, ("sprk_regardingmatter", "sprk_matter", SecureMatter))
            .UserOwnedChild("sprk_communication", FiledCommunication, ("sprk_communicationthread", "sprk_communicationthread", SourceThread));
        var directThread = ThreadRow(RecordThreadFiling.DirectThreadType, SecureMatter);

        var act = () => StepOneThenA(SourceReads(communicationFiled: false, thread: directThread).Object);

        var refused = (await act.Should().ThrowAsync<InvoiceSubmissionEndpoints.SourceFilingRefusedException>()).Which.Answer;
        Status(refused).Should().Be(StatusCodes.Status409Conflict);
        ErrorCode(refused).Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must fire): step A refuses an isolated communication (case 2): 409; no write")]
    public async Task SubmitInvoice_StepA_IsolatedCommunication_Case2_Is409_NoWrite()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);
        ShareWorld.SecureChild("sprk_communication", FiledCommunication);

        var act = () => StepOneThenA(SourceReads(communicationFiled: false).Object);

        var refused = (await act.Should().ThrowAsync<InvoiceSubmissionEndpoints.SourceFilingRefusedException>()).Which.Answer;
        Status(refused).Should().Be(StatusCodes.Status409Conflict);
        ErrorCode(refused).Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must fire): the communication's isolation cannot be read: 503; never filed")]
    public async Task SubmitInvoice_UnfiledCommunication_IsolationUnreadable_Is503_NotFiled()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);
        ShareWorld.FailingQueriesOf("sprk_communication");

        var act = () => StepOneThenA(SourceReads(communicationFiled: false).Object);

        var refused = (await act.Should().ThrowAsync<InvoiceSubmissionEndpoints.SourceFilingRefusedException>()).Which.Answer;
        Status(refused).Should().Be(StatusCodes.Status503ServiceUnavailable);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 1 (must fire): no Read on the source document is 403 source_forbidden, even with Read on its communication; nothing is read further or written")]
    public async Task SubmitInvoice_NoReadOnTheDocument_IsForbidden()
    {
        UnfiledCommunicationExists();
        _sourceRights.Grant("sprk_communications", FiledCommunication, AccessRights.Read | AccessRights.Write);
        var entities = SourceReads(communicationFiled: false);

        var prepared = await StepOne(entities.Object);

        Status(prepared.Refusal!).Should().Be(StatusCodes.Status403Forbidden);
        ErrorCode(prepared.Refusal!).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        prepared.Hook.Should().BeNull("a refusal hands no step A to the intake");
        entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 1 (must fire): Read on the document but not on its communication is 403 source_forbidden; nothing written")]
    public async Task SubmitInvoice_NoReadOnTheCommunication_IsForbidden()
    {
        UnfiledCommunicationExists();
        _sourceRights.Grant("sprk_documents", SourceDocument, AccessRights.Read);

        var prepared = await StepOne(SourceReads(communicationFiled: false).Object);

        Status(prepared.Refusal!).Should().Be(StatusCodes.Status403Forbidden);
        ErrorCode(prepared.Refusal!).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        prepared.Hook.Should().BeNull();
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 1 (must fire): a communication named that is not the document's own is 422 source_communication_mismatch; nothing written")]
    public async Task SubmitInvoice_CommunicationMismatch_Is422()
    {
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var prepared = await StepOne(SourceReads(communicationFiled: false).Object, communicationId: Guid.NewGuid());

        Status(prepared.Refusal!).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ErrorCode(prepared.Refusal!).Should().Be(InvoiceSubmissionErrors.SourceCommunicationMismatch);
        _user.Patches.Should().BeEmpty();
    }

    /// <summary>The submission routes' refusal code (<c>InvoiceSubmissionHttp.Refused</c>: a ProblemDetails with <c>errorCode</c>).</summary>
    private static string? ErrorCode(IResult result) =>
        result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem && problem.ProblemDetails.Extensions.TryGetValue("errorCode", out var code)
            ? code?.ToString()
            : null;
}
