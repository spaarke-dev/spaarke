using System.Net;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InviteOrganizationTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// Slice B2 — <see cref="InviteOrganizationResolver"/>: a work-account invite only for a domain whose Entra tenant is a
/// configured customer tenant; a portal-account invite for every other definite answer; no definite answer is
/// <see cref="InviteOrganizationStatus.LookupUnavailable"/>. The discovery endpoint is a scripted handler (no network).
/// </summary>
public class InviteOrganizationResolverTests
{
    private static readonly Guid Customer = IdentityBindingTestKit.CustomerTenant;
    private const string CallerTenant = "aaaaaaaa-0000-4000-8000-00000000000c";

    private readonly DiscoveryHandler _handler = new();
    private readonly RecordingTenantCache _cache = new();

    private InviteOrganizationResolver Resolver(params Guid[] customers)
        => InviteOrganizationTestKit.Resolver(_handler, _cache, customers.Length == 0 ? new[] { Customer } : customers,
            timeout: TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// The domain grammar: normalised (trim, lower-case, one trailing dot, IDN to punycode), then a plain host name with at
    /// least one dot, labels of [a-z0-9-] not starting or ending with a hyphen, at most 253 characters. Anything else is
    /// null (a 400 at invite time).
    /// </summary>
    [Theory]
    [InlineData("person@Customer.Example", "customer.example")]
    [InlineData("  person@customer.example.  ", "customer.example")]
    [InlineData("person@sub.customer-1.example", "sub.customer-1.example")]
    [InlineData("person@bücher.example", "xn--bcher-kva.example")]
    [InlineData("person@customer", null)]
    [InlineData("person@customer..example", null)]
    [InlineData("person@-customer.example", null)]
    [InlineData("person@customer-.example", null)]
    [InlineData("person@customer.example..", null)]
    [InlineData("person@cust_omer.example", null)]
    [InlineData("person@customer.example/x", null)]
    [InlineData("person@customer.example?x", null)]
    [InlineData("person@customer.example#x", null)]
    [InlineData("person@customer%2eexample", null)]
    [InlineData("person@customer\\example.test", null)]
    [InlineData("person@customer.example:443", null)]
    [InlineData("person@", null)]
    [InlineData("@customer.example", null)]
    [InlineData("a@b@customer.example", null)]
    [InlineData("customer.example", null)]
    [InlineData("", null)]
    public void TheDomainGrammar(string email, string? expected)
        => InviteOrganizationResolver.NormaliseDomain(email).Should().Be(expected);

    [Fact]
    public void TheDomainGrammar_RefusesMoreThan253Characters_AndALabelOver63()
    {
        var label = new string('a', 63);
        var at253 = string.Join('.', label, label, label, new string('b', 61));
        InviteOrganizationResolver.NormaliseDomain("p@" + at253).Should().Be(at253);
        InviteOrganizationResolver.NormaliseDomain("p@" + at253 + "c").Should().BeNull();
        InviteOrganizationResolver.NormaliseDomain("p@" + new string('a', 64) + ".example").Should().BeNull();
    }

    /// <summary>The tenant comes only from an issuer of exactly https://login.microsoftonline.com/{GUID}/v2.0.</summary>
    [Theory]
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/{tenantid}/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0", false)]
    [InlineData("http://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/v2.0", false)]
    [InlineData("https://sts.windows.net/11111111-aaaa-4aaa-8aaa-111111111111/", false)]
    [InlineData("https://login.microsoftonline.com.evil.test/11111111-aaaa-4aaa-8aaa-111111111111/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111/v2.0/extra", false)]
    [InlineData("https://login.microsoftonline.com/11111111-aaaa-4aaa-8aaa-111111111111", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void TheTenant_ComesOnlyFromAWellFormedIssuer(string? issuer, bool parses)
        => InviteOrganizationResolver.TenantFromIssuer(issuer).Should().Be(parses ? Customer : null);

    [Fact]
    public async Task ACustomerTenantDomain_IsAWorkAccount_FromOneCredentiallessGetOfTheFixedUrl()
    {
        _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Customer));

        var decision = await Resolver().DecideAsync("Person@Customer.Example.", false, CallerTenant, CancellationToken.None);

        decision.Should().Be(InviteOrganizationDecision.WorkAccount(Customer));
        var call = _handler.Requests.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Get);
        call.RequestUri!.AbsoluteUri.Should().Be(
            "https://login.microsoftonline.com/customer.example/v2.0/.well-known/openid-configuration");
        call.Headers.Should().BeEmpty("no credentials, cookies or other headers are sent");
    }

