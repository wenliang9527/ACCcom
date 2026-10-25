'use strict'
// ============================================================
//  dsh-serial-panel — Host 半区(永久 cordis 插件)  v1.8.2
//
//  v1.8.2(安全 + 健壮性,数据契约零变化):
//   * 写路由同源判定:POST 校验 Sec-Fetch-Site / Origin / Referer,跨站页面伪造的
//     简单请求一律 403(此前任何人都能 POST /clear 截断日志、POST /mcp 关掉 MCP);
//     curl/agent 这类不带浏览器标记的非浏览器客户端仍然放行(AGENTS.md 的干净复现
//     工作流不受影响),桌面端 dsh-app://app 显式放行(详见 crossOriginWrite 注释)。
//   * POST /mcp 的 body 必须是显式布尔 {enabled:true|false}:旧实现把解析失败的
//     body 一律读成 enabled:false,畸形请求 = 静默关闭 MCP。
//   * 增量尾随改分块读(1MiB/块 + 流式 TextDecoder):不再按 delta 大小整块分配,
//     也不再一次性阻塞读完整段增量;跨块边界的多字节字符不会被劈开。
//   * POST JSON body 加上限(64KiB),超限回 413 并排空请求体
//     (不排空会污染 keep-alive,下一个请求 ECONNRESET)。
//
//  v1.7.1:注册契约对齐宿主 0.2.0-rc.2(修「侧栏图标点了没反应」)——纯 Client 侧,
//  数据层无变化,Host 继续哑管道;仅版本号随插件整体 bump。
//
//  职责:
//   1. 尾随 %LOCALAPPDATA%\ACCcom\mcp-traffic.jsonl —— ACCcom.McpServer 的
//      McpTrafficLog 把 AI 每条串口收发镜像到这个共享 JSONL(100ms 批量刷盘,
//      5000 行轮转到 .1,GUI 清空 = 截断;MCP 侧检测收缩会自动重开)。
//   2. 经 webServer.register 挂同源私有路由 /api/acccom-serial,把增量行喂给
//      浏览器面板(模式照抄 dsh-theme-palettes:handler(req,res) Node 风格)。
//
//  行格式(TrafficRecord, camelCase):
//    { id, tool, timestamp, direction: 'TX'|'RX', rawHex, text, portTag }
//
//  ── v1.1.0 优化 ──
//   * 内存环 1000 → 5000(对齐写入端轮转阈值),可用环境变量调整
//   * 批量裁剪代替逐行 shift():摊还掉 O(n) 搬移
//   * 增量查询由全表 filter 改为按 seq 直接切片 O(1)
//   * 首次挂载只读文件尾部窗口,大日志不再整文件读入
//   * 无换行超长写入保护,避免 remainder 无限膨胀
//   * 只读参数均可用 DSH_SERIAL_PANEL_* 覆盖,便于按现场调优
//
//  ── v1.2.0 优化 ──
//   * 每条记录带 dtMs(相对上一条的帧间隔,Host 统一计算,乱序钳 0)
//   * stats 增加 badLines/logSize/logMtimeMs/rotated(链路自检,面板与 agent 共用)
//   * compact=1&max=N 紧凑模式:去掉 text/hex 双份载荷,单字段按码点截断(≤4096)
//   * GET /api/acccom-serial/export?format=jsonl|csv 导出内存环为附件
// ============================================================

const fs = require('fs')
const path = require('path')

const ROUTE = '/api/acccom-serial'
const ROUTE_CLEAR = '/api/acccom-serial/clear'
const ROUTE_EXPORT = '/api/acccom-serial/export'
const ROUTE_HELP = '/api/acccom-serial/help'
const ROUTE_MCP = '/api/acccom-serial/mcp' // v1.8.0:MCP 启停开关(GET 读状态 / POST 切换)

// host 层 MCP 条目 id(住在 profile patch 里,插件面板/本开关共同管理;不属于本插件)
const MCP_ENTRY_ID = 'include:mcp-acccom'

/** 正整数环境变量读取(非法值回落默认)。 */
function intEnv(name, fallback) {
  const raw = Number(process.env[name])
  return Number.isFinite(raw) && raw > 0 ? Math.floor(raw) : fallback
}

