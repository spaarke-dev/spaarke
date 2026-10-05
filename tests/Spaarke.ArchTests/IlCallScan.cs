using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Spaarke.ArchTests;

/// <summary>
/// The COMPILED counterpart of <see cref="SourceScan"/>: which methods a type's IL actually references, and which
/// string constants it loads.
/// </summary>
/// <remarks>
/// <para><b>Why IL, not text.</b> A text scan answers "does this file mention a call shaped like X"; it cannot know a
/// receiver's TYPE. <c>sp.GetRequiredService&lt;DataverseWebApiService&gt;().RevokeAccessAsync(…)</c> and a parameter of
/// the concrete client that happens to share a name with a seam field are both invisible to it (task 132 verifier seeds
/// S5 and S6). The compiler has already resolved every call to a method token, so reading those tokens answers the
/// question exactly, whatever the receiver expression, lambda, local function, async state machine or method-group
/// conversion wraps it. Likewise a string the compiler folded from constant pieces (<c>"Grant" + "Access"</c>), a
/// <c>nameof</c> and a <c>const</c> are all one <c>ldstr</c> by the time they reach IL.</para>
///
/// <para><b>What counts as a method reference</b>: every instruction whose operand is a method token —
/// <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c>, <c>jmp</c> — and an <c>ldtoken</c> of a
/// method (the handle an expression tree or <c>MethodBase.GetMethodFromHandle</c> is built from). <b>A string load</b>
/// is an <c>ldstr</c>. Compiler-generated types (closures, state machines) are attributed to their OUTERMOST declaring
/// type, which is the type a reviewer reads; <see cref="SourceMethodName"/> maps an async / iterator state machine back
/// to the method it was compiled from.</para>
///
/// <para><b>Fails loud.</b> An IL stream that cannot be decoded, or a token that cannot be resolved, throws — a scanner
/// that skipped what it could not read would under-report silently, which for a guard is worse than none.</para>
///
/// <para><b>Not a method reference</b>: a <c>dynamic</c> call — its member name is a binder STRING, which
/// <see cref="StringLoads"/> does see. <b>Out of its reach by construction</b>: anything decided at RUN time — a method
/// found by a name computed from non-constant pieces or read from configuration or attribute metadata, an action URL
/// assembled from variables. Those are review territory.</para>
/// </remarks>
internal static class IlCallScan
{
    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    static IlCallScan()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode op)
            {
                continue;
            }

            var value = unchecked((ushort)op.Value);
            if (op.Size == 1)
            {
                OneByte[value] = op;
            }
            else
            {
                TwoByte[value & 0xFF] = op;
            }
        }
    }

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Metadata tables an <c>ldtoken</c> operand can name a METHOD from (MethodDef, MemberRef, MethodSpec).</summary>
    private const int MethodDefTable = 0x06;
    private const int MemberRefTable = 0x0A;
    private const int MethodSpecTable = 0x2B;

    /// <summary>The outermost declaring type — what a closure or state machine is attributed to.</summary>
    internal static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    /// <summary>A type and every type nested in it, at any depth (closures and state machines included).</summary>
    internal static IEnumerable<Type> WithNested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested));

    /// <summary>
    /// The source-level method a compiled body belongs to: the method itself for a body of the outermost type; for an
    /// async / iterator state machine, the method the compiler built it from (its <see cref="StateMachineAttribute"/>);
    /// for any other compiler-generated body (a lambda's closure), <c>"{nested type}.{method}"</c> — never one of the
    /// outermost type's method names, so a pin on method names cannot be satisfied by a lambda by accident.
    /// </summary>
    internal static string SourceMethodName(Type caller, MethodBase callerMethod)
    {
        var outermost = Outermost(caller);
        if (caller == outermost)
        {
            return callerMethod.Name;
        }

        var stateMachine = caller.IsGenericType ? caller.GetGenericTypeDefinition() : caller;
        var owner = WithNested(outermost)
            .SelectMany(t => t.GetMethods(Declared))
            .FirstOrDefault(m => m.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType == stateMachine);

        return owner is not null && owner.DeclaringType == outermost
            ? owner.Name
            : $"{caller.Name}.{callerMethod.Name}";
    }

    /// <summary>
    /// Every method referenced from <paramref name="types"/>' own method and constructor bodies, as
    /// (the calling type, the calling method, the referenced method).
    /// </summary>
    internal static IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> MethodReferences(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            foreach (var method in Bodies(type))
            {
                foreach (var operand in TokenOperands(method))
                {
                    var target = operand.Kind switch
                    {
                        OperandType.InlineMethod => Resolve(method, operand.Token, (module, token, ta, ma) => module.ResolveMethod(token, ta, ma)),
                        OperandType.InlineTok when IsMethodTable(operand.Token) =>
                            Resolve(method, operand.Token, (module, token, ta, ma) => module.ResolveMember(token, ta, ma)) as MethodBase,
                        _ => null,
                    };

                    if (target is not null)
                    {
                        yield return (type, method, target);
                    }
                }
            }
        }
    }

    /// <summary>Every string constant (<c>ldstr</c>) loaded from <paramref name="types"/>' own method and constructor bodies.</summary>
    internal static IEnumerable<(Type Caller, MethodBase CallerMethod, string Value)> StringLoads(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            foreach (var method in Bodies(type))
            {
                foreach (var operand in TokenOperands(method).Where(o => o.Kind == OperandType.InlineString))
                {
                    string value;
                    try
                    {
                        value = method.Module.ResolveString(operand.Token);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException(
                            $"Could not resolve string token 0x{operand.Token:X8} in {Describe(method)} — the scan would under-report.", ex);
                    }

                    yield return (type, method, value);
                }
            }
        }
    }

    private static IEnumerable<MethodBase> Bodies(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

    private static bool IsMethodTable(int token) => (token >> 24) is MethodDefTable or MemberRefTable or MethodSpecTable;

    private static T? Resolve<T>(MethodBase method, int token, Func<Module, int, Type[]?, Type[]?, T?> resolve)
        where T : class
    {
        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        try
        {
            return resolve(method.Module, token, typeArgs, methodArgs);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"Could not resolve token 0x{token:X8} in {Describe(method)} — the scan would under-report.", ex);
        }
    }

    /// <summary>The token operands of one body: method (<c>call</c> …), token (<c>ldtoken</c>) and string (<c>ldstr</c>).</summary>
    private static IEnumerable<(OperandType Kind, int Token)> TokenOperands(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var i = 0;
        while (i < il.Length)
        {
            OpCode op;
            if (il[i] == 0xFE)
            {
                if (i + 1 >= il.Length)
                {
                    throw new InvalidOperationException($"Truncated two-byte opcode in {Describe(method)} at IL_{i:X4}.");
                }

                op = TwoByte[il[i + 1]];
                i += 2;
            }
            else
            {
                op = OneByte[il[i]];
                i += 1;
            }

            if (op.Size == 0)
            {
                throw new InvalidOperationException($"Unknown opcode in {Describe(method)} at IL_{i - 1:X4}.");
            }

            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineString)
            {
                yield return (op.OperandType, BitConverter.ToInt32(il, i));
            }

            i += OperandSize(op, il, i);
        }
    }

    private static int OperandSize(OpCode op, byte[] il, int at) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
        _ => 4,
    };

    private static string Describe(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}
