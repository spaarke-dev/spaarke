using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// UAC-r2 re-check of #2191 — the caller's own rights on the record an Office quick-create files a project under. The create is
/// app-only, so the service asks the OBO probe for AppendTo on the source it was created from (every source, ordinary or
/// secure). A host that builds <c>OfficeRecordCreationService</c> for a project created FROM a matter or project passes these as
/// its <c>callerAccess</c> and <c>http</c> arguments, or the create is refused (<c>caller_rights_unverifiable</c>).
/// </summary>
/// <remarks>Global namespace, like <see cref="RecordTypeCatalogSetup"/>.</remarks>
public static class OfficeCallerRightsSetup
{
    /// <summary>The bearer token the request carries and the probe is set up for.</summary>
    public const string Token = "caller-token";

    /// <summary>A probe that answers <paramref name="rights"/> for every record, and a request carrying <see cref="Token"/>.</summary>
    public static (CallerRecordAccessProbe Probe, IHttpContextAccessor Http) Granting(
        AccessRights rights = AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo)
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(),
            NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        probe.Setup(p => p.GetCallerRightsAsync(Token, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + Token;
        return (probe.Object, new HttpContextAccessor { HttpContext = context });
    }
}
