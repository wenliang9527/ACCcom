# dsh-serial-panel — ACCCOM 串口实时面板  v1.1.0

DeepSeek Harness 的常驻插件(desktop profile):在侧边栏加一个 🔌 面板入口,
实时显示 **AI 通过 ACCCOM MCP 工具收发的串口数据**(等价于 ACCCOM 桌面端的
"MCP 流量"窗口,但直接长在 DSH 界面里)。

## 数据链路

```
ACCcom.McpServer(MCP 工具收发)
  → McpTrafficLog 追加 %LOCALAPPDATA%\ACCcom\mcp-traffic.jsonl(100ms 刷盘)
  → 本插件 Host 半区尾随增量(默认 400ms 轮询)
  → 同源私有路由 GET /api/acccom-serial?since=<seq>
  → 浏览器面板自适应增量拉取渲染(有数据 700ms / 空闲 2.5s)
```

## 目录职责

**唯一真源 = 本目录(仓库 `tools/dsh-serial-panel/`)**,纳入 git 版本管理。
`%USERPROFILE%\.dsh\profiles\` 下的所有副本都只是**部署目标**:

| 路径 | 说明 |
|------|------|
| `~\.dsh\profiles\desktop\dsh-serial-panel\` | 部署目标(desktop profile 源目录) |
| `~\.dsh\profiles\desktop\node_modules\dsh-serial-panel\` | 部署目标(安装闭包,DSH 实际加载) |
| `~\.dsh\profiles\web\...`(两处) | 可选:浏览器形态 dsh 的部署目标(`deploy.cmd web`) |

改代码只改本目录,然后:

```cmd
deploy.cmd        :: 部署到 desktop profile
deploy.cmd web    :: 追加部署到 web profile
```

再重启 DeepSeek Harness(loader 启动时读取,HMR 默认关闭)。
旧入口 `D:\DSH\sync-dsh-serial-panel.cmd` 已改为转发到本脚本的 `web` 模式。

## v1.1.0 优化点

**Host 半区**

| 项 | v1.0.0 | v1.1.0 |
|---|---|---|
| 内存环上限 | 1000(实测被顶满) | 5000,对齐写入端轮转阈值 |
| 环裁剪 | 逐行 `shift()` O(n) | 批量 `splice()`,滞后量 64(按上限缩放,小上限为 0) |
| 增量查询 | 全表 `filter()` O(n) | 按 seq 直接算下标切片 O(1) |
| 首次挂载 | 整个日志文件读入 | 只读尾部 256KB 窗口 |
| 异常保护 | 无 | 无换行超长写入时丢弃残留,防内存膨胀 |
| 可调参数 | 硬编码 | `DSH_SERIAL_PANEL_*` 环境变量覆盖 |

**Client 半区**

| 项 | v1.0.0 | v1.1.0 |
|---|---|---|
| 行渲染 | 每次 300 行全量重渲染 | `React.memo`,只有变化的行重渲染 |
| 过滤计算 | 每次 render 重算 | `useMemo` 缓存(entries/方向/关键字变化才算) |
| stats 更新 | 每轮新建对象 → 触发重渲染 | 逐字段浅比较,无变化不更新 state |
| 轮询 | 固定 800ms(空闲也空转) | 自适应:有数据 700ms / 空闲 2.5s / 隐藏或暂停 2s |
| 保留上限 | 2000 | 5000(与 Host 一致) |
| 超长帧 | 全量进 DOM | 行内截断 4000 字符(详情区仍显示全文) |

计数器含义:`渲染 300 / 可见 1234 / 总 5000 条`(过滤后行数超过 300 时只挂载最近 300 行)。

## 测试

Host 半区带一套零依赖行为测试(20 项断言),已实测通过:

```powershell
$plug = "$env:USERPROFILE\.dsh\profiles\desktop\dsh-serial-panel"
$t = Join-Path $env:TEMP 'dsh-sp-tests'; New-Item -ItemType Directory -Force $t | Out-Null

# 增量/半行拼接/序号切片/批量裁剪/轮转/清空/方法守卫/坏行容错/诊断
$env:DSH_SERIAL_PANEL_MAX_ENTRIES = '5'
$env:DSH_SERIAL_PANEL_LOG = Join-Path $t 'core.jsonl'
node "$plug\test.js" core

# 首次挂载的尾部窗口(大日志不全量读入,且丢掉窗口首部半行)
$env:DSH_SERIAL_PANEL_FIRST_TAIL = '1000'
$env:DSH_SERIAL_PANEL_LOG = Join-Path $t 'big.jsonl'
node "$plug\test.js" window
```

用环境变量把参数压小是为了快速触发边界(上限 5 条即可验证裁剪)。
`test.js` 只存在于真源目录,不同步到安装闭包。

## 可调环境变量(Host 半区读取)

| 变量 | 默认 | 说明 |
|------|------|------|
| `DSH_SERIAL_PANEL_MAX_ENTRIES` | 5000 | 内存环上限 |
| `DSH_SERIAL_PANEL_POLL_MS` | 400 | 尾随文件轮询间隔 |
| `DSH_SERIAL_PANEL_FIRST_TAIL` | 262144 | 首次只读的尾部字节窗口 |
| `DSH_SERIAL_PANEL_FIRST_LIMIT` | 300 | 首屏/回退返回条数 |
| `DSH_SERIAL_PANEL_MAX_LINE` | 1048576 | 单行残留保护阈值 |
| `DSH_SERIAL_PANEL_LOG` | %LOCALAPPDATA%\ACCcom\mcp-traffic.jsonl | 日志路径覆盖 |

设置方式:`profiles\desktop\cordis.patch.yml` 里给 dsh-serial-panel 条目加
`config` 不生效(插件读的是进程环境),需在启动 DSH 前设置环境变量;
或参考 `D:\DSH\start-dsh-with-gitbash.cmd` 的 `set` 写法。

## 安装 / 卸载

- 安装:包目录出现在 `profiles\desktop\dsh-serial-panel` 与
  `profiles\desktop\node_modules\dsh-serial-panel`,并在
  `profiles\desktop\cordis.patch.yml` 有 insert 条目,重启 DSH。
- 卸载:删除上述两处目录 + patch.yml 里的 insert 块,重启 DSH。
