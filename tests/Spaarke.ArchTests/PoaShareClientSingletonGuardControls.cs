using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Spaarke.ArchTests;

// The control fixtures for PoaShareClientSingletonGuardTests (task 060 / task 132). Kept apart from the rules: these types
// are compiled only to be READ — by the IL scans and the path analysis — and never executed.

// ── Controls for the compiled rules — never executed, only scanned. One bypass shape per type, because the compiled
//    rules report a bypass by its outermost type; each shape is one the original text detector could not see.

/// <summary>Task 132 verifier seed S5's shape: the concrete client resolved inline — a receiver no text rule can type.</summary>
internal static class PoaBypassControl_ResolvedInline
{
    internal static Task ResolvedInline(IServiceProvider sp) =>
        sp.GetRequiredService<DataverseWebApiService>()
            .RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty));
}

/// <summary>A write inside an async lambda — compiled into a closure's state machine, not the method that holds it.</summary>
internal static class PoaBypassControl_AsyncLambda
{
    internal static Func<Task> InsideAnAsyncLambda(DataverseWebApiService client) =>
        async () => await client.GrantAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty), "ReadAccess");
}

/// <summary>A method-group conversion — an <c>ldftn</c>, no call instruction, and no <c>(</c> for a text rule to find.</summary>
internal static class PoaBypassControl_MethodGroup
{
    internal static Func<string, Guid, DataversePrincipalRef, string, CancellationToken, Task> AsAMethodGroup(DataverseWebApiService client) =>
        client.ModifyAccessAsync;
}

/// <summary>An expression tree — an <c>ldtoken</c> of the method, compiled and invoked at run time.</summary>
internal static class PoaBypassControl_ExpressionTree
{
    internal static Expression<Func<Task>> AsAnExpressionTree(DataverseWebApiService client) =>
        () => client.RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty), CancellationToken.None);
}

/// <summary>The SDK's POA message — however its namespace is imported.</summary>
internal static class PoaBypassControl_SdkMessage
{
    internal static object SdkGrant() => new Microsoft.Crm.Sdk.Messages.GrantAccessRequest();
}

/// <summary>
/// POA actions and write methods named as string constants — and, as the positive half, constants that only share a
/// prefix with one (other words, not names).
/// </summary>
internal static class PoaBypassControl_NamedAsConstants
{
    private const string Grant = "Grant";

    /// <summary>Folded by the compiler into ONE constant, <c>"GrantAccess"</c> — invisible to a text rule.</summary>
    internal static string FoldedFromConstants() => Grant + "Access";

    internal static string ActionUrl(string baseUrl) => $"{baseUrl}/RevokeAccess";

    internal static string BatchLine() => "POST Microsoft.Dynamics.CRM.ModifyAccess HTTP/1.1";

    internal static string WriteMethodName() => nameof(DataverseWebApiService.ModifyAccessAsync);

    /// <summary>A <c>dynamic</c> call: its member name is a binder string, not a method token — C4 sees the string.</summary>
    internal static Task ThroughDynamic(object client) =>
        ((dynamic)client).RevokeAccessAsync("sprk_projects", Guid.Empty, DataversePrincipalRef.User(Guid.Empty));

    internal static string OtherWords() => "GrantAccessRequest rejected; RevokeAccessAsyncHandler, ModifyAccessible";
}

/// <summary>Positive control for the compiled rules — the sanctioned shape: every write through the seam's interface.</summary>
internal static class PoaWriteThroughSeamControl
{
    internal static async Task AllThreeWrites(IDataverseRecordShareService seam)
    {
        var principal = DataversePrincipalRef.User(Guid.Empty);
        await seam.GrantAccessAsync("sprk_projects", Guid.Empty, principal, "ReadAccess");
        await seam.ModifyAccessAsync("sprk_projects", Guid.Empty, principal, "ReadAccess");
        await seam.RevokeAccessAsync("sprk_projects", Guid.Empty, principal);
    }
}

/// <summary>
/// C3 (a)'s pin controls — never executed, only scanned: the method a pin binds, a same-name overload that writes (task
/// 132 seed N2), and a write inside a lambda held by a pinned-name method.
/// </summary>
internal sealed class PoaSeamPinControl
{
    private readonly DataverseWebApiService _client = null!;

