# dsh-serial-panel — Agent 接入说明

本插件在 DeepSeek Harness 的 webServer 上注册了一组同源路由。Agent 的 shell 工具
(curl/pwsh)可以直接访问,**把串口流量当飞行记录仪读**——即使 MCP `read_data`
的缓冲已被消费过,这里仍保留最近 5000 条历史。

完整地址 = Harness 页面的 origin + 路径(面板空态有完整地址和复制按钮;
本插件无独立端口,与 DSH 界面同源)。

## 路由

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/acccom-serial?since=<seq>` | 增量:返回 seq 大于 since 的条目;省略 since 返回最近 300 条 |
| GET | `/api/acccom-serial?since=<seq>&compact=1&max=512` | 紧凑模式(agent 推荐,省 token) |
| GET | `/api/acccom-serial?stats=1` | 零条目仅统计(存活检查/水位探测的最小开销方式) |
| GET | `/api/acccom-serial/export?format=jsonl\|csv` | 导出 Host 内存环全部条目为附件(非完整磁盘日志) |
| POST | `/api/acccom-serial/clear` | 截断共享日志(桌面 GUI 流量窗口同步清空) |

响应统一为 `{ ok, entries, stats }`;`stats` 含 `tx/rx/txBytes/rxBytes/kept/lastSeq/
badLines/logSize/logMtimeMs/rotated/diag`,可用于链路自检(文件不存在 → MCP 未跑过;
mtime 很旧 → 写入端疑似挂了;size 在涨但 entries 不涨 → 格式漂移,看 badLines)。

## 语义与坑

- **seq 单调递增,但只在 Host(DSH)进程生命周期内**:DSH 重启后 seq 归零,
  跨重启不能用旧 since 续读,应重取 `stats.lastSeq` 重新定位。
- `since` 早于保留窗口(5000 条)时,退化为返回最近 300 条——注意中间有断层。
- 每条 entry:`{ seq, dir: 'TX'|'RX', tool, tag, len, dtMs, ts, hex, text }`;
  `dtMs` 是相对上一条的毫秒间隔(乱序钳 0,坏时间戳为 null)。
- compact 模式每条:`{ seq, dir, tool, tag, len, dtMs, payload }`,`payload`
  为 text 优先、否则 hex,按码点截断到 `max`(≤4096,默认 512)。

## 推荐工作流(干净复现)

1. `curl -X POST <origin>/api/acccom-serial/clear`,记下返回的 `stats.lastSeq = S`
   (seq 不重置,S 即当前水位)。
2. 让 AI 通过 MCP 工具执行串口操作。
3. `curl "<origin>/api/acccom-serial?since=<S>&compact=1"` 读回本次操作的全部收发。

需要更早的历史时用 `export` 导出完整内存环再离线分析。
