using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
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

/// <summary>
/// Task 132 f1-v1c-v1 verifier seed U, verbatim: an <c>[UnsafeAccessor]</c> extern that IS the client's
/// <c>RevokeAccessAsync</c>, called unqualified. The call's target is declared on THIS type, so no reference to the
/// client's write exists for C1 to see, and nothing is a string — only C8 (no UnsafeAccessor) sees it.
/// </summary>
internal static class PoaBypassControl_UnsafeAccessor
{
    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern Task RevokeAccessAsync(DataverseWebApiService client, string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct);

    internal static Task Unshare(DataverseWebApiService dv, Guid id, DataversePrincipalRef p) =>
        RevokeAccessAsync(dv, "sprk_projects", id, p, CancellationToken.None);
}

/// <summary>
/// Two more UnsafeAccessor shapes: a FIELD accessor that lifts the seam's private client out of it, and .NET 10's
/// <c>[UnsafeAccessorType]</c>, which names an inaccessible type by string on a parameter (no compile-time reference to
/// the type at all).
/// </summary>
internal static class PoaBypassControl_UnsafeAccessorOtherShapes
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_dataverse")]
    internal static extern ref DataverseWebApiService ClientOf(DataverseRecordShareService seam);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Dispose")]
    internal static extern void DisposeByTypeName([UnsafeAccessorType("System.IO.MemoryStream")] object target);
}

/// <summary>
/// Task 132 f1-v1c-v1 verifier seed R, verbatim: the client's write chosen by its SIGNATURE — no name, no constant, no
/// call instruction to the write — then invoked. C7 sees the lookup (<c>Type.GetMethods</c>) and the invocation
/// (<c>MethodBase.Invoke</c>).
/// </summary>
internal static class PoaBypassControl_ReflectionBySignature
{
    internal static object? Unshare(DataverseWebApiService dv, Guid id, DataversePrincipalRef p) =>
        typeof(DataverseWebApiService).GetMethods()
            .First(x => x.GetParameters().Length == 4
                        && x.GetParameters()[2].ParameterType == typeof(DataversePrincipalRef)
                        && x.ReturnType == typeof(Task))
            .Invoke(dv, new object[] { "sprk_projects", id, p, CancellationToken.None });
}

/// <summary>
/// C7's other shapes — never executed, only scanned. One use of each API on C7's list (C7 bans exactly these, not every
/// way to choose or invoke a method by reflection). The Type each starts from is deliberately NOT a Dataverse service type,
/// or is obtained without naming one: the rule bans the API, so where the Type came from does not matter.
/// </summary>
internal static class PoaBypassControl_Reflection
{
    internal static object FromAnInstance(object target) => target.GetType().GetMember("RunAsync", BindingFlags.Public | BindingFlags.Instance);

    internal static object ThroughIReflect(Type type) => ((IReflect)type).GetMethods(BindingFlags.Public | BindingFlags.Instance);

    internal static object DeclaredMethods(Type type) => type.GetTypeInfo().DeclaredMethods;

    internal static object RuntimeMethods(Type type) => type.GetRuntimeMethods();

    internal static object InterfaceMap(Type type) => type.GetInterfaceMap(typeof(IDisposable)).TargetMethods;

    internal static object? ByMetadataToken(Type type) => type.Module.ResolveMethod(0x06000001);

    internal static object? PropertyAccessor(Type type) => type.GetProperty("Length")!.GetMethod;

    internal static object? InvokeMember(Type type, object target) => type.InvokeMember("Run", BindingFlags.InvokeMethod, null, target, null);

    internal static Delegate StaticCreateDelegate(MethodInfo method, object target) => Delegate.CreateDelegate(typeof(Action), target, method);

    internal static Action InstanceCreateDelegate(MethodInfo method, object target) => method.CreateDelegate<Action>(target);

    internal static object Invoker(MethodInfo method) => MethodInvoker.Create(method);

    internal static IntPtr FunctionPointer(MethodInfo method) => method.MethodHandle.GetFunctionPointer();

    internal static object CompiledTree(MethodInfo method, object target) =>
        Expression.Lambda<Func<object?>>(Expression.Call(Expression.Constant(target), method)).Compile();

    internal static object CallByName(object target) => Expression.Call(Expression.Constant(target), "Run", null);

    internal static object ThroughDynamic(Type type) => ((dynamic)type).GetMethods();

    internal static object Emitted() => new DynamicMethod("m", typeof(void), Type.EmptyTypes);

    internal static object LoadedFromBytes(byte[] image) => Assembly.Load(image);

    internal static object LoadedFromAStream(Stream image) => AssemblyLoadContext.Default.LoadFromStream(image);

    /// <summary>A generic helper: the Type is a type parameter, which a caller could bind to the client.</summary>
    internal static MethodInfo[] OfTypeParameter<T>() => typeof(T).GetMethods();
}