const MAX_ENTRIES = intEnv('DSH_SERIAL_PANEL_MAX_ENTRIES', 5000)          // 内存环上限(= 写入端轮转行数)
const POLL_MS = intEnv('DSH_SERIAL_PANEL_POLL_MS', 400)                   // 尾随轮询间隔
const FIRST_TAIL_BYTES = intEnv('DSH_SERIAL_PANEL_FIRST_TAIL', 256 * 1024) // 首次只读尾部窗口
const MAX_LINE_CHARS = intEnv('DSH_SERIAL_PANEL_MAX_LINE', 1024 * 1024)    // 单行保护
const DEFAULT_FIRST = intEnv('DSH_SERIAL_PANEL_FIRST_LIMIT', 300)          // 首屏返回条数
// 裁剪滞后量:超过 MAX_ENTRIES+TRIM_SLACK 才裁一次,把逐行 splice 的搬移成本摊薄。
// 上限较小时取 0(或按比例缩小),保证 kept 不会明显超出配置的上限。
const TRIM_SLACK = MAX_ENTRIES >= 256 ? Math.min(64, MAX_ENTRIES >> 4) : 0

function logPath() {
  const override = (process.env.DSH_SERIAL_PANEL_LOG || '').trim()
  if (override) return override
  const base = (process.env.LOCALAPPDATA || '').trim()
  return path.join(base, 'ACCcom', 'mcp-traffic.jsonl')
}

function jsonResponse(res, status, body) {
  // 413 等提前拒绝的场景:请求体可能还没读完。不声明 Connection: close 并排空,
  // 未消费的 body 会污染 keep-alive 连接,下一个请求直接 ECONNRESET(真实 http 实测)。
  if (status === 413 && typeof res.setHeader === 'function') {
    try { res.setHeader('connection', 'close') } catch (e) { /* ignore */ }
  }
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store',
  })
  res.end(JSON.stringify(body))
}

// ---- 写路由同源保护 ----
// DSH 宿主的 isTrustedApiRequest 只护 Remote/API 层,webServer.register 挂的自定义
// 路由完全不经它;所以本插件必须自己判。攻击面是「跨站页面的简单请求」:
// 任意网页都能 POST /api/acccom-serial/clear(截断共享日志)或 /mcp(关掉 MCP),
// 无需 preflight、响应读不到也不要紧——副作用已经发生。

/** 大小写不敏感的取头(真实 IncomingMessage 全是小写,这里同时容忍测试桩)。 */
function headerOf(headers, name) {
  if (!headers) return undefined
  const direct = headers[name]
  if (direct !== undefined) return direct
  const lower = name.toLowerCase()
  if (headers[lower] !== undefined) return headers[lower]
  for (const key of Object.keys(headers)) {
    if (key.toLowerCase() === lower) return headers[key]
  }
  return undefined
}

/** URL 的 authority(含端口),解析失败返回 null。 */
function authorityOf(url) {
  try { return new URL(String(url)).host.toLowerCase() } catch (e) { return null }
}

/**
 * 判定一次写请求是否来自同源页面。
 * @returns null = 放行;否则返回拒绝原因(供 403 响应体)
 *
 * 判据顺序:Sec-Fetch-Site → Origin → Referer。三者皆无视为非浏览器客户端
 * (curl / agent 脚本),放行——那是 AGENTS.md 记录的干净复现工作流。浏览器发出的
 * 跨站写请求一定带 `Sec-Fetch-Site: cross-site`(或异源 Origin),被挡住。
 *
 * ── 关于桌面端(Electron)──
 * 桌面壳用自定义协议 `dsh-app://app/` 载入界面(见宿主 registerSchemesAsPrivileged),
 * 所以页面内 fetch 的 Origin 是 `dsh-app://app`。但桌面壳的 forwardWebRequest 会把
 * `host/origin/cookie/sec-fetch-site` 四个头**全部剥掉**再转发给后端,因此插件实际
 * 收到的是「无 Origin、无 Sec-Fetch-Site」→ 走下面的放行分支,桌面端开关正常。
 *
 * 即便如此这里仍显式接纳 `dsh-app://app`:它不可能被外部网站伪造(Scheme 注册在
 * 桌面进程内,普通网页拿不到),而一旦将来宿主不再剥头,这条兼容分支能避免把桌面端
 * 自己的写请求判成跨站——即「宁可显式允许一个不可伪造的来源,也不依赖上游一直剥头」。
 */
