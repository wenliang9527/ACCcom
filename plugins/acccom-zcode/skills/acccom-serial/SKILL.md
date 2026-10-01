---
name: acccom-serial
description: Use when operating serial ports through the ACCCOM toolchain — ACCCOM MCP tools (list_ports/open_port/send/read_data/wait_for_response/send_and_wait/clear_buffer), the DeepSeek Harness serial-panel HTTP API (/api/acccom-serial*), or the ACCCOM desktop app. Covers which layer to use for which question (live buffer vs flight-recorder log vs desktop GUI), the clean-repro workflow, hex/byte-count conventions, and port discipline (COM exclusivity, reserved ports). 触发词:串口、COM 口、发卡、读卡、刷机联调、MCP 串口工具、ACCCOM。
---

# ACCCOM 串口工具链(AI 操作手册)

ACCCOM 是本机的串口调试平台,对 AI 暴露三层接口。本技能只管**判断层**:
什么问题用哪层、按什么流程操作、有哪些纪律。API 参数细节不要凭记忆猜——
现场拉取权威文档(见下)。

## 三层接口总览

| 层 | 入口 | 回答的问题 |
|----|------|-----------|
| **行动层** | ACCCOM MCP 工具(本环境以 `mcp__acccom__*` 或 DSH 挂载的 MCP 形式提供) | "帮我**做**:开端口、发数据、读响应、等匹配" |
| **审计层** | MCP `traffic_log` 工具(ZCode/DSH 原生可用)与 DSH 插件 HTTP API `/api/acccom-serial*`(同源) | "刚才**发生过什么**:包括已被 read_data 消费掉的历史" |
| **人在环** | ACCCOM 桌面端(WPF)及其 `:8899` HTTP API | 需要人工看波形/深度调试时引导用户打开 |

## 关键判断:read_data vs 流量日志

- MCP `read_data` 读的是**实时接收缓冲,读完即消费**;`wait_for_response`/
  `send_and_wait` 同理在等活数据。
- `/api/acccom-serial` 读的是**持久化飞行记录仪**(最近 5000 条,含已消费的):
  复盘"刚才那轮收发了什么"、"TX 发出去的确切字节"、断连后的历史——用这个。
- 两者不互斥:先 `send_and_wait` 拿响应,存疑时再用审计层核对原始帧。

## 干净复现工作流(验证一条协议/一次发卡)

1. `curl -X POST <origin>/api/acccom-serial/clear`(仅 DSH 内可用),记下返回的
   `stats.lastSeq = S`(seq 单调不重置,S 即当前水位)。
2. 执行串口操作(MCP 工具)。
3. `traffic_log(sinceSeq=S, compact=true)`(ZCode/DSH 原生 MCP 工具;DSH 内也可 `curl "<origin>/api/acccom-serial?since=<S>&compact=1"`)读回本次全部收发。

`origin` 未知时先 `GET /api/acccom-serial/help` 自发现——它能拉回完整的
AGENTS.md(路由表、seq 生命周期、compact 语义)。面板空态里也有可复制的完整地址。

## 惯例与陷阱

- **hex 输入宽松**:MCP 发送接受空格/Tab 分隔、大小写混写;`send_and_wait` 返回
  的 `byteLength` 是真实字节数(不是字符串长度)。
- **seq 生命周期**:`seq` 只在 DSH 进程存活期内单调;DSH 重启后归零,跨重启不可用
  旧 since 续读,应重新取 `stats.lastSeq`。
- **COM 独占**:一个 COM 口同时只能被一个程序打开。acccom 打开期间其他串口工具
  会失败,反之亦然;**用完必 close_port**,失败先想"被谁占着"。
- **保留端口**:COM9 在本机不可用(系统保留,勿尝试连接)。
- **DSH 的 bash 陷阱**:PATH 里 System32 的 bash 是 WSL 存根,真实 shell 用
  `start-dsh-with-gitbash.cmd` 启动;面板空态的诊断行会显示当前解析结果。
- **多端口**:MCP 工具用 `portTag` 区分会话;审计层每条记录带 `tag`,面板里
  端口色块/下拉可过滤。

## 故障升级路径

1. 面板/审计层看不到数据 → `?stats=1` 查链路(文件不存在=MCP 没跑过;mtime 很旧
   =McpServer 挂了;size 涨但 entries 不涨=格式漂移,看 badLines)。
2. 链路正常但协议行为不对(无响应/校验错/丢字节)→ 使用 `serial-protocol-debug`
   技能的分层隔离法,不要盲目重发。
3. 需要人工深度参与(波形、逻辑分析仪)→ 引导用户打开 ACCCOM 桌面端。

## 参考文档

- API 权威文档:`GET /api/acccom-serial/help`(即 `tools/dsh-serial-panel/AGENTS.md`)
- 桌面端 HTTP API:仓库 `docs/guide/integration.md`
- 插件本体:仓库 `tools/dsh-serial-panel/README.md`
