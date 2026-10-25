'use strict'
// ============================================================
//  dsh-serial-panel — Host 半区行为测试(无框架,纯 node)
//
//  运行(在插件目录下):
//    node test.js core     # 增量/半行/切片/批量裁剪/轮转/清空/方法守卫/诊断
//    node test.js window   # 首次挂载只读尾部窗口(大日志不全量读入)
//    node test.js api      # Δt/compact/导出/链路诊断(需要默认 MAX_ENTRIES≥40,
//                          #   勿沿用 core 的 =5,否则环裁剪会吞掉断言目标行)
//    node test.js client   # client.js 注册结构冒烟(stub React,不起浏览器)
//    node test.js mcp      # MCP 启停开关 + 写路由同源防护 + body 上限
//    node test.js chunk    # 分块尾随读 + 跨块边界多字节字符
//    node test.js http     # 真实 http.Server 冒烟(桩测不出的连接/流式问题)
//
//  用环境变量把插件参数压小,便于快速触发边界:
//    DSH_SERIAL_PANEL_LOG / DSH_SERIAL_PANEL_MAX_ENTRIES / DSH_SERIAL_PANEL_FIRST_TAIL
//
//  说明:插件在 require 时读取这些常量,所以必须在 require 之前设置 env。
// ============================================================

const fs = require('fs')
const path = require('path')
const os = require('os')

const mode = process.argv[2] || 'core'
// chunk 模式要读**整份**大于 1MiB 的日志来验证分块解码,不能走首次尾部窗口。
// 插件常量在 require 时求值,所以必须在下面 require 之前设置。
if (mode === 'chunk') process.env.DSH_SERIAL_PANEL_FIRST_TAIL = String(8 * 1024 * 1024)
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'dsh-sp-'))
const logFile = process.env.DSH_SERIAL_PANEL_LOG

if (!logFile && mode !== 'client') { console.error('DSH_SERIAL_PANEL_LOG must be set'); process.exit(1) }

// ---- 真实日志护栏 ----
// 本套件里有几条断言会真的 `POST /clear`(方法守卫、同源放行、curl 式放行),
// 那是**真的截断** DSH_SERIAL_PANEL_LOG 指向的文件。若哪天 env 漏设/拼错而回落到
// 插件的默认路径,就会把用户真实的 %LOCALAPPDATA%\ACCcom\mcp-traffic.jsonl 清空
// (曾发生过一次:文件被写成 0 字节)。这里直接拒绝任何指向真实日志或非临时目录的
// 路径,让「测试污染真实数据」在物理上不可能发生,而不是靠调用者记得设 env。
if (logFile) {
  const resolved = path.resolve(logFile)
  const realLog = path.resolve(path.join(
    process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'),
    'ACCcom', 'mcp-traffic.jsonl'))
  const tmpRoot = path.resolve(os.tmpdir())
  if (resolved === realLog) {
    console.error('拒绝运行:DSH_SERIAL_PANEL_LOG 指向真实日志 ' + realLog
      + '\n本套件会真实截断该文件。请指向临时目录。')
    process.exit(1)
  }
  if (!resolved.startsWith(tmpRoot + path.sep)) {
    console.error('拒绝运行:DSH_SERIAL_PANEL_LOG 必须在临时目录内(当前 ' + resolved + ')'
      + '\n理由同上:测试会真实截断该文件。')
    process.exit(1)
  }
  fs.mkdirSync(path.dirname(resolved), { recursive: true })
}

const line = (id, dir, text, tag, ts) => JSON.stringify({
  id, tool: 'send', timestamp: ts || '2026-08-18T10:00:00.123+08:00',
  direction: dir,
  rawHex: Buffer.from(text, 'utf8').toString('hex').toUpperCase(),
  text, portTag: tag || '',
})

const mod = require(path.join(__dirname, 'index.js'))
const routes = {}
const ctx = {
  get: (name) => (name === 'webServer' ? { register: (r) => { routes[r.path] = r.handler } } : undefined),
  effect: (fn) => fn(),
}
const dispose = mod.apply(ctx)
let dispose2 = null

const call = (route, req) => new Promise((resolve) => {
  const res = { writeHead() {}, end: (t) => resolve(JSON.parse(t)) }
  routes[route](req, res)
})
// export 路由返回非 JSON 附件,需要捕获状态码/响应头/原始 body
const callRaw = (route, req) => new Promise((resolve) => {
  const cap = { status: 0, headers: {}, body: '' }
  const res = {
    writeHead: (s, h2) => { cap.status = s; cap.headers = h2 || {} },
    end: (t) => { cap.body = t; resolve(cap) },
  }
  routes[route](req, res)
})
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
let checks = 0
const ok = (cond, label, extra) => {
  checks += 1
  if (!cond) throw new Error('FAIL: ' + label + (extra ? '  -> ' + JSON.stringify(extra) : ''))
  console.log('  ok  ' + label)
}