    /// <summary>
    /// MUST NOT FIRE: the Microsoft account (consumer) tenant, which hotmail.com and outlook.com resolve to, and the CIAM
    /// tenant are never work accounts — even if the customer list named them; another tenant and "not found" are portal
    /// accounts.
    /// </summary>
    [Theory]
    [InlineData("consumer")]
    [InlineData("ciam")]
    [InlineData("unconfigured")]
    [InlineData("not-found")]
    public async Task EveryOtherDefiniteAnswer_IsAPortalAccount(string answer)
    {
        _ = answer switch
        {
            "consumer" => _handler.Answer(HttpStatusCode.OK, DiscoveryBody(InviteOrganizationResolver.ConsumerTenantId)),
            "ciam" => _handler.Answer(HttpStatusCode.OK, DiscoveryBody(CiamTenant)),
            "unconfigured" => _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Guid.NewGuid())),
            _ => _handler.Answer(HttpStatusCode.BadRequest, TenantNotFoundBody),
        };

        var decision = await Resolver(Customer, InviteOrganizationResolver.ConsumerTenantId, CiamTenant)
            .DecideAsync("person@hotmail.com", false, CallerTenant, CancellationToken.None);

        decision.Should().Be(InviteOrganizationDecision.PortalAccount, answer);
    }

    /// <summary>
    /// "Not found" only on a definite AADSTS90002 answer; any other 400 is a fault. MUST FIRE: error_codes or the
    /// description. MUST NOT FIRE: another AADSTS code, a body naming 90002 elsewhere, an empty body.
    /// </summary>
    [Theory]
    [InlineData("{\"error_codes\":[90002]}", true)]
    [InlineData("{\"error\":\"invalid_tenant\",\"error_description\":\"AADSTS90002: Tenant 'x' not found.\"}", true)]
    [InlineData("{\"error_codes\":[900144]}", false)]
    [InlineData("{\"error_description\":\"AADSTS50020: user. See AADSTS90002: no.\"}", false)]
    [InlineData("{\"error_codes\":[\"90002\"]}", false)]
    [InlineData("", false)]
    [InlineData("not json", false)]
    public void NotFound_IsOnlyADefiniteAadsts90002(string body, bool notFound)
        => InviteOrganizationResolver.IsTenantNotFound(body).Should().Be(notFound);

    /// <summary>
    /// The cache holds only the parsed answer, keyed on the normalised domain under the caller's tenant: a tenant for 24 hours,
    /// "not found" for 15 minutes. A second invite to the same domain makes no call.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADefiniteAnswer_IsCached_ByDomainOnly_WithItsTtl(bool found)
    {
        if (found)
            _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Customer));
        else
            _handler.Answer(HttpStatusCode.BadRequest, TenantNotFoundBody);
        var resolver = Resolver();

        var first = await resolver.DecideAsync("one@Customer.example", false, CallerTenant, CancellationToken.None);
        var second = await resolver.DecideAsync("two@customer.example", false, CallerTenant, CancellationToken.None);

        second.Should().Be(first);
        _handler.Requests.Should().ContainSingle("the second invite reads the cache");
        var write = _cache.Writes.Should().ContainSingle().Subject;
        (write.Tenant, write.Resource, write.Id, write.Version)
            .Should().Be((CallerTenant, "invite-org-discovery", "customer.example", 1));
        write.Ttl.Should().Be(found ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(15));
        write.Json.Should().Be(found ? $"{{\"tenantId\":\"{Customer:D}\"}}" : "{\"tenantId\":null}");
        write.Json.Should().NotContain("one@").And.NotContain("issuer");
    }

    /// <summary>The customer list is applied after the cache: a tenant added to it applies at once to a cached answer.</summary>
    [Fact]
    public async Task TheCustomerList_IsAppliedAfterTheCache()
    {
        var later = Guid.NewGuid();
        _handler.Answer(HttpStatusCode.OK, DiscoveryBody(later));

        (await Resolver().DecideAsync("p@customer.example", false, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.PortalAccount);
        (await Resolver(Customer, later).DecideAsync("p@customer.example", false, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.WorkAccount(later));
        _handler.Requests.Should().ContainSingle();
    }

    /// <summary>MUST FIRE: no definite answer is LookupUnavailable and is never cached.</summary>
    [Theory]
    [InlineData("timeout")]
    [InlineData("500")]
    [InlineData("429")]
    [InlineData("301")]
    [InlineData("oversized")]
    [InlineData("200-no-issuer")]
    [InlineData("network")]
    public async Task NoDefiniteAnswer_IsUnavailable_AndNotCached(string fault)
    {
        _ = fault switch
        {
            "timeout" => _handler.Hang(),
            "500" => _handler.Answer(HttpStatusCode.InternalServerError, "{}"),
            "429" => _handler.Answer(HttpStatusCode.TooManyRequests, "{}"),
            "301" => _handler.Answer(HttpStatusCode.MovedPermanently, "{}"),
            "oversized" => _handler.Answer(HttpStatusCode.OK, "{\"issuer\":\"" + new string('x', 70_000) + "\"}"),
            "200-no-issuer" => _handler.Answer(HttpStatusCode.OK, "{\"token_endpoint\":\"https://x/\"}"),
            _ => _handler.Throw(new HttpRequestException("dns")),
        };

        var decision = await Resolver().DecideAsync("p@customer.example", false, CallerTenant, CancellationToken.None);

        decision.Status.Should().Be(InviteOrganizationStatus.LookupUnavailable, fault);
        decision.WorkAccountTenantId.Should().BeNull();
        _cache.Writes.Should().BeEmpty();
    }

    /// <summary>The caller's own cancellation propagates; it is not reported as a lookup fault.</summary>
    [Fact]
    public async Task TheCallersCancellation_Propagates()
    {
        _handler.Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var resolver = InviteOrganizationTestKit.Resolver(_handler, _cache, new[] { Customer }, TimeSpan.FromSeconds(30));

        var act = () => resolver.DecideAsync("p@customer.example", false, CallerTenant, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// MUST NOT FIRE: the override and an empty customer list make no call (the handler fails the test if called); a cache
    /// outage is skipped, never a fault; no caller tenant skips the cache.
    /// </summary>
    [Fact]
    public async Task TheOverride_AndAnEmptyList_MakeNoCall_ACacheOutageIsSkipped()
    {
        (await Resolver().DecideAsync("p@customer.example", true, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.PortalAccount);
        (await InviteOrganizationTestKit.Resolver(_handler, _cache, Array.Empty<Guid>())
                .DecideAsync("p@customer.example", false, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.PortalAccount);
        (await InviteOrganizationTestKit.Resolver(_handler, _cache, Array.Empty<Guid>())
                .DecideAsync("not an email", false, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.PortalAccount, "with no lookup to run there is no domain to judge");
        _handler.Requests.Should().BeEmpty();

        _cache.Fault = new InvalidOperationException("cache down");
        _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Customer));
        (await Resolver().DecideAsync("p@customer.example", false, CallerTenant, CancellationToken.None))
            .Should().Be(InviteOrganizationDecision.WorkAccount(Customer));

        _cache.Fault = null;
        _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Customer));
        (await Resolver().DecideAsync("p@customer.example", false, null, CancellationToken.None)).IsWorkAccount.Should().BeTrue();
        _cache.Writes.Should().BeEmpty("no caller tenant, no cache key");
    }

    /// <summary>Logs name the domain and the outcome, never the email address.</summary>
    [Fact]
    public async Task TheLogs_NeverCarryTheEmail()
    {
        var log = new BindingLogCapture<InviteOrganizationResolver>();
        _handler.Answer(HttpStatusCode.OK, DiscoveryBody(Customer)).Answer(HttpStatusCode.InternalServerError, "{}");
        var resolver = InviteOrganizationTestKit.Resolver(_handler, _cache, new[] { Customer }, logger: log);

        await resolver.DecideAsync("secret.person@customer.example", false, null, CancellationToken.None);
        await resolver.DecideAsync("secret.person@customer.example", false, null, CancellationToken.None);

        log.Messages.Should().NotBeEmpty();
        log.Messages.Should().OnlyContain(m => !m.Contains("secret.person", StringComparison.Ordinal));
        log.Messages.Should().Contain(m => m.Contains("customer.example", StringComparison.Ordinal));
    }
}