const DESKTOP_ORIGIN = 'dsh-app://app'

function crossOriginWrite(req) {
  const method = String((req && req.method) || 'GET').toUpperCase()
  if (method !== 'POST') return null
  const headers = req && req.headers
  if (!headers) return null // 无头部信息:无法判定,不阻断(仅测试桩会走到)

  const origin = headerOf(headers, 'origin')
  // 桌面壳必须先判:它从 `dsh-app://app` 访问 `http://127.0.0.1:port` 属于跨 scheme,
  // 浏览器会标成 Sec-Fetch-Site: cross-site。若先看 sec-fetch-site,会在读到
  // Origin 之前就误拒桌面端自己的写请求(开关会「点了没反应」)。
  if (origin !== undefined && origin !== null && String(origin).toLowerCase() === DESKTOP_ORIGIN) return null

  const site = String(headerOf(headers, 'sec-fetch-site') || '').toLowerCase()
  if (site && site !== 'same-origin' && site !== 'none') return 'Sec-Fetch-Site: ' + site

  const host = String(headerOf(headers, 'host') || '').toLowerCase()
  if (origin !== undefined && origin !== null) {
    const auth = authorityOf(origin)
    if (!auth || !host || auth !== host) return 'Origin: ' + String(origin)
    return null
  }

  const referer = headerOf(headers, 'referer')
  if (referer !== undefined && referer !== null) {
    if (String(referer).toLowerCase().indexOf(DESKTOP_ORIGIN) === 0) return null
    const auth = authorityOf(referer)
    if (!auth || !host || auth !== host) return 'Referer: ' + String(referer)
  }
  return null
}

const MAX_JSON_BODY = 64 * 1024        // POST body 上限
const CHUNK_BYTES = 1024 * 1024        // 增量尾随的单块读上限

/** body 超限信号(处理分支转 413;不能静默回落成 {}——那会被读成 enabled:false)。 */
class BodyTooLarge extends Error {
  constructor() {
    super('request body exceeds ' + MAX_JSON_BODY + ' bytes')
    this.name = 'BodyTooLarge'
    this.status = 413
  }
}

/** 码点安全截断:普通 slice 可能劈开代理对,JSON 序列化会产出孤立代理。 */
function truncateCodePoints(s, max) {
  const pts = Array.from(s)
  return pts.length > max ? pts.slice(0, max).join('') : s
}

const CSV_COLUMNS = ['seq', 'ts', 'dir', 'tool', 'tag', 'len', 'dtMs', 'text', 'hex']

function csvCell(v) {
  const s = v == null ? '' : String(v)
  return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s
}

function toCsv(entries) {
  const rows = entries.map((e) => [
    e.seq, e.ts, e.dir, e.tool, e.tag, e.len, e.dtMs == null ? '' : e.dtMs, e.text, e.hex,
  ].map(csvCell).join(','))
  // BOM 前缀:让 Excel 识别 UTF-8
  return '\uFEFF' + [CSV_COLUMNS.join(',')].concat(rows).join('\r\n') + (rows.length ? '\r\n' : '')
}

/**
 * 诊断:在 Host 进程里复刻一次 resolveExecutable 的 PATH/PATHEXT 扫描,
 * 回答「模型的一次性 shell 工具究竟会执行哪个可执行文件」。
 * 用于区分「面板没数据」与「shell 解析到错误目标(如 WSL 存根)」这类环境问题,
 * 无需任何外部 shell。进程生命周期内环境不变,故只探测一次。
 */
function probeExecutable(name) {
  const dirs = String(process.env.PATH || '').split(';').filter(Boolean)
  const exts = String(process.env.PATHEXT || '.COM;.EXE;.BAT;.CMD').split(';').filter(Boolean)
  let winner = null
  for (const dir of dirs) {
    for (const ext of exts) {
      const candidate = path.join(dir, name + ext)
      try {
        if (fs.existsSync(candidate)) { winner = candidate; break }
      } catch (e) { /* ignore */ }
    }
    if (winner) break
  }
  return winner
}

