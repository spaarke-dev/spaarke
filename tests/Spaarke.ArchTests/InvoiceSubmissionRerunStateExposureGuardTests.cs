using System.Reflection;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095d-2b-1 (uac-r2 K3b, 2026-11-06): <see cref="InvoiceSubmissionRerunResult.RerunState"/> carries the raw stored
/// <c>assignFirmRerun.state</c> of a submission's findings (row data; only the re-run's own log line sanitises it). It must never reach a
/// partner-visible surface. Over the compiled IL (<see cref="IlCallScan"/>): (G1) only the reviewed types read the property; (G2) no type of
/// the route layer (<c>Sprk.Bff.Api.Api.*</c>) or of the external-access infrastructure references the result type at all; (G3) no property
/// of any BFF type is of the result type, so no DTO can carry it to a serializer.
/// </summary>
/// <remarks>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): the record is public (the job maps it), so the compiler cannot keep it
/// off a response; a type-level ArchTest with must-fire and must-not-fire controls can.</para>
/// <para><b>Not covered:</b> a reader that copies the value through reflection or a dynamic serializer of an object graph that holds a
/// result in a field (no such graph exists; G3 covers properties, the shape a response DTO uses).</para>
/// </remarks>
public sealed class InvoiceSubmissionRerunStateExposureGuardTests
{
    /// <summary>The reviewed readers of <see cref="InvoiceSubmissionRerunResult.RerunState"/>.</summary>
    private static readonly Type[] ReviewedReaders =
    [
        typeof(InvoiceSubmissionRerunResult), // the record's own members (equality, printing)
        typeof(InvoiceSubmissionRerunJobHandler), // compares it with StateCommitted to decide the after-commit steps; never logs or returns it
    ];

    private static readonly Assembly Bff = typeof(InvoiceSubmissionRerun).Assembly;

    private static readonly string[] PartnerFacingNamespaces = ["Sprk.Bff.Api.Api.", "Sprk.Bff.Api.Infrastructure.ExternalAccess."];

    [Fact(DisplayName = "095d-2b-1 K3b G1: only the reviewed types read InvoiceSubmissionRerunResult.RerunState")]
    public void OnlyTheReviewedTypesReadTheState()
    {
        var readers = ReadersOfTheState(Bff.GetTypes());

        var unreviewed = readers.Except(ReviewedReaders).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            "These types read InvoiceSubmissionRerunResult.RerunState (raw stored row data, uac-r2 K3b) but are not reviewed: "
            + string.Join(", ", unreviewed) + ". It must never be logged unsanitised or projected to a response.");
    }

    [Fact(DisplayName = "095d-2b-1 K3b G2: no route or external-access type references InvoiceSubmissionRerunResult")]
    public void NoPartnerFacingTypeReferencesTheResult()
    {
        var violations = PartnerFacingReferences(Bff.GetTypes()).ToList();

        Assert.True(violations.Count == 0, "A route or external-access type references the re-run result: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "095d-2b-1 K3b G3: no BFF property is of type InvoiceSubmissionRerunResult (no DTO carries it)")]
    public void NoPropertyCarriesTheResult()
    {
        var carriers = PropertiesCarryingTheResult(Bff.GetTypes()).ToList();

        Assert.True(carriers.Count == 0, "These properties carry the re-run result: " + string.Join(", ", carriers));
    }

    // ── Controls ──

    [Fact(DisplayName = "095d-2b-1 K3b controls: G1 fires on a type that reads the state, and not on one that reads only the status")]
    public void G1_Controls()
    {
        Assert.Contains(typeof(RerunStateReaderFixture), ReadersOfTheState(IlCallScan.WithNested(typeof(RerunStateReaderFixture))));
        Assert.Empty(ReadersOfTheState(IlCallScan.WithNested(typeof(RerunStatusReaderFixture))));
    }

    [Fact(DisplayName = "095d-2b-1 K3b controls: G3 fires on a DTO with a result property, and not on one without")]
    public void G3_Controls()
    {
        Assert.Single(PropertiesCarryingTheResult([typeof(RerunResultDtoFixture)]));
        Assert.Empty(PropertiesCarryingTheResult([typeof(RerunStatusReaderFixture)]));
    }

    [Fact(DisplayName = "095d-2b-1 K3b controls: G2 sees a reference to the result type in a scanned type")]
    public void G2_Controls()
    {
        Assert.NotEmpty(ResultReferences(IlCallScan.WithNested(typeof(RerunStateReaderFixture))));
        Assert.Empty(ResultReferences(IlCallScan.WithNested(typeof(RerunUnrelatedFixture))));
    }

    // ── The scan ──

    private static HashSet<Type> ReadersOfTheState(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(InvoiceSubmissionRerunResult)
                && r.Target.Name == "get_" + nameof(InvoiceSubmissionRerunResult.RerunState))
            .Select(r => IlCallScan.Outermost(r.Caller))
            .ToHashSet();

    private static IEnumerable<string> ResultReferences(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(InvoiceSubmissionRerunResult))
            .Select(r => $"{IlCallScan.Outermost(r.Caller).FullName} -> {r.Target.Name}")
            .Concat(IlCallScan.TypeReferences(types)
                .Where(r => r.Target == typeof(InvoiceSubmissionRerunResult))
                .Select(r => $"{IlCallScan.Outermost(r.Caller).FullName} -> {r.Target.Name}"))
            .Distinct();

    private static IEnumerable<string> PartnerFacingReferences(IEnumerable<Type> types) =>
        ResultReferences(types.Where(t => PartnerFacingNamespaces.Any(n => (IlCallScan.Outermost(t).FullName ?? string.Empty).StartsWith(n, StringComparison.Ordinal))));

    private static IEnumerable<string> PropertiesCarryingTheResult(IEnumerable<Type> types) =>
        types.SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(p => p.PropertyType == typeof(InvoiceSubmissionRerunResult)
                || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Contains(typeof(InvoiceSubmissionRerunResult))))
            .Select(p => $"{p.DeclaringType?.FullName}.{p.Name}");
}

/// <summary>Reads the state (G1 and G2 must fire). Never run; only its IL is read.</summary>
internal static class RerunStateReaderFixture
{
    public static string? State(InvoiceSubmissionRerunResult result) => result.RerunState;
}

/// <summary>Reads the status only (G1 must not fire) and has no result property (G3 must not fire).</summary>
internal sealed class RerunStatusReaderFixture
{
    public string Name { get; init; } = string.Empty;

    public static bool Done(InvoiceSubmissionRerunResult result) => result.Status == InvoiceSubmissionRerunStatus.Completed;
}

/// <summary>A DTO that would carry the result to a serializer (G3 must fire).</summary>
internal sealed class RerunResultDtoFixture
{
    public InvoiceSubmissionRerunResult? Result { get; init; }
}

/// <summary>References nothing of the re-run (G2 must not fire).</summary>
internal static class RerunUnrelatedFixture
{
    public static int Two() => 1 + 1;
}
