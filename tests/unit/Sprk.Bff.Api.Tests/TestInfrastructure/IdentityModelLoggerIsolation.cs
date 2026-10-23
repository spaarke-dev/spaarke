using System.Runtime.CompilerServices;
using Microsoft.IdentityModel.Abstractions;
using Microsoft.IdentityModel.Logging;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// Pins IdentityModel's process-wide logger (<see cref="LogHelper.Logger"/>) to a logger that lives as long as the test
/// process, before any fixture builds a host.
/// </summary>
/// <remarks>
/// Identity Web binds that static, once per process, to the logger of the first BFF host whose JwtBearer options are
/// built. In a production process that is the one host. In this assembly it is whichever fixture comes first, and that
/// fixture is disposed when its test class finishes; from then on every IdentityModel log line in the process writes into
/// a disposed logger factory. On Windows the EventLog provider throws, the logger wraps it in an
/// <see cref="AggregateException"/>, and the token handler reports THAT as the validation failure (an expired token reads
/// as invalid), in whichever later test validates a token. Which test that is depends on test order, so it shows only in
/// some runs. Identity Web leaves a logger that is already set in place, so pinning it here first removes the coupling
/// between hosts (verified against Microsoft.Identity.Web 4.14.2: <c>MicrosoftIdentityBaseAuthenticationBuilder.SetIdentityModelLogger</c>
/// returns unless <c>LogHelper.Logger</c> is still <c>NullIdentityModelLogger.Instance</c>). Pinned by
/// <see cref="IdentityModelLoggerIsolationTests"/> and, through a WorkforceHome host (ISS-012 / #1930), by
/// <c>AuthorizationModuleWorkforceHomeTests.ExpiredToken_ReportsExpired_EvenAfterAnotherHostWasDisposed</c>. No test asserts IdentityModel's own log
/// lines (they reached only the first host's logger before, so none could rely on them).
/// </remarks>
internal static class IdentityModelLoggerIsolation
{
    [ModuleInitializer]
    internal static void Initialize() => LogHelper.Logger = ProcessLifetimeIdentityLogger.Instance;

    /// <summary>Discards IdentityModel's log lines; it owns nothing that a host can dispose.</summary>
    private sealed class ProcessLifetimeIdentityLogger : IIdentityLogger
    {
        public static readonly ProcessLifetimeIdentityLogger Instance = new();

        public bool IsEnabled(EventLogLevel eventLogLevel) => false;

        public void Log(LogEntry entry)
        {
        }
    }
}