const SHELL_DIAG = (() => {
  const bash = probeExecutable('bash')
  const pwsh = probeExecutable('pwsh') || probeExecutable('powershell')
  return {
    bash,
    bashOk: !!bash && /git/i.test(bash), // System32 的 bash 是 WSL 存根,只有 Git 的可用
    pwsh,
    pwshOk: !!pwsh,
    pathCount: String(process.env.PATH || '').split(';').filter(Boolean).length,
  }
})()

module.exports = {
  name: 'dsh-serial-panel',
  inject: ['webServer'],
  apply(ctx) {
    const webServer = ctx.get('webServer')
    if (!webServer || typeof webServer.register !== 'function') return

    // v1.8.0:MCP 启停开关。状态实时读 loader 里对应条目的 disabled(不存偏好,
    // 重启后、外部改动后都永远说真话);切换走 pluginManager.setPluginEnabled
    // ——它改 profile patch 的 disabled 并热 reload,无需重启 DSH。
    const pluginManager = ctx.get('pluginManager')
    const loaderOf = () => ctx.get('loader') || ctx.loader || null
    const mcpState = () => {
      const loader = loaderOf()
      const entry = loader && typeof loader.entries === 'function'
        ? loader.entries().find((e) => e.id === MCP_ENTRY_ID)
        : null
      if (!entry) return { available: false, enabled: null }
      return { available: true, enabled: !entry.disabled }
    }
    const readJsonBody = async (req) => {
      const chunks = []
      let total = 0
      try {
        for await (const c of req) {
          total += c.length
          // 超限直接中止:静默截断成 {} 会让「超大 body」被解释成 enabled:false,
          // 那等于一次越权关闭 MCP,所以必须显式拒绝而不是回落默认值。
          if (total > MAX_JSON_BODY) throw new BodyTooLarge()
          chunks.push(c)
        }
      } catch (e) {
        // 超限时把剩余请求体读掉再抛:提前停止 for-await 会留下未消费的字节,
        // keep-alive 连接上的下一个请求就会 ECONNRESET(真实 http 实测)。
        if (e instanceof BodyTooLarge) {
          try { for await (const _ of req) { /* drain */ } } catch (e2) { /* ignore */ }
        }
        throw e
      }
      if (!chunks.length) return {}
      try { return JSON.parse(Buffer.concat(chunks).toString('utf8')) } catch (e) { return {} }
    }

    // 写请求同源校验:不通过则回 403 并返回 true(各 POST 处理分支据此提前 return)
    const blockedForeignWrite = (req, res) => {
      const reason = crossOriginWrite(req)
      if (!reason) return false
      jsonResponse(res, 403, { ok: false, error: 'cross-origin write refused', detail: reason })
      return true
    }

    const file = logPath()
    let offset = 0          // 已读字节偏移
    let remainder = ''      // 半行残留(写入端按行追加,可能读到半行)
    let firstRead = true    // 是否尚未读过(决定能否启用尾部窗口)
    let seq = 0             // 单调递增序号(不随 clear/轮转重置,客户端 since 依赖它)
    let prevTsMs = null     // 上一条记录的毫秒时间戳(Δt 用;坏时间戳不更新基准)
    const entries = []      // { seq, id, tool, dir, hex, text, tag, ts, len, dtMs },seq 递增
    const stats = { tx: 0, rx: 0, txBytes: 0, rxBytes: 0, lastAt: null, exists: false, badLines: 0, logSize: 0, logMtimeMs: null, rotated: false }

    const reset = () => {
      offset = 0
      remainder = ''
      firstRead = true
      prevTsMs = null
      entries.length = 0
      stats.tx = 0; stats.rx = 0; stats.txBytes = 0; stats.rxBytes = 0; stats.lastAt = null
      stats.badLines = 0
    }

    const payloadLen = (rec) => {
      if (rec.rawHex) {
        const n = String(rec.rawHex).replace(/[^0-9a-fA-F]/g, '').length
        if (n > 0) return Math.floor(n / 2)
      }
      return typeof rec.text === 'string' ? Buffer.byteLength(rec.text, 'utf8') : 0
    }

    const consume = (text) => {
      const data = remainder + text
      const lines = data.split('\n')
      remainder = lines.pop() || ''
      // 写入端若长时间不换行(异常/超大帧),丢弃残留而不是无限增长
      if (remainder.length > MAX_LINE_CHARS) { stats.badLines += 1; remainder = '' }
      for (const line of lines) {
        const s = line.trim()
        if (!s) continue
        let rec
        try { rec = JSON.parse(s) } catch (e) { stats.badLines += 1; continue } // 坏行跳过,不致命
        seq += 1
        const dir = rec.direction === 'TX' ? 'TX' : 'RX'
        const len = payloadLen(rec)
        // Δt 相对上一条有效时间戳;乱序(多进程追加)钳到 0,坏时间戳记 null
        const tsMs = Date.parse(rec.timestamp || '')
        const dtMs = Number.isFinite(tsMs) && prevTsMs != null ? Math.max(0, tsMs - prevTsMs) : null
        if (Number.isFinite(tsMs)) prevTsMs = tsMs
        entries.push({
          seq,
          id: rec.id,
          tool: rec.tool || '',
          dir,
          hex: rec.rawHex || '',
          text: rec.text || '',
          tag: rec.portTag || '',
          ts: rec.timestamp || '',
          len,
          dtMs,
        })
        if (dir === 'TX') { stats.tx += 1; stats.txBytes += len }
        else { stats.rx += 1; stats.rxBytes += len }
        stats.lastAt = rec.timestamp || new Date().toISOString()
      }
      // 批量裁剪:一次 splice 顶掉逐行 shift 的搬移成本
      if (entries.length > MAX_ENTRIES + TRIM_SLACK) {
        entries.splice(0, entries.length - MAX_ENTRIES)
      }
    }

    const tail = () => {
      try {
        let st = null
        try { st = fs.statSync(file) } catch (e) { st = null }
        stats.exists = !!st
        if (st) { stats.logSize = st.size; stats.logMtimeMs = st.mtimeMs }
        else { stats.logSize = 0; stats.logMtimeMs = null }
        try { stats.rotated = fs.existsSync(file + '.1') } catch (e) { stats.rotated = false }
        if (!st) { if (offset !== 0) reset(); return }
        if (st.size < offset) reset() // GUI 清空(截断)或 MCP 轮转成新文件
        if (st.size === offset) return

        let from = offset
        let dropPartial = false
        // 首次挂载遇到大日志:只读尾部窗口,并丢掉窗口首部的半行
        if (firstRead && st.size - from > FIRST_TAIL_BYTES) {
          from = st.size - FIRST_TAIL_BYTES
          dropPartial = true
        }

        const fd = fs.openSync(file, 'r')
        try {
          // v1.8.2:分块读 + 流式解码。旧实现按 `st.size - from` 一次性 allocUnsafe
          // 并阻塞读完整段增量——写入端突发长帧时既可能有巨大临时缓冲,又让
          // 单次 tail() 长时间占住事件循环。现在按 CHUNK_BYTES 逐块读,块间共享
          // 一个 TextDecoder(stream:true)保证多字节字符不被块边界劈开。
          const target = st.size
          offset = target
          firstRead = false
          const decoder = new TextDecoder('utf-8')
          let dropFirst = dropPartial
          let pos = from
          while (pos < target) {
            const want = Math.min(CHUNK_BYTES, target - pos)
            const buf = Buffer.allocUnsafe(want)
            const read = fs.readSync(fd, buf, 0, want, pos)
            if (read <= 0) break
            pos += read
            let text = decoder.decode(buf.subarray(0, read), { stream: pos < target })
            if (dropFirst) {
              // 尾部窗口起点落在行中间:丢掉本窗口的第一段残行
              const nl = text.indexOf('\n')
              text = nl >= 0 ? text.slice(nl + 1) : ''
              if (nl >= 0) dropFirst = false
            }
            if (text) consume(text)
          }
        } finally {
          try { fs.closeSync(fd) } catch (e) { /* ignore */ }
        }
      } catch (e) { /* 尾随失败不致命,下轮重试 */ }
    }

    tail()
    const timer = setInterval(tail, POLL_MS)

    const view = () => ({
      tx: stats.tx, rx: stats.rx,
      txBytes: stats.txBytes, rxBytes: stats.rxBytes,
      lastAt: stats.lastAt, exists: stats.exists,
      kept: entries.length, lastSeq: seq, logPath: file,
      badLines: stats.badLines, logSize: stats.logSize,
      logMtimeMs: stats.logMtimeMs, rotated: stats.rotated,
      diag: SHELL_DIAG,
      mcp: mcpState(),
    })

    /**
     * 按 since 取增量。entries 的 seq 严格递增,起点可直接算下标,无需全表
     * filter;since 早于保留窗口时退化为「最近 DEFAULT_FIRST 条」。
     */
    const sliceSince = (since) => {
      if (entries.length === 0) return []
      const firstSeq = entries[0].seq
      if (!Number.isFinite(since) || since < firstSeq) return entries.slice(-DEFAULT_FIRST)
      const start = since - firstSeq + 1
      if (start >= entries.length) return []
      return start <= 0 ? entries.slice() : entries.slice(start)
    }

    // ---- 同源私有路由:GET 增量,POST 清空 ----
    ctx.effect(() => webServer.register({
      kind: 'exact',
      path: ROUTE,
      async handler(req, res) {
        try {
          if (req.method !== 'GET') {
            jsonResponse(res, 405, { ok: false, error: 'GET only' })
            return
          }
          const url = new URL(req.url, 'http://local')
          const sinceRaw = url.searchParams.get('since')
          const since = Number(sinceRaw)
          // stats=1 心律模式:零条目仅统计(图标徽章轮询/agent 存活检查用)
          const statsOnly = url.searchParams.get('stats') === '1'
          let out = statsOnly
            ? []
            : (sinceRaw === null ? entries.slice(-DEFAULT_FIRST) : sliceSince(since))
          // 紧凑模式(agent 友好):去掉 text/hex 双份载荷,只留单字段按码点截断
          if (!statsOnly && url.searchParams.get('compact') === '1') {
            const rawMax = Number(url.searchParams.get('max'))
            const max = Number.isFinite(rawMax) && rawMax > 0 ? Math.min(Math.floor(rawMax), 4096) : 512
            out = out.map((e) => ({
              seq: e.seq, dir: e.dir, tool: e.tool, tag: e.tag, len: e.len, dtMs: e.dtMs,
              payload: truncateCodePoints(e.text || e.hex || '', max),
            }))
          }
          jsonResponse(res, 200, { ok: true, entries: out, stats: view() })
        } catch (error) {
          jsonResponse(res, 500, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    }), 'dsh-serial-panel: ' + ROUTE)

    ctx.effect(() => webServer.register({
      kind: 'exact',
      path: ROUTE_CLEAR,
      async handler(req, res) {
        try {
          if (req.method !== 'POST') {
            jsonResponse(res, 405, { ok: false, error: 'POST only' })
            return
          }
          if (blockedForeignWrite(req, res)) return // v1.8.2:跨站页面不许截断共享日志
          // 截断共享日志(MCP 侧检测收缩会自动重开);目录不存在则先建
          try {
            const dir = path.dirname(file)
            if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true })
            fs.writeFileSync(file, '')
          } catch (e) { /* 文件尚不存在时无需截断 */ }
          reset()
          jsonResponse(res, 200, { ok: true, stats: view() })
        } catch (error) {
          jsonResponse(res, 500, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    }), 'dsh-serial-panel: ' + ROUTE_CLEAR)

    // 导出 Host 内存环(≤MAX_ENTRIES 条,非完整磁盘日志)为 jsonl/csv 附件
    ctx.effect(() => webServer.register({
      kind: 'exact',
      path: ROUTE_EXPORT,
      async handler(req, res) {
        try {
          if (req.method !== 'GET') {
            jsonResponse(res, 405, { ok: false, error: 'GET only' })
            return
          }
          const url = new URL(req.url, 'http://local')
          const format = url.searchParams.get('format') === 'csv' ? 'csv' : 'jsonl'
          const stamp = new Date().toISOString().replace(/[:.]/g, '-')
          const body = format === 'csv'
            ? toCsv(entries)
            : entries.map((e) => JSON.stringify(e)).join('\n') + (entries.length ? '\n' : '')
          res.writeHead(200, {
            'content-type': format === 'csv' ? 'text/csv; charset=utf-8' : 'application/jsonl; charset=utf-8',
            'content-disposition': 'attachment; filename="acccom-serial-' + stamp + '.' + format + '"',
            'cache-control': 'no-store',
          })
          res.end(body)
        } catch (error) {
          jsonResponse(res, 500, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    }), 'dsh-serial-panel: ' + ROUTE_EXPORT)

    // agent 自发现:返回本目录 AGENTS.md(部署闭包内也有副本),首次读取后缓存
    let helpBody = null
    ctx.effect(() => webServer.register({
      kind: 'exact',
      path: ROUTE_HELP,
      async handler(req, res) {
        try {
          if (req.method !== 'GET') {
            jsonResponse(res, 405, { ok: false, error: 'GET only' })
            return
          }
          if (helpBody === null) {
            try { helpBody = fs.readFileSync(path.join(__dirname, 'AGENTS.md'), 'utf8') } catch (e) { helpBody = '# AGENTS.md not deployed\n\n接口速见:GET /api/acccom-serial?since=<seq>&compact=1\n' }
          }
          res.writeHead(200, {
            'content-type': 'text/markdown; charset=utf-8',
            'cache-control': 'no-store',
          })
          res.end(helpBody)
        } catch (error) {
          jsonResponse(res, 500, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    }), 'dsh-serial-panel: ' + ROUTE_HELP)

    // v1.8.0:MCP 启停开关。GET 返回当前状态(随主路由 view() 一并给面板轮询);
    // POST {enabled:bool} 切条目。注意:切换只影响「新会话」的工具注册——
    // 已开会话的工具列表是快照,不会变(UI 需提示,别让用户以为立刻生效)。
    ctx.effect(() => webServer.register({
      kind: 'exact',
      path: ROUTE_MCP,
      async handler(req, res) {
        try {
          if (req.method === 'GET') {
            jsonResponse(res, 200, { ok: true, mcp: mcpState() })
            return
          }
          if (req.method !== 'POST') {
            jsonResponse(res, 405, { ok: false, error: 'GET or POST only' })
            return
          }
          if (blockedForeignWrite(req, res)) return // v1.8.2:跨站页面不许关掉 MCP
          const current = mcpState()
          if (!current.available) {
            jsonResponse(res, 404, { ok: false, error: 'mcp entry not found: ' + MCP_ENTRY_ID })
            return
          }
          if (!pluginManager || typeof pluginManager.setPluginEnabled !== 'function') {
            jsonResponse(res, 501, { ok: false, error: 'pluginManager service unavailable' })
            return
          }
          const body = await readJsonBody(req)
          // v1.8.2:必须是显式布尔。旧实现 `!!(body && body.enabled === true)` 会把
          // 解析失败的 body / 漏传字段一律当成 false,即「一次畸形请求 = 静默关闭 MCP」。
          if (!body || typeof body.enabled !== 'boolean') {
            jsonResponse(res, 400, { ok: false, error: 'body must be {"enabled": true|false}' })
            return
          }
          const enabled = body.enabled
          if (current.enabled === enabled) {
            jsonResponse(res, 200, { ok: true, mcp: current, changed: false })
            return
          }
          await pluginManager.setPluginEnabled(MCP_ENTRY_ID, enabled)
          jsonResponse(res, 200, { ok: true, mcp: mcpState(), changed: true })
        } catch (error) {
          if (error instanceof BodyTooLarge || (error && error.status === 413)) {
            jsonResponse(res, 413, { ok: false, error: error.message })
            return
          }
          jsonResponse(res, 500, { ok: false, error: error instanceof Error ? error.message : String(error) })
        }
      },
    }), 'dsh-serial-panel: ' + ROUTE_MCP)

    return () => {
      clearInterval(timer)
      reset()
    }
  },
}
