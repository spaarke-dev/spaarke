using System.Reflection;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 113 (model 8.2): the Procedure-change banner is computed on the internal Invoice Review route only. <see cref="ProcedureChangeBanner"/>
/// performs no caller check of its own (the review service gates it on the step-0 verdict), so the only non-test types that may reference it are the
/// review read service that gates it and the DI registration that builds it. Its output, <see cref="ProcedureChangeBannerDto"/>, may be named only
/// by the banner, the review model that carries it and the review service. A firm or external route, the firm findings projection, a list, an export
/// or any other consumer fails this test until it is reviewed and listed here.
/// </summary>
/// <remarks>
/// <para><b>What counts as a reference</b> (the compiled assembly, not the text): a field, parameter or return of the type (a generic argument
/// included), a call to one of its members, to a method that takes or returns it, or to a member of a type that returns it (so reading
/// <c>InvoiceReviewDto.ProcedureChange</c> counts), a generic method instantiated over it, an IL type operand, and a string constant that names it.
/// A lambda or an async state machine is attributed to the outermost type it is written in. The type itself is not a referrer.</para>
/// <para><b>What else is listed.</b> Every type that names <c>InvoiceReviewDto</c> (the model that carries the member), and every caller of the
/// <c>InvoiceReviewReadService.GetAsync</c> overload that takes step 0's verdict. Not covered: the value a caller passes (the route's wiring is a
/// contract test), a reflection or delegate hand-off, and test assemblies.</para>
/// <para><b>Closed lists, no baseline.</b> The expected set must equal the found set: an extra referrer fails, and a listed type that no longer
/// references the target fails too, so the lists only shrink. <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector
/// over the <c>BannerControl*</c> fixtures below. Why not a source scan: a text search misses a reference by <c>var</c>, a generic argument or a
/// getter, and cannot see the compiled shape. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class ProcedureChangeBannerCallerBoundaryGuardTests
{
    private static readonly Type Banner = typeof(ProcedureChangeBanner);
    private static readonly Type BannerDto = typeof(ProcedureChangeBannerDto);

    private static string[] Sorted(params Type[] types) => types.Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact(DisplayName = "113 (model 8.2): only the review read service and the DI registration reference ProcedureChangeBanner (no firm, external or other route computes it)")]
    public void OnlyTheReviewServiceAndTheRegistration_ReferenceTheBanner()
    {
        var found = Referrers(typeof(Program).Assembly.GetTypes(), Banner);

        AssertSame(found, Sorted(typeof(InvoiceReviewReadService), typeof(FinanceModule)), Banner);
    }

    [Fact(DisplayName = "113 (model 8.2): only the banner, the review model and the review service name ProcedureChangeBannerDto (nothing else carries or reads the member)")]
    public void OnlyTheReviewModelAndService_NameTheBannerDto()
    {
        var found = Referrers(typeof(Program).Assembly.GetTypes(), BannerDto);

        AssertSame(found, Sorted(typeof(ProcedureChangeBanner), typeof(InvoiceReviewDto), typeof(InvoiceReviewReadService)), BannerDto);
    }

    [Fact(DisplayName = "113 (model 8.2): only the review route and the review service name InvoiceReviewDto (the model that carries the member reaches no other route or projection)")]
    public void OnlyTheReviewRouteAndService_NameTheReviewModel()
    {
        var found = Referrers(typeof(Program).Assembly.GetTypes(), typeof(InvoiceReviewDto));

        AssertSame(found, Sorted(typeof(InvoiceReviewReadService), typeof(InvoiceLineReviewEndpoints)), typeof(InvoiceReviewDto));
    }

    [Fact(DisplayName = "113 (model 8.2): only the review route (and the service's own verdict-less overload) calls the GetAsync overload that takes step 0's verdict (no other caller can request the banner)")]
    public void OnlyTheReviewRoute_CallsTheVerdictOverload()
    {
        var found = CallersOf(typeof(Program).Assembly.GetTypes(), VerdictOverload);

        // The service itself calls it from its verdict-less overload (passing none), which is why it is listed beside the route.
        AssertSame(found, Sorted(typeof(InvoiceLineReviewEndpoints), typeof(InvoiceReviewReadService)), typeof(InvoiceReviewReadService));
    }

    private static readonly MethodInfo VerdictOverload = typeof(InvoiceReviewReadService).GetMethods()
        .Single(m => m.Name == nameof(InvoiceReviewReadService.GetAsync) && m.GetParameters().Any(p => p.ParameterType == typeof(FinanceStepZeroVerdict?)));

    [Fact(DisplayName = "113 (controls): the verdict-overload detector flags a caller of the verdict overload and passes a caller of the verdict-less one and an unrelated type")]
    public void VerdictOverloadDetector_MustFireAndMustNotFire()
    {
        var found = CallersOf([typeof(VerdictControlCaller), typeof(VerdictControlWithoutVerdict), typeof(BannerControlBystander)], VerdictOverload);

        Assert.Equal(new[] { typeof(VerdictControlCaller).FullName! }, found.ToArray());
    }

    [Fact(DisplayName = "113 (controls): the detector flags a caller by call, by field, by generic resolution and by a getter's return, and passes an unrelated type")]
    public void Detector_MustFireAndMustNotFire()
    {
        var found = Referrers(
        [
            typeof(BannerControlCaller),
            typeof(BannerControlFieldHolder),
            typeof(BannerControlResolver),
            typeof(BannerControlGetterReader),
            typeof(BannerControlBystander),
        ],
        Banner);

        // Must fire: each way a type can come to hold or call the banner.
        Assert.Contains(typeof(BannerControlCaller).FullName!, found);
        Assert.Contains(typeof(BannerControlFieldHolder).FullName!, found);
        Assert.Contains(typeof(BannerControlResolver).FullName!, found);

        // Must not fire: a type that has nothing to do with it.
        Assert.DoesNotContain(typeof(BannerControlBystander).FullName!, found);
        Assert.Equal(3, found.Count);

        var viaDto = Referrers([typeof(BannerControlGetterReader), typeof(BannerControlBystander)], BannerDto);
        Assert.Contains(typeof(BannerControlGetterReader).FullName!, viaDto);
        Assert.DoesNotContain(typeof(BannerControlBystander).FullName!, viaDto);
    }

    private static void AssertSame(HashSet<string> found, string[] allowed, Type target) =>
        Assert.True(
            found.SetEquals(allowed),
            target.Name + " has NO caller check of its own; it may only be computed on the internal Invoice Review route. "
            + "Allowed (and expected): " + string.Join(", ", allowed)
            + ". Found: " + string.Join(", ", found.OrderBy(n => n, StringComparer.Ordinal))
            + ". A new referrer must be reviewed (internal callers only, model 8.2) and be listed here with that reason.");

    /// <summary>The outermost types (full names) whose method bodies call exactly <paramref name="method"/>.</summary>
    private static HashSet<string> CallersOf(IEnumerable<Type> types, MethodInfo method) =>
        IlCallScan.MethodReferences(types.ToList())
            .Where(r => r.Target is MethodInfo called && called.MetadataToken == method.MetadataToken && called.Module == method.Module)
            .Select(r => IlCallScan.Outermost(r.Caller).FullName!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The outermost types (full names) that hold, resolve, call or name <paramref name="target"/>. The target and its nested types are not referrers.</summary>
    private static HashSet<string> Referrers(IEnumerable<Type> types, Type target)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var list = types.Where(t => IlCallScan.Outermost(t) != target).ToList();
        var found = new HashSet<string>(StringComparer.Ordinal);

        bool Mentions(Type? type) =>
            type is not null
            && (type == target
                || (type.IsGenericType && type.GetGenericArguments().Any(Mentions))
                || (type.HasElementType && Mentions(type.GetElementType())));

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

        foreach (var (caller, _, callee) in IlCallScan.MethodReferences(list))
        {
            if (Mentions(callee.DeclaringType)
                || (callee is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(Mentions))
                || (callee is MethodInfo returning && Mentions(returning.ReturnType))
                || callee.GetParameters().Any(p => Mentions(p.ParameterType)))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        foreach (var (caller, _, referenced) in IlCallScan.TypeReferences(list))
        {
            if (Mentions(referenced))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        foreach (var (caller, _, value) in IlCallScan.StringLoads(list))
        {
            // A whole name: the record's generated ToString names "ProcedureChangeBannerDto" and must not count as naming the banner.
            if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\b" + target.Name + @"\b"))
            {
                found.Add(IlCallScan.Outermost(caller).FullName!);
            }
        }

        return found;
    }
}

/// <summary>Control (must fire): takes the banner as a parameter and calls it.</summary>
internal static class BannerControlCaller
{
    internal static Task<ProcedureChangeBannerDto?> Call(ProcedureChangeBanner banner, Microsoft.Xrm.Sdk.Entity invoice) =>
        banner.ComputeAsync(invoice, [], null, null, true, CancellationToken.None);
}

/// <summary>Control (must fire): holds the banner in a field and never calls it.</summary>
internal sealed class BannerControlFieldHolder
{
    private readonly ProcedureChangeBanner? _held = null;

    internal bool HasOne => _held is not null;
}

/// <summary>Control (must fire): resolves the banner by a generic method instantiated over it.</summary>
internal static class BannerControlResolver
{
    internal static T Resolve<T>(Func<Type, object> get) where T : class => (T)get(typeof(T));

    internal static ProcedureChangeBanner Get(Func<Type, object> get) => Resolve<ProcedureChangeBanner>(get);
}

/// <summary>Control (must fire on the DTO, not the banner): reads the member off a review model.</summary>
internal static class BannerControlGetterReader
{
    internal static string? Read(InvoiceReviewDto review) => review.ProcedureChange?.State;
}

/// <summary>Control (must fire): calls the overload that takes step 0's verdict.</summary>
internal static class VerdictControlCaller
{
    internal static Task<InvoiceReviewDto?> Call(InvoiceReviewReadService service, Guid id, InvoiceContextService.InvoiceCaller caller) =>
        service.GetAsync(id, caller, FinanceStepZeroVerdict.Internal, CancellationToken.None);
}

/// <summary>Control (must not fire): calls the overload with no verdict, which can never carry the banner.</summary>
internal static class VerdictControlWithoutVerdict
{
    internal static Task<InvoiceReviewDto?> Call(InvoiceReviewReadService service, Guid id, InvoiceContextService.InvoiceCaller caller) =>
        service.GetAsync(id, caller, CancellationToken.None);
}

/// <summary>Control (must not fire): has nothing to do with the banner or its member.</summary>
internal static class BannerControlBystander
{
    internal static string Label(string value) => value.Trim();
}
