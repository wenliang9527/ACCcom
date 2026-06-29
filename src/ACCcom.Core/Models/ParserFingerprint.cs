namespace ACCcom.Core.Models;

public class ParserFingerprint
{
    public string Name { get; set; } = "";
    public string? HeaderHex { get; set; }
    public int HeaderLength { get; set; }
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public int CommandOffset { get; set; } = -1;
    public byte[] CommandValues { get; set; } = Array.Empty<byte>();
    public int Priority { get; set; }

    public bool Matches(byte[] data)
    {
        if (data.Length < MinLength)
            return false;

        if (MaxLength > 0 && data.Length > MaxLength)
            return false;

        if (!string.IsNullOrEmpty(HeaderHex) && HeaderLength > 0)
        {
            if (data.Length < HeaderLength)
                return false;

            var dataHeader = Convert.ToHexString(data, 0, HeaderLength);
            if (!dataHeader.Equals(HeaderHex, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (CommandOffset >= 0 && CommandValues.Length > 0)
        {
            if (data.Length <= CommandOffset)
                return false;

            var cmdByte = data[CommandOffset];
            if (!CommandValues.Contains(cmdByte))
                return false;
        }

        return true;
    }

    public static ParserFingerprint FromSchema(ProtocolSchema schema)
    {
        var fp = new ParserFingerprint
        {
            Name = schema.Name,
            MinLength = schema.MinLength,
            Priority = schema.AutoMatch?.Priority ?? 0
        };

        if (schema.AutoMatch != null)
        {
            if (!string.IsNullOrEmpty(schema.AutoMatch.HeaderPattern))
            {
                fp.HeaderHex = schema.AutoMatch.HeaderPattern.Replace(" ", "");
                fp.HeaderLength = fp.HeaderHex.Length / 2;
            }

            if (schema.AutoMatch.CommandOffset.HasValue)
            {
                fp.CommandOffset = schema.AutoMatch.CommandOffset.Value;
            }

            if (schema.AutoMatch.KnownCommands != null && schema.AutoMatch.KnownCommands.Length > 0)
            {
                fp.CommandValues = schema.AutoMatch.KnownCommands
                    .Select(c => Convert.ToByte(c.Replace("0x", ""), 16))
                    .ToArray();
            }
        }
        else if (schema.Frame?.Header != null)
        {
            fp.HeaderHex = schema.Frame.Header.Replace(" ", "");
            fp.HeaderLength = fp.HeaderHex.Length / 2;
        }

        if (schema.Frame?.CommandField != null)
        {
            fp.CommandOffset = schema.Frame.CommandField.Offset;
            fp.CommandValues = schema.Commands.Keys
                .Select(k => Convert.ToByte(k.Replace("0x", ""), 16))
                .ToArray();
        }

        return fp;
    }
}