;(async () => {
  if (mode === 'core') {
    const max = Number(process.env.DSH_SERIAL_PANEL_MAX_ENTRIES)

    // T1 首次读取
    fs.writeFileSync(logFile, line(1, 'TX', 'hello') + '\n' + line(2, 'RX', 'world') + '\n')
    await sleep(600)
    let r = await call('/api/acccom-serial', { method: 'GET', url: '/' })
    console.log('T1 initial:', r.ok, 'entries=' + r.entries.length, 'lastSeq=' + r.stats.lastSeq)
    ok(r.ok && r.entries.length === 2, 'T1 首次读取返回全部行')
    ok(r.stats.lastSeq === 2 && r.stats.tx === 1 && r.stats.rx === 1, 'T1 统计与序号正确', r.stats)
    ok(r.stats.diag && typeof r.stats.diag === 'object', 'T1 附带 shell 诊断', r.stats.diag && Object.keys(r.stats.diag))
    ok(r.entries[0].dtMs === null && r.entries[1].dtMs === 0, 'T1 Δt:首条 null,同毫秒钳 0', r.entries.map((e) => e.dtMs))

    // T2 半行拼接(写入端分批写)
    fs.appendFileSync(logFile, line(3, 'RX', 'part'))
    await sleep(600)
    fs.appendFileSync(logFile, '\n' + line(4, 'TX', 'full', 'p2') + '\n')
    await sleep(600)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=2' })
    ok(r.entries.length === 2 && r.entries[0].seq === 3 && r.entries[0].text === 'part', 'T2 半行残留拼接正确', r.entries)
    ok(r.entries[1].tag === 'p2', 'T2 端口 tag 透传')

    // T3 since 切片(直接算下标)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=3' })
    ok(r.entries.length === 1 && r.entries[0].seq === 4, 'T3 since 精确切片', r.entries.map((e) => e.seq))
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=4' })
    ok(r.entries.length === 0, 'T3 已到末尾返回空')

    // T4 批量裁剪(MAX_ENTRIES=5,写入到 12 条)
    for (let i = 5; i <= 12; i++) fs.appendFileSync(logFile, line(i, i % 2 ? 'TX' : 'RX', 'x' + i) + '\n')
    await sleep(700)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=12' })
    ok(r.stats.kept === max, 'T4 内存环裁剪到上限 ' + max, { kept: r.stats.kept })
    ok(r.stats.lastSeq === 12, 'T4 序号继续单调(不被裁剪重置)', r.stats.lastSeq)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=1' })
    ok(r.entries.length === max && r.entries[0].seq === 8, 'T4 早于窗口的 since 退化为保留窗口', { n: r.entries.length, first: r.entries[0] && r.entries[0].seq })

    // T5 轮转/清空 → 文件变小,重置偏移
    fs.writeFileSync(logFile, line(99, 'RX', 'fresh') + '\n')
    await sleep(600)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0' })
    ok(r.entries.length === 1 && r.entries[0].text === 'fresh', 'T5 文件变小后重新同步', r.entries)

    // T6 清空路由 + 方法守卫
    r = await call('/api/acccom-serial/clear', { method: 'POST', url: '/' })
    ok(r.ok && fs.statSync(logFile).size === 0, 'T6 POST clear 截断文件')
    r = await call('/api/acccom-serial', { method: 'POST', url: '/' })
    ok(r.ok === false && /GET only/.test(r.error), 'T6 GET 路由拒绝 POST')
    r = await call('/api/acccom-serial/clear', { method: 'GET', url: '/' })
    ok(r.ok === false && /POST only/.test(r.error), 'T6 clear 路由拒绝 GET')

    // T7 坏行不致命
    fs.appendFileSync(logFile, '{not json\n' + line(100, 'RX', 'after-bad') + '\n')
    await sleep(600)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0' })
    ok(r.ok && r.entries.length === 1 && r.entries[0].text === 'after-bad', 'T7 坏行跳过,后续行照常解析', r.entries)
    ok(r.stats.badLines === 1, 'T7 坏行计数入 stats', r.stats.badLines)
  }

  if (mode === 'window') {
    // T8 首次挂载只读尾部窗口:先造一个大日志(每行 ~70B),窗口压到 300B
    const lines = []
    for (let i = 1; i <= 60; i++) lines.push(line(i, i % 2 ? 'TX' : 'RX', 'payload-' + i))
    fs.writeFileSync(logFile, lines.join('\n') + '\n')
    const size = fs.statSync(logFile).size

    const fresh = require(path.join(__dirname, 'index.js'))
    const r2 = {}
    const ctx2 = {
      get: (n) => (n === 'webServer' ? { register: (r) => { r2[r.path] = r.handler } } : undefined),
      effect: (fn) => fn(),
    }
    dispose2 = fresh.apply(ctx2) // tail() 在 apply 内同步跑一次
    const res = await new Promise((resolve) => {
      const rr = { writeHead() {}, end: (t) => resolve(JSON.parse(t)) }
      r2['/api/acccom-serial']({ method: 'GET', url: '/' }, rr)
    })

    console.log('T8 file=' + size + 'B, parsed=' + res.entries.length + ', firstId=' + (res.entries[0] && res.entries[0].id) + ', lastId=' + (res.entries[res.entries.length - 1] && res.entries[res.entries.length - 1].id))
    ok(size > 3000, 'T8 前置条件:日志远大于窗口', size)
    ok(res.entries.length >= 3 && res.entries.length < 20, 'T8 只解析窗口内的少量行', res.entries.length)
    ok(res.entries[res.entries.length - 1].id === 60, 'T8 最后一行完整解析')
    ok(res.entries[0].id > 1 && res.entries[0].id < 60, 'T8 首行来自窗口(证明未全量读入)', res.entries[0].id)
    ok(res.entries.every((e) => typeof e.text === 'string' && e.text.startsWith('payload-')), 'T8 无半行造成的脏数据')
  }

  if (mode === 'api') {
    // 防呆:core 模式遗留的 MAX_ENTRIES=5 会把断言目标行裁出内存环;
    // FIRST_TAIL 遗留则会让首次读走尾部窗口、丢掉首行,伪装成「Δt 算错」。
    const envMax = Number(process.env.DSH_SERIAL_PANEL_MAX_ENTRIES || 0)
    if (envMax > 0 && envMax < 40) {
      throw new Error('api 模式需要 MAX_ENTRIES≥40(或 Remove-Item Env:DSH_SERIAL_PANEL_MAX_ENTRIES 后运行)')
    }
    const envTail = Number(process.env.DSH_SERIAL_PANEL_FIRST_TAIL || 0)
    if (envTail > 0 && envTail < 8192) {
      throw new Error('api 模式需要 FIRST_TAIL≥8192 或未设置(否则尾部窗口会吞掉首行,断言变成假阴性)')
    }

    // T9 Δt / 链路诊断字段
    const raw = (obj) => JSON.stringify(obj)
    fs.writeFileSync(logFile, [
      line(1, 'TX', 'dt1', '', '2026-08-18T10:00:01.000+08:00'),
      line(2, 'RX', 'dt2', '', '2026-08-18T10:00:02.250+08:00'),
      line(3, 'TX', 'dt3', '', '2026-08-18T10:00:02.000+08:00'),
      line(4, 'TX', 'badts', '', 'not-a-date'),
      raw({ id: 5, tool: 'send', timestamp: '2026-08-18T10:00:03.000+08:00', direction: 'TX', rawHex: '', text: '😀😀', portTag: '' }),
      raw({ id: 6, tool: 'send', timestamp: '2026-08-18T10:00:03.100+08:00', direction: 'RX', rawHex: '', text: 'x'.repeat(600), portTag: 'p9' }),
      raw({ id: 7, tool: 'send', timestamp: '2026-08-18T10:00:03.200+08:00', direction: 'RX', rawHex: '', text: 'y'.repeat(5000), portTag: '' }),
      raw({ id: 8, tool: 'send', timestamp: '2026-08-18T10:00:03.300+08:00', direction: 'TX', rawHex: 'DEADBEEF', text: '', portTag: '' }),
      raw({ id: 9, tool: 'send', timestamp: '2026-08-18T10:00:03.400+08:00', direction: 'TX', rawHex: '', text: 'a,b"c\nd', portTag: '' }),
    ].join('\n') + '\n')
    await sleep(600)
    let r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0' })
    const byId = {}
    for (const e of r.entries) byId[e.id] = e
    ok(byId[1] && byId[1].dtMs === null, 'T9 首条 Δt 为 null', byId[1] && byId[1].dtMs)
    ok(byId[2] && byId[2].dtMs === 1250, 'T9 Δt 正常计算', byId[2] && byId[2].dtMs)
    ok(byId[3] && byId[3].dtMs === 0, 'T9 乱序时间戳钳 0', byId[3] && byId[3].dtMs)
    ok(byId[4] && byId[4].dtMs === null, 'T9 坏时间戳记 null(基准不推进)', byId[4] && byId[4].dtMs)
    ok(typeof r.stats.logSize === 'number' && r.stats.logSize > 0
      && typeof r.stats.logMtimeMs === 'number' && typeof r.stats.rotated === 'boolean'
      && r.stats.badLines === 0, 'T9 链路诊断字段齐备', r.stats)

    // stats=1 心律模式:零条目仅统计
    const rh = await call('/api/acccom-serial', { method: 'GET', url: '/?stats=1' })
    ok(rh.ok && rh.entries.length === 0 && rh.stats.lastSeq === 9 && rh.stats.tx === 6,
      'T9 stats=1 返回零条目仅统计', rh.stats)

    // T10 compact 模式(compact 输出无 id 字段,按 seq 索引;此文件 seq=id)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0&compact=1' })
    const c = {}
    for (const e of r.entries) c[e.seq] = e
    ok(!('text' in c[1]) && !('hex' in c[1]) && typeof c[1].payload === 'string', 'T10 去掉 text/hex 只留 payload', Object.keys(c[1] || {}))
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0&compact=1&max=1' })
    ok(r.entries.find((e) => e.seq === 5).payload === '😀', 'T10 截断按码点(代理对不劈开)', r.entries.find((e) => e.seq === 5))
    ok(c[6].payload.length === 512, 'T10 默认 max=512', c[6] && c[6].payload.length)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0&compact=1&max=999999' })
    ok(r.entries.find((e) => e.seq === 7).payload.length === 4096, 'T10 max 上限 4096', r.entries.find((e) => e.seq === 7).payload.length)
    r = await call('/api/acccom-serial', { method: 'GET', url: '/?since=0&compact=1&max=8' })
    ok(r.entries.find((e) => e.seq === 8).payload === 'DEADBEEF', 'T10 text 空回落 hex', r.entries.find((e) => e.seq === 8))

    // T11 导出 jsonl/csv + 方法守卫
    const rJsonl = await callRaw('/api/acccom-serial/export', { method: 'GET', url: '/api/acccom-serial/export?format=jsonl' })
    const jsonlLines = rJsonl.body.split('\n').filter(Boolean)
    ok(rJsonl.status === 200
      && /jsonl/.test(rJsonl.headers['content-type'] || '')
      && /acccom-serial-/.test(rJsonl.headers['content-disposition'] || '')
      && jsonlLines.length === r.entries.length
      && typeof JSON.parse(jsonlLines[0]).seq === 'number', 'T11 jsonl 导出逐行可解析', { n: jsonlLines.length, h: rJsonl.headers })
    const rCsv = await callRaw('/api/acccom-serial/export', { method: 'GET', url: '/api/acccom-serial/export?format=csv' })
    ok(rCsv.status === 200
      && /csv/.test(rCsv.headers['content-type'] || '')
      && rCsv.body.charCodeAt(0) === 0xFEFF
      && rCsv.body.slice(1).startsWith('seq,ts,dir,tool,tag,len,dtMs,text,hex'), 'T11 csv 表头与 BOM', rCsv.body.slice(0, 60))
    ok(rCsv.body.indexOf('"a,b""c\nd"') >= 0, 'T11 csv 逗号/引号/换行转义', JSON.stringify(rCsv.body.slice(-60)))
    const r405 = await call('/api/acccom-serial/export', { method: 'POST', url: '/' })
    ok(r405.ok === false && /GET only/.test(r405.error), 'T11 export 拒绝 POST')

    // T11b help 路由:返回 AGENTS.md(agent 自发现)
    const rHelp = await callRaw('/api/acccom-serial/help', { method: 'GET', url: '/api/acccom-serial/help' })
    ok(rHelp.status === 200
      && /markdown/.test(rHelp.headers['content-type'] || '')
      && /api\/acccom-serial/.test(rHelp.body), 'T11b help 路由返回 AGENTS.md', { s: rHelp.status, h: rHelp.headers })
    const rHelp405 = await call('/api/acccom-serial/help', { method: 'POST', url: '/' })
    ok(rHelp405.ok === false && /GET only/.test(rHelp405.error), 'T11b help 拒绝 POST')
  }

  if (mode === 'mcp') {
    // T15 MCP 启停开关 + 写路由同源防护(v1.8.2 新增覆盖)
    // 用可编程的 ctx 直接驱动路由,不起浏览器、不碰真实 profile。
    fs.writeFileSync(logFile, line(1, 'TX', 'seed') + '\n')

    const mkRoutes = (opts) => {
      const r = {}
      const fakeLoader = { entries: () => [{ id: 'include:mcp-acccom', disabled: !!opts.disabled }] }
      const calls = { setPluginEnabled: [] }
      const ctx = {
        get: (n) => {
          if (n === 'webServer') return { register: (x) => { r[x.path] = x.handler } }
          if (n === 'loader') return opts.noLoader ? undefined : fakeLoader
          if (n === 'pluginManager') {
            return opts.noManager ? undefined : {
              setPluginEnabled: (id, enabled) => {
                calls.setPluginEnabled.push([id, enabled])
                opts.disabled = !enabled
                return Promise.resolve({ changed: true, application: 'applied' })
              },
            }
          }
          return undefined
        },
        effect: (fn) => fn(),
      }
      const dispose3 = require(path.join(__dirname, 'index.js')).apply(ctx)
      return { routes: r, calls, dispose: dispose3 }
    }

    // 带 headers + 状态码的调用器(async iterable body 供 readJsonBody 用)
    const callFull = async (routes, path, method, opts) => {
      const o = opts || {}
      const cap = { status: 0, body: null }
      const bodyText = o.body === undefined ? '' : o.body
      const req = {
        method,
        url: o.url || '/',
        headers: o.headers || {},
        async* [Symbol.asyncIterator]() {
          if (bodyText) yield Buffer.from(bodyText, 'utf8')
        },
      }
      const res = {
        writeHead: (s) => { cap.status = s },
        end: (t) => { try { cap.body = JSON.parse(t) } catch (e) { cap.body = t } },
      }
      await routes[path](req, res)
      return cap
    }

    // --- 状态读取(loader 说 enabled / disabled 都要如实反映) ---
    let h = mkRoutes({ disabled: true })
    let res = await callFull(h.routes, '/api/acccom-serial/mcp', 'GET')
    ok(res.status === 200 && res.body.mcp.available === true && res.body.mcp.enabled === false,
      'T15 GET 状态:entry 存在且 disabled → enabled:false', res.body)
    h.dispose()
    h = mkRoutes({ disabled: false })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'GET')
    ok(res.status === 200 && res.body.mcp.enabled === true, 'T15 GET 状态:未 disabled → enabled:true', res.body)
    h.dispose()

    // --- loader 里没有该条目 → available:false(UI 据此置灰 chip) ---
    h = mkRoutes({ noLoader: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'GET')
    ok(res.status === 200 && res.body.mcp.available === false && res.body.mcp.enabled === null,
      'T15 loader 缺条目 → available:false', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":true}' })
    ok(res.status === 404 && /mcp entry not found/.test(res.body.error), 'T15 缺条目时 POST 404', res.body)
    h.dispose()

    // --- 正常切换走 pluginManager ---
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":true}' })
    ok(res.status === 200 && res.body.ok === true && res.body.changed === true && res.body.mcp.enabled === true,
      'T15 POST 切换后 changed:true 且状态翻转', res.body)
    ok(h.calls.setPluginEnabled.length === 1
      && h.calls.setPluginEnabled[0][0] === 'include:mcp-acccom'
      && h.calls.setPluginEnabled[0][1] === true,
      'T15 切换调用 pluginManager.setPluginEnabled(include:mcp-acccom, true)', h.calls.setPluginEnabled)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":true}' })
    ok(res.status === 200 && res.body.changed === false && h.calls.setPluginEnabled.length === 1,
      'T15 幂等:同状态再 POST 不再调 setPluginEnabled', res.body)
    h.dispose()

    // --- pluginManager 服务缺失 → 501(而不是静默成功) ---
    h = mkRoutes({ disabled: true, noManager: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":true}' })
    ok(res.status === 501 && /pluginManager/.test(res.body.error), 'T15 缺 pluginManager → 501', res.body)
    h.dispose()

    // --- 畸形 body 必须 400,绝不能静默当成 enabled:false(否则=越权关闭 MCP) ---
    h = mkRoutes({ disabled: false })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":"yes"}' })
    ok(res.status === 400 && h.calls.setPluginEnabled.length === 0,
      'T15 非布尔 enabled → 400 且不调 setPluginEnabled', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: 'not json' })
    ok(res.status === 400 && h.calls.setPluginEnabled.length === 0, 'T15 坏 JSON → 400(不默认关)', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '' })
    ok(res.status === 400 && h.calls.setPluginEnabled.length === 0, 'T15 空 body → 400', res.body)
    h.dispose()

    // --- body 上限 → 413,且**必须排空请求体**:否则 keep-alive 连接上
    //     下一个请求会 ECONNRESET(真实 http.Server 实测到的坑,桩测不出来)。
    h = mkRoutes({ disabled: false })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: JSON.stringify({ enabled: false, pad: 'x'.repeat(70000) }) })
    ok(res.status === 413 && h.calls.setPluginEnabled.length === 0, 'T15 超大 body → 413', { s: res.status, b: res.body })
    h.dispose()

    // --- 同源防护:跨站写一律 403(实测过 Origin: evil 能截断日志/关 MCP) ---
    const CROSS = [
      ['Sec-Fetch-Site: cross-site', { 'sec-fetch-site': 'cross-site' }],
      ['异源 Origin', { origin: 'https://evil.example', host: '127.0.0.1:61808' }],
      ['异源 Referer', { referer: 'https://evil.example/x', host: '127.0.0.1:61808' }],
    ]
    for (const [label, headers] of CROSS) {
      h = mkRoutes({ disabled: false })
      res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
        { headers: Object.assign({ 'content-type': 'application/json' }, headers), body: '{"enabled":true}' })
      ok(res.status === 403 && /cross-origin/.test(res.body.error) && h.calls.setPluginEnabled.length === 0,
        'T15 跨站写被拒(403):' + label, res.body)
      res = await callFull(h.routes, '/api/acccom-serial/clear', 'POST', { headers })
      ok(res.status === 403 && /cross-origin/.test(res.body.error),
        'T15 跨站 clear 被拒(403):' + label, res.body)
      h.dispose()
    }

    // --- AGENTS.md 的干净复现工作流:curl 不带浏览器标记 → 必须仍然放行 ---
    h = mkRoutes({ disabled: false })
    res = await callFull(h.routes, '/api/acccom-serial/clear', 'POST', {})
    ok(res.status === 200 && res.body.ok === true, 'T15 无浏览器标记的 curl 式 clear 放行', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":false}' })
    ok(res.status === 200 && res.body.changed === true, 'T15 无浏览器标记的 curl 式 MCP 切换放行', res.body)
    h.dispose()

    // --- 同源(Origin 与 Host 一致)→ 放行 ---
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      {
        headers: {
          'content-type': 'application/json',
          'sec-fetch-site': 'same-origin',
          origin: 'http://127.0.0.1:61808',
          host: '127.0.0.1:61808',
        },
        body: '{"enabled":true}',
      })
    ok(res.status === 200 && res.body.changed === true, 'T15 页内同源写放行', res.body)
    h.dispose()

    // --- 桌面端(Electron)形态:必须放行,否则 MCP 开关在桌面版点了没反应 ---
    // 桌面壳自定义协议 dsh-app://app 载入界面,forwardWebRequest 会剥掉
    // host/origin/cookie/sec-fetch-site 再转发 → 后端看到的是「无标记」请求。
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      { headers: { 'content-type': 'application/json' }, body: '{"enabled":true}' })
    ok(res.status === 200 && res.body.changed === true,
      'T15 桌面端形态(无 origin/sec-fetch-site)开关放行', res.body)
    ok(h.calls.setPluginEnabled.length === 1, 'T15 桌面端点击确实调到 setPluginEnabled', h.calls.setPluginEnabled)
    h.dispose()

    // 桌面壳若将来不再剥头,Origin 会是 dsh-app://app;且跨 scheme 访问必然带
    // Sec-Fetch-Site: cross-site。这条必须放行(否则桌面端开关再次失效)。
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      {
        headers: {
          'content-type': 'application/json',
          origin: 'dsh-app://app',
          'sec-fetch-site': 'cross-site', // 跨 scheme 的真实标记
          host: '127.0.0.1:61808',
        },
        body: '{"enabled":true}',
      })
    ok(res.status === 200 && res.body.changed === true,
      'T15 桌面端 Origin=dsh-app://app(即使带 cross-site)仍放行', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/clear', 'POST',
      { headers: { origin: 'dsh-app://app', 'sec-fetch-site': 'cross-site', host: '127.0.0.1:61808' } })
    ok(res.status === 200, 'T15 桌面端 clear 同样放行', res.body)
    h.dispose()

    // 但 dsh-app:// 以外的自定义协议不能网开一面
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'POST',
      {
        headers: { 'content-type': 'application/json', origin: 'dsh-appx://app', host: '127.0.0.1:61808' },
        body: '{"enabled":true}',
      })
    ok(res.status === 403, 'T15 近似协议名(dsh-appx://)不被误放行', res.body)
    h.dispose()

    // --- GET 不受同源判定影响(异源 Origin 的读请求照常),方法守卫仍在 ---
    h = mkRoutes({ disabled: true })
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'GET',
      { headers: { origin: 'https://evil.example' } })
    ok(res.status === 200, 'T15 异源 GET 只读放行(写才拦)', res.body)
    res = await callFull(h.routes, '/api/acccom-serial/mcp', 'PUT', {})
    ok(res.status === 405, 'T15 PUT → 405 方法守卫', res.body)
    h.dispose()
  }

  if (mode === 'chunk') {
    // T16 v1.8.2 分块读(1MiB/块 + 流式 TextDecoder):
    //   1) 大于一块的增量必须完整解析;
    //   2) **多字节字符必须正好骑在块边界上**——这是分块解码最典型的坑。
    //      不能靠"大概会跨"，必须精确算出偏移并自校验(否则测试是假牙)。
    const CHUNK = 1024 * 1024
    const filler = 'a'.repeat(4000)
    const rows = []
    let total = 0
    let guard = 0
    while (total < CHUNK - 20000 && guard++ < 1000) {
      const r = line(rows.length + 1, 'TX', filler, '', '2026-08-18T10:00:00.000+08:00')
      rows.push(r)
      total += Buffer.byteLength(r + '\n', 'utf8')
    }
    // 现在精确构造一行,让其中「汉」的首字节正好落在 CHUNK-1
    // (即 1 字节在块 1、2 字节在块 2 —— 最坏切法)
    const mkStraddler = (k) => JSON.stringify({
      id: rows.length + 1, tool: 'straddle', timestamp: '2026-08-18T10:00:01.000+08:00',
      direction: 'RX', rawHex: '', text: 'a'.repeat(k) + '汉字跨块' + 'b'.repeat(10), portTag: '',
    })
    const prefixLen = total // 本行之前的全部字节(每行都含尾随 \n)
    const probe = mkStraddler(0)
    const offInLine = Buffer.byteLength(probe.slice(0, probe.indexOf('汉')), 'utf8')
    const k = CHUNK - 1 - prefixLen - offInLine
    if (k < 0 || k > 600000) throw new Error('chunk 模式构造失败:k=' + k + '(调整填充步长)')
    const straddler = mkStraddler(k)
    rows.push(straddler)
    for (let i = 0; i < 8; i++) {
      rows.push(line(rows.length + 1, i % 2 ? 'TX' : 'RX', '尾部-' + i, '', '2026-08-18T10:00:02.000+08:00'))
    }
    const buf = Buffer.from(rows.join('\n') + '\n', 'utf8')
    fs.writeFileSync(logFile, buf)
    const fileSize = buf.length
    const wantText = 'a'.repeat(k) + '汉字跨块' + 'b'.repeat(10)

    // 自校验:确认"汉"的字面字节确实在 CHUNK-1 处被切开
    const han = Buffer.from('汉', 'utf8') // e6 b1 89
    const cutOk = buf[CHUNK - 1] === han[0] && buf[CHUNK] === han[1] && buf[CHUNK + 1] === han[2]
    ok(cutOk, 'T16 前置条件:多字节字符精确骑在块边界上', {
      at: [buf[CHUNK - 1], buf[CHUNK], buf[CHUNK + 1]].map((b) => b.toString(16)),
      want: [...han].map((b) => b.toString(16)),
    })
    ok(rows.length < 300, 'T16 前置条件:条目数少于首屏上限 300(可单次取全)', rows.length)
    ok(fileSize > CHUNK, 'T16 前置条件:日志大于单块(1MiB)', fileSize)

    const fresh = require(path.join(__dirname, 'index.js'))
    const r3 = {}
    const ctx3 = {
      get: (n) => (n === 'webServer' ? { register: (r) => { r3[r.path] = r.handler } } : undefined),
      effect: (fn) => fn(),
    }
    dispose2 = fresh.apply(ctx3)
    const got = await new Promise((resolve) => {
      const rr = { writeHead() {}, end: (t) => resolve(JSON.parse(t)) }
      r3['/api/acccom-serial']({ method: 'GET', url: '/?since=0' }, rr)
    })

    ok(got.stats.badLines === 0, 'T16 分块读无坏行(块边界未截断 JSON 行)', got.stats.badLines)
    ok(got.entries.length === rows.length, 'T16 分块读条目数与写入行数一致',
      { got: got.entries.length, want: rows.length })
    const straddleEntry = got.entries.find((e) => e.tool === 'straddle')
    ok(!!straddleEntry && straddleEntry.text === wantText,
      'T16 跨块边界的多字节字符未被劈开(内容逐字节一致)', straddleEntry && {
        len: (straddleEntry.text || '').length, want: wantText.length,
      })
    const hasReplacement = got.entries.some((e) => String(e.text || '').indexOf('\uFFFD') >= 0)
    ok(!hasReplacement, 'T16 全量文本无 U+FFFD 替换符')
  }

  if (mode === 'http') {
    // T17 真实 http.Server 冒烟:把路由挂到真的 http 服务器上,用真的
    // IncomingMessage/ServerResponse 打一遍。桩(手写 req/res)测不出:
    //   * keep-alive 连接被未排空的请求体污染(413 之后的 ECONNRESET);
    //   * fetch 归一化后的 header 大小写与 undici 的分块发送行为。
    fs.writeFileSync(logFile, line(1, 'TX', 'A', 'COM1') + '\n')
    const http = require('http')
    const handlers = {}
    const setCalls = []
    const dispose3 = require(path.join(__dirname, 'index.js')).apply({
      get: (n) => {
        if (n === 'webServer') return { register: (r) => { handlers[r.path] = r.handler } }
        if (n === 'loader') return { entries: () => [{ id: 'include:mcp-acccom', disabled: true }] }
        if (n === 'pluginManager') {
          return { setPluginEnabled: (id, en) => { setCalls.push([id, en]); return Promise.resolve({}) } }
        }
        return undefined
      },
      effect: (fn) => fn(),
    })
    const server = http.createServer((req, res) => {
      const h = handlers[req.url.split('?')[0]]
      if (!h) { res.writeHead(404); res.end('nf'); return }
      Promise.resolve(h(req, res)).catch((e) => { res.writeHead(500); res.end(String(e)) })
    })
    await new Promise((r) => server.listen(0, '127.0.0.1', r))
    const base = 'http://127.0.0.1:' + server.address().port
    const hit = async (p, opts) => {
      const res = await fetch(base + p, opts)
      let body = null
      try { body = await res.json() } catch (e) { body = null }
      return { status: res.status, body }
    }
    try {
      let r = await hit('/api/acccom-serial?stats=1')
      ok(r.status === 200 && r.body.stats.tx === 1, 'T17 真实 http:GET 统计正常', { s: r.status })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: '{"enabled":true}',
      })
      ok(r.status === 200 && r.body.changed === true, 'T17 真实 http:无浏览器标记的写放行', { s: r.status, b: r.body })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST', headers: { 'content-type': 'application/json', 'sec-fetch-site': 'cross-site' },
        body: '{"enabled":false}',
      })
      ok(r.status === 403, 'T17 真实 http:Sec-Fetch-Site cross-site → 403', { s: r.status, b: r.body })

      r = await hit('/api/acccom-serial/clear', { method: 'POST', headers: { origin: 'https://evil.example' } })
      ok(r.status === 403, 'T17 真实 http:异源 Origin clear → 403', { s: r.status, b: r.body })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST',
        headers: { 'content-type': 'application/json', origin: base, 'sec-fetch-site': 'same-origin' },
        body: '{"enabled":false}',
      })
      ok(r.status === 200, 'T17 真实 http:同源 Origin 放行', { s: r.status, b: r.body })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: '{"enabled":"nope"}',
      })
      ok(r.status === 400, 'T17 真实 http:非布尔 → 400', { s: r.status })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: '{{{',
      })
      ok(r.status === 400, 'T17 真实 http:坏 JSON → 400', { s: r.status })

      r = await hit('/api/acccom-serial/mcp', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ enabled: false, pad: 'x'.repeat(200000) }),
      })
      ok(r.status === 413, 'T17 真实 http:200KB body → 413', { s: r.status })

      // 关键回归:413 之后同一连接上的下一个请求必须成功(请求体已排空)
      r = await hit('/api/acccom-serial?stats=1')
      ok(r.status === 200 && r.body.ok === true, 'T17 413 之后连接仍可用(请求体已排空)', { s: r.status })

      const exp = await fetch(base + '/api/acccom-serial/export?format=csv')
      ok(exp.status === 200 && /csv/.test(exp.headers.get('content-type') || ''), 'T17 真实 http:export csv')
    } finally {
      await new Promise((r) => server.close(r))
      if (typeof dispose3 === 'function') dispose3()
    }
  }

  if (mode === 'client') {
    // T12 client.js 注册结构冒烟:stub 模块环境,验证两条注册分支不跑浏览器
    const ReactStub = {
      createElement: (type, props, ...children) => ({ type, props, children }),
      memo: (f) => f,
      useState: (v) => [typeof v === 'function' ? v() : v, () => {}],
      useEffect: () => {},
      useRef: (v) => ({ current: v }),
      useMemo: (f) => f(),
    }
    let loaded = null
    globalThis.window = { __ModuleLoader__: { load: (m) => { loaded = m } } }
    const injectedStyles = { text: '' }
    globalThis.document = {
      getElementById: () => null,
      createElement: () => ({ textContent: '' }),
      head: { appendChild: (el) => { injectedStyles.text = el.textContent } },
    }
    globalThis.localStorage = { getItem: () => null, setItem: () => {} }
    require(path.join(__dirname, 'client.js'))
    ok(loaded && typeof loaded.factory === 'function', 'T12 模块经 __ModuleLoader__.load 注册')
    const mod = loaded.factory(() => ReactStub)
    ok(Array.isArray(mod.inject) && mod.inject.includes('slots') && mod.inject.includes('sidebarRightTabs')
      && mod.inject.includes('sidebarRight'), 'T12 inject 声明含 slots + rightbar 服务', mod.inject)

    // 纯函数契约(_test 导出)
    const hi = mod._test.highlight('hello world hello', 'hello')
    ok(Array.isArray(hi) && hi.length === 3 && hi[0].type === 'mark' && hi[1] === ' world ' && hi[2].type === 'mark',
      'T13 highlight 命中包裹为 mark', hi.map((x) => (typeof x === 'string' ? x : x.type)))
    ok(mod._test.highlight('abc', 'zz').length === 1 && mod._test.highlight('abc', 'zz')[0] === 'abc', 'T13 highlight 无命中原样返回')
    ok(mod._test.highlight('ABC', 'a').length === 2, 'T13 highlight 大小写不敏感')
    ok(mod._test.fmtDt(null) === '—' && mod._test.fmtDt(500) === '500ms' && mod._test.fmtDt(1500) === '1.50s', 'T13 fmtDt 三档格式')
    ok(mod._test.hueOf('COM15') === mod._test.hueOf('COM15') && Number.isInteger(mod._test.hueOf('COM15')), 'T13 hueOf 稳定')

    // T14 v1.5.0:匹配器/正则高亮/徽章共享态
    const cm = mod._test.compileMatcher
    ok(cm('ABC', false) === 'abc', 'T14 子串模式小写化', cm('ABC', false))
    const re = cm('^53 48', true)
    ok(re instanceof RegExp && re.source === '^53 48' && re.ignoreCase, 'T14 正则模式返回 RegExp', re)
    ok(cm('((', true) === '((', 'T14 非法正则回退子串', cm('((', true))
    ok(cm('  ', true) === null, 'T14 空查询返回 null')
    const hitRe = mod._test.matcherHit(cm('53 48', true), { text: '', hex: '53 48 03', tool: 'send', tag: '' })
    ok(hitRe === true, 'T14 matcherHit 正则命中 hex 字段', hitRe)
    const hr = mod._test.highlightRe('ab', /x*/)
    ok(Array.isArray(hr) && hr.length >= 2, 'T14 正则高亮零长匹配不死循环', hr.map((x) => (typeof x === 'string' ? x : x.type)))
    const hr2 = mod._test.highlightRe('a1b2', /\d/)
    ok(hr2.length === 4 && hr2[1].type === 'mark' && hr2[1].children[0] === '1'
      && hr2[3].children[0] === '2', 'T14 正则高亮包裹数字命中(非 g 正则不死循环)', hr2.map((x) => (typeof x === 'string' ? x : x.children)))

    const bs = mod._test.badgeStore
    const events = []
    const un = bs.subscribe(() => events.push(bs.unread()))
    bs.markSeen(10)
    bs.update(15)
    ok(events.length === 2 && events[0] === false && events[1] === true, 'T14 badgeStore markSeen→update 依次通知', events)
    bs.markSeen(15)
    ok(events.length === 3 && events[2] === false && bs.unread() === false, 'T14 markSeen 推进已读水位并清零', events)
    bs.update(15)
    ok(events.length === 3, 'T14 seq 不前进不重复通知')
    un()
    bs.update(20)
    ok(events.length === 3, 'T14 退订后不再通知')

    // T15(客户端纯函数)v1.8.2 新增
    const tcp = mod._test.truncateCodePoints
    ok(tcp('😀😀😀', 2) === '😀😀', 'T15b 码点截断不劈开代理对', tcp('😀😀😀', 2))
    ok(tcp('abc', 5) === 'abc', 'T15b 未超限原样返回')
    const sm = mod._test.sameMcp
    ok(sm({ available: true, enabled: false }, { available: true, enabled: false }) === true,
      'T15b sameMcp 同状态判等')
    ok(sm({ available: true, enabled: false }, { available: true, enabled: true }) === false,
      'T15b sameMcp 外部改开关必须判不等(chip 才会刷新)')
    ok(sm(undefined, undefined) === true && sm(null, null) === true, 'T15b sameMcp 双空判等')
    ok(sm(undefined, { available: true }) === false, 'T15b sameMcp 单空判不等')
    // 嵌套 mcp 参与 stats 比较:这是「外部改开关后 chip 显示旧值」那个 bug 的回归点
    const base = { tx: 1, rx: 2, mcp: { available: true, enabled: false } }
    ok(mod._test.sameStats(base, Object.assign({}, base, { mcp: { available: true, enabled: false } })) === true,
      'T15b sameStats:mcp 未变 → 判等(不制造多余重渲染)')
    ok(mod._test.sameStats(base, Object.assign({}, base, { mcp: { available: true, enabled: true } })) === false,
      'T15b sameStats:mcp 变了 → 判不等')

    // 徽章轮询让位:面板 live 时 isPanelLive() 应为真,退出后归零
    const bs2 = mod._test.badgeStore
    const before = bs2.isPanelLive()
    bs2.panelLive(true)
    ok(bs2.isPanelLive() === true, 'T15b panelLive(true) 后 isPanelLive 为真')
    bs2.panelLive(false)
    ok(bs2.isPanelLive() === before, 'T15b panelLive(false) 后复原')
    bs2.panelLive(false)
    ok(bs2.livePanel >= 0, 'T15b livePanel 不会变成负数(多实例卸载安全)', bs2.livePanel)

    // v1.6.x 注册契约(与 dsh-ocdup-panel v1.3.3/v1.3.4 对齐):
    //   body 无条件注册进 main keyed slot,key = panellist id(严格同值)——宿主
    //   的 PanelRow 自己绑 onClick → layout.selectPanel(id),缺 key 会抛错;
    //   右栏 tab 类型 + guide 卡保留为可选增强(openTab 认 kind,body 认 id)。
    const calls = []
    const registered = {}
    const tabDefs = []
    const slotsApi = {
      inject: (name, fn) => { calls.push(['inject', name]); fn() },
      register: (spec, comp) => { calls.push(['register', spec.name, spec.key || spec.id]); registered[spec.name + '|' + (spec.key || spec.id)] = comp },
    }
    const ctxNoRight = { get: (n) => (n === 'slots' ? slotsApi : undefined), effect: (fn) => fn() }
    mod.apply(ctxNoRight)
    ok(!!registered['sidebar.panellist|acccom-serial'], 'T12 侧栏图标按 panellist id 注册')
    ok(!!registered['main|acccom-serial'],
      'T12 main keyed slot 用**同一个 id** 注册 body(selectPanel 缺 key 会抛)', Object.keys(registered))
    ok(!calls.some((c) => c[1] === 'sidebar.right.pane.tab'), 'T12 无右栏服务时不注册右栏 body')
    ok(/sp-row/.test(injectedStyles.text) && /640px/.test(injectedStyles.text)
      && /@container sp-panel/.test(injectedStyles.text), 'T12 一次性样式表注入(含容器查询列降级)', injectedStyles.text)

    // 右栏服务在 → 额外出 tab 类型 + guide 卡 + 右栏 body(main 仍然注册)
    calls.length = 0
    for (const k of Object.keys(registered)) delete registered[k]
    const ctxRight = {
      get: (n) => {
        if (n === 'slots') return slotsApi
        if (n === 'sidebarRightTabs') return { register: (d) => tabDefs.push(d) }
        if (n === 'sidebarRight') return { openTab: () => {} }
        return undefined
      },
      effect: (fn) => fn(),
    }
    mod.apply(ctxRight)
    ok(tabDefs.length === 1 && tabDefs[0].id === 'dsh-serial-panel' && tabDefs[0].kind === 'acccom-serial'
      && tabDefs[0].keepMounted === true, 'T12 右栏 tab 类型注册(id/kind/keepMounted)', tabDefs[0])
    ok(Array.isArray(tabDefs[0].guide) && tabDefs[0].guide.length === 1
      && tabDefs[0].guide[0].id === 'new' && typeof tabDefs[0].guide[0].icon === 'function',
      'T12 guide 入口卡声明(无 commandId,terminal 同款)', tabDefs[0].guide)
    ok(calls.some((c) => c[0] === 'register' && c[1] === 'sidebar.right.pane.tab' && c[2] === 'dsh-serial-panel'),
      'T12 pane.tab 体以插件 id 为 key 注册')
    ok(!!registered['main|acccom-serial'], 'T12 右栏分支也注册 main(两条入口都在)')

    // 图标点击:宿主 button 已绑 selectPanel;PanelIcon 的兜底走 layout.selectPanel(panelId)
    ok(Array.isArray(calls.find((c) => c[0] === 'inject' && c[1] === 'sidebar.panellist')), 'T12 侧边栏图标已注册')
    const selected = []
    for (const k of Object.keys(registered)) delete registered[k]
    mod.apply({
      get: (n) => (n === 'slots' ? slotsApi
        : n === 'sidebarRightTabs' ? { register: () => {} }
          : n === 'sidebarRight' ? { openTab: () => {} }
            : n === 'layout' ? { selectPanel: (id) => selected.push(id) } : undefined),
      effect: (fn) => fn(),
    })
    const outer = registered['sidebar.panellist|acccom-serial']
    ok(typeof outer === 'function', 'T12 图标可取到')
    const iconEl = outer({ panelId: 'acccom-serial' }).type({ panelId: 'acccom-serial' })
    ok(iconEl && typeof iconEl.props.onClick === 'function', 'T12 图标渲染为可点击元素')
    iconEl.props.onClick({})
    ok(selected.length === 1 && selected[0] === 'acccom-serial', 'T12 图标点击转发 selectPanel(panelId)', selected)

    delete globalThis.window
    delete globalThis.document
    delete globalThis.localStorage
  }

  // 两个实例都要释放:apply 返回的 disposer 会 clearInterval(tail),
  // 漏掉任何一个都会让 node 进程因定时器存活而无法退出。
  dispose()
  if (typeof dispose2 === 'function') dispose2()
  console.log('\nALL PASS (' + checks + ' checks, mode=' + mode + ')')
  fs.rmSync(tmp, { recursive: true, force: true })
})().catch((e) => { console.error(e.message); process.exit(1) })
