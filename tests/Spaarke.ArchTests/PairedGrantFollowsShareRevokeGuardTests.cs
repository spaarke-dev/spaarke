using System.Reflection;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 193 G2b (#2338, owner F1-b: the paired grant follows the share) — every BFF type that
/// calls the share-revoke core (<c>IDataverseRecordShareService.RevokeAccessAsync</c>, either overload) also ends the grant PAIRED with the share it removes
/// (<see cref="ExternalGrantRevocationService"/>.<c>RevokePairedWithAsync</c>), or is on <see cref="Declared"/> with the
/// reason it does not. A new share-removal path then cannot silently leave an "Add person" grant (workforce-app access)
/// active after the person was walled off the record.
/// </summary>
/// <remarks>
/// <para><b>What counts</b> (<see cref="IlCallScan"/>, the compiled scan): a type's IL — its closures, local functions and
/// async state machines attributed to the outermost type — references a <c>RevokeAccessAsync</c> declared on the share
/// seam (or a type implementing it), and the type does not itself implement the seam (the client and its decorators are
/// the core, not callers). "Ends the paired grant" means the same type's IL references <c>RevokePairedWithAsync</c>.</para>
/// <para><b>Known limit.</b> The rule is per TYPE, not per removal: a type that ends the pairing on one path is not checked
/// on its others. The behavioural suites pin each path (<c>PairedGrantFollowsShareRemovalTests</c>,
/// <c>SecureRootInheritanceRound31Tests.G2b_*</c>, <c>ContactGrantSharesInternalUserTests.G2_*</c>). Must-fire and
/// must-not-fire controls below; a declared type that no longer revokes, or now ends the pairing, must leave the list.</para>
/// <para>Enforced by: this file (ArchTests, CI and the Stop hook).</para>
/// </remarks>
public class PairedGrantFollowsShareRevokeGuardTests
{
    /// <summary>
    /// The share-revoke callers that do NOT end a paired grant, each with why. Changing this list is a reviewed decision.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Declared = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Api.ExternalAccess.UnsecureProjectEndpoint"] =
            "owner round 135: unsecure keeps all access; the share and its paired grant both stay. The " +
            "UnsecureProjectEndpoint change is tracked separately.",
        ["Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint"] =
            "provisioning undo: removes only the creator share its own failed Make Secure wrote; no grant was paired with it",
        ["Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer"] =
            "child records (documents, events, to-dos, communications): a grant is paired only on a project, matter or work " +
            "assignment",
        ["Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing"] =
            "decision records: not a grant root, so no grant is paired there",
        ["Sprk.Bff.Api.Services.Ai.PlaybookSharingService"] =
            "playbooks: not a grant root, so no grant is paired there",
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() =>
        typeof(ExternalGrantRevocationService).Assembly.GetTypes());

    private static bool IsShareRevoke(MethodBase target) =>
        target.Name == nameof(IDataverseRecordShareService.RevokeAccessAsync)
        && target.DeclaringType is { } declaring
        && typeof(IDataverseRecordShareService).IsAssignableFrom(declaring);

    private static bool IsPairedRevoke(MethodBase target) =>
        target.Name == "RevokePairedWithAsync" && target.DeclaringType == typeof(ExternalGrantRevocationService);

    /// <summary>For each outermost type in <paramref name="types"/>: whether it revokes a share, and whether it ends a pairing.</summary>
    internal static IReadOnlyDictionary<Type, (bool Revokes, bool EndsPairing)> Callers(IEnumerable<Type> types)
        => IlCallScan.MethodReferences(types)
            .Select(r => (Caller: IlCallScan.Outermost(r.Caller), r.Target))
            .Where(r => !typeof(IDataverseRecordShareService).IsAssignableFrom(r.Caller))
            .GroupBy(r => r.Caller)
            .Select(g => (Type: g.Key, Revokes: g.Any(r => IsShareRevoke(r.Target)), EndsPairing: g.Any(r => IsPairedRevoke(r.Target))))
            .Where(c => c.Revokes || c.EndsPairing)
            .ToDictionary(c => c.Type, c => (c.Revokes, c.EndsPairing));

    /// <summary>The types that revoke a share, end no pairing and are not declared: "{type}" for each.</summary>
    internal static IReadOnlyList<string> Violations(IEnumerable<Type> types, IReadOnlyDictionary<string, string> declared)
        => Callers(types)
            .Where(c => c.Value.Revokes && !c.Value.EndsPairing && !declared.ContainsKey(c.Key.FullName!))
            .Select(c => c.Key.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    // ── The rule over the BFF ────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 193 G2b: every share-revoke caller ends the paired grant, or is declared with a reason")]
    public void EveryShareRevokeCaller_EndsThePairedGrant_OrIsDeclared()
        => Assert.Empty(Violations(BffTypes.Value, Declared));

    [Fact(DisplayName = "Task 193 G2b: the system share-removal paths end the paired grant")]
    public void TheSystemShareRemovalPaths_EndThePairedGrant()
    {
        var callers = Callers(BffTypes.Value);
        foreach (var path in new[]
                 {
                     typeof(InternalShareEndpoints), typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.NoAccessShareEnforcer),
                     typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.RestrictedExternalShareRemover), typeof(SecureRootInheritance),
                     typeof(Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer),
                 })
        {
            Assert.True(callers.TryGetValue(path, out var c) && c.Revokes && c.EndsPairing, $"{path.FullName} revokes a share and ends its pairing");
        }
    }

    [Fact(DisplayName = "Task 193 G2b: every declared exception still revokes a share and still ends no pairing")]
    public void EveryDeclaredException_IsStillOne()
    {
        var callers = Callers(BffTypes.Value).ToDictionary(c => c.Key.FullName!, c => c.Value, StringComparer.Ordinal);
        foreach (var (type, reason) in Declared)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{type} carries a reason");
            Assert.True(callers.TryGetValue(type, out var c) && c.Revokes && !c.EndsPairing,
                $"{type} is declared but no longer revokes a share without ending its pairing; take it off the list");
        }
    }

    // ── Must fire / must not fire ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustFire_AShareRevokeThatEndsNoPairing()
        => Assert.Equal(new[] { typeof(PairedGrantGuardControl_RevokesOnly).FullName! },
            Violations(IlCallScan.WithNested(typeof(PairedGrantGuardControl_RevokesOnly)), Declared));

    [Fact]
    public void MustFire_AShareRevokeInsideALambda_IsTheOutermostTypes()
        => Assert.Equal(new[] { typeof(PairedGrantGuardControl_RevokesInLambda).FullName! },
            Violations(IlCallScan.WithNested(typeof(PairedGrantGuardControl_RevokesInLambda)), Declared));

    [Fact]
    public void MustNotFire_AShareRevokeThatEndsThePairing_OrIsDeclared_OrNoRevokeAtAll()
    {
        Assert.Empty(Violations(IlCallScan.WithNested(typeof(PairedGrantGuardControl_RevokesAndEnds)), Declared));
        Assert.Empty(Violations(IlCallScan.WithNested(typeof(PairedGrantGuardControl_RevokesOnly)),
            new Dictionary<string, string> { [typeof(PairedGrantGuardControl_RevokesOnly).FullName!] = "a control" }));
        Assert.Empty(Violations(IlCallScan.WithNested(typeof(PairedGrantGuardControl_GrantsOnly)), Declared));
    }
}

