using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Task 171 attach fix (verifier K3, 2026-10-07): the RECORD-LESS upload <c>PUT /api/obo/me/files/{*path}</c> — what the
/// EmailComposer local attachment and the upload wizard's "skip associate" use — records the item it created as uploaded
/// FOR THE CALLER, in the caller's tenant. Without that binding every record-less upload is unattachable through
/// <c>POST /api/v1/documents/{id}/file</c> (the live regression).
/// </summary>
/// <remarks>Real route, real container resolution (<see cref="TestActingUserBusinessUnit"/>'s acting user, an enabled
/// internal person); the SPE facade and the attribution are recorded (ADR-038 §4).</remarks>
public class MeFilesUploadAttributionTests : IClassFixture<MeFilesUploadAttributionTests.Host>
{
    private readonly Host _host;

    public MeFilesUploadAttributionTests(Host host)
    {
        _host = host;
        _host.Recorded.Clear();
    }

    [Fact(DisplayName = "Attach fix (K3, F1): /me/files records the created item for the caller, in the caller's tenant and drive — from the LONG-FORM claims production delivers")]
    public async Task MeFiles_RecordsTheItemForTheCaller()
    {
        var response = await _host.Client().PutAsync("/api/obo/me/files/notes.txt", new ByteArrayContent([1]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var recorded = _host.Recorded.Should().ContainSingle(
            "without it every record-less upload is unattachable — the regression this PR fixes").Subject;
        recorded.Tenant.Should().Be(Host.TenantId);
        recorded.Caller.Should().Be(TestSessionOwner.Oid);
        recorded.Drive.Should().Be(TestActingUserBusinessUnit.ContainerId);
        recorded.Item.Should().Be(Host.CreatedItem);
    }

    public sealed class Host : CustomWebAppFactory
    {
        public const string TenantId = "17160000-0000-4000-8000-0000000000aa";
        public const string CreatedItem = "item-me-files-1";
        internal const string SchemeName = "MeFilesGuidOid";

        public ConcurrentQueue<(string Tenant, string Caller, string Drive, string Item)> Recorded { get; } = new();

        public HttpClient Client()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, GuidOidHandler>(SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = SchemeName;
                    o.DefaultChallengeScheme = SchemeName;
                });

                var rows = new Mock<IGenericEntityService>();
                TestActingUserBusinessUnit.Arrange(rows);
                services.RemoveAll<IGenericEntityService>();
                services.AddSingleton(rows.Object);

                services.RemoveAll<SpeFileStore>();
                services.AddScoped<SpeFileStore>(sp => new CreatingStore(sp));

                services.RemoveAll<UploadAttribution>();
                services.AddSingleton<UploadAttribution>(new RecordingAttribution(Recorded));
            });
        }

        private sealed class GuidOidHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                // ONLY the long-form claim types (verifier F1, 2026-10-07): the BFF keeps inbound claim-type mapping ON,
                // so in production "oid" / "tid" arrive as these. A short-form-only read records no binding.
                var identity = new ClaimsIdentity(
                    [
                        new Claim(Sprk.Bff.Api.Infrastructure.Authentication.CallerResolution.ObjectIdSchemaClaim, TestSessionOwner.Oid),
                        new Claim(Sprk.Bff.Api.Infrastructure.Authentication.TenantResolution.TenantIdSchemaClaim, TenantId),
                    ],
                    SchemeName);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }

        private sealed class CreatingStore(IServiceProvider sp) : SpeFileStore(
            sp.GetRequiredService<ContainerOperations>(), sp.GetRequiredService<DriveItemOperations>(),
            sp.GetRequiredService<UploadSessionManager>(), sp.GetRequiredService<UserOperations>())
        {
            public override Task<FileHandleDto?> UploadSmallAsync(
                string driveId, string path, Stream content, ConflictBehavior conflictBehavior, CancellationToken ct = default)
            {
                var now = DateTimeOffset.UtcNow;
                return Task.FromResult<FileHandleDto?>(new FileHandleDto(CreatedItem, path, null, 1, now, now, null, false, null, driveId));
            }
        }

        private sealed class RecordingAttribution(ConcurrentQueue<(string, string, string, string)> recorded) : UploadAttribution(
            new Sprk.Bff.Api.Infrastructure.Cache.TenantCache(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<Sprk.Bff.Api.Infrastructure.Cache.TenantCache>.Instance))
        {
            public override Task RecordItemAsync(string tenantId, string callerObjectId, string drive, string item, CancellationToken ct = default)
            {
                recorded.Enqueue((tenantId, callerObjectId, drive, item));
                return base.RecordItemAsync(tenantId, callerObjectId, drive, item, ct);
            }
        }
    }
}
