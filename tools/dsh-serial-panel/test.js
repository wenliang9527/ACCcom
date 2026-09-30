'use strict'
// ============================================================
//  dsh-serial-panel — Host 半区行为测试(无框架,纯 node)
//
//  运行(在插件目录下):
//    node test.js core     # 增量/半行/切片/批量裁剪/轮转/清空/方法守卫/诊断
//    node test.js window   # 首次挂载只读尾部窗口(大日志不全量读入)
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

if (!logFile) { console.error('DSH_SERIAL_PANEL_LOG must be set'); process.exit(1) }
fs.mkdirSync(path.dirname(logFile), { recursive: true })

const line = (id, dir, text, tag) => JSON.stringify({
  id, tool: 'send', timestamp: '2026-08-18T10:00:00.123+08:00',
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

  // 两个实例都要释放:apply 返回的 disposer 会 clearInterval(tail),
  // 漏掉任何一个都会让 node 进程因定时器存活而无法退出。
  dispose()
  if (typeof dispose2 === 'function') dispose2()
  console.log('\nALL PASS (' + checks + ' checks, mode=' + mode + ')')
  fs.rmSync(tmp, { recursive: true, force: true })
})().catch((e) => { console.error(e.message); process.exit(1) })
