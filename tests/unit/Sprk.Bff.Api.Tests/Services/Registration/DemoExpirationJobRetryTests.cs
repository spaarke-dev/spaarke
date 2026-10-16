using System.Globalization;
using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Serialization.Json;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// #1363 — demo expiry on the scheduler (<see cref="DemoExpirationJob"/>): a request is marked Expired only once every
/// cleanup step succeeded; a failed step leaves it Provisioned for the next run, which repeats every step idempotently;
/// the expired notice and each day's warning are sent once.
/// </summary>
/// <remarks>
/// <para>Before #1363 a failed step (the SPE revoke, the team removal…) was logged and the request was marked Expired
/// anyway — so nothing ever retried it and the user's access stayed.</para>
/// <para>Mocking boundary (ADR-038 §4): the real job and the real <see cref="RegistrationDataverseService"/> and
/// <see cref="GraphUserService"/> run over in-memory doubles of Dataverse (an HTTP handler, the T186 convention) and Graph
/// (a recording <see cref="IRequestAdapter"/>, the SDK's own seam); the marked-grant primitives are the stateful
/// <see cref="DemoSpeGrantLifecycleTests.InMemoryContainer"/>; the two emails are virtual.</para>
/// </remarks>
public class DemoExpirationJobRetryTests
{
    private const string AdminDataverse = "https://admin.crm.dynamics.com";
    private const string TargetDataverse = "https://zeta-demo.crm.dynamics.com";
    private const string Container = "b!zeta-container";
    private static readonly Guid RequestId = Guid.Parse("13630000-0000-4000-8000-0000000000a1");
    private static readonly Guid ObjectId = Guid.Parse("13630000-0000-4000-8000-0000000000e1");
    private static readonly Guid EnvironmentId = Guid.Parse("13630000-0000-4000-8000-0000000000f1");
    private static readonly DateTimeOffset Today = DateTimeOffset.Parse("2026-10-16T00:00:00Z", CultureInfo.InvariantCulture);

    private readonly StatefulDataverse _dataverse = new();
    private readonly StatefulGraph _graph = new();
    private readonly DemoSpeGrantLifecycleTests.InMemoryContainer _container = new();
    private readonly Mock<RegistrationEmailService> _email = new(
        null!, Options.Create(new CommunicationOptions()), NullLogger<RegistrationEmailService>.Instance);
    private readonly HashSet<string> _markers = new(StringComparer.Ordinal);
    private readonly CapturingLogger<DemoExpirationJob> _log = new();
    private readonly FakeTimeProvider _time = new(Today);

