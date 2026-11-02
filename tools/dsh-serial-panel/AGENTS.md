# dsh-serial-panel — Agent 接入说明

本插件在 DeepSeek Harness 的 webServer 上注册了一组同源路由。Agent 的 shell 工具
(curl/pwsh)可以直接访问,**把串口流量当飞行记录仪读**——即使 MCP `read_data`
的缓冲已被消费过,这里仍保留最近 5000 条历史。

完整地址 = Harness 页面的 origin + 路径(面板空态有完整地址和复制按钮;
本插件无独立端口,与 DSH 界面同源)。

## 路由

| 方法 | 路径 | 说明 |
|------|------|------|
| MCP | `traffic_log` 工具 | 同一飞行记录仪的 MCP 原生入口(ZCode/DSH 都挂同一 McpServer,无需 curl):游标 sinceSeq / 过滤 direction·portTag·search / compact 投影 |
| GET | `/api/acccom-serial/help` | 返回本文件(自发现:agent 找到任一路由后可拉取完整说明) |
| GET | `/api/acccom-serial?since=<seq>` | 增量:返回 seq 大于 since 的条目;省略 since 返回最近 300 条 |
| GET | `/api/acccom-serial?since=<seq>&compact=1&max=512` | 紧凑模式(agent 推荐,省 token) |
| GET | `/api/acccom-serial?stats=1` | 零条目仅统计(存活检查/水位探测的最小开销方式) |
| GET | `/api/acccom-serial/export?format=jsonl\|csv` | 导出 Host 内存环全部条目为附件(非完整磁盘日志) |
| POST | `/api/acccom-serial/clear` | 截断共享日志(桌面 GUI 流量窗口同步清空) |
| GET/POST | `/api/acccom-serial/mcp` | acccom MCP 条目(`include:mcp-acccom`)启停:GET 读状态;POST `{"enabled":true\|false}` 切换(热生效,新会话才注册工具) |

响应统一为 `{ ok, entries, stats }`;`stats` 含 `tx/rx/txBytes/rxBytes/kept/lastSeq/
badLines/logSize/logMtimeMs/rotated/diag/mcp`,可用于链路自检(文件不存在 → MCP 未跑过;
mtime 很旧 → 写入端疑似挂了;size 在涨但 entries 不涨 → 格式漂移,看 badLines)。

## MCP 串口工具参数备忘

工具 schema 是参数的一手来源;这里只记工作流级的用法与易踩点。

- **send 返回 `latestId`(发送锚点)**:游标取自发送**前**——`read_data(sinceId=<latestId>,
  waitMs=N)` 收获 TX 回显行与本帧之后的全部到达,极速回环应答也不会落在锚点之下。
  未知协议探测循环的标准姿势,别用 `clear_buffer` 毁现场。
- **send_and_wait 支持 `retries`(1-10,默认 1)/`intervalMs`(0-60000,默认 0)**:
  单次尝试 = 发→等 timeoutMs;未命中且有余量→隔 intervalMs 重发;响应带
  `attempts`(第几次命中)。设备丢首发、无应答重发,一个调用搞定,不必逐发往返。
- **read_data 支持 `stripAnsi`(默认 false)**:true 时剥 text 列的 ANSI CSI
  转义(ESP 级设备启动日志的彩色码);hex 列恒为原始字节,不受影响。
- **open_port 失败消息带分类提示**:`access denied` = 口被其他进程占用(GUI
  会话/上一会话/vendor 工具)→去那边关或换口;`FileNotFoundException` /
  `does not exist` = 设备不在或 USB 枚举失效(设备重启后常见)→`list_ports`
  重扫。消息原文携带底层异常,判定不再靠猜。

## 写路由的同源约束(v1.8.3)

`POST /clear` 与 `POST /mcp` 会做写防护判定,判据顺序:**Host 回环 → Sec-Fetch-Site →
Origin → Referer**。带非回环 `Host` 头(127.0.0.1/localhost/[::1] 之外的域名,
含 DNS rebinding 形态)、`Sec-Fetch-Site: cross-site`、或 `Origin`/`Referer` 的
authority 与 `Host` 不一致的请求一律 **403**(“cross-origin write refused”)。
目的是挡住任意网页跨站/重绑域名截断日志、关掉 MCP。

- **curl / agent 脚本不受影响**:不带浏览器标记且 Host 是回环的请求照常放行,
  上面「推荐工作流」的 `curl -X POST .../clear` 仍然可用。
- 若从自建页面/另一端口调用,请让 `Origin` 与请求 `Host` 一致(同源)且 Host 是
  回环地址,否则会 403。
- `POST /mcp` 的 body 必须是**显式布尔** `{"enabled":true|false}`:缺字段/非布尔/
  坏 JSON 都是 **400**(不会像旧版那样静默当成 `false`),body 上限 64KiB(超出 **413**)。

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

## 与 skill 的关系

本机 ZCode 与 DSH 均装有 `acccom-serial` skill(`~/.zcode/skills/` 与
`~/.dsh/skills/`):那是本工具链的**判断层**(三层接口选择、read_data vs 飞行
记录仪、干净复现工作流、端口纪律);本文件是 **API 参考**。参数细节以本文件
(经 `/help` 实时返回)为准,操作策略听 skill。
