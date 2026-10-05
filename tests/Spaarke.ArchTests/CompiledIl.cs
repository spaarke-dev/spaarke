using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Spaarke.ArchTests;

/// <summary>
/// A small reader of COMPILED code (unified-access-control-r2 task 167 f2-v2, main-session round 52 item 2): every method
/// call, constructor call and method-pointer load in every method body of an assembly, with the member it targets — read with
/// <see cref="System.Reflection.Metadata"/> from the file, so no dependency is loaded and nothing runs. Every lambda, local
/// function and async state machine is an ordinary method body here, so a call cannot hide behind one.
/// </summary>
internal static class CompiledIl
{
    /// <summary>A referenced member: <c>Type</c> is the declaring type's full name ("Ns.Outer+Inner"; a generic instantiation
    /// is named by its definition), <c>Assembly</c> the assembly that defines it, <c>Member</c> its name.</summary>
    internal sealed record MemberRef(string Type, string Assembly, string Member);

    /// <summary>One call-like instruction inside <c>Method</c> ("Type::Name") of <c>File</c>.</summary>
    internal sealed record Use(string File, string Method, string OpCode, MemberRef? Target);

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static readonly IReadOnlySet<string> CallOpCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "call", "callvirt", "newobj", "ldftn", "ldvirtftn", "jmp",
    };

    /// <summary>Every call-like instruction in every method body of the assembly at <paramref name="path"/>.</summary>
    internal static List<Use> Uses(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var file = Path.GetFileName(path);
        var self = reader.GetString(reader.GetAssemblyDefinition().Name);
        var uses = new List<Use>();

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;   // abstract, extern, interface member
            }

            var owner = $"{TypeName(reader, method.GetDeclaringType())}::{reader.GetString(method.Name)}";
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            while (il.RemainingBytes > 0)
            {
                short value = il.ReadByte();
                if (value == 0xFE)
                {
                    value = (short)(0xFE00 | il.ReadByte());
                }

                if (!OpCodesByValue.TryGetValue(value, out var op))
                {
                    throw new InvalidOperationException($"{file} {owner}: unknown IL opcode 0x{value:X}");
                }

                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        il.ReadByte();
                        break;
                    case OperandType.InlineVar:
                        il.ReadInt16();
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        il.ReadInt64();
                        break;
                    case OperandType.InlineSwitch:
                        var count = il.ReadInt32();
                        for (var i = 0; i < count; i++)
                        {
                            il.ReadInt32();
                        }

                        break;
                    case OperandType.InlineMethod:
                    {
                        var token = il.ReadInt32();
                        if (CallOpCodes.Contains(op.Name!))
                        {
                            uses.Add(new Use(file, owner, op.Name!, Resolve(reader, self, MetadataTokens.EntityHandle(token))));
                        }

                        break;
                    }

                    default:
                        il.ReadInt32();   // the other 4-byte operands: tokens, strings, branch targets, ShortInlineR
                        break;
                }
            }
        }

        return uses;
    }

    private static MemberRef? Resolve(MetadataReader reader, string self, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var m = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return new MemberRef(TypeName(reader, m.GetDeclaringType()), self, reader.GetString(m.Name));
            }

            case HandleKind.MemberReference:
            {
                var m = reader.GetMemberReference((MemberReferenceHandle)handle);
                var (type, assembly) = EntityType(reader, self, m.Parent);
                return new MemberRef(type, assembly, reader.GetString(m.Name));
            }

            case HandleKind.MethodSpecification:
                return Resolve(reader, self, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            default:
                return null;
        }
    }

    /// <summary>The full name and defining assembly of a type handle; a generic instantiation resolves to its definition.</summary>
    private static (string Type, string Assembly) EntityType(MetadataReader reader, string self, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return (TypeName(reader, (TypeDefinitionHandle)handle), self);
            case HandleKind.TypeReference:
            {
                var r = reader.GetTypeReference((TypeReferenceHandle)handle);
                var name = reader.GetString(r.Name);
                var ns = reader.GetString(r.Namespace);
                switch (r.ResolutionScope.Kind)
                {
                    case HandleKind.TypeReference:
                    {
                        var (outer, assembly) = EntityType(reader, self, (EntityHandle)r.ResolutionScope);
                        return ($"{outer}+{name}", assembly);
                    }

                    case HandleKind.AssemblyReference:
                    {
                        var assembly = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)r.ResolutionScope).Name);
                        return (ns.Length == 0 ? name : $"{ns}.{name}", assembly);
                    }

                    default:
                        return (ns.Length == 0 ? name : $"{ns}.{name}", self);
                }
            }

            case HandleKind.TypeSpecification:
            {
                var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (blob.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
                {
                    blob.ReadSignatureTypeCode();   // CLASS or VALUETYPE
                    return EntityType(reader, self, blob.ReadTypeHandle());
                }

                return ("<type-spec>", "?");
            }

            default:
                return ("<unknown>", "?");
        }
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{TypeName(reader, declaring)}+{name}";
        }

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}
