using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// A real <see cref="CommunicationThreadReadService"/> over doubles at its Dataverse and identity seams, plus the request
/// principal accessor the AI email handler reads it with (task 065 fix round 6, F1: <c>email.summarize_thread</c> /
/// <c>draft_reply</c> / <c>draft_forward</c> run the communication through
/// <see cref="CommunicationThreadReadService.ReadVisibleCommunicationAsync"/> before reading any content).
/// </summary>
/// <remarks>
/// The service is sealed, so the visibility decision is exercised for real (the shared flag rule, and for an external caller
/// the explicitly-Open thread rule); only what it reads is canned. Defaults to an internal caller reading a shareable email
/// in an Open thread, the case every pre-existing handler test is about.
/// </remarks>
internal sealed class CommunicationVisibilityFixtures
{
    private const int PrivacyOpen = 100000000;

    private static readonly Guid CallerSystemUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>True when the caller is a systemuser flagged <c>sprk_isexternal</c>.</summary>
    public bool CallerIsExternal { get; init; }

    /// <summary>The email's <c>sprk_isinternalonly</c> flag.</summary>
    public bool InternalOnly { get; init; }

    /// <summary>The thread's privacy value; null = the thread row carries none.</summary>
    public int? ThreadPrivacy { get; init; } = PrivacyOpen;

    /// <summary>False = the impersonated read returns no communication row (it does not exist or the caller cannot read it).</summary>
    public bool CommunicationReadable { get; init; } = true;

    /// <summary>False = the request carries no principal at all.</summary>
    public bool HasPrincipal { get; init; } = true;

    /// <summary>False = the principal cannot be resolved to a systemuser (the visibility read refuses with a 403).</summary>
    public bool CallerResolvable { get; init; } = true;

    /// <summary>The communication ids the service was asked about, in call order.</summary>
    public List<string> CommunicationQueries { get; } = new();

    /// <summary>The visibility service and the accessor, ready to hand to <c>EmailDraftToolHandler</c>.</summary>
    public (CommunicationThreadReadService Service, IHttpContextAccessor Accessor) Build()
    {
        var threadId = Guid.NewGuid();
        var query = new Mock<IImpersonatedCommunicationQuery>();
        query
            .Setup(q => q.QueryAsync("sprk_communications", It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, Guid, CancellationToken>((_, odata, _, _) =>
            {
                CommunicationQueries.Add(odata);
                var id = odata.Split("sprk_communicationid eq ")[1].Split('&')[0];
                IReadOnlyList<Dictionary<string, JsonElement>> rows = CommunicationReadable
                    ? new[]
                    {
                        new Dictionary<string, JsonElement>
                        {
                            ["sprk_communicationid"] = JsonSerializer.SerializeToElement(id),
                            ["sprk_isinternalonly"] = JsonSerializer.SerializeToElement(InternalOnly),
                            ["_sprk_communicationthread_value"] = JsonSerializer.SerializeToElement(threadId.ToString("D")),
                        },
                    }
                    : Array.Empty<Dictionary<string, JsonElement>>();
                return Task.FromResult(rows);
            });
        query
            .Setup(q => q.QueryAsync("sprk_communicationthreads", It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var row = new Dictionary<string, JsonElement>
                {
                    ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(threadId.ToString("D")),
                };
                if (ThreadPrivacy is { } privacy)
                    row["sprk_privacystate"] = JsonSerializer.SerializeToElement(privacy);
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(new[] { row });
            });

        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CallerResolvable
                ? CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D"))
                : CallerSystemUserResolution.Unresolved("test: no systemuser for this principal"));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity
            .Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerIsExternal);

        var service = new CommunicationThreadReadService(
            query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            resolver.Object,
            identity.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var accessor = new Mock<IHttpContextAccessor>();
        accessor
            .SetupGet(a => a.HttpContext)
            .Returns(HasPrincipal
                ? new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test")),
                }
                : null);
        return (service, accessor.Object);
    }
}