    internal async Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct) =>
        await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);

    internal Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, bool quiet, CancellationToken ct) =>
        _client.RevokeAccessAsync(entitySetName, recordId, principal, ct);

    internal Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string rights, CancellationToken ct) =>
        Task.Run(() => _client.GrantAccessAsync(entitySetName, recordId, principal, rights, ct), ct);
}

/// <summary>
/// C3 (b) / (c) path-analysis controls — never executed, only analysed. The first three write and then evict on EVERY
/// path; each of the rest has one path that does not, named by what it does wrong. <c>EvictAsync</c> stands in for the
/// seam's eviction helper and <c>InvalidateAsync</c> for the invalidator the helper calls.
/// </summary>
internal sealed class PoaEvictionPathControls
{
    private readonly DataverseWebApiService _client = null!;

    private Task EvictAsync(string entitySetName, Guid recordId) => Task.CompletedTask;

    private Task InvalidateAsync(string entitySetName, Guid recordId) => Task.CompletedTask;

    // ── every path evicts ──

    internal async Task Canonical(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task CatchAndReturnInsideTheTry(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            try
            {
                await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return;
            }
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task UnderAUsing(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        using var scope = new MemoryStream();
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }

        await scope.FlushAsync(ct).ConfigureAwait(false);
    }

    internal async Task HelperCanonical(string entitySetName, Guid recordId)
    {
        try
        {
            await InvalidateAsync(entitySetName, recordId).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // the seam's helper logs here
        }
    }

    // ── a path writes and does not evict ──

    /// <summary>Task 132 seed N1: a team share written before the try, returned from with no eviction.</summary>
    internal async Task EarlyReturnForTeams(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        if (principal.Kind == DataversePrincipalKind.Team)
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task ConditionalEviction(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            if (principal.Kind == DataversePrincipalKind.SystemUser)
            {
                await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
            }
        }
    }

    internal async Task EvictsBeforeWriting(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
    }

    internal async Task SwallowsTheFailureAndReturns(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return;
        }

        await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
    }

    internal async Task WriteNotAwaited(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            _ = _client.RevokeAccessAsync(entitySetName, recordId, principal, ct);
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task EvictionNotAwaited(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = EvictAsync(entitySetName, recordId);
        }
    }

    internal async Task EvictsAnotherRecord(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAsync(entitySetName, Guid.Empty).ConfigureAwait(false);
        }
    }

    internal async Task ReassignsTheRecordFirst(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            recordId = Guid.NewGuid();
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal Task PassThrough(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct) =>
        _client.RevokeAccessAsync(entitySetName, recordId, principal, ct);

    internal async Task TwoWrites(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct)
    {
        try
        {
            await _client.GrantAccessAsync(entitySetName, recordId, principal, "ReadAccess", ct).ConfigureAwait(false);
            await _client.ModifyAccessAsync(entitySetName, recordId, principal, "ReadAccess,WriteAccess", ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task HelperSkipsAnEntitySet(string entitySetName, Guid recordId)
    {
        if (entitySetName == "sprk_playbooks")
        {
            return;
        }

        await InvalidateAsync(entitySetName, recordId).ConfigureAwait(false);
    }

    internal Task HelperNotAwaited(string entitySetName, Guid recordId)
    {
        _ = InvalidateAsync(entitySetName, recordId);
        return Task.CompletedTask;
    }

    internal async Task HelperOtherRecord(string entitySetName, Guid recordId) =>
        await InvalidateAsync(entitySetName, Guid.Empty).ConfigureAwait(false);
}

/// <summary>
/// C6 controls — never executed, only scanned: a POA action carried by metadata (an enum value, a member or parameter
/// name, a const, an attribute argument, a default parameter value), and names that only share a prefix (other words).
/// </summary>
internal static class PoaBypassControl_Metadata
{
    internal enum PoaAction
    {
        GrantAccess,
        Unrelated,
    }

    internal const string ConstAction = "RevokeAccess";

    internal const string OtherWords = "GrantAccessRequest";

    [System.ComponentModel.Description("ModifyAccess")]
    internal static void Attributed()
    {
    }

    internal static void DefaultValue(string method = "GrantAccessAsync") => _ = method;

    internal static void RevokeAccess()
    {
    }

    internal static void Parameter(string modifyAccess) => _ = modifyAccess;

    internal static void GrantAccessAsync()
    {
    }
}
