using System.Text;
using System.Text.Json;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// 协议解析器生成器 - 从 ProtocolSchema 生成 .csx 解析器代码
/// </summary>
public class ParserGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 从 JSON 字符串反序列化
    /// </summary>
    public ProtocolSchema? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProtocolSchema>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 验证 Schema 是否合法
    /// </summary>
    public (bool valid, List<string> errors) Validate(ProtocolSchema schema)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(schema.Name))
            errors.Add("Name is required");

        if (schema.Fields == null)
            schema.Fields = new List<FieldSchema>();
        
        if (schema.Fields.Count == 0)
            errors.Add("At least one field is required");

        if (schema.Frame?.CommandField != null && schema.Commands.Count == 0)
            errors.Add("CommandField defined but no Commands provided");

        if (schema.Commands.Count > 0 && schema.Frame?.CommandField == null)
            errors.Add("Commands defined but no CommandField specified");

        var offsets = schema.Fields.Select(f => f.Offset).ToList();
        if (offsets.Distinct().Count() != offsets.Count)
            errors.Add("Duplicate field offsets detected");

        return (errors.Count == 0, errors);
    }

    /// <summary>
    /// 从 ProtocolSchema 生成 .csx 代码
    /// </summary>
    public string Generate(ProtocolSchema schema)
    {
        var sb = new StringBuilder();

        GenerateHeader(sb, schema);
        GenerateFrameValidation(sb, schema);
        GenerateFieldParsing(sb, schema);
        GenerateCommandDispatch(sb, schema);
        GenerateChecksum(sb, schema);
        GenerateFooterValidation(sb, schema);

        sb.AppendLine();
        sb.AppendLine("return fields;");

        return sb.ToString();
    }

    /// <summary>
    /// 从 JSON 字符串生成
    /// </summary>
    public string GenerateFromJson(string json)
    {
        var schema = ParseJson(json);
        if (schema == null)
            throw new InvalidOperationException("Invalid JSON schema");
        return Generate(schema);
    }

    private void GenerateHeader(StringBuilder sb, ProtocolSchema schema)
    {
        sb.AppendLine($"// 自动生成 - {schema.Name}");
        if (!string.IsNullOrEmpty(schema.Description))
            sb.AppendLine($"// {schema.Description}");
        sb.AppendLine($"// 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("var fields = new List<FieldAnnotation>();");
    }

    private void GenerateFrameValidation(StringBuilder sb, ProtocolSchema schema)
    {
        if (schema.MinLength > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"if (RawData.Length < {schema.MinLength})");
            sb.AppendLine("{");
            sb.AppendLine($"    fields.Add(new FieldAnnotation {{ Name = \"错误\", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = \"帧长度不足{schema.MinLength}字节\", Color = \"#EF5350\", Severity = FieldSeverity.Error }});");
            sb.AppendLine("    return fields;");
            sb.AppendLine("}");
        }

        if (schema.Frame?.Header != null)
        {
            var headerBytes = schema.Frame.Header.Replace(" ", "");
            var headerVal = $"0x{headerBytes}";
            sb.AppendLine();
            sb.AppendLine($"var header = ToUInt16(0, true);");
            sb.AppendLine($"bool headerOk = header == {headerVal};");
            sb.AppendLine("fields.Add(new FieldAnnotation");
            sb.AppendLine("{");
            sb.AppendLine("    Name = \"帧头\", Offset = 0, Length = 2,");
            sb.AppendLine("    RawHex = RawHex(0, 2),");
            sb.AppendLine($"    DisplayValue = headerOk ? \"{schema.Frame.Header} (正确)\" : $\"0x{{header:X4}} (期望{schema.Frame.Header})\",");
            sb.AppendLine("    Color = headerOk ? \"#22C55E\" : \"#EF5350\",");
            sb.AppendLine("    Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error");
            sb.AppendLine("});");
        }
    }

    private void GenerateFieldParsing(StringBuilder sb, ProtocolSchema schema)
    {
        var fields = schema.Fields.OrderBy(f => f.Offset).ToList();
        foreach (var field in fields)
        {
            sb.AppendLine();
            GenerateSingleField(sb, field, "");
        }
    }

    private void GenerateSingleField(StringBuilder sb, FieldSchema field, string indent)
    {
        switch (field.Type.ToLower())
        {
            case "hex":
                GenerateHexField(sb, field, indent);
                break;
            case "uint8":
                GenerateUInt8Field(sb, field, indent);
                break;
            case "uint16":
                GenerateUInt16Field(sb, field, indent);
                break;
            case "uint32":
                GenerateUInt32Field(sb, field, indent);
                break;
            case "int8":
                GenerateInt8Field(sb, field, indent);
                break;
            case "int16":
                GenerateInt16Field(sb, field, indent);
                break;
            case "int32":
                GenerateInt32Field(sb, field, indent);
                break;
            case "float":
                GenerateFloatField(sb, field, indent);
                break;
            case "double":
                GenerateDoubleField(sb, field, indent);
                break;
            case "string":
                GenerateStringField(sb, field, indent);
                break;
            case "bcd":
                GenerateBcdField(sb, field, indent);
                break;
            case "enum":
                GenerateEnumField(sb, field, indent);
                break;
            case "bitfield":
                GenerateBitfieldField(sb, field, indent);
                break;
            default:
                sb.AppendLine($"{indent}// 未知类型: {field.Type}");
                sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = {field.Length}, RawHex = RawHex({field.Offset}, {field.Length}), DisplayValue = $\"{{RawData[{field.Offset}]:X2}}\" }});");
                break;
        }
    }

    private void GenerateHexField(StringBuilder sb, FieldSchema field, string indent)
    {
        var hexStr = field.Value ?? "";
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = {field.Length}, RawHex = RawHex({field.Offset}, {field.Length}), DisplayValue = \"{hexStr}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateUInt8Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 1, RawHex = RawHex({field.Offset}, 1), DisplayValue = $\"{{RawData[{field.Offset}]}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateUInt16Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToUInt16({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 2, RawHex = RawHex({field.Offset}, 2), DisplayValue = $\"{{{varName}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateUInt32Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToUInt32({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 4, RawHex = RawHex({field.Offset}, 4), DisplayValue = $\"{{{varName}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateInt8Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 1, RawHex = RawHex({field.Offset}, 1), DisplayValue = $\"{{(sbyte)RawData[{field.Offset}]}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateInt16Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToInt16({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 2, RawHex = RawHex({field.Offset}, 2), DisplayValue = $\"{{{varName}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateInt32Field(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToInt32({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 4, RawHex = RawHex({field.Offset}, 4), DisplayValue = $\"{{{varName}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateFloatField(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var fmt = string.IsNullOrEmpty(field.Format) ? "F2" : field.Format;
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToFloat({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 4, RawHex = RawHex({field.Offset}, 4), DisplayValue = $\"{{{varName}:{fmt}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateDoubleField(StringBuilder sb, FieldSchema field, string indent)
    {
        var be = field.BigEndian ? "true" : "false";
        var fmt = string.IsNullOrEmpty(field.Format) ? "F2" : field.Format;
        var unit = string.IsNullOrEmpty(field.Unit) ? "" : $" {field.Unit}";
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = ToDouble({field.Offset}, {be});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 8, RawHex = RawHex({field.Offset}, 8), DisplayValue = $\"{{{varName}:{fmt}}}{unit}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateStringField(StringBuilder sb, FieldSchema field, string indent)
    {
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = System.Text.Encoding.ASCII.GetString(RawData, {field.Offset}, {field.Length}).TrimEnd('\\0');");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = {field.Length}, RawHex = RawHex({field.Offset}, {field.Length}), DisplayValue = {varName}, Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateBcdField(StringBuilder sb, FieldSchema field, string indent)
    {
        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = FromBcd({field.Offset}, {field.Length});");
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = {field.Length}, RawHex = RawHex({field.Offset}, {field.Length}), DisplayValue = $\"{{{varName}}}\", Color = \"{field.Color ?? "#3478F6"}\" }});");
    }

    private void GenerateEnumField(StringBuilder sb, FieldSchema field, string indent)
    {
        if (field.Values == null || field.Values.Count == 0)
        {
            GenerateUInt8Field(sb, field, indent);
            return;
        }

        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = RawData[{field.Offset}];");
        sb.AppendLine($"{indent}string {varName}_name = {varName} switch");
        sb.AppendLine($"{indent}{{");
        foreach (var kv in field.Values)
        {
            sb.AppendLine($"{indent}    {kv.Key} => \"{kv.Value}\",");
        }
        sb.AppendLine($"{indent}    _ => $\"未知(0x{{{varName}:X2}})\"");
        sb.AppendLine($"{indent}}};");

        var color = field.Color ?? "#3478F6";
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 1, RawHex = RawHex({field.Offset}, 1), DisplayValue = $\"0x{{{varName}:X2}} ({{{varName}_name}})\", Color = \"{color}\" }});");
    }

    private void GenerateBitfieldField(StringBuilder sb, FieldSchema field, string indent)
    {
        if (field.Values == null || field.Values.Count == 0)
        {
            GenerateUInt8Field(sb, field, indent);
            return;
        }

        var varName = field.Name.Replace(" ", "_");
        sb.AppendLine($"{indent}var {varName} = RawData[{field.Offset}];");
        sb.AppendLine($"{indent}var {varName}_bits = new System.Collections.Generic.List<string>();");
        foreach (var kv in field.Values)
        {
            var bitStr = kv.Key.Replace("0x", "");
            if (int.TryParse(bitStr, System.Globalization.NumberStyles.HexNumber, null, out var bitNum))
            {
                sb.AppendLine($"{indent}if (({varName} & {kv.Key}) != 0) {varName}_bits.Add(\"{kv.Value}\");");
            }
        }

        var color = field.Color ?? "#3478F6";
        sb.AppendLine($"{indent}fields.Add(new FieldAnnotation {{ Name = \"{field.Name}\", Offset = {field.Offset}, Length = 1, RawHex = RawHex({field.Offset}, 1), DisplayValue = {varName}_bits.Count > 0 ? string.Join(\", \", {varName}_bits) : \"无\", Color = \"{color}\" }});");
    }

    private void GenerateCommandDispatch(StringBuilder sb, ProtocolSchema schema)
    {
        if (schema.Commands.Count == 0 || schema.Frame?.CommandField == null)
            return;

        var cf = schema.Frame.CommandField;

        sb.AppendLine();
        sb.AppendLine("// ===== 命令码解析 =====");
        if (cf.Length == 1)
        {
            sb.AppendLine($"var cmd = RawData[{cf.Offset}];");
        }
        else
        {
            sb.AppendLine($"var cmd = ToUInt16({cf.Offset}, false);");
        }

        sb.AppendLine("switch (cmd)");
        sb.AppendLine("{");

        foreach (var kv in schema.Commands)
        {
            var cmdSchema = kv.Value;
            sb.AppendLine($"    case {kv.Key}: // {cmdSchema.Name}");
            sb.AppendLine("    {");

            if (cmdSchema.Fields.Count > 0)
            {
                foreach (var field in cmdSchema.Fields)
                {
                    GenerateSingleField(sb, field, "        ");
                }
            }
            else
            {
                sb.AppendLine($"        // {cmdSchema.Name} - 无额外字段");
            }

            sb.AppendLine("        break;");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
    }

    private void GenerateChecksum(StringBuilder sb, ProtocolSchema schema)
    {
        if (schema.Frame?.Checksum == null)
            return;

        var cs = schema.Frame.Checksum;
        var csFunc = cs.Type.ToLower() switch
        {
            "crc16" => string.Equals(cs.Algorithm, "ccitt", StringComparison.OrdinalIgnoreCase) ? "Crc16Ccitt" : "Crc16",
            "sum8" => "Sum8",
            "xor8" => "Xor8",
            "sum16" => "Sum16",
            _ => "Crc16"
        };

        var csName = cs.Type.ToUpper();
        sb.AppendLine();
        sb.AppendLine($"// ===== {csName} 校验 =====");
        sb.AppendLine($"var receivedCs = {(csFunc == "Sum8" || csFunc == "Xor8" ? $"(ushort)RawData[RawData.Length - 1]" : $"ToUInt16(RawData.Length - 2, false)")};");
        sb.AppendLine($"var calcCs = {csFunc}(0, RawData.Length - {(csFunc == "Sum8" || csFunc == "Xor8" ? 1 : 2)});");
        sb.AppendLine("bool csOk = receivedCs == calcCs;");
        sb.AppendLine("fields.Add(new FieldAnnotation");
        sb.AppendLine("{");
        sb.AppendLine($"    Name = \"{csName}\", Offset = RawData.Length - {(csFunc == "Sum8" || csFunc == "Xor8" ? 1 : 2)}, Length = {(csFunc == "Sum8" || csFunc == "Xor8" ? 1 : 2)},");
        sb.AppendLine($"    RawHex = RawHex(RawData.Length - {(csFunc == "Sum8" || csFunc == "Xor8" ? 1 : 2)}, {(csFunc == "Sum8" || csFunc == "Xor8" ? 1 : 2)}),");
        sb.AppendLine("    DisplayValue = csOk ? $\"0x{receivedCs:X4} (校验通过)\" : $\"0x{receivedCs:X4} (计算值=0x{calcCs:X4})\",");
        sb.AppendLine("    Color = csOk ? \"#22C55E\" : \"#EF5350\",");
        sb.AppendLine("    Severity = csOk ? FieldSeverity.Normal : FieldSeverity.Error");
        sb.AppendLine("});");
    }

    private void GenerateFooterValidation(StringBuilder sb, ProtocolSchema schema)
    {
        if (schema.Frame?.Footer == null)
            return;

        var footerBytes = schema.Frame.Footer.Replace(" ", "");
        var footerVal = $"0x{footerBytes}";
        sb.AppendLine();
        sb.AppendLine("// ===== 帧尾验证 =====");
        sb.AppendLine($"var footer = RawData[RawData.Length - 1];");
        sb.AppendLine($"bool footerOk = footer == {footerVal};");
        sb.AppendLine("fields.Add(new FieldAnnotation");
        sb.AppendLine("{");
        sb.AppendLine("    Name = \"帧尾\", Offset = RawData.Length - 1, Length = 1,");
        sb.AppendLine($"    RawHex = RawHex(RawData.Length - 1, 1),");
        sb.AppendLine($"    DisplayValue = footerOk ? \"{schema.Frame.Footer} (正确)\" : $\"0x{{footer:X2}} (期望{schema.Frame.Footer})\",");
        sb.AppendLine("    Color = footerOk ? \"#22C55E\" : \"#EF5350\",");
        sb.AppendLine("    Severity = footerOk ? FieldSeverity.Normal : FieldSeverity.Error");
        sb.AppendLine("});");
    }
}
