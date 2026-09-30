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
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'dsh-sp-'))
const logFile = process.env.DSH_SERIAL_PANEL_LOG

if (!logFile && mode !== 'client') { console.error('DSH_SERIAL_PANEL_LOG must be set'); process.exit(1) }
if (logFile) fs.mkdirSync(path.dirname(logFile), { recursive: true })

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
    // 防呆:core 模式遗留的 MAX_ENTRIES=5 会把断言目标行裁出内存环
    const envMax = Number(process.env.DSH_SERIAL_PANEL_MAX_ENTRIES || 0)
    if (envMax > 0 && envMax < 40) {
      throw new Error('api 模式需要 MAX_ENTRIES≥40(或 Remove-Item Env:DSH_SERIAL_PANEL_MAX_ENTRIES 后运行)')
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

    // 兜底分支:无 rightbar 服务 → main 槽
    const calls = []
    const registered = {}
    const slotsApi = {
      inject: (name, fn) => { calls.push(['inject', name]); fn() },
      register: (spec, comp) => { calls.push(['register', spec.name, spec.key || spec.id]); registered[spec.name + '|' + (spec.key || spec.id)] = comp },
    }
    const ctxNoRight = { get: (n) => (n === 'slots' ? slotsApi : undefined), effect: (fn) => fn() }
    mod.apply(ctxNoRight)
    ok(calls.some((c) => c[0] === 'register' && c[1] === 'main'), 'T12 兜底分支注册 main 槽')
    ok(!calls.some((c) => c[1] === 'sidebar.right.pane.tab'), 'T12 兜底分支不注册右栏 tab 体')
    ok(/sp-row/.test(injectedStyles.text) && /640px/.test(injectedStyles.text), 'T12 一次性样式表注入', injectedStyles.text)

    // 右栏分支:tab 类型 + pane.tab 体 + 图标转发 openTab
    calls.length = 0
    const tabDefs = []
    const opened = []
    const ctxRight = {
      get: (n) => {
        if (n === 'slots') return slotsApi
        if (n === 'sidebarRightTabs') return { register: (d) => tabDefs.push(d) }
        if (n === 'sidebarRight') return { openTab: (k) => opened.push(k) }
        return undefined
      },
      effect: (fn) => fn(),
    }
    mod.apply(ctxRight)
    ok(tabDefs.length === 1 && tabDefs[0].id === 'dsh-serial-panel' && tabDefs[0].kind === 'acccom-serial'
      && tabDefs[0].keepMounted === true, 'T12 右栏 tab 类型注册(id/kind/keepMounted)', tabDefs[0])
    ok(Array.isArray(tabDefs[0].guide) && tabDefs[0].guide.length === 1
      && tabDefs[0].guide[0].kind === 'acccom-serial' && typeof tabDefs[0].guide[0].icon === 'function',
    'T12 guide 入口卡声明', tabDefs[0].guide)
    ok(calls.some((c) => c[0] === 'register' && c[1] === 'sidebar.right.pane.tab' && c[2] === 'dsh-serial-panel'),
      'T12 pane.tab 体以插件 id 为 key 注册')
    ok(!calls.some((c) => c[0] === 'register' && c[1] === 'main'), 'T12 右栏分支不再注册整屏 main')

    // 图标点击 → openTab(kind):先渲染包装层,再渲染 PanelIcon 本体拿到 onClick
    ok(Array.isArray(calls.find((c) => c[0] === 'inject' && c[1] === 'sidebar.panellist')), 'T12 侧边栏图标已注册')
    const outer = registered['sidebar.panellist|acccom-serial']({})
    const iconEl = outer.type(outer.props)
    ok(iconEl && typeof iconEl.props.onClick === 'function', 'T12 图标渲染为可点击元素')
    iconEl.props.onClick({})
    ok(opened.length === 1 && opened[0] === 'acccom-serial', 'T12 图标点击转发 openTab(kind)', opened)

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