/// <summary>
/// C7's positive control — reflection the BFF does use, which reaches no method: property, field and attribute reading, a
/// type's name, constructing by type, an embedded resource, a compiler-built expression tree (handed to a LINQ provider,
/// not compiled here). None of it is flagged.
/// </summary>
internal static class PoaReflectionPermittedControl
{
    internal static object Properties(object target) => target.GetType().GetProperties().Select(p => p.GetValue(target)).ToList();

    internal static string Name(object target) => target.GetType().FullName ?? target.GetType().Name;

    internal static object? Construct() => Activator.CreateInstance(typeof(List<int>));

    internal static object Attributes() => typeof(PoaReflectionPermittedControl).GetCustomAttributes(false);

    internal static object Resources() => Assembly.GetExecutingAssembly().GetManifestResourceNames();

    internal static Expression<Func<string, bool>> TranslatedTree() => s => s.StartsWith("a", StringComparison.Ordinal);
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
/// C3 path-analysis controls — never executed, only analysed. The first three send and then notify on EVERY path; each of
/// the rest has one path that does not, named by what it does wrong. <c>NotifyAsync</c> stands in for the client's
/// notification and <c>ObserveAsync</c> for the observer call the notification makes. (Task 132 seed N1's shape — a team
/// branch that sends and returns before the <c>try</c> — is <see cref="EarlyReturnForTeams"/>.)
/// </summary>
internal sealed class PoaShareWriteSenderControls
{
    private readonly HttpClient _http = null!;

    internal Task NotifyAsync(string entitySetName, Guid recordId) => Task.CompletedTask;

    internal Task ObserveAsync(string entitySetName, Guid recordId) => Task.CompletedTask;

    // ── every path notifies ──

    internal async Task Canonical(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task CatchAndReturnInsideTheTry(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            try
            {
                await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return;
            }
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task UnderAUsing(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        using var scope = new MemoryStream();
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }

        await scope.FlushAsync(ct).ConfigureAwait(false);
    }

    internal async Task HelperCanonical(string entitySetName, Guid recordId)
    {
        try
        {
            await ObserveAsync(entitySetName, recordId).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // the client's notification logs here
        }
    }

    // ── a path sends and does not notify ──

    /// <summary>Task 132 seed N1's shape: a team share sent before the try, returned from with no notification.</summary>
    internal async Task EarlyReturnForTeams(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, HttpRequestMessage request, CancellationToken ct)
    {
        if (principal.Kind == DataversePrincipalKind.Team)
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task ConditionalNotification(
        string entitySetName, Guid recordId, DataversePrincipalRef principal, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            if (principal.Kind == DataversePrincipalKind.SystemUser)
            {
                await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
            }
        }
    }

    internal async Task NotifiesBeforeSending(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    internal async Task SwallowsTheFailureAndReturns(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return;
        }

        await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
    }

    internal async Task SendNotAwaited(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            _ = _http.SendAsync(request, ct);
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task NotificationNotAwaited(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = NotifyAsync(entitySetName, recordId);
        }
    }

    internal async Task NotifiesAnotherRecord(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            await NotifyAsync(entitySetName, Guid.Empty).ConfigureAwait(false);
        }
    }

    internal async Task ReassignsTheRecordFirst(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            recordId = Guid.NewGuid();
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal Task<HttpResponseMessage> PassThrough(string entitySetName, Guid recordId, HttpRequestMessage request, CancellationToken ct) =>
        _http.SendAsync(request, ct);

    internal async Task TwoSends(string entitySetName, Guid recordId, HttpRequestMessage first, HttpRequestMessage second, CancellationToken ct)
    {
        try
        {
            await _http.SendAsync(first, ct).ConfigureAwait(false);
            await _http.SendAsync(second, ct).ConfigureAwait(false);
        }
        finally
        {
            await NotifyAsync(entitySetName, recordId).ConfigureAwait(false);
        }
    }

    internal async Task HelperSkipsAnEntitySet(string entitySetName, Guid recordId)
    {
        if (entitySetName == "sprk_playbooks")
        {
            return;
        }

        await ObserveAsync(entitySetName, recordId).ConfigureAwait(false);
    }

    internal Task HelperNotAwaited(string entitySetName, Guid recordId)
    {
        _ = ObserveAsync(entitySetName, recordId);
        return Task.CompletedTask;
    }

    internal async Task HelperOtherRecord(string entitySetName, Guid recordId) =>
        await ObserveAsync(entitySetName, Guid.Empty).ConfigureAwait(false);
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

    /// <summary>A UTF-8 literal: an RVA field's bytes, no <c>ldstr</c>.</summary>
    internal static ReadOnlySpan<byte> Utf8Action => "ModifyAccess"u8;

    /// <summary>A char array initializer: an RVA field's UTF-16 bytes, no <c>ldstr</c>.</summary>
    internal static char[] CharArrayAction() => new[] { 'R', 'e', 'v', 'o', 'k', 'e', 'A', 'c', 'c', 'e', 's', 's' };

    internal static void GrantAccessAsync()
    {
    }
}
