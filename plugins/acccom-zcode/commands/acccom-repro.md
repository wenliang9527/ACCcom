---
description: 干净复现工作流:记水位 → 等我操作串口 → 读回本次全部收发
---

执行 ACCCOM 串口的"干净复现"三步工作流(acccom-serial skill 的标准流程):

1. **记水位**:调用 `mcp__acccom__traffic_log`(limit=1),把返回的 `lastSeq`
   明确告诉用户("水位 S=<数字>,接下来请让 AI 操作串口/或你手动操作后告诉我");
2. **停在这里等用户指示**——用户会让 AI 通过 MCP 串口工具(open_port/send/read_data)
   执行操作,或自行在设备上产生流量;
3. **读回**:用户确认后,调用 `mcp__acccom__traffic_log`(sinceSeq=S, limit=200,
   compact=true),把本次操作的全部收发按时间线整理输出:每条给出 方向(TX/RX)、
   工具、端口 tag、Δ 时序(相邻条目时间差)、载荷(HEX 优先);结尾给一句协议层面的
   观察(请求-响应是否配对、有无异常帧)。

$ARGUMENTS
