using System.Reflection;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 130b (fix round 4): <see cref="FirmFindingsProjection"/> performs NO caller check; its safety is that every caller sits
/// behind <c>FirmOutcomeAuthorizationFilter</c>. So the only non-test types that may reference it are the route handlers that run behind
/// that filter (<see cref="FirmOutcomeEndpoints"/>) and the DI registration that builds it (<see cref="FinanceModule"/>). A new caller (a job,
/// a service, another route) fails this test until it is reviewed and listed here.
/// </summary>
/// <remarks>
/// <para><b>What counts as a reference</b> (the compiled assembly, not the text): a field, parameter or return of the type (a generic argument
/// included), a call to one of its members or to a method that takes it, a generic method instantiated over it
/// (<c>AddScoped&lt;FirmFindingsProjection&gt;</c>, <c>GetRequiredService&lt;FirmFindingsProjection&gt;</c>), an IL type operand
/// (<c>typeof</c>, a cast), and a string constant that names it (a reflection lookup). A lambda or an async state machine is attributed to the
/// outermost type it is written in. The projection itself is not a caller.</para>
/// <para><b>Closed list, no baseline.</b> The expected set must equal the found set: an extra caller fails, and a listed type that no longer
/// references the projection fails too, so the list only shrinks. <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the same
/// detector over the <c>FirmProjectionControl*</c> fixtures below. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class FirmFindingsProjectionCallerBoundaryGuardTests
{
    private static readonly Type Projection = typeof(FirmFindingsProjection);

    /// <summary>The closed list: the route handlers behind the filter, and the registration.</summary>
    private static readonly string[] AllowedReferrers =
    [
        typeof(FirmOutcomeEndpoints).FullName!,
        typeof(FinanceModule).FullName!,
    ];

    [Fact(DisplayName = "130b fix 4: only the firm outcome routes and their DI registration reference FirmFindingsProjection (it has no caller check)")]
    public void OnlyTheFirmOutcomeRoutesAndTheirRegistration_ReferenceTheProjection()
    {
        var found = Referrers(typeof(Program).Assembly.GetTypes());

        Assert.True(
            found.SetEquals(AllowedReferrers),
            "FirmFindingsProjection has NO caller check; it may only be called behind FirmOutcomeAuthorizationFilter. "
            + "Allowed (and expected): " + string.Join(", ", AllowedReferrers.OrderBy(n => n))
            + ". Found: " + string.Join(", ", found.OrderBy(n => n))
            + ". A new caller must sit behind the filter (or have its own reviewed caller check) and be listed here with that reason.");
    }

    [Fact(DisplayName = "130b fix 4 (controls): the detector flags a caller by call, by field and by generic resolution, and passes an unrelated type")]
    public void Detector_MustFireAndMustNotFire()
    {
        var found = Referrers(
        [
            typeof(FirmProjectionControlCaller),
            typeof(FirmProjectionControlFieldHolder),
            typeof(FirmProjectionControlResolver),
            typeof(FirmProjectionControlBystander),
        ]);

        // Must fire: each way a type can come to hold or call the projection.
        Assert.Contains(typeof(FirmProjectionControlCaller).FullName!, found);
        Assert.Contains(typeof(FirmProjectionControlFieldHolder).FullName!, found);
        Assert.Contains(typeof(FirmProjectionControlResolver).FullName!, found);

        // Must not fire: a type that uses only the projection's output records.
        Assert.DoesNotContain(typeof(FirmProjectionControlBystander).FullName!, found);
        Assert.Equal(3, found.Count);
    }

    /// <summary>
    /// The outermost types (full names) that hold, resolve, call or name <see cref="FirmFindingsProjection"/>. The projection itself and its
    /// nested types are not callers.
    /// </summary>
    private static HashSet<string> Referrers(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var list = types.Where(t => IlCallScan.Outermost(t) != Projection).ToList();
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in list)
        {
            if (type.GetFields(all).Any(f => Mentions(f.FieldType)))
            {
                found.Add(IlCallScan.Outermost(type).FullName!);
            }

            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.GetParameters().Any(p => Mentions(p.ParameterType)) || (method is MethodInfo m && Mentions(m.ReturnType)))
                {
                    found.Add(IlCallScan.Outermost(type).FullName!);
                }
            }
        }

        foreach (var (caller, _, target) in IlCallScan.MethodReferences(list))
        {
            if (Mentions(target.DeclaringType)
                || (target is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(Mentions))
                || target.GetParameters().Any(p => Mentions(p.ParameterType)))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        foreach (var (caller, _, target) in IlCallScan.TypeReferences(list))
        {
            if (Mentions(target))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        foreach (var (caller, _, value) in IlCallScan.StringLoads(list))
        {
            if (value.Contains(nameof(FirmFindingsProjection), StringComparison.Ordinal))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        return found;
    }

    private static bool Mentions(Type? type) =>
        type is not null
        && (type == Projection
            || (type.IsGenericType && type.GetGenericArguments().Any(Mentions))
            || (type.HasElementType && Mentions(type.GetElementType())));
}

/// <summary>Control (must fire): takes the projection as a parameter and calls it.</summary>
internal static class FirmProjectionControlCaller
{
    internal static Task<FirmOutcome?> Call(FirmFindingsProjection projection, Guid invoiceId) =>
        projection.GetForInvoiceAsync(invoiceId, CancellationToken.None);
}

/// <summary>Control (must fire): holds the projection in a field and never calls it.</summary>
internal sealed class FirmProjectionControlFieldHolder
{
    private readonly FirmFindingsProjection? _held = null;

    internal bool HasOne => _held is not null;
}

/// <summary>Control (must fire): resolves the projection by a generic method instantiated over it.</summary>
internal static class FirmProjectionControlResolver
{
    internal static T Resolve<T>(Func<Type, object> get) where T : class => (T)get(typeof(T));

    internal static FirmFindingsProjection Get(Func<Type, object> get) => Resolve<FirmFindingsProjection>(get);
}

/// <summary>Control (must not fire): reads only the projection's output records, never the projection.</summary>
internal static class FirmProjectionControlBystander
{
    internal static string Label(FirmOutcome outcome) => outcome.StatusLabel;
}
