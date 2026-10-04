using System.Reflection;
using System.Reflection.Emit;

namespace Spaarke.ArchTests;

/// <summary>
/// The COMPILED counterpart of <see cref="SourceScan"/>: which methods a type's IL actually references.
/// </summary>
/// <remarks>
/// <para><b>Why IL, not text.</b> A text scan answers "does this file mention a call shaped like X"; it cannot know a
/// receiver's TYPE. <c>sp.GetRequiredService&lt;DataverseWebApiService&gt;().RevokeAccessAsync(…)</c> and a parameter of
/// the concrete client that happens to share a name with a seam field are both invisible to it (task 132 verifier seeds
/// S5 and S6). The compiler has already resolved every call to a method token, so reading those tokens answers the
/// question exactly, whatever the receiver expression, lambda, local function, async state machine or method-group
/// conversion wraps it.</para>
///
/// <para><b>What counts as a reference</b>: every instruction whose operand is a method token —
/// <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c>, <c>jmp</c>. Compiler-generated types
/// (closures, state machines) are attributed to their OUTERMOST declaring type, which is the type a reviewer reads.</para>
///
/// <para><b>Fails loud.</b> An IL stream that cannot be decoded, or a method token that cannot be resolved, throws — a
/// scanner that skipped what it could not read would under-report silently, which for a guard is worse than none.</para>
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

    /// <summary>The outermost declaring type — what a closure or state machine is attributed to.</summary>
    internal static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    /// <summary>
    /// Every method referenced from <paramref name="types"/>' own method and constructor bodies, as
    /// (the calling type, the calling method, the referenced method).
    /// </summary>
    internal static IEnumerable<(Type Caller, MethodBase CallerMethod, MethodBase Target)> MethodReferences(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            var members = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));
            foreach (var method in members)
            {
                foreach (var target in MethodReferences(method))
                {
                    yield return (type, method, target);
                }
            }
        }
    }

    private static IEnumerable<MethodBase> MethodReferences(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

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

            if (op.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, i);
                MethodBase? target;
                try
                {
                    target = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                }
                catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
                {
                    throw new InvalidOperationException(
                        $"Could not resolve method token 0x{token:X8} in {Describe(method)} — the scan would under-report.", ex);
                }

                if (target is not null)
                {
                    yield return target;
                }
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
