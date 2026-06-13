# 协议 → 解析器 操作指南

将一份设备协议文档变成 ACCCOM 可用的解析脚本，有 **3 种路径**，按难度从低到高排列。

---

## 目录

- [前置准备](#前置准备)
- [路径 A：JSON Schema → 自动生成（推荐）](#路径-ajson-schema--自动生成推荐)
- [路径 B：MCP AI 帮你写（在 AI 客户端内操作）](#路径-bmcp-ai-帮你写在-ai-客户端内操作)
- [路径 C：手写 .csx 脚本](#路径-c手写-csx-脚本)
- [离线测试解析结果](#离线测试解析结果)
- [激活并查看效果](#激活并查看效果)
- [常见问题](#常见问题)
- [参考](#参考)

---

## 前置准备

你手上必须有一份协议文档，至少包含：

- **帧头**（固定字节，如 `AA 55`）
- **各字段的偏移、长度、类型**（如偏移 2 的 1 字节 uint8 是"温度"）
- **校验方式**（CRC16 / Sum8 / Xor8 / 无），可选
- **帧尾**（固定字节，如 `FF`），可选

---

## 路径 A：JSON Schema → 自动生成（推荐）

无需写代码，写好 JSON 描述，工具自动生成解析脚本。

### 第 1 步：填写协议 Schema JSON

参照模板（模版内容可通过 MCP 工具 `acccom_get_schema_template` 获取或查看底部参考）：

```json
{
  "name": "my_device",
  "description": "我的设备协议",
  "type": "binary",
  "minLength": 6,
  "frame": {
    "header": "AA 55",
    "lengthField": { "offset": 2, "length": 1 },
    "commandField": { "offset": 3, "length": 1 },
    "checksum": { "type": "xor8" },
    "footer": "FF"
  },
  "fields": [
    { "name": "帧头",   "offset": 0, "length": 2, "type": "hex",   "value": "AA 55" },
    { "name": "长度",   "offset": 2, "length": 1, "type": "uint8" },
    { "name": "命令",   "offset": 3, "length": 1, "type": "uint8" },
    { "name": "数据",   "offset": 4, "length": 1, "type": "uint8" },
    { "name": "校验",   "offset": 5, "length": 1, "type": "uint8" },
    { "name": "帧尾",   "offset": 6, "length": 1, "type": "hex",   "value": "FF" }
  ]
}
```

支持的字段类型：`hex`、`uint8`、`uint16`、`uint32`、`int8`、`int16`、`int32`、`float`、`double`、`string`、`bcd`、`enum`、`bitfield`。

### 第 2 步：生成解析器（二选一）

#### 方式 A1：在 AI 客户端中使用 `generate_parser` 工具

```json
acccom_generate_parser({
  "schemaJson": "{...上面的 JSON...}",
  "name": "my_device"
})
```

工具会验证 JSON → 生成 `.csx` → 保存到 `parsers/` 目录 → 返回生成的脚本代码。

#### 方式 A2：直接运行桌面客户端，通过 HTTP API

```bash
# 先写入 schema JSON 文件
echo "{...上面的 JSON...}" > schema.json

# 调用 HTTP API 生成
curl -X POST http://127.0.0.1:8899/api/parser/generate `
  -H "Content-Type: application/json" `
  -d (Get-Content schema.json -Raw)
```

### 第 3 步：验证生成是否成功

调用 `list_parsers`，列表中应出现 `my_device`：

```json
acccom_list_parsers()
// 返回 → { "parsers": ["my_device", "sample", ...], "active": null }
```

---

## 路径 B：MCP AI 帮你写（在 AI 客户端内操作）

把协议文档丢给 AI，让 AI 直接写脚本。

### 第 1 步：把协议文档提供给 AI

直接说出帧结构，例如：

```
帮我写一个 ACCCOM 解析脚本，协议如下：
帧头 AA 55，长度在偏移 2（1 字节），命令在偏移 3，
温度在偏移 4（1 字节 uint8，单位 °C），校验 Xor8(0~5)
```

### 第 2 步：AI 调用 `write_parser` 写入脚本

AI 会生成 `.csx` 代码并通过 MCP 工具写入：

```json
acccom_write_parser({
  "name": "my_device",
  "code": "var result = new List<FieldAnnotation>();\n..."
})
```

### 第 3 步：AI 激活解析器

```json
acccom_activate_parser({ "name": "my_device" })
```

---

## 路径 C：手写 .csx 脚本

适合协议结构复杂、或者想完全自主定制的场景。

### 第 1 步：复制模板

```bash
# 方法 1：直接复制 sample.csx 改名
cp ACCcom.Core/parsers/sample.csx ACCcom.Core/parsers/my_device.csx

# 方法 2：用 HTTP API 读取模板
curl http://127.0.0.1:8899/api/parser/read?name=sample
```

### 第 2 步：修改字段定义

编辑 `my_device.csx`，脚本中可以直接使用这些内置函数：

| 函数 | 说明 |
|------|------|
| `RawHex(offset, length)` | 取指定范围的 Hex 字符串 |
| `ToUInt16(offset, bigEndian)` | 取 uint16 |
| `ToInt16(offset, bigEndian)` | 取 int16 |
| `ToUInt32(offset, bigEndian)` | 取 uint32 |
| `ToInt32(offset, bigEndian)` | 取 int32 |
| `ToFloat(offset, bigEndian)` | 取 float (4字节) |
| `ToDouble(offset, bigEndian)` | 取 double (8字节) |
| `FromBcd(offset, length)` | 取 BCD 值 |
| `Crc16(offset, length)` | CRC16 (Modbus) |
| `Crc16Ccitt(offset, length)` | CRC16 (CCITT) |
| `Sum8(offset, length)` | 和校验 |
| `Xor8(offset, length)` | 异或校验 |
| `Sum16(offset, length)` | 16位和校验 |

脚本输出 `List<FieldAnnotation>`，每个字段包含：`Name` / `Offset` / `Length` / `RawHex` / `DisplayValue` / `Color` / `Severity`。

### 第 3 步：写入并激活

#### 方式 C1：HTTP API

```bash
# 读取文件内容
$code = Get-Content .\parsers\my_device.csx -Raw

# 写入
curl -X POST http://127.0.0.1:8899/api/parser/write `
  -H "Content-Type: application/json" `
  -d "{\"name\":\"my_device\",\"code\":$(ConvertTo-Json $code)}"

# 激活
curl -X POST http://127.0.0.1:8899/api/parser/activate `
  -H "Content-Type: application/json" `
  -d '{"name":"my_device"}'
```

#### 方式 C2：直接保存到 parsers/ 目录（热加载自动生效）

```bash
# 直接把文件放到 parsers/ 目录下
code ACCcom.Core/parsers/my_device.csx    # 编辑保存

# 自动热加载（500ms 内生效），不需要重启
```

启动桌面客户端，在 UI 解析器下拉菜单中选择 `my_device` 即可。

---

## 离线测试解析结果

### 方式 1：MCP 工具

```json
acccom_parse_raw({
  "hex": "AA 55 06 01 19 2E",
  "parserName": "my_device"
})
// 返回所有解析出的字段
```

### 方式 2：HTTP API

```bash
curl -X POST http://127.0.0.1:8899/api/parser/parse-raw `
  -H "Content-Type: application/json" `
  -d '{"hex":"AA 55 06 01 19 2E","parserName":"my_device"}'
```

### 方式 3：UI 桌面端

打开 ACCCOM → 连接串口 → 下拉选择解析器 → 选中接收到的数据条目 → 查看右侧"字段解析"面板。

---

## 激活并查看效果

| 方式 | 操作 |
|------|------|
| MCP | `acccom_activate_parser({ "name": "my_device" })` |
| HTTP | `curl -X POST ... -d '{"name":"my_device"}'` |
| UI 桌面端 | 在下拉菜单选择解析器名字 |

激活后，所有后续 RX 数据会自动经解析器处理，条目显示字段解析结果。

如果要停用：`activate_parser(null)` 或 UI 中选择"无"。

---

## 常见问题

**Q：解析器写好了但看不到字段解析？**
→ 检查解析器是否已激活（`list_parsers` 看 active 字段）
→ 检查数据是否对方向（只有 RX 数据会被解析）
→ 检查 `.csx` 是否有语法错误（看 `ParserEngine.LastError`）

**Q：脚本修改后需要重启？**
→ 不需要。`FileSystemWatcher` 自动检测文件变化 → 防抖 500ms → 重新编译 → 即时生效。

**Q：如何调试脚本？**
→ 用 `parse_raw` 离线测试（无需串口），逐步调整字段偏移和类型。
→ 查看 Color/Severity：绿色=正常，红色=错误/异常。

**Q：如何限制只在某个命令码下解析子字段？**
→ 在 JSON schema 的 `commands` 段添加命令码及其子字段，或手写 `switch(cmd)` 分支。

---

## 参考

### 文件位置

| 文件 | 说明 |
|------|------|
| `docs/design/2026-06-09-acccom-parser-engine-design.md` | 解析引擎设计文档 |
| `docs/plan/2026-06-09-acccom-parser-engine.md` | 实施计划 |
| `src/ACCcom.Core/parsers/sample.csx` | 完整示例脚本（温控仪协议） |
| `src/ACCcom.Core/parsers/modbus_rtu_template.csx` | Modbus RTU 模板 |
| `src/ACCcom.Core/parsers/simple_frame_template.csx` | 简单帧结构模板 |
| `src/ACCcom.Core/Services/ParserGenerator.cs` | JSON Schema → .csx 生成器 |
| `src/ACCcom.Core/Services/ParserEngine.cs` | 脚本引擎（Roslyn 编译+执行） |
| `src/ACCcom.Core/Services/ScriptGlobals.cs` | 脚本运行时辅助函数 |
| `src/ACCcom.Core/Models/FieldAnnotation.cs` | 输出字段模型 |
| `src/ACCcom.Core/Models/ProtocolSchema.cs` | 协议 Schema 模型 |

### 完整 Schema JSON 模板

```json
{
  "name": "my_protocol",
  "description": "My custom protocol",
  "type": "binary",
  "minLength": 6,
  "frame": {
    "header": "AA 55",
    "footer": "FF",
    "lengthField": { "offset": 2, "length": 1 },
    "commandField": { "offset": 3, "length": 1 },
    "checksum": { "type": "crc16", "algorithm": "modbus" }
  },
  "fields": [
    { "name": "帧头", "offset": 0, "length": 2, "type": "hex", "value": "AA 55" },
    { "name": "长度", "offset": 2, "length": 1, "type": "uint8" },
    { "name": "命令", "offset": 3, "length": 1, "type": "enum",
      "values": { "0x01": "查询", "0x02": "设置" } },
    { "name": "数据", "offset": 4, "length": 1, "type": "uint8" }
  ],
  "commands": {
    "0x01": {
      "name": "查询命令",
      "fields": [
        { "name": "查询类型", "offset": 4, "length": 1, "type": "enum",
          "values": { "0x01": "温度", "0x02": "状态" } }
      ]
    }
  }
}
```

*`acccom_get_schema_template()` 可在线获取此模板。*
