using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Task 138 (#1061), criterion 10: the write-time access-permission policy runs INSIDE the grant handlers, AFTER
/// <see cref="DelegationRuleFilter"/> has established Write — so a caller without Write gets the filter's 403 and
/// never learns the record's access policy.
/// </summary>
/// <remarks>
/// <para><b>Why each negative has a positive twin.</b> The twin differs ONLY in the caller's rights and gets the
/// policy's 422. That proves the record really is Restricted to the handler — so the 403 is the gate answering
/// first, not a record the policy would have admitted anyway.</para>
/// <para>ADR-038 KEEP path #1 (<c>tests/integration/auth/**</c>) — authorization behaviour. The wire contract of
/// the refusals themselves is <c>GrantPolicyContractTests</c>.</para>
/// </remarks>
public class GrantPolicyOrderingTests : IClassFixture<DelegationRuleTestFixture>
{
    private const string ReadOnly = "ReadAccess";
    private const string ReadWrite = "ReadAccess,WriteAccess";

    private readonly DelegationRuleTestFixture _fixture;

    public GrantPolicyOrderingTests(DelegationRuleTestFixture fixture) => _fixture = fixture;

    private static (string Path, object Body) RequestFor(string route, Guid matterId) => route switch
    {
        "grant" => ("/api/v1/external-access/grant", new
        {
            contactId = Guid.NewGuid(),
            recordType = "matter",
            recordId = matterId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        "invite" => ("/api/v1/external-access/invite", new
        {
            email = "ordering@firm.example",
            recordType = "matter",
            recordId = matterId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        "invite-and-grant" => ("/api/v1/external-access/invite-and-grant", new
        {
            email = "ordering@firm.example",
            recordType = "matter",
            recordId = matterId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
    };

    private Guid RestrictedMatter()
    {
        var matterId = Guid.NewGuid();
        _fixture.RootFlags.Flags[matterId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        return matterId;
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("invite")]
    [InlineData("invite-and-grant")]
    public async Task OnARestrictedRecord_ACallerWithoutWrite_GetsTheDelegation403_AndThePolicyIsNeverRead(string route)
    {
        var matterId = RestrictedMatter();
        using var client = _fixture.CreateClientWithRights(ReadOnly);
        var (path, body) = RequestFor(route, matterId);

        var response = await client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired,
            "the gate answers first; a 422 here would disclose the record's access policy to a non-Write caller");
        _fixture.RootFlags.Reads.Should().NotContain(r => r.RecordId == matterId,
            "the handler — and with it the policy read — is never reached");
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("invite")]
    [InlineData("invite-and-grant")]
    public async Task OnARestrictedRecord_ACallerWithWrite_GetsThePolicy422_ReadByTheRootsLogicalName(string route)
    {
        var matterId = RestrictedMatter();
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        var (path, body) = RequestFor(route, matterId);

        var response = await client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReasonCodeOf(response)).Should().Be(ExternalGrantLifecycle.RecordRestrictedReasonCode);
        _fixture.RootFlags.Reads.Should().Contain(("sprk_matter", matterId),
            "the policy addresses the flag reader by the LOGICAL name; an entity-set name would read nothing");
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }
}
