namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Test-only handlers that LIVE IN the Finance namespace (so the step-0 coverage guard's handler selector sees them) but are
/// mapped under a non-<c>/api/finance</c> prefix by <c>FinanceStepZeroCoverageGuardTests</c>. Never mapped by the application.
/// </summary>
internal static class PlantedFinanceHandlers
{
    public static string Planted(string id) => id;

    public static Delegate PlantedLambda() => () => "planted";
}