/// <summary>Must fire: revokes a share and ends no pairing.</summary>
internal sealed class PairedGrantGuardControl_RevokesOnly
{
    internal static Task Remove(IDataverseRecordShareService shares, Guid id, CancellationToken ct)
        => shares.RevokeAccessAsync("sprk_matters", id, Spaarke.Dataverse.DataversePrincipalRef.User(id), ct);
}

/// <summary>Must fire: the revoke is inside a lambda (a compiler-generated closure type).</summary>
internal sealed class PairedGrantGuardControl_RevokesInLambda
{
    internal static Func<Task> Remove(IDataverseRecordShareService shares, Guid id, CancellationToken ct)
        => () => shares.RevokeAccessAsync("sprk_matters", id, Spaarke.Dataverse.DataversePrincipalRef.User(id),
            Spaarke.Dataverse.DataversePrincipalRef.User(id), ct);
}

/// <summary>Must not fire: the share first, then the grant paired with it.</summary>
internal sealed class PairedGrantGuardControl_RevokesAndEnds
{
    internal static async Task Remove(
        IDataverseRecordShareService shares, ExternalGrantRevocationService grants, Guid id, CancellationToken ct)
    {
        await shares.RevokeAccessAsync("sprk_matters", id, Spaarke.Dataverse.DataversePrincipalRef.User(id), ct);
        await grants.RevokePairedWithAsync("sprk_matter", id, id, GrantEndCause.System, ct);
    }
}

/// <summary>Must not fire: writes a share, removes none.</summary>
internal sealed class PairedGrantGuardControl_GrantsOnly
{
    internal static Task Add(IDataverseRecordShareService shares, Guid id, CancellationToken ct)
        => shares.GrantAccessAsync("sprk_matters", id, Spaarke.Dataverse.DataversePrincipalRef.User(id), "ReadAccess", ct);
}
