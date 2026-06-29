namespace ACCcom.Core.Models;

/// <summary>
/// 协议描述格式 - 用于描述串口通讯协议的帧结构
/// </summary>
public class ProtocolSchema
{
    /// <summary>协议名称 (必填, 用作文件名)</summary>
    public string Name { get; set; } = "";
    
    /// <summary>协议描述 (可选)</summary>
    public string Description { get; set; } = "";
    
    /// <summary>协议类型: binary(二进制) | text(文本) | mixed(混合)</summary>
    public string Type { get; set; } = "binary";
    
    /// <summary>帧结构定义 (可选, 不填则不验证帧头帧尾)</summary>
    public FrameSchema? Frame { get; set; }
    
    /// <summary>公共字段 (所有命令都有的字段, 按offset排序)</summary>
    public List<FieldSchema> Fields { get; set; } = new();
    
    /// <summary>命令码映射 (key=命令码十六进制字符串, value=命令定义)</summary>
    public Dictionary<string, CommandSchema> Commands { get; set; } = new();
    
    /// <summary>最小帧长度 (可选, 用于快速校验)</summary>
    public int MinLength { get; set; } = 0;
    
    /// <summary>自动匹配配置 (可选)</summary>
    public AutoMatchConfig? AutoMatch { get; set; }
}

public class AutoMatchConfig
{
    /// <summary>是否启用自动匹配</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>匹配优先级 (数值越大优先级越高)</summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>帧头特征 (十六进制字符串，如 "A5 5A")</summary>
    public string? HeaderPattern { get; set; }
    
    /// <summary>命令码偏移 (用于多命令协议)</summary>
    public int? CommandOffset { get; set; }
    
    /// <summary>已知命令码列表 (十六进制)</summary>
    public string[]? KnownCommands { get; set; }
}

/// <summary>
/// 帧结构定义
/// </summary>
public class FrameSchema
{
    /// <summary>帧头字节 (十六进制字符串, 如 "A5 5A")</summary>
    public string? Header { get; set; }
    
    /// <summary>帧尾字节 (十六进制字符串, 如 "DD")</summary>
    public string? Footer { get; set; }
    
    /// <summary>长度字段定义 (可选)</summary>
    public LengthFieldSchema? LengthField { get; set; }
    
    /// <summary>校验字段定义 (可选)</summary>
    public ChecksumSchema? Checksum { get; set; }
    
    /// <summary>命令码字段位置 (可选, 用于分发)</summary>
    public CommandFieldSchema? CommandField { get; set; }
}

/// <summary>
/// 长度字段定义
/// </summary>
public class LengthFieldSchema
{
    /// <summary>字段偏移</summary>
    public int Offset { get; set; }
    
    /// <summary>字段长度 (1/2字节)</summary>
    public int Length { get; set; } = 1;
    
    /// <summary>长度值从哪字节开始计算 (0=帧头, 2=跳过帧头)</summary>
    public int IncludesStart { get; set; } = 0;
    
    /// <summary>长度包含哪些部分: "all"(整帧) | "header" | "payload" | "data"</summary>
    public string? IncludesWhat { get; set; }
}

/// <summary>
/// 校验字段定义
/// </summary>
public class ChecksumSchema
{
    /// <summary>校验类型: crc16 | sum8 | xor8 | sum16</summary>
    public string Type { get; set; } = "crc16";
    
    /// <summary>CRC算法: modbus | ccitt (仅crc16有效)</summary>
    public string Algorithm { get; set; } = "modbus";
    
    /// <summary>校验字段在帧中的位置: "end"(末尾) | "before_footer" | 数字偏移</summary>
    public string? Position { get; set; } = "end";
    
    /// <summary>校验范围: "all"(整帧除校验) | "data"(仅数据区) | "header_to_data"</summary>
    public string Range { get; set; } = "all";
}

/// <summary>
/// 命令码字段定义
/// </summary>
public class CommandFieldSchema
{
    /// <summary>字段偏移</summary>
    public int Offset { get; set; }
    
    /// <summary>字段长度 (1/2字节)</summary>
    public int Length { get; set; } = 1;
}

/// <summary>
/// 字段定义
/// </summary>
public class FieldSchema
{
    /// <summary>字段名称 (必填)</summary>
    public string Name { get; set; } = "";
    
    /// <summary>字段偏移</summary>
    public int Offset { get; set; }
    
    /// <summary>字段长度 (-1 表示动态长度, 到校验/帧尾前)</summary>
    public int Length { get; set; } = 1;
    
    /// <summary>字段类型: hex | uint8 | uint16 | uint32 | int8 | int16 | int32 | float | double | string | bcd | enum | bitfield</summary>
    public string Type { get; set; } = "uint8";
    
    /// <summary>单位 (可选): "°C", "W", "V", "A"</summary>
    public string? Unit { get; set; }
    
    /// <summary>固定值 (十六进制字符串, 用于验证): "A5 5A"</summary>
    public string? Value { get; set; }
    
    /// <summary>枚举映射 (可选): {"0x01": "开", "0x02": "关"}</summary>
    public Dictionary<string, string>? Values { get; set; }
    
    /// <summary>合法值范围 (可选): [16, 30]</summary>
    public int[]? Range { get; set; }
    
    /// <summary>默认颜色 (可选, 格式 "#RRGGBB")</summary>
    public string? Color { get; set; }
    
    /// <summary>字段描述 (可选)</summary>
    public string? Description { get; set; }
    
    /// <summary>大端序 (默认false=小端序)</summary>
    public bool BigEndian { get; set; } = false;
    
    /// <summary>格式化字符串 (可选, 如 "F1" 表示保留1位小数)</summary>
    public string? Format { get; set; }
}

/// <summary>
/// 命令定义
/// </summary>
public class CommandSchema
{
    /// <summary>命令名称</summary>
    public string Name { get; set; } = "";
    
    /// <summary>命令描述 (可选)</summary>
    public string? Description { get; set; }
    
    /// <summary>该命令的特有字段 (在公共字段之后解析)</summary>
    public List<FieldSchema> Fields { get; set; } = new();
}
