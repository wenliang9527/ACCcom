# dsh-serial-panel — ACCCOM 串口实时面板  v1.6.0

DeepSeek Harness 的常驻插件(desktop profile):在**右侧停靠栏**加一个
"ACCCOM 串口" tab,实时显示 **AI 通过 ACCCOM MCP 工具收发的串口数据**——
对话与串口数据并排可见(等价于 ACCCOM 桌面端的"MCP 流量"窗口,但直接长在
DSH 界面里)。左侧边栏图标点击打开;右栏 guide 默认页也有入口卡片。
agent 侧的接口说明见 [AGENTS.md](AGENTS.md)(compact 增量、导出、干净复现工作流)。
DSH 版本差异导致 rightbar 服务缺席时,自动回退为整屏 main 面板(v1.2.0 行为)。

## 功能总览(v1.5.0)

**面板(人类视角)**

- **右侧停靠**:对话与串口数据并排;拖拽调宽时列随容器宽度自动降级(工具列→长度列→统计文本),右栏自带全屏切换;DSH 缺 rightbar 服务时自动回退整屏模式
- **实时增量**:自适应轮询(有数据 700ms / 空闲 2.5s / 隐藏或挂起降频),跟随自动挂起(向上翻历史)/回底自动恢复,挂起期间「↓ N 条新数据」浮出按钮
- **行布局**:时间 · TX/RX 徽章 · 端口色块(**点击=过滤该端口,再点切回全部**) · 工具/端口 · 长度 · Δt 帧间隔 · 载荷(HEX 切换、行内 4000 字符截断、**搜索命中高亮**)
- **过滤**:方向(全部/RX/TX)、端口下拉、子串/**正则**搜索(`.*` 开关,非法表达式自动回退子串,偏好持久化)
- **详情区**:点击行看 text+HEX 全文(含完整日期,跨天会话可对),一键复制(clipboard API,失败回退 execCommand)
- **导出**:工具栏「导出」= 当前过滤视图一键下载 JSONL(Blob 本地生成);整个内存环走 Host `/export` 路由
- **可靠性辅助**:断层标记(Host 环滚出/seq 回退守卫)、清空两段式确认、连接态三色(尾随中/等日志文件/未响应)、空态显示可复制的 API 地址与链路自检(shell 解析、日志大小、坏行数)
- **快捷键**(面板持有焦点时):空格=暂停/继续,`/`=聚焦搜索,Esc=取消选中;不影响对话输入
- **流量徽章**:左侧图标红点提示新串口流量(面板关着也能感知,打开并实时跟随时自动消失)

**Agent 接口(同源 HTTP,详见 [AGENTS.md](AGENTS.md))**

- `GET /api/acccom-serial/help` 返回 AGENTS.md(agent 自发现入口)
- `GET /api/acccom-serial?since=<seq>&compact=1&max=N` 紧凑增量(单字段载荷,码点截断 ≤4096)
- `GET /api/acccom-serial?stats=1` 零载荷统计(心跳/水位探测)
- `GET /api/acccom-serial/export?format=jsonl|csv` 导出内存环
- `POST /api/acccom-serial/clear` 截断共享日志(干净复现工作流的起点)
- **MCP `traffic_log` 工具**同源提供同一飞行记录仪(ZCode/DSH 原生可用,无需 curl)

**Host 半区工程**

- 5000 条内存环(对齐写入端轮转阈值)、按 seq 直接切片的 O(1) 增量、首次挂载只读尾部 256KB 窗口、坏行计数
- 每条记录带 `dtMs`(consume 时统一计算,乱序钳 0);stats 含 `badLines/logSize/logMtimeMs/rotated` 链路自检字段
- 6 个 `DSH_SERIAL_PANEL_*` 环境变量可调(见下表);语义色走 DSH 主题 token(旧十六进制作兜底)

## 数据链路

```
ACCcom.McpServer(MCP 工具收发)
  → McpTrafficLog 追加 %LOCALAPPDATA%\ACCcom\mcp-traffic.jsonl(100ms 刷盘)
  → 本插件 Host 半区尾随增量(默认 400ms 轮询)
  → 同源私有路由 GET /api/acccom-serial?since=<seq>(另有 compact=1 紧凑模式、
    /export jsonl|csv 导出、/clear 截断)
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
| 轮询 | 固定 800ms(空闲也空转) | 自适应:有数据 700ms / 空闲 2.5s / 隐藏 2s / 暂停 1.5s |
| 保留上限 | 2000 | 5000(与 Host 一致) |
| 超长帧 | 全量进 DOM | 行内截断 4000 字符(详情区仍显示全文) |

## v1.2.0 优化点

**Host 半区**

| 项 | 说明 |
|---|---|
| Δt 帧间隔 | consume 时统一计算 `dtMs`(相对上一条;乱序钳 0,坏时间戳记 null) |
| 链路诊断 | stats 增加 `badLines/logSize/logMtimeMs/rotated`,面板空态与 agent 共用 |
| compact 模式 | `?compact=1&max=N`:去 text/hex 双份载荷,按码点截断(≤4096,默认 512) |
| 导出路由 | `GET /export?format=jsonl\|csv`:导出内存环为附件(csv 带 BOM 与引号转义) |

**Client 半区**

| 项 | 说明 |
|---|---|
| Δt 列 + 详情复制 | 行内 Δt 列;详情区 text/hex 一键复制(clipboard API 失败回退 execCommand) |
| 跟随自动挂起 | 向上翻历史自动暂停跟随,回到底部自动恢复(按钮可强制开关) |
| 端口过滤 | 端口下拉 + portTag 稳定色块(多端口流量一眼区分) |
| 断层标记 | 增量 seq 跳变时插入「已省略 N 条」;Host 重启 seq 回退时整体替换防 key 重复 |
| 连接态三色 | 绿=尾随中 / 黄=等日志文件出现 / 红=Host 未响应 |
| 其他 | 清空两段式确认(部分 webview 吞原生 confirm);回前台补拉加在途守卫;空态显示可复制的 API 地址;侧边栏图标换 inline SVG;≤560px 隐藏长度列 |

## v1.3.0 布局改造

| 项 | 说明 |
|---|---|
| 右侧停靠 | 面板从整屏 main 槽迁到 rightbar 停靠栏 tab(`sidebarRightTabs.register` 类型 + `sidebar.right.pane.tab` 挂体),对话与串口数据并排可见;右栏自带全屏切换(Strip 控件),全屏时仍可覆盖视口 |
| keepMounted | tab 切走/面板收起后组件不卸载,轮询与数据持续;tab 不可见时轮询自动降为 2s |
| 入口 | 左侧边栏图标点击 = 打开右栏 tab;右栏 guide 默认页有入口卡片(插头图标) |
| 兜底 | 检测 `sidebarRight`/`sidebarRightTabs` 服务缺席 → 自动回退整屏 main 注册 |
| 窄宽度 | ≤640px 隐藏工具列、≤560px 隐藏长度列、≤520px 隐藏 RX/TX 统计(容器查询,跟随停靠栏拖拽实时生效;media query 作小窗口兜底) |

### v1.3.1 修复:停靠栏拖拽时数据不跟随

面板根元素原先没有宽度约束,dockkit 的 pane 收窄后不换行的 hex 行会把内容撑到
比 pane 更宽,被 tabBody 的 `overflow:hidden` 直接裁切——表现为拖拽调宽后行不重排、
右缘内容被切。修复:根元素 `width:100% / min-width:0 / overflow:hidden` 锁宽,
列表 `overflow-x:hidden`;列降级从视口 `@media`(1920 视口下永不触发)换成
`@container sp-panel` 容器查询,随面板实际宽度实时生效。

## v1.4.0 易用性

| 项 | 说明 |
|---|---|
| 新数据浮出提示 | 跟随挂起(翻历史)期间到达的条目计数,列表右下角浮出「↓ N 条新数据」按钮,点击回底并自动恢复跟随 |
| 端口色块点击过滤 | 行内 portTag 色块可点击 = 过滤该端口;再点同端口切回全部 |
| 搜索命中高亮 | 搜索时行内 payload 命中片段高亮(mark,amber 半透明,深浅主题通用) |
| 语义色接入主题 | 状态点/诊断行/复制成功色改用 DSH 语义 token(`--dsw-alias-state-success/warn/error-primary`、`--dsw-alias-brand-primary`),原十六进制作兜底值,token 缺失时渲染不变 |

## v1.5.0 感知与效率

| 项 | 说明 |
|---|---|
| 图标流量徽章 | 左侧 🔌 图标带「有新流量」红点:图标自带 `?stats=1` 零载荷轮询(5s,页面隐藏 8s),tab 体未挂载时徽章仍工作;面板可见且实时跟随时自动标记已读 |
| 正则搜索 | 工具栏 `.*` 开关;非法表达式自动回退子串匹配;命中高亮同样支持正则(内部克隆加 g 标志,零长匹配防死循环) |
| 快捷键 | 面板持有焦点时:空格 = 暂停/继续,`/` = 聚焦搜索框,Esc = 取消选中;点击面板非表单区域即获得焦点;对话输入框的事件不经过面板 DOM,不受影响 |
| 心律端点 | Host 新增 `GET /?stats=1`(零条目仅统计),也供 agent 做存活检查 |

### v1.5.0 测试要点

`highlightRe` 的 exec 循环必须用带 `g` 标志的正则——非全局正则的 `exec` 永远返回
首个命中,靠计数上限才停(第一版实现的真实 bug,冒烟测试当场抓住)。

计数器含义:`渲染 300 / 可见 1234 / 总 5000 条`(过滤后行数超过 300 时只挂载最近 300 行)。
## v1.6.0 按需导出与自发现

| 项 | 说明 |
|---|---|
| 视图导出 | 工具栏「导出」按钮:当前过滤视图(可见条目,剔除断层标记)一键下载 JSONL,Blob 本地生成不经 Host;整个内存环仍走 `GET /export` |
| help 端点 | Host 新增 `GET /api/acccom-serial/help` 返回 AGENTS.md(部署闭包内副本,首次读取后缓存)——agent 发现任一路由后可自助获取完整接口说明 |
| 详情日期 | 详情区时间补完整日期(跨天会话可对) |


## 测试

Host 半区带一套零依赖行为测试(**71 项断言**:core 17 + window 5 + api 17 + client 32),已实测通过:

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

# Δt/compact/导出/链路诊断(需要默认 MAX_ENTRIES≥40,先清掉 core 留下的 =5)
Remove-Item Env:DSH_SERIAL_PANEL_MAX_ENTRIES -ErrorAction SilentlyContinue
$env:DSH_SERIAL_PANEL_LOG = Join-Path $t 'api.jsonl'
node "$plug\test.js" api

# client.js 注册结构冒烟(stub React,无需浏览器与日志环境变量)
node "$plug\test.js" client
```

用环境变量把参数压小是为了快速触发边界(上限 5 条即可验证裁剪)。
`test.js` 由 deploy.cmd 一并同步到两处部署目标,闭包内也可直接跑。

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
