// -----------------------------------------------------------------------------
// H11ReplicationRetryTests.cs
//
// T186 audit (H11). The live run kept dying on one class of defect: an Entra object is created, the very next call
// about it hits a replica that has not seen it, and the handler treated that transient answer as fatal. H11's raw-HTTP
// seams now wait it out — bounded, and only for the narrow "not replicated yet" replies. ADR-038 path #1.
//
// COVERAGE (one group per audit finding):
//   1. guest invited, then added to the group / read in Dataverse straight away
//        GraphRestEnvironmentSecurityGroupClient.AddMemberAsync — 404 or 400 "does not reference a valid object"
//          retried; 403 and "already exist" are not; attempts bounded;
//        DataverseWebApiGuestUserWriter.EnsureGuestUserAsync — the alternate-key read's 404 retried; 403 not.
//   2. native user created, then assignLicense straight away
//        GraphRestUserProvisioner.AssignLicenseAsync — 404 / 400 "does not exist" / 400 usage location retried;
//          "no available licenses", an invalid SKU and 403 are not; attempts bounded.
//   3. group-membership add on a new user — the same AddMemberAsync (H11 adds only guests, right after consent).
//   4. (T186 L51, owner 2026-10-16) guest licence — GraphRestUserProvisioner.AssignGuestLicenseAsync: idempotent read,
//        usage location only when absent, only the guest SKU assigned; and the Dataverse alternate-key read waits
//        DataverseUserSyncRetryCount (minutes), not the Entra ReplicationRetryCount.
// Hand-rolled HttpMessageHandler (NOT Mock<HttpMessageHandler> — banned per testing.md), credential injected through
// each class's internal test constructor, delays injected via the options (1 ms).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H11ReplicationRetryTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string GroupId = "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string UserId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string RoleId = "cccccccc-1111-2222-3333-444444444444";
    private const string SystemUserId = "dddddddd-1111-2222-3333-444444444444";
    private const string CustomerBuId = "eeeeeeee-1111-2222-3333-444444444444";
    private const int Attempts = 4;
    private const int SyncAttempts = 6;   // deliberately != Attempts: the Dataverse read must use its own count
    private const string GuestSkuId = "DCB1A3AE-B33F-4487-846A-A640262FADF4";   // upper-case: the compare is case-insensitive

    private const string NotFoundBody =
        """{"error":{"code":"Request_ResourceNotFound","message":"Resource 'aaaaaaaa' does not exist or one of its queried reference-property objects are not present."}}""";
    private const string NotValidReferenceBody =
        """{"error":{"code":"Request_BadRequest","message":"The object 'aaaaaaaa' does not reference a valid object."}}""";
    private const string UsageLocationBody =
        """{"error":{"code":"Request_BadRequest","message":"License assignment failed because the user's usage location is not set."}}""";
    private const string NoLicensesBody =
        """{"error":{"code":"Request_BadRequest","message":"License assignment failed because there are no available licenses for SKU 'x'."}}""";
    private const string InvalidSkuBody =
        """{"error":{"code":"Request_BadRequest","message":"Invalid license sku id 'x' does not exist in the tenant's subscribed SKUs."}}""";
    private const string ForbiddenBody = """{"error":{"code":"Authorization_RequestDenied"}}""";

    // ---------------- finding 2: native user, then assignLicense ----------------

    [Theory]
    [InlineData(HttpStatusCode.NotFound, NotFoundBody)]
    [InlineData(HttpStatusCode.BadRequest, NotFoundBody)]           // 400 "Resource ... does not exist"
    [InlineData(HttpStatusCode.BadRequest, UsageLocationBody)]      // replica has not seen the usage location set at create
    public async Task AssignLicenseAsync_AUserThatHasNotReplicated_IsRetriedUntilItSucceeds(HttpStatusCode status, string body)
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls < 3 ? Json(status, body) : Json(HttpStatusCode.OK, "{}"));

        var outcome = await Licenser(http, sku: true).AssignLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Success>("a just-created user is not found until Entra replicates it");
        http.Requests.Should().HaveCount(3);
        http.Requests.Should().OnlyContain(r => r.Uri.EndsWith($"/v1.0/users/{UserId}/assignLicense"));
    }

    [Fact]
    public async Task AssignLicenseAsync_NotFoundOnEveryAttempt_FailsAfterTheLastAttempt()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.NotFound, NotFoundBody));

        var outcome = await Licenser(http, sku: true).AssignLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic.Should().Contain("404");
        http.Requests.Should().HaveCount(Attempts, "the wait is bounded");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, NoLicensesBody)]
    [InlineData(HttpStatusCode.BadRequest, InvalidSkuBody)]
    [InlineData(HttpStatusCode.Forbidden, ForbiddenBody)]
    public async Task AssignLicenseAsync_AGenuineLicenceOrPermissionError_IsNotRetried(HttpStatusCode status, string body)
    {
        var http = new FakeHttp(_ => Json(status, body));

        var outcome = await Licenser(http, sku: true).AssignLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic.Should().Contain(((int)status).ToString());
        http.Requests.Should().ContainSingle("only replication lag is waited out — a licence error keeps its old outcome");
    }

    // ---------------- findings 1 + 3: guest, then group add ----------------

    [Theory]
    [InlineData(HttpStatusCode.NotFound, NotFoundBody)]
    [InlineData(HttpStatusCode.BadRequest, NotValidReferenceBody)]
    public async Task AddMemberAsync_AGuestThatHasNotReplicated_IsRetriedUntilItSucceeds(HttpStatusCode status, string body)
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls < 3 ? Json(status, body) : new HttpResponseMessage(HttpStatusCode.NoContent));

        var outcome = await Groups(http).AddMemberAsync(GroupId, UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<SecurityGroupMembershipOutcome.Success>();
        http.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddMemberAsync_NotReplicatedOnEveryAttempt_FailsAfterTheLastAttempt()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.BadRequest, NotValidReferenceBody));

        var outcome = await Groups(http).AddMemberAsync(GroupId, UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<SecurityGroupMembershipOutcome.Failure>().Which.Diagnostic.Should().Contain("400");
        http.Requests.Should().HaveCount(Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ForbiddenBody, false)]
    [InlineData(HttpStatusCode.BadRequest,
        """{"error":{"code":"Request_BadRequest","message":"One or more added object references already exist for the following modified properties: 'members'."}}""",
        true)]   // already a member — success at once, never "waited out"
    public async Task AddMemberAsync_AnotherAnswer_IsNotRetried(HttpStatusCode status, string body, bool expectSuccess)
    {
        var http = new FakeHttp(_ => Json(status, body));

        var outcome = await Groups(http).AddMemberAsync(GroupId, UserId, TenantId, CancellationToken.None);

        (outcome is SecurityGroupMembershipOutcome.Success).Should().Be(expectSuccess);
        http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureGuestUserAsync_AGroupMembershipNotYetVisibleToDataverse_IsRetriedUntilItIs()
    {
        var reads = 0;
        var http = DataverseFake(() => ++reads < 3
            ? Json(HttpStatusCode.NotFound, """{"error":{"code":"0x80072560"}}""")
            : Json(HttpStatusCode.OK, $$"""{"systemuserid":"{{SystemUserId}}","_businessunitid_value":"{{CustomerBuId}}","isdisabled":false}"""));

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseGuestUserOutcome.Success(SystemUserId));
        reads.Should().Be(3);
    }

    [Fact]
    public async Task EnsureGuestUserAsync_NotVisibleOnEveryAttempt_FailsSayingWhyAfterTheLastAttempt()
    {
        var http = DataverseFake(() => Json(HttpStatusCode.NotFound, """{"error":{"code":"0x80072560"}}"""));

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseGuestUserOutcome.Failure>().Which.Diagnostic
            .Should().Contain("DIRECT member of the environment security group");
        http.Requests.Count(r => r.Uri.Contains("/systemusers(azureactivedirectoryobjectid=")).Should().Be(
            SyncAttempts, "Dataverse's user sync takes minutes — the read waits DataverseUserSyncRetryCount, not the Entra ReplicationRetryCount");
        SyncAttempts.Should().NotBe(Attempts);
    }

    // ---------------- T186 L51: guest licence ----------------

    [Fact]
    public async Task AssignGuestLicenseAsync_AlreadyLicensed_OnlyReadsTheUser()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.OK,
            $$"""{"usageLocation":"US","assignedLicenses":[{"skuId":"{{GuestSkuId.ToLowerInvariant()}}","disabledPlans":[]}]}"""));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Success>();
        http.Requests.Should().ContainSingle().Which.Should().Be((HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/users/{UserId}?$select=usageLocation,assignedLicenses"));
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_NoUsageLocation_SetsItThenAssignsOnlyTheGuestSku()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"usageLocation":null,"assignedLicenses":[{"skuId":"11111111-2222-3333-4444-555555555555"}]}""")
            : req.Method == HttpMethod.Patch ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(HttpStatusCode.OK, "{}"));

        var outcome = await Licenser(http, sku: true).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Success>();
        http.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Patch, HttpMethod.Post);
        http.Requests[1].Uri.Should().Be($"https://graph.microsoft.com/v1.0/users/{UserId}");
        http.Bodies[1].Should().Contain("\"usageLocation\":\"US\"");
        http.Requests[2].Uri.Should().EndWith($"/v1.0/users/{UserId}/assignLicense");
        using var body = System.Text.Json.JsonDocument.Parse(http.Bodies[2]);
        body.RootElement.GetProperty("addLicenses").EnumerateArray().Select(a => a.GetProperty("skuId").GetString())
            .Should().Equal(
                new[] { GuestSkuId }, "only the guest SKU — never the NativeAccount set (PowerAppsPlan2TrialSkuId is also configured)");
        body.RootElement.GetProperty("removeLicenses").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_UsageLocationAlreadySet_DoesNotPatch()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"usageLocation":"GB","assignedLicenses":[]}""")
            : Json(HttpStatusCode.OK, "{}"));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Success>();
        http.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post);
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_NoAvailableLicences_FailsAndIsNotRetried()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"usageLocation":"US","assignedLicenses":[]}""")
            : Json(HttpStatusCode.BadRequest, NoLicensesBody));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic
            .Should().Contain("assignLicense failed").And.Contain("400").And.Contain("no available licenses");
        http.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1, "a genuine licence error is not replication lag");
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_AGuestNotYetReplicated_IsReadAgainUntilItIs()
    {
        var reads = 0;
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? (++reads < 3 ? Json(HttpStatusCode.NotFound, NotFoundBody) : Json(HttpStatusCode.OK, """{"usageLocation":"US","assignedLicenses":[]}"""))
            : Json(HttpStatusCode.OK, "{}"));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Success>();
        reads.Should().Be(3);
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_TheReadIsRefused_FailsWithTheStatus()
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.Forbidden, ForbiddenBody));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic.Should().Contain("GET").And.Contain("403");
        http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AssignGuestLicenseAsync_ThePatchIsRefused_FailsWithoutAssigning()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"usageLocation":null,"assignedLicenses":[]}""")
            : Json(HttpStatusCode.Forbidden, ForbiddenBody));

        var outcome = await Licenser(http, sku: false).AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic.Should().Contain("PATCH").And.Contain("403");
        http.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AssignGuestLicenseAsync_BlankGuestSku_FailsWithoutAnyHttpCall(string? sku)
    {
        var http = new FakeHttp(_ => Json(HttpStatusCode.OK, "{}"));
        var provisioner = new GraphRestUserProvisioner(
            new HttpClient(http),
            Options.Create(new H11UserProvisioningOptions { GuestLicenseSkuId = sku }),
            NullLogger<GraphRestUserProvisioner>.Instance, _ => new FakeCredential());

        var outcome = await provisioner.AssignGuestLicenseAsync(UserId, TenantId, CancellationToken.None);

        outcome.Should().BeOfType<LicenseAssignmentOutcome.Failure>().Which.Diagnostic.Should().Contain("GuestLicenseSkuId");
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureGuestUserAsync_ARefusedRead_IsNotRetried()
    {
        var http = DataverseFake(() => Json(HttpStatusCode.Forbidden, """{"error":{"code":"0x80072560"}}"""));

        var outcome = await Writer(http).EnsureGuestUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseGuestUserOutcome.Failure>().Which.Diagnostic.Should().Contain("Dataverse refused the read");
        http.Requests.Should().ContainSingle();
    }

    // ---------------- options + predicates ----------------

    [Fact]
    public void ReplicationRetry_HasBoundedDefaults()
    {
        var options = new H11UserProvisioningOptions();

        options.ReplicationRetryCount.Should().BeInRange(2, 12);
        options.ReplicationRetryDelay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void L51_DefaultsCoverDataverseUserSyncAndTheGuestLicence()
    {
        var options = new H11UserProvisioningOptions();

        (options.DataverseUserSyncRetryCount * options.DataverseUserSyncRetryDelay)
            .Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(3)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10),
                "live T186 needed more than 4 minutes; the wait stays bounded");
        options.GuestLicenseSkuId.Should().Be("dcb1a3ae-b33f-4487-846a-a640262fadf4");
        options.GuestUsageLocation.Should().Be("US");
        options.LicenseSkuIds.Should().NotContain("dcb1a3ae-b33f-4487-846a-a640262fadf4", "the NativeAccount set is separate");
    }

    [Theory]
    [InlineData(404, "{}", true)]
    [InlineData(400, NotValidReferenceBody, true)]
    [InlineData(400, NotFoundBody, true)]
    [InlineData(400, """{"error":{"message":"One or more added object references already exist."}}""", false)]
    [InlineData(403, ForbiddenBody, false)]
    [InlineData(500, "{}", false)]
    public void IsGraphObjectNotYetReplicated_RecognisesOnlyReplicationLag(int status, string body, bool expected)
        => H11ReplicationRetry.IsGraphObjectNotYetReplicated(new((HttpStatusCode)status, body)).Should().Be(expected);

    [Theory]
    [InlineData(404, "{}", true)]
    [InlineData(400, UsageLocationBody, true)]
    [InlineData(400, NotFoundBody, true)]
    [InlineData(400, NoLicensesBody, false)]
    [InlineData(400, InvalidSkuBody, false)]
    [InlineData(403, ForbiddenBody, false)]
    public void IsLicenseTargetNotYetReplicated_NeverTreatsAGenuineLicenceErrorAsLag(int status, string body, bool expected)
        => H11ReplicationRetry.IsLicenseTargetNotYetReplicated(new((HttpStatusCode)status, body)).Should().Be(expected);

    // ---------------- helpers ----------------

    private static IOptions<H11UserProvisioningOptions> FastRetry(bool sku = false)
        => Options.Create(new H11UserProvisioningOptions
        {
            ReplicationRetryCount = Attempts,
            ReplicationRetryDelay = TimeSpan.FromMilliseconds(1),
            DataverseUserSyncRetryCount = SyncAttempts,
            DataverseUserSyncRetryDelay = TimeSpan.FromMilliseconds(1),
            GuestLicenseSkuId = GuestSkuId,
            PowerAppsPlan2TrialSkuId = sku ? "11111111-2222-3333-4444-555555555555" : null,
        });

    private static GraphRestUserProvisioner Licenser(FakeHttp http, bool sku)
        => new(new HttpClient(http), FastRetry(sku), NullLogger<GraphRestUserProvisioner>.Instance, _ => new FakeCredential());

    private static GraphRestEnvironmentSecurityGroupClient Groups(FakeHttp http)
        => new(new HttpClient(http), FastRetry(), _ => new FakeCredential());

    private static DataverseWebApiGuestUserWriter Writer(FakeHttp http)
        => new(new HttpClient(http), FastRetry(), new FakeRootBusinessUnitReader(), _ => new FakeCredential());

    private static DataverseGuestUserRequest Request()
        => new(EnvUrl, TenantId, UserId, Guid.Parse(CustomerBuId), [Guid.Parse(RoleId)]);

    /// <summary>A Dataverse that returns <paramref name="userRead"/> for the alternate-key read and the role already held.</summary>
    private static FakeHttp DataverseFake(Func<HttpResponseMessage> userRead)
        => new(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("/systemusers(azureactivedirectoryobjectid="))
            {
                return userRead();
            }
            if (uri.Contains("/systemuserroles_association"))
            {
                return Json(HttpStatusCode.OK, $$"""{"value":[{"roleid":"{{RoleId}}","_businessunitid_value":"{{CustomerBuId}}"}]}""");
            }
            return Json(HttpStatusCode.BadRequest, $"{{\"error\":{{\"code\":\"unexpected request {req.Method} {uri}\"}}}}");
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeRootBusinessUnitReader : IDataverseRootBusinessUnitReader
    {
        public Task<Guid?> ReadRootBusinessUnitIdAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken)
            => Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<string?> ReadRecordedContainerIdAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken)
            => throw new NotSupportedException("not read by the guest-user writer");
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Hand-rolled fake handler (NOT Mock&lt;HttpMessageHandler&gt;) recording each request.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];

        /// <summary>The request body text per request (parallel to <see cref="Requests"/>; empty when there is none).</summary>
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri));
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
