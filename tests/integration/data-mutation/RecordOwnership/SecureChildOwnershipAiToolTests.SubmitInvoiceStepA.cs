using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 1, A): Submit Invoice files an UNFILED source communication to the core record
/// first, AS THE CALLER, through the REAL communication re-file core (<c>CommunicationEndpoints.RefileCommunicationAsync</c> over
/// <c>ChildRecordEndpoints.UpdateAsync</c>), over this harness's scripted caller client, ownership directory, restamper and reconciler.
/// Any answer of that core but 204 is the route's answer and nothing is submitted.
/// </summary>
/// <remarks>Maintain-class (ADR-038): A is the one write Submit Invoice makes as the caller, and the only one it does not undo.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid SourceDocument = Guid.Parse("09400000-0000-4000-8000-00000000d0c1");

    private readonly Sprk.Bff.Api.Tests.Finance.Files.RecordingRightsProbe _sourceRights = new();

    /// <summary>The app-only reads of step 1: the document (linked to <see cref="FiledCommunication"/>) and the communication's filing.</summary>
    private static Mock<IGenericEntityService> SourceReads(bool communicationFiled)
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

        entities.Setup(e => e.RetrieveAsync("sprk_communication", FiledCommunication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);
        return entities;
    }

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
        var http = HttpContextOfCaller();
        http.Request.Headers.Authorization = "Bearer caller-token";
        http.RequestServices = services.BuildServiceProvider();
        return http;
    }

    private Task<IResult?> StepOne(IGenericEntityService entities, Guid? communicationId = null) =>
        InvoiceSubmissionEndpoints.PrepareSourceAsync(
            SourceDocument, communicationId ?? FiledCommunication, new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, OrdinaryMatter),
            SubmitInvoiceHttp(entities), documentReadChecked: false, CancellationToken.None);

    private void CallerCanReadTheSource(AccessRights onCommunication)
    {
        _sourceRights.Grant("sprk_documents", SourceDocument, AccessRights.Read);
        _sourceRights.Grant("sprk_communications", FiledCommunication, onCommunication);
    }

    [Fact(DisplayName = "094 A (must fire): an unfiled source communication is filed to the core record AS THE CALLER through the re-file core, then the submission may proceed")]
    public async Task SubmitInvoice_UnfiledCommunication_IsFiledAsTheCaller_First()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var refused = await StepOne(SourceReads(communicationFiled: false).Object);

        refused.Should().BeNull("A filed it (204), so the submission proceeds");
        var patch = _user.Patches.Should().ContainSingle("one PATCH, as the caller, by the re-file core").Subject;
        patch.Path.Should().Contain(FiledCommunication.ToString("D"));
        patch.Body.Should().Contain($"/sprk_matters({OrdinaryMatter:D})");
    }

    [Fact(DisplayName = "094 A (must fire): the re-file core's refusal (no AppendTo on the core record) is the answer as it is; nothing is written or submitted")]
    public async Task SubmitInvoice_ReFileRefused_IsTheAnswer()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);
        _user.NoAppendTo.Add(OrdinaryMatter);

        var refused = await StepOne(SourceReads(communicationFiled: false).Object);

        refused.Should().NotBeNull();
        Status(refused!).Should().Be(StatusCodes.Status404NotFound, "the re-file core's own answer for a record the caller cannot append to");
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must fire): an unfiled communication the caller may not Write is the re-file route's own 403 (COMMUNICATION_REFILE_FORBIDDEN); the core is never asked")]
    public async Task SubmitInvoice_UnfiledCommunication_WithoutWrite_IsTheReFileForbidden()
    {
        UnfiledCommunicationExists();
        CallerCanReadTheSource(AccessRights.Read);

        var act = () => StepOne(SourceReads(communicationFiled: false).Object);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(
            Sprk.Bff.Api.Api.Filters.CommunicationRecordAuthorizationFilter.RefileDenyCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 A (must not fire): a communication already filed is not re-filed; no write")]
    public async Task SubmitInvoice_FiledCommunication_IsNotReFiled()
    {
        CallerCanReadTheSource(AccessRights.Read);

        (await StepOne(SourceReads(communicationFiled: true).Object)).Should().BeNull();

        _user.Patches.Should().BeEmpty();
    }

    [Theory(DisplayName = "094 step 1 (must fire): no Read on the source document, or on its communication, is the same 403 source_forbidden; nothing is read further or written")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitInvoice_NoReadOnTheSource_IsForbidden(bool documentReadable)
    {
        UnfiledCommunicationExists();
        if (documentReadable)
        {
            _sourceRights.Grant("sprk_documents", SourceDocument, AccessRights.Read);
        }

        var refused = await StepOne(SourceReads(communicationFiled: false).Object);

        Status(refused!).Should().Be(StatusCodes.Status403Forbidden);
        ErrorCode(refused!).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 1 (must fire): a communication named that is not the document's own is 422 source_communication_mismatch; nothing written")]
    public async Task SubmitInvoice_CommunicationMismatch_Is422()
    {
        CallerCanReadTheSource(AccessRights.Read | AccessRights.Write);

        var refused = await StepOne(SourceReads(communicationFiled: false).Object, communicationId: Guid.NewGuid());

        Status(refused!).Should().Be(StatusCodes.Status422UnprocessableEntity);
        ErrorCode(refused!).Should().Be(InvoiceSubmissionErrors.SourceCommunicationMismatch);
        _user.Patches.Should().BeEmpty();
    }

    /// <summary>The submission routes' refusal code (<c>InvoiceSubmissionHttp.Refused</c>: a ProblemDetails with <c>errorCode</c>).</summary>
    private static string? ErrorCode(IResult result) =>
        result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem && problem.ProblemDetails.Extensions.TryGetValue("errorCode", out var code)
            ? code?.ToString()
            : null;
}
