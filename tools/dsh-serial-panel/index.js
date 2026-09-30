'use strict'
// ============================================================
//  dsh-serial-panel — Host 半区(永久 cordis 插件)  v1.1.0
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
// ============================================================

const fs = require('fs')
const path = require('path')

const ROUTE = '/api/acccom-serial'
const ROUTE_CLEAR = '/api/acccom-serial/clear'

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
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store',
  })
  res.end(JSON.stringify(body))
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

    const file = logPath()
    let offset = 0          // 已读字节偏移
    let remainder = ''      // 半行残留(写入端按行追加,可能读到半行)
    let firstRead = true    // 是否尚未读过(决定能否启用尾部窗口)
    let seq = 0             // 单调递增序号(不随 clear/轮转重置,客户端 since 依赖它)
    const entries = []      // { seq, id, tool, dir, hex, text, tag, ts, len },seq 递增
    const stats = { tx: 0, rx: 0, txBytes: 0, rxBytes: 0, lastAt: null, exists: false }

    const reset = () => {
      offset = 0
      remainder = ''
      firstRead = true
      entries.length = 0
      stats.tx = 0; stats.rx = 0; stats.txBytes = 0; stats.rxBytes = 0; stats.lastAt = null
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
      if (remainder.length > MAX_LINE_CHARS) remainder = ''
      for (const line of lines) {
        const s = line.trim()
        if (!s) continue
        let rec
        try { rec = JSON.parse(s) } catch (e) { continue } // 坏行跳过,不致命
        seq += 1
        const dir = rec.direction === 'TX' ? 'TX' : 'RX'
        const len = payloadLen(rec)
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
          const buf = Buffer.allocUnsafe(st.size - from)
          const read = fs.readSync(fd, buf, 0, buf.length, from)
          offset = st.size
          firstRead = false
          let text = buf.toString('utf8', 0, read)
          if (dropPartial) {
            const nl = text.indexOf('\n')
            text = nl >= 0 ? text.slice(nl + 1) : '' // 本窗口还没写满一行:整段丢弃
          }
          if (text) consume(text)
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
      diag: SHELL_DIAG,
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
          const out = sinceRaw === null ? entries.slice(-DEFAULT_FIRST) : sliceSince(since)
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

    return () => {
      clearInterval(timer)
      reset()
    }
  },
}
