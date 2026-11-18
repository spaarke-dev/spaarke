using System.Reflection;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 131 (review round 1): <see cref="FirmNoticeComposer"/> performs NO caller check of its own (it reads
/// <see cref="FirmFindingsProjection"/>, which has none either); its safety is that every caller supplies a server-read id behind a caller check.
/// The only non-test types that may reference it are the draft route (<see cref="FirmNoticeEndpoints"/>, behind
/// <c>ebilling.invoice.decide</c>) and the DI registration that builds it (<see cref="FinanceModule"/>). Task 132's notification handler is added
/// here when it lands, with its own reason. A new caller fails this test until it is reviewed and listed.
/// </summary>
/// <remarks>
/// Same detector and closed-list rule as <c>FirmFindingsProjectionCallerBoundaryGuardTests</c>: the expected set must equal the found set, so an extra
/// caller fails and a listed type that no longer references the composer fails too. <b>Controls</b>:
/// <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the <c>FirmNoticeControl*</c> fixtures below.
/// Enforced by: this guard (ArchTests in CI and the Stop hook).
/// </remarks>
public class FirmNoticeComposerCallerBoundaryGuardTests
{
    private static readonly Type Composer = typeof(FirmNoticeComposer);

    /// <summary>The closed list: the draft route behind the decide filter, and the registration.</summary>
    private static readonly string[] AllowedReferrers =
    [
        typeof(FirmNoticeEndpoints).FullName!,
        typeof(FinanceModule).FullName!,
    ];

    [Fact(DisplayName = "131 review 1: only the firm notice draft route and its DI registration reference FirmNoticeComposer (it has no caller check)")]
    public void OnlyTheDraftRouteAndItsRegistration_ReferenceTheComposer()
    {
        var found = Referrers(typeof(Program).Assembly.GetTypes());

        Assert.True(
            found.SetEquals(AllowedReferrers),
            "FirmNoticeComposer has NO caller check; it may only be called with a server-read id behind a caller check. "
            + "Allowed (and expected): " + string.Join(", ", AllowedReferrers.OrderBy(n => n))
            + ". Found: " + string.Join(", ", found.OrderBy(n => n))
            + ". A new caller must have its own reviewed caller check and be listed here with that reason.");
    }

    [Fact(DisplayName = "131 review 1 (controls): the detector flags a caller by call, by field and by generic resolution, and passes an unrelated type")]
    public void Detector_MustFireAndMustNotFire()
    {
        var found = Referrers(
        [
            typeof(FirmNoticeControlCaller),
            typeof(FirmNoticeControlFieldHolder),
            typeof(FirmNoticeControlResolver),
            typeof(FirmNoticeControlBystander),
        ]);

        // Must fire: each way a type can come to hold or call the composer.
        Assert.Contains(typeof(FirmNoticeControlCaller).FullName!, found);
        Assert.Contains(typeof(FirmNoticeControlFieldHolder).FullName!, found);
        Assert.Contains(typeof(FirmNoticeControlResolver).FullName!, found);

        // Must not fire: a type that uses only the composer's output records.
        Assert.DoesNotContain(typeof(FirmNoticeControlBystander).FullName!, found);
        Assert.Equal(3, found.Count);
    }

    /// <summary>The outermost types (full names) that hold, resolve, call or name <see cref="FirmNoticeComposer"/>. The composer itself is not a caller.</summary>
    private static HashSet<string> Referrers(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var list = types.Where(t => IlCallScan.Outermost(t) != Composer).ToList();
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
            if (value.Contains(nameof(FirmNoticeComposer), StringComparison.Ordinal))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        return found;
    }

    private static bool Mentions(Type? type) =>
        type is not null
        && (type == Composer
            || (type.IsGenericType && type.GetGenericArguments().Any(Mentions))
            || (type.HasElementType && Mentions(type.GetElementType())));
}

/// <summary>Control (must fire): takes the composer as a parameter and calls it.</summary>
internal static class FirmNoticeControlCaller
{
    internal static Task<FirmNoticeResult> Call(FirmNoticeComposer composer, Guid invoiceId) =>
        composer.ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, invoiceId, CancellationToken.None);
}

/// <summary>Control (must fire): holds the composer in a field and never calls it.</summary>
internal sealed class FirmNoticeControlFieldHolder
{
    private readonly FirmNoticeComposer? _held = null;

    internal bool HasOne => _held is not null;
}

/// <summary>Control (must fire): resolves the composer by a generic method instantiated over it.</summary>
internal static class FirmNoticeControlResolver
{
    internal static T Resolve<T>(Func<Type, object> get) where T : class => (T)get(typeof(T));

    internal static FirmNoticeComposer Get(Func<Type, object> get) => Resolve<FirmNoticeComposer>(get);
}

/// <summary>Control (must not fire): reads only the composer's output records, never the composer.</summary>
internal static class FirmNoticeControlBystander
{
    internal static string Label(FirmNoticeDraft draft) => draft.Subject;
}
