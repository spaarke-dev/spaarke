using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// #2091: the re-file writers that run <see cref="SecureChildReconciler.AfterRefileAsync"/> get the reconciler from the
/// REAL host composition (<see cref="CustomWebAppFactory"/>, which replaces only the network-bound services), with scope
/// validation ON — so a dropped registration, or a captive scoped dependency, fails here instead of silently skipping the
/// step after a re-file (a row moved under a Secure record left business-unit readable).
/// </summary>
/// <remarks>
/// <see cref="DataverseUpdateHandler"/> (scoped) takes the reconciler as a REQUIRED constructor dependency. The playbook node's
/// core (<c>UpdateRecordActionCore</c>, constructor frozen by task 031) resolves it from a scope of the
/// <see cref="IServiceScopeFactory"/> the node executor is given — the root's.
/// </remarks>
public sealed class RefileWriterReconcilerRegistrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public RefileWriterReconcilerRegistrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void WithScopeValidation_TheOrchestratorsUpdateHandler_ResolvesWithTheReconciler()
    {
        using var validating = Validating();
        using var scope = validating.Services.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<IDataverseUpdateHandler>()
            .Should().BeOfType<DataverseUpdateHandler>().Subject;

        ReconcilerOf(handler).Should().NotBeNull().And.BeSameAs(scope.ServiceProvider.GetRequiredService<SecureChildReconciler>(),
            "the handler's reconciler is the scope's own (both scoped), never a captured or missing one");
    }

    [Fact]
    public void WithScopeValidation_ThePlaybookNodesScopePath_ResolvesTheReconciler()
    {
        using var validating = Validating();

        // The factory the node executor (a long-lived service) is handed comes from the root; the core opens a scope from it.
        var scopes = validating.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopes.CreateScope();

        scope.ServiceProvider.GetService<SecureChildReconciler>().Should().NotBeNull(
            "UpdateRecordActionCore resolves the reconciler from this scope; absent, the playbook node's re-file would skip the step");
    }

    [Fact]
    public void WithScopeValidation_TheInvoiceDocumentRefile_ResolvesWithTheReconciler_AndTheSubmissionAfterStepsCompose()
    {
        // #2095 / ebilling-r1 050a (D-134): confirm no longer files anything; the submission job's after-steps re-file a confirmed
        // document under the invoice (InvoiceDocumentRefile), then run AfterRefileAsync.
        using var validating = Validating();
        using var scope = validating.Services.CreateScope();

        var refile = scope.ServiceProvider.GetRequiredService<Sprk.Bff.Api.Services.Finance.Submission.InvoiceDocumentRefile>();
        typeof(Sprk.Bff.Api.Services.Finance.Submission.InvoiceDocumentRefile)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(SecureChildReconciler))
            .GetValue(refile).Should().BeSameAs(scope.ServiceProvider.GetRequiredService<SecureChildReconciler>(),
                "the re-file's reconciler is the scope's own (both scoped), never a captured or missing one");

        scope.ServiceProvider.GetRequiredService<Sprk.Bff.Api.Services.Finance.Submission.IInvoiceSubmissionAfterSteps>()
            .Should().BeOfType<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionAfterSteps>(
                "the after-steps take the re-file as a constructor dependency, so they compose only with it registered");
    }

    [Fact]
    public void WithScopeValidation_TheThreadJoinWriters_HoldTheRootsScopeFactory_WhoseScopeResolvesTheReconciler()
    {
        // #2091: ThreadResolver's join and CommunicationService's respond-into-thread (both singletons the reconciler itself
        // depends on) open a scope for the step after a re-file. A factory the host did not hand them would leave every join's
        // sharing as it was, logged only.
        using var validating = Validating();

        var writers = new object[]
        {
            validating.Services.GetRequiredService<Sprk.Bff.Api.Services.Communication.IThreadResolver>()
                .Should().BeOfType<Sprk.Bff.Api.Services.Communication.ThreadResolver>().Subject,
            validating.Services.GetRequiredService<Sprk.Bff.Api.Services.Communication.CommunicationService>(),
        };

        foreach (var writer in writers)
        {
            var scopes = ScopeFactoryOf(writer);
            scopes.Should().NotBeNull($"{writer.GetType().Name} is given the host's scope factory");
            using var scope = scopes!.CreateScope();
            scope.ServiceProvider.GetService<SecureChildReconciler>().Should().NotBeNull(
                $"{writer.GetType().Name}'s join resolves the reconciler from this scope; absent, the step after a re-file is skipped");
        }
    }

    private static IServiceScopeFactory? ScopeFactoryOf(object writer) => (IServiceScopeFactory?)writer.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(IServiceScopeFactory))
        .GetValue(writer);

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Validating() =>
        _factory.WithWebHostBuilder(builder => builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));

    private static object? ReconcilerOf(DataverseUpdateHandler handler) => typeof(DataverseUpdateHandler)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(SecureChildReconciler))
        .GetValue(handler);
}
