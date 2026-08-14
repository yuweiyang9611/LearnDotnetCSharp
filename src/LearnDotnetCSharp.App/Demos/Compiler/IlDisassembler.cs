using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;

namespace LearnDotnetCSharp.Demos.Compiler;

internal static class IlDisassembler
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

    static IlDisassembler()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode) || field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                SingleByteOpCodes[value] = opCode;
            }
            else if ((value & 0xff00) == 0xfe00)
            {
                MultiByteOpCodes[value & 0xff] = opCode;
            }
        }
    }

    public static IReadOnlyList<string> Disassemble(MethodBase method, int maximumInstructionCount)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumInstructionCount);

        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length == 0)
        {
            return ["<没有托管 IL 方法体>"];
        }

        var lines = new List<string>();
        var offset = 0;

        while (offset < il.Length && lines.Count < maximumInstructionCount)
        {
            var instructionOffset = offset;
            var opCode = ReadOpCode(il, ref offset);
            var operand = ReadOperand(il, ref offset, opCode, method);
            lines.Add($"IL_{instructionOffset:X4}: {opCode.Name,-13}{operand}");
        }

        if (offset < il.Length)
        {
            lines.Add($"... 其余 {il.Length - offset} 个 IL 字节已省略");
        }

        return lines;
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        var opCode = first == 0xfe
            ? MultiByteOpCodes[il[offset++]]
            : SingleByteOpCodes[first];

        if (opCode.Size == 0)
        {
            throw new InvalidOperationException($"无法识别 IL 操作码 0x{first:X2}。");
        }

        return opCode;
    }

    private static string ReadOperand(
        byte[] il,
        ref int offset,
        OpCode opCode,
        MethodBase owner)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                return string.Empty;

            case OperandType.ShortInlineI:
                return ((sbyte)il[offset++]).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case OperandType.InlineI:
                return ReadInt32(il, ref offset).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case OperandType.InlineI8:
                return ReadInt64(il, ref offset).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case OperandType.ShortInlineR:
                return ReadSingle(il, ref offset).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            case OperandType.InlineR:
                return ReadDouble(il, ref offset).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            case OperandType.ShortInlineVar:
                return FormatVariableOperand(opCode, il[offset++]);

            case OperandType.InlineVar:
                return FormatVariableOperand(opCode, ReadUInt16(il, ref offset));

            case OperandType.ShortInlineBrTarget:
                {
                    var delta = (sbyte)il[offset++];
                    return $"IL_{offset + delta:X4}";
                }

            case OperandType.InlineBrTarget:
                {
                    var delta = ReadInt32(il, ref offset);
                    return $"IL_{offset + delta:X4}";
                }

            case OperandType.InlineSwitch:
                return ReadSwitchTargets(il, ref offset);

            case OperandType.InlineString:
                {
                    var token = ReadInt32(il, ref offset);
                    return $"\"{ResolveString(owner.Module, token)}\"";
                }

            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineType:
            case OperandType.InlineTok:
                {
                    var token = ReadInt32(il, ref offset);
                    return ResolveMember(owner, token);
                }

            case OperandType.InlineSig:
                {
                    var token = ReadInt32(il, ref offset);
                    return ResolveSignature(owner.Module, token);
                }

            default:
                throw new NotSupportedException($"尚未实现 IL 操作数类型 {opCode.OperandType}。");
        }
    }

    private static string FormatVariableOperand(OpCode opCode, int index) =>
        IsArgumentOperand(opCode) ? $"arg.{index}" : $"V_{index}";

    private static bool IsArgumentOperand(OpCode opCode) =>
        opCode == OpCodes.Ldarg ||
        opCode == OpCodes.Ldarg_S ||
        opCode == OpCodes.Ldarga ||
        opCode == OpCodes.Ldarga_S ||
        opCode == OpCodes.Starg ||
        opCode == OpCodes.Starg_S;

    private static string ReadSwitchTargets(byte[] il, ref int offset)
    {
        var count = ReadInt32(il, ref offset);
        var deltasOffset = offset;
        var baseOffset = checked(offset + (count * sizeof(int)));
        var targets = new string[count];

        for (var index = 0; index < count; index++)
        {
            var delta = BinaryPrimitives.ReadInt32LittleEndian(
                il.AsSpan(deltasOffset + (index * sizeof(int)), sizeof(int)));
            targets[index] = $"IL_{baseOffset + delta:X4}";
        }

        offset = baseOffset;
        return $"({string.Join(", ", targets)})";
    }

    private static string ResolveString(Module module, int metadataToken)
    {
        try
        {
            return module.ResolveString(metadataToken);
        }
        catch (ArgumentException)
        {
            return $"token 0x{metadataToken:X8}";
        }
        catch (BadImageFormatException)
        {
            return $"token 0x{metadataToken:X8}";
        }
    }

    private static string ResolveMember(MethodBase owner, int metadataToken)
    {
        try
        {
            var typeArguments = owner.DeclaringType?.GetGenericArguments();
            var methodArguments = owner.IsGenericMethod ? owner.GetGenericArguments() : null;
            var member = owner.Module.ResolveMember(metadataToken, typeArguments, methodArguments);

            return member switch
            {
                Type type => type.FullName ?? type.Name,
                MethodBase method => $"{method.DeclaringType?.FullName}.{method.Name}",
                FieldInfo field => $"{field.DeclaringType?.FullName}.{field.Name}",
                _ => member?.ToString() ?? $"token 0x{metadataToken:X8}",
            };
        }
        catch (ArgumentException)
        {
            return $"token 0x{metadataToken:X8}";
        }
        catch (BadImageFormatException)
        {
            return $"token 0x{metadataToken:X8}";
        }
    }

    private static string ResolveSignature(Module module, int metadataToken)
    {
        try
        {
            return $"sig {Convert.ToHexString(module.ResolveSignature(metadataToken))}";
        }
        catch (ArgumentException)
        {
            return $"sig token 0x{metadataToken:X8}";
        }
        catch (BadImageFormatException)
        {
            return $"sig token 0x{metadataToken:X8}";
        }
    }

    private static ushort ReadUInt16(byte[] il, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        return value;
    }

    private static int ReadInt32(byte[] il, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static long ReadInt64(byte[] il, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(il.AsSpan(offset, sizeof(long)));
        offset += sizeof(long);
        return value;
    }

    private static float ReadSingle(byte[] il, ref int offset)
    {
        var bits = ReadInt32(il, ref offset);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static double ReadDouble(byte[] il, ref int offset)
    {
        var bits = ReadInt64(il, ref offset);
        return BitConverter.Int64BitsToDouble(bits);
    }
}
