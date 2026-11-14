using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1, #2081 follow-up. <see cref="DecisionRouteCores.UpdateChildAsync"/> is the decision executors' in-process
/// child-records update (the REAL <c>ChildRecordEndpoints.UpdateAsync</c>). Like the re-file route and the chat update tool it
/// must hand the core the APP-only <see cref="IGenericEntityService"/> that D-96 counts an invoice's lines with; without it an
/// invoice core-lookup move failed closed as <c>invoice_family_read_failed</c> even when no line was left behind, and the
/// D-96 refusal could never be the answer.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The services the commit route's request carries for the update core.</summary>
    private HttpContext DecisionRouteHttp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseUserClient>(_user);
        services.AddSingleton<IRecordOwnershipResolver>(_world.Resolver());
        services.AddSingleton(Restamper());
        services.AddSingleton(Children());
        services.AddSingleton(AppEntities()); // IGenericEntityService, the app's: its RetrieveMultiple evaluates the line query over _appLines
        var http = HttpContextOfCaller();
        http.RequestServices = services.BuildServiceProvider();
        return http;
    }

    private Task<RouteReply> DecisionRouteUpdate(string table, Guid id, Dictionary<string, object?> payload) =>
        new DecisionRouteCores().UpdateChildAsync(DecisionRouteHttp(), table, id, Payload(payload), CancellationToken.None);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DecisionRoute_AnInvoiceMovedToAProject_IsRefusedCoreRecordFixed_AsTheOtherPathsRefuseIt(bool aLineStaysBehind)
    {
        // Task 045g rule 1 (owner, EB-D-231): wherever its lines are, an invoice is never moved; the reply says what is done instead.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);
        if (aLineStaysBehind)
            AppLine(GuardInvoice, "sprk_matter", "sprk_matter", OrdinaryMatter);

        var reply = await DecisionRouteUpdate("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        reply.Status.Should().Be(StatusCodes.Status400BadRequest, reply.Detail);
        reply.ReasonCode.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode,
            "through the decision route, not a read failure for want of the app-only reader");
        reply.Detail.Should().Contain(InvoiceFamilyCoreRecordGuard.ReturnAndResubmitHint);
        _user.Patches.Should().BeEmpty();
        _appLineQueries.Should().BeEmpty("decided on the stored row before any other read");
    }

    [Fact]
    public async Task DecisionRoute_ALineMovedToAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);
        StoredLine(GuardLine, OrdinaryMatter, GuardInvoice);

        var reply = await DecisionRouteUpdate("sprk_billingevent", GuardLine,
            new() { ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})" });

        reply.Status.Should().Be(StatusCodes.Status400BadRequest, reply.Detail);
        reply.ReasonCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        reply.Detail.Should().Contain(InvoiceFamilyCoreRecordGuard.ReturnAndResubmitHint);
        _user.Patches.Should().BeEmpty();
    }
}
