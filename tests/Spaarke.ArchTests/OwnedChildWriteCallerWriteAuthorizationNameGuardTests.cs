using System.Reflection;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Field-mapping-server-write-path-r1 task 013 (unified-access-control-r2 review condition K2): <see cref="OwnedChildWrite"/>
/// and <see cref="CallerWriteAuthorization"/> share no member name.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The two files import each other with <c>using static</c> (task 011 moved the caller checks out of
/// <see cref="OwnedChildWrite"/> unchanged, so every moved body reads as it did). C# looks up a simple name in the class's
/// own members before an imported one: if either class ever declares a member with the name of a member of the other,
/// every unqualified call to the other's member in that file binds to its own, silently and with no compile error — on
/// the security boundary (the G5 caller checks and the re-file). This guard makes that a build failure.</para>
/// <para><b>What counts.</b> Every member each class itself declares (static, public or not: a private member shadows an
/// imported one as well) and every nested type (also imported by <c>using static</c>). Accessor and operator methods
/// (special names, covered by their property) and compiler-generated members (names with <c>&lt;</c>: closures, state
/// machines, local functions) are not names a call can bind to and are left out.</para>
/// <para><b>Controls.</b> <see cref="Guard_MustFire_TwoTypesSharingAMethodAConstantAndANestedType_AreReported"/> runs the
/// detector over two fixture classes that share a method, a constant and a nested type name and requires all three;
/// <see cref="Guard_MustNotFire_TwoTypesWithOnlyCompilerGeneratedOverlap_ReportNothing"/> runs it over two that share only
/// compiler-generated members (each has a static lambda, so both hold a <c>&lt;&gt;c</c> closure class, and an async
/// method) and requires nothing.</para>
/// </remarks>
public class OwnedChildWriteCallerWriteAuthorizationNameGuardTests
{
    private const BindingFlags DeclaredStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The detector: the names both <paramref name="first"/> and <paramref name="second"/> declare.</summary>
    private static IReadOnlyList<string> SharedMemberNames(Type first, Type second) =>
        BindableNamesOf(first).Intersect(BindableNamesOf(second), StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> BindableNamesOf(Type type) =>
        type.GetMembers(DeclaredStatic)
            .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m is not MethodBase { IsSpecialName: true })
            .Select(m => m.Name)
            .Where(name => !name.Contains('<', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

    [Fact]
    public void OwnedChildWriteAndCallerWriteAuthorization_ShareNoMemberName()
    {
        var shared = SharedMemberNames(typeof(OwnedChildWrite), typeof(CallerWriteAuthorization));

        Assert.True(BindableNamesOf(typeof(OwnedChildWrite)).Count() >= 10, "OwnedChildWrite's members were not read.");
        Assert.True(BindableNamesOf(typeof(CallerWriteAuthorization)).Count() >= 5, "CallerWriteAuthorization's members were not read.");
        Assert.True(shared.Count == 0,
            "OwnedChildWrite and CallerWriteAuthorization import each other with `using static`, so a member of one named like " +
            "a member of the other silently captures every unqualified call to it in that file (UAC-r2 K2). Rename one, or " +
            "qualify every cross-class call and drop the import: " + string.Join(", ", shared));
    }

    [Fact]
    public void Guard_MustFire_TwoTypesSharingAMethodAConstantAndANestedType_AreReported()
    {
        Assert.Equal(["Outcome", "Shared", "SharedConstant"],
            SharedMemberNames(typeof(CollidingFixtureFirst), typeof(CollidingFixtureSecond)));
    }

    [Fact]
    public void Guard_MustNotFire_TwoTypesWithOnlyCompilerGeneratedOverlap_ReportNothing()
    {
        Assert.Empty(SharedMemberNames(typeof(DistinctFixtureFirst), typeof(DistinctFixtureSecond)));

        // The fixtures do overlap in compiler-generated names (the static lambdas' closure class), which the detector skips.
        var generatedFirst = typeof(DistinctFixtureFirst).GetNestedTypes(BindingFlags.NonPublic).Select(t => t.Name);
        var generatedSecond = typeof(DistinctFixtureSecond).GetNestedTypes(BindingFlags.NonPublic).Select(t => t.Name);
        Assert.NotEmpty(generatedFirst.Intersect(generatedSecond));
    }

    // ── fixtures ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static class CollidingFixtureFirst
    {
        internal const string SharedConstant = "first";

        internal static bool Shared(string value) => value.Length > 0;

        internal static int OnlyFirst() => 1;

        internal sealed record Outcome(bool Succeeded);
    }

    private static class CollidingFixtureSecond
    {
        private const string SharedConstant = "second";

        private static bool Shared(int value) => value > 0 && SharedConstant.Length > 0;

        internal static int OnlySecond() => Shared(2) ? 2 : 0;

        private sealed class Outcome;
    }

    private static class DistinctFixtureFirst
    {
        internal static readonly Func<int, int> Twice = x => x * 2;

        internal static async Task<int> FirstAsync() => await Task.FromResult(Twice(1)).ConfigureAwait(false);

        internal static string FirstProperty => "first";
    }

    private static class DistinctFixtureSecond
    {
        internal static readonly Func<int, int> Thrice = x => x * 3;

        internal static async Task<int> SecondAsync() => await Task.FromResult(Thrice(1)).ConfigureAwait(false);

        internal static string SecondProperty => "second";
    }
}
