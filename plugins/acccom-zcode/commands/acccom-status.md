---
description: 查看 ACCCOM 串口链路状态(飞行记录仪水位 + 链路自检)
---

调用 MCP 工具 `mcp__acccom__traffic_log`(参数 sinceSeq=0, limit=5, compact=true),
读取串口飞行记录仪的当前状态,然后:

1. 报告 `lastSeq` 水位与 `kept` 条数,以及最近 5 条收发的方向/工具/载荷摘要;
2. 若 `entries` 为空,按 acccom-serial skill 的链路自检顺序排查:mcp-traffic.jsonl
   是否存在(MCP server 是否跑过串口操作)→ 建议先让 AI 做一次 list_ports;
3. 提醒:COM 口独占,操作前确认没有其他程序占用;COM9 为系统保留不可用。

$ARGUMENTS