    public DemoExpirationJobRetryTests()
    {
        // The user holds the demo grant Step 8 made.
        var demoKey = Membership.MarkerKey(Membership.DemoWriterMarkerPrefix, ObjectId);
        _container.Roles.Add(new("perm-demo", ["writer"], "jane@demo.spaarke.com", ObjectId.ToString()));
        _container.Markers[demoKey] = "perm-demo";
        _email.Setup(e => e.SendExpiredNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendExpirationWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── Warnings ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>MUTATION: no completion marker sends a second warning on every manual trigger or extra run that day.</summary>
    [Fact(DisplayName = "#1363: the warning is sent once per request per UTC day, however often the job runs")]
    public async Task Warning_IsSentOncePerDay()
    {
        _dataverse.Warned(expires: Today.AddDays(2));

        await Run();
        await Run();

        Warnings().Should().Be(1);

        _time.Advance(TimeSpan.FromDays(1));
        await Run();

        Warnings().Should().Be(2, "the next day's warning still goes out (the daily schedule the loop kept)");
    }

    // ── The scheduler contract (ADR-036 A1) ─────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1363: the job runs daily at midnight UTC under a stable id")]
    public void Schedule_IsDailyAtMidnightUtc()
    {
        DemoExpirationJob.DefaultCronSchedule.Should().Be("0 0 * * *");
        Job().JobId.Should().Be("demo-expiration");
    }

    /// <summary>MUTATION: swallowing an unreadable Provisioned list reports a clean run that did nothing.</summary>
    [Fact(DisplayName = "#1363: an unreadable Provisioned list THROWS (nothing was done or sent, so the host's retry is safe)")]
    public async Task UnreadableRequestList_Throws()
    {
        _dataverse.FailRequestReads = true;

        var act = () => Run();

        await act.Should().ThrowAsync<HttpRequestException>();
        ExpiredNotices().Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private int ExpiredNotices() => _email.Invocations.Count(i => i.Method.Name == nameof(RegistrationEmailService.SendExpiredNotificationAsync));

    private int Warnings() => _email.Invocations.Count(i => i.Method.Name == nameof(RegistrationEmailService.SendExpirationWarningAsync));

    private Task<JobRunResult> Run()
        => Job().ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-1", JobRunTrigger.Scheduled, new Dictionary<string, object>()), CancellationToken.None);

    private DemoExpirationJob Job()
    {
        var options = Options.Create(new DemoProvisioningOptions { DemoUsersGroupId = "group-1" });
        var graphFactory = new Mock<IGraphClientFactory>();
        graphFactory.Setup(f => f.ForApp()).Returns(() => new GraphServiceClient(_graph));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = AdminDataverse })
            .Build();
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_dataverse, disposeHandler: false));

