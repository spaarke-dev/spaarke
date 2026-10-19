using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// #2091: the two re-file writers that run <see cref="SecureChildReconciler.AfterRefileAsync"/> get the reconciler from the
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
    public void WithScopeValidation_TheInvoiceReviewService_ResolvesWithTheReconciler()
    {
        // #2095: the invoice-review confirm files the document under the new invoice, then runs AfterRefileAsync.
        using var validating = Validating();
        using var scope = validating.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<Sprk.Bff.Api.Services.Finance.IInvoiceReviewService>()
            .Should().BeOfType<Sprk.Bff.Api.Services.Finance.InvoiceReviewService>().Subject;

        typeof(Sprk.Bff.Api.Services.Finance.InvoiceReviewService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(SecureChildReconciler))
            .GetValue(service).Should().BeSameAs(scope.ServiceProvider.GetRequiredService<SecureChildReconciler>(),
                "the service's reconciler is the scope's own (both scoped), never a captured or missing one");
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Validating() =>
        _factory.WithWebHostBuilder(builder => builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));

    private static object? ReconcilerOf(DataverseUpdateHandler handler) => typeof(DataverseUpdateHandler)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(SecureChildReconciler))
        .GetValue(handler);
}
