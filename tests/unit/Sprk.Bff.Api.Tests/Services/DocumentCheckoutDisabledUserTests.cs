using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Issue #1576 — <see cref="DocumentCheckoutService"/> resolves the caller's systemuser with
/// <c>isdisabled eq false</c>, so a Dataverse-DISABLED user cannot check a document out: the lookup finds nobody and
/// the checkout is refused before any document is read or locked.
/// </summary>
/// <remarks>The production service over an in-memory ASP.NET Core server standing in for the Dataverse Web API
/// (ADR-038 §7). The fake answers the systemuser lookup by EVALUATING its filter against an <c>isdisabled = true</c>
/// row, as Dataverse would, and records every other request so the test can prove nothing past the lookup ran.</remarks>
public sealed class DocumentCheckoutDisabledUserTests
{
    private static readonly Guid DisabledOid = Guid.Parse("15760000-0000-0000-0000-0000000004d1");
    private const string DisabledSystemUserId = "15760000-0000-0000-0000-0000000004d2";

    [Fact]
    public async Task A_disabled_user_cannot_check_a_document_out()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var service = new DocumentCheckoutService(
            dataverse.CreateClient(),
            speFileStore: null!, // never reached: the refusal comes first
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "http://localhost" })
                .Build(),
            new StaticTokenCredential(),
            new RecordOwnershipResolverDouble(),
            NullLogger<DocumentCheckoutService>.Instance,
            TestRecordContainerResolver.ForBusinessUnitContainers(_ => true));

        var caller = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", DisabledOid.ToString("D")) }, "test"));

        var act = () => service.CheckoutAsync(Guid.NewGuid(), caller, "corr-1576");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found in Dataverse*");
        dataverse.Paths.Should().Equal(new[] { "systemusers" }, "nothing past the caller lookup may run for a disabled user");
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }

    private sealed class FakeDataverse : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<string> _paths = new();

        private FakeDataverse(WebApplication app) => _app = app;

        public IReadOnlyList<string> Paths => _paths.ToArray();

        public static async Task<FakeDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new FakeDataverse(app);
            app.Map("/api/data/v9.2/{**rest}", (HttpContext context) => fake.HandleAsync(context));
            await app.StartAsync();
            return fake;
        }

        public HttpClient CreateClient() => _app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            var path = Uri.UnescapeDataString(context.Request.Path.Value ?? string.Empty)["/api/data/v9.2/".Length..];
            _paths.Enqueue(path);
            context.Response.ContentType = "application/json";

            if (path == "systemusers")
            {
                // The only systemuser with this oid is DISABLED: a filter that excludes disabled users finds nobody.
                var filter = context.Request.Query["$filter"].ToString();
                var excludesDisabled = filter.Contains("isdisabled eq false", StringComparison.OrdinalIgnoreCase);
                await context.Response.WriteAsync(excludesDisabled
                    ? "{\"value\":[]}"
                    : $"{{\"value\":[{{\"systemuserid\":\"{DisabledSystemUserId}\"}}]}}", Encoding.UTF8);
                return;
            }

            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("{}", Encoding.UTF8);
        }
    }
}