        var idempotency = new Mock<IIdempotencyService>();
        idempotency.Setup(i => i.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _markers.Contains(key));
        idempotency.Setup(i => i.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback((string key, TimeSpan? _, CancellationToken _) => _markers.Add(key))
            .Returns(Task.CompletedTask);
        var scopes = new ServiceCollection().AddSingleton(idempotency.Object).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new DemoExpirationJob(
            new GraphUserService(graphFactory.Object, new PasswordGenerator(), options, NullLogger<GraphUserService>.Instance),
            new RegistrationDataverseService(
                configuration, new TrackingIdGenerator(), new StaticTokenCredential(), http.Object,
                NullLogger<RegistrationDataverseService>.Instance,
                new ContactIdentityBinderFactory(
                    Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now)),
                new NullMembershipCacheInvalidator(NullLogger<NullMembershipCacheInvalidator>.Instance)),
            _email.Object,
            TestSpeOwnership.AllowAll(graphFactory.Object),
            _container,
            new Registry(configuration),
            scopes,
            _time,
            options,
            _log);
    }

    /// <summary>The environment registry at its virtual read: the request's target environment.</summary>
    private sealed class Registry(IConfiguration configuration)
        : DataverseEnvironmentService(configuration, new StaticTokenCredential(), NullLogger<DataverseEnvironmentService>.Instance)
    {
        public override Task<DataverseEnvironmentRecord?> GetByIdAsync(Guid environmentId, CancellationToken ct = default)
            => Task.FromResult<DataverseEnvironmentRecord?>(new DataverseEnvironmentRecord
            {
                Id = EnvironmentId, Name = "Zeta Demo", DataverseUrl = TargetDataverse, IsActive = true,
                TeamName = "Zeta Team", SpeContainerId = Container,
            });
    }

    /// <summary>The admin and target Dataverse: one registration request, its user and team membership, with state.</summary>
    private sealed class StatefulDataverse : HttpMessageHandler
    {
        private DateTimeOffset _expires;

        public RegistrationStatus Status { get; private set; } = RegistrationStatus.Provisioned;
        public bool InTeam { get; private set; } = true;
        public int TeamRemovals { get; private set; }
        public int ExpiredWrites { get; private set; }
        public int FailNextStatusWrites { get; set; }
        public bool FailRequestReads { get; set; }

        public void Expiring() => _expires = Today.AddDays(-1);

        public void Warned(DateTimeOffset expires) => _expires = expires;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.EndsWith("/sprk_registrationrequests", StringComparison.Ordinal))
            {
                if (FailRequestReads)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return Json(Status != RegistrationStatus.Provisioned ? "{\"value\":[]}" : $$"""
                    {"value":[{
                      "sprk_registrationrequestid":"{{RequestId}}",
                      "sprk_status":{{(int)RegistrationStatus.Provisioned}},
                      "sprk_email":"jane@contoso.example",
                      "sprk_firstname":"Jane",
                      "sprk_demouserobjectid":"{{ObjectId}}",
                      "sprk_expirationdate":"{{_expires.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}",
                      "_sprk_dataverseenvironmentid_value":"{{EnvironmentId}}"
                    }]}
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/systemusers", StringComparison.Ordinal))
                return Json("{\"value\":[{\"systemuserid\":\"99999999-0000-0000-0000-000000000001\"}]}");

            if (request.Method == HttpMethod.Get && path.EndsWith("/teammembership_association", StringComparison.Ordinal))
                return Json(InTeam ? "{\"value\":[{\"systemuserid\":\"99999999-0000-0000-0000-000000000001\"}]}" : "{\"value\":[]}");

            if (request.Method == HttpMethod.Get && path.EndsWith("/teams", StringComparison.Ordinal))
                return Json("{\"value\":[{\"teamid\":\"77777777-0000-0000-0000-000000000001\"," +
                            "\"_businessunitid_value\":\"66666666-0000-0000-0000-000000000001\"}]}");

            if (request.Method == HttpMethod.Delete && path.Contains("/teammembership_association(", StringComparison.Ordinal))
            {
                InTeam = false;
                TeamRemovals++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Patch && path.Contains("/sprk_registrationrequests(", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains($"\"sprk_status\":{(int)RegistrationStatus.Expired}", StringComparison.Ordinal))
                {
                    if (FailNextStatusWrites > 0)
                    {
                        FailNextStatusWrites--;
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    }

                    Status = RegistrationStatus.Expired;
                    ExpiredWrites++;
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Graph for one user: the account (enabled until disabled) and its demo-group membership.</summary>
    private sealed class StatefulGraph : IRequestAdapter
    {
        public bool Disabled { get; private set; }
        public bool InGroup { get; private set; } = true;
        public int UserPatches { get; private set; }
        public int GroupRemovals { get; private set; }
        public bool FailUserReads { get; set; }

        public ISerializationWriterFactory SerializationWriterFactory { get; } = new JsonSerializationWriterFactory();

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
        {
            var url = requestInfo.URI.ToString();
            if (url.Contains("/users/", StringComparison.Ordinal))
            {
                if (FailUserReads)
                    throw new HttpRequestException("Graph unavailable");
                if (requestInfo.HttpMethod == Method.PATCH)
                {
                    Disabled = true;
                    UserPatches++;
                }

                return Task.FromResult((ModelType?)(object)new User { Id = ObjectId.ToString(), AccountEnabled = !Disabled });
            }

            if (url.Contains("/groups/", StringComparison.Ordinal) && requestInfo.HttpMethod == Method.GET)
            {
                var members = new DirectoryObjectCollectionResponse
                {
                    Value = InGroup ? [new DirectoryObject { Id = ObjectId.ToString() }] : [],
                };
                return Task.FromResult((ModelType?)(object)members);
            }

            throw new NotSupportedException($"Unmodelled Graph call {requestInfo.HttpMethod} {url}");
        }

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
        {
            var url = requestInfo.URI.ToString();
            if (requestInfo.HttpMethod == Method.DELETE && url.Contains("/groups/", StringComparison.Ordinal))
            {
                InGroup = false;
                GroupRemovals++;
                return Task.CompletedTask;
            }

            throw new NotSupportedException($"Unmodelled Graph call {requestInfo.HttpMethod} {url}");
        }

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
            => throw new NotSupportedException();

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
            RequestInformation requestInfo, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
