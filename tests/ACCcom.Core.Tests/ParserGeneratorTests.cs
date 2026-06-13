using System.Threading.Tasks;
using ACCcom.Core.Services;
using ACCcom.Core.Models;
using Xunit;

namespace ACCcom.Core.Tests;

public class ParserGeneratorTests
{
    [Fact]
    public void Generate_SimpleFrame_GeneratesValidCsx()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_simple",
            MinLength = 5,
            Frame = new FrameSchema
            {
                Header = "AA 55",
                Checksum = new ChecksumSchema { Type = "xor8" }
            },
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "帧头", Offset = 0, Length = 2, Type = "hex", Value = "AA 55" },
                new FieldSchema { Name = "命令", Offset = 2, Length = 1, Type = "uint8" },
                new FieldSchema { Name = "数据", Offset = 3, Length = 1, Type = "uint8" }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("AA 55", csx);
        Assert.Contains("test_simple", csx);
        Assert.Contains("var fields = new List<FieldAnnotation>();", csx);
        Assert.Contains("return fields;", csx);
    }

    [Fact]
    public void Generate_WithCommands_GeneratesSwitchStatement()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_cmd",
            Frame = new FrameSchema
            {
                CommandField = new CommandFieldSchema { Offset = 2, Length = 1 }
            },
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "帧头", Offset = 0, Length = 2, Type = "hex" },
                new FieldSchema { Name = "命令", Offset = 2, Length = 1, Type = "uint8" }
            },
            Commands = new Dictionary<string, CommandSchema>
            {
                ["0x01"] = new CommandSchema { Name = "查询", Fields = new List<FieldSchema> { new FieldSchema { Name = "参数", Offset = 3, Length = 1, Type = "uint8" } } },
                ["0x02"] = new CommandSchema { Name = "设置", Fields = new List<FieldSchema> { new FieldSchema { Name = "值", Offset = 3, Length = 1, Type = "uint8" } } }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("switch (cmd)", csx);
        Assert.Contains("case 0x01:", csx);
        Assert.Contains("case 0x02:", csx);
    }

    [Fact]
    public async Task Generate_ModbusRtu_ProducesWorkingParser()
    {
        var schema = CreateModbusSchema();
        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        var engine = new ParserEngine();
        Assert.True(engine.Load(csx), $"Failed to load generated csx: {engine.LastError}");

        var data = new byte[] { 0x01, 0x03, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);
    }

    [Fact]
    public void Generate_EnumField_GeneratesSwitchExpression()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_enum",
            Fields = new List<FieldSchema>
            {
                new FieldSchema
                {
                    Name = "模式",
                    Offset = 0,
                    Length = 1,
                    Type = "enum",
                    Values = new Dictionary<string, string>
                    {
                        { "0x00", "Auto" },
                        { "0x01", "Cold" },
                        { "0x02", "Hot" }
                    }
                }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("switch", csx);
        Assert.Contains("0x00", csx);
        Assert.Contains("Auto", csx);
        Assert.Contains("Cold", csx);
        Assert.Contains("Hot", csx);
    }

    [Fact]
    public void Generate_FloatField_GeneratesToFloatCall()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_float",
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "温度", Offset = 0, Length = 4, Type = "float", Unit = "°C" }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("ToFloat(0, false)", csx);
        Assert.Contains("°C", csx);
    }

    [Fact]
    public void Validate_MissingName_ReturnsError()
    {
        var schema = new ProtocolSchema { Name = "" };
        var generator = new ParserGenerator();
        var (valid, errors) = generator.Validate(schema);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains("Name"));
    }

    [Fact]
    public void Validate_NoFields_ReturnsError()
    {
        var schema = new ProtocolSchema { Name = "test", Fields = new List<FieldSchema>() };
        var generator = new ParserGenerator();
        var (valid, errors) = generator.Validate(schema);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains("field"));
    }

    [Fact]
    public void Validate_ValidSchema_ReturnsSuccess()
    {
        var schema = new ProtocolSchema
        {
            Name = "valid",
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "test", Offset = 0, Length = 1, Type = "uint8" }
            }
        };

        var generator = new ParserGenerator();
        var (valid, errors) = generator.Validate(schema);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ParseJson_ValidJson_ReturnsSchema()
    {
        var json = @"{ ""name"": ""test"", ""type"": ""binary"" }";
        var generator = new ParserGenerator();
        var schema = generator.ParseJson(json);

        Assert.NotNull(schema);
        Assert.Equal("test", schema!.Name);
    }

    [Fact]
    public void ParseJson_InvalidJson_ReturnsNull()
    {
        var json = "not json";
        var generator = new ParserGenerator();
        var schema = generator.ParseJson(json);

        Assert.Null(schema);
    }

    [Fact]
    public void ParseJson_WithFields_ParsesCorrectly()
    {
        var json = @"{
            ""name"": ""test"",
            ""fields"": [
                { ""name"": ""帧头"", ""offset"": 0, ""length"": 2, ""type"": ""hex"" },
                { ""name"": ""数据"", ""offset"": 2, ""length"": 1, ""type"": ""uint8"" }
            ]
        }";
        var generator = new ParserGenerator();
        var schema = generator.ParseJson(json);

        Assert.NotNull(schema);
        Assert.Equal(2, schema!.Fields.Count);
        Assert.Equal("帧头", schema.Fields[0].Name);
        Assert.Equal(0, schema.Fields[0].Offset);
    }

    [Fact]
    public async Task Generate_FromJson_ProducesValidCsx()
    {
        var json = @"{
            ""name"": ""json_test"",
            ""minLength"": 4,
            ""fields"": [
                { ""name"": ""addr"", ""offset"": 0, ""length"": 1, ""type"": ""uint8"" },
                { ""name"": ""func"", ""offset"": 1, ""length"": 1, ""type"": ""uint8"" }
            ]
        }";

        var generator = new ParserGenerator();
        var csx = generator.GenerateFromJson(json);

        Assert.Contains("json_test", csx);
        Assert.Contains("var fields = new List<FieldAnnotation>();", csx);

        var engine = new ParserEngine();
        Assert.True(engine.Load(csx), $"Failed to load generated csx: {engine.LastError}");

        var data = new byte[] { 0x01, 0x03, 0x00, 0x01 };
        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.Equal(2, fields!.Count);
    }

    [Fact]
    public void Generate_StringField_GeneratesEncodingCall()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_string",
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "名称", Offset = 0, Length = 10, Type = "string" }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("Encoding.ASCII.GetString", csx);
        Assert.Contains("TrimEnd", csx);
    }

    [Fact]
    public void Generate_Sum16Checksum_GeneratesSum16Call()
    {
        var schema = new ProtocolSchema
        {
            Name = "test_sum16",
            MinLength = 4,
            Frame = new FrameSchema
            {
                Checksum = new ChecksumSchema { Type = "sum16" }
            },
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "数据", Offset = 0, Length = 2, Type = "uint16" }
            }
        };

        var generator = new ParserGenerator();
        var csx = generator.Generate(schema);

        Assert.Contains("Sum16", csx);
    }

    private ProtocolSchema CreateModbusSchema()
    {
        return new ProtocolSchema
        {
            Name = "modbus_rtu",
            Description = "Modbus RTU 协议",
            MinLength = 4,
            Frame = new FrameSchema
            {
                Checksum = new ChecksumSchema { Type = "crc16", Algorithm = "modbus", Range = "all" }
            },
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "设备地址", Offset = 0, Length = 1, Type = "uint8" },
                new FieldSchema { Name = "功能码", Offset = 1, Length = 1, Type = "uint8" }
            },
            Commands = new Dictionary<string, CommandSchema>
            {
                ["0x03"] = new CommandSchema { Name = "读保持寄存器", Fields = new List<FieldSchema> { new FieldSchema { Name = "寄存器数量", Offset = 4, Length = 2, Type = "uint16" } } },
                ["0x06"] = new CommandSchema { Name = "写单个寄存器", Fields = new List<FieldSchema> { new FieldSchema { Name = "寄存器值", Offset = 4, Length = 2, Type = "uint16" } } }
            }
        };
    }
}
