'use strict'
// ============================================================
//  dsh-serial-panel — Client 半区(浏览器面板,永久 cordis 插件)  v1.1.0
//
//  - sidebar.panellist 注册全局面板图标(id=acccom-serial)
//  - main(keyed) 以同名 key 注册中央面板本体
//  - 数据:同源 fetch /api/acccom-serial?since=<seq> 增量拉取(Host 见 index.js)
//
//  闭包环境:React 走 require,禁 JSX/import;网络属于本包同源路由。
//
//  ── v1.1.0 优化 ──
//   * Row 用 React.memo:entries 里旧对象引用不变 → 未变化的行跳过重渲染
//   * 过滤结果用 useMemo 缓存,只在 entries/方向/关键字变化时重算
//   * stats 逐字段浅比较,数据无变化时不再制造新 state(消除空闲重渲染)
//   * 轮询改为自适应 setTimeout 链:有数据 700ms,空闲退避到 2.5s,
//     隐藏/暂停 2s,不再固定 800ms 空转
//   * 客户端保留上限提到 5000,与 Host 内存环一致
//   * 行内 payload 显示截断,超长帧不再撑大 DOM(详情区仍给全文)
// ============================================================

window.__ModuleLoader__.load({
  id: 'dsh-serial-panel',
  factory: function (require) {
    const React = require('react')
    const h = React.createElement

    const ROUTE = '/api/acccom-serial'
    const ROUTE_CLEAR = '/api/acccom-serial/clear'
    const LS = 'dsh-serial-panel/prefs/v1'

    const POLL_ACTIVE_MS = 700   // 刚拿到数据:贴近写入端节奏
    const POLL_IDLE_MS = 2500    // 空闲退避
    const POLL_HIDDEN_MS = 2000  // 面板不可见
    const POLL_PAUSED_MS = 1500  // 用户暂停
    const MAX_ROWS_KEPT = 5000   // 与 Host MAX_ENTRIES 对齐
    const MAX_RENDER_ROWS = 300  // 单次实际挂载的行数
    const ROW_PAYLOAD_MAX = 4000 // 行内 payload 显示上限(详情区不受限)

    // ---- 偏好(localStorage,带版本号) ----
    const loadPrefs = () => {
      const d = { hex: false, dir: 'ALL', follow: true }
      try { return Object.assign(d, JSON.parse(localStorage.getItem(LS) || '{}')) } catch (e) { return d }
    }
    const savePrefs = (p) => { try { localStorage.setItem(LS, JSON.stringify(p)) } catch (e) {} }

    const shortTime = (ts) => (typeof ts === 'string' && ts.length >= 23 ? ts.slice(11, 23) : ts || '')
    const fmtHex = (hex) => String(hex || '').replace(/[^0-9a-fA-F]/g, '').replace(/(..)/g, '$1 ').trim()
    const fmtBytes = (n) => (n >= 1024 ? (n / 1024).toFixed(1) + 'K' : String(n | 0))

    const DIR_COLOR = { TX: '#d97706', RX: '#059669' }

    // stats 浅比较:Host 每轮都回一个全新对象,不比较就会每轮触发重渲染
    const STATS_KEYS = ['tx', 'rx', 'txBytes', 'rxBytes', 'lastAt', 'exists', 'kept', 'lastSeq']
    const sameStats = (a, b) => {
      if (!a || !b) return false
      for (let i = 0; i < STATS_KEYS.length; i++) {
        const k = STATS_KEYS[i]
        if (a[k] !== b[k]) return false
      }
      return true
    }

    let ctxRef = null // apply 时捕获,供图标兜底切换面板用

    // ---- 组件:行条目(memo:引用未变的行不重渲染) ----
    const Row = React.memo(function Row({ e, hex, selected, onSelect }) {
      const raw = hex ? (fmtHex(e.hex) || e.text) : (e.text || fmtHex(e.hex))
      const payload = raw.length > ROW_PAYLOAD_MAX ? raw.slice(0, ROW_PAYLOAD_MAX) + ' …' : raw
      return h('div', {
        onClick: () => onSelect(e),
        style: {
          display: 'flex', alignItems: 'baseline', gap: 8,
          padding: '2px 10px', cursor: 'pointer',
          background: selected ? 'var(--dsw-alias-bg-layer-3, #f0f0f0)' : 'transparent',
          borderBottom: '1px solid var(--dsw-alias-border-l2, #f0f0f0)',
          fontSize: 12, lineHeight: 1.6,
        },
      },
        h('span', { style: { flex: '0 0 84px', color: 'var(--dsw-alias-label-tertiary, #999)', fontVariantNumeric: 'tabular-nums' } }, shortTime(e.ts)),
        h('span', {
          style: {
            flex: '0 0 26px', textAlign: 'center', borderRadius: 4, fontSize: 11,
            fontWeight: 700, color: '#fff', background: DIR_COLOR[e.dir] || '#888', padding: '0 4px',
          },
        }, e.dir),
        h('span', { style: { flex: '0 0 110px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--dsw-alias-label-secondary, #666)' } }, e.tool + (e.tag ? ' · ' + e.tag : '')),
        h('span', { style: { flex: '0 0 44px', textAlign: 'right', color: 'var(--dsw-alias-label-tertiary, #999)', fontVariantNumeric: 'tabular-nums' } }, e.len),
        h('span', {
          style: {
            flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            fontFamily: 'Consolas, Menlo, monospace', color: 'var(--dsw-alias-label-primary, #222)',
          },
        }, payload),
      )
    })

    // ---- 组件:串口实时面板(中央 main 面板) ----
    const SerialPanel = () => {
      const prefs0 = React.useMemo(loadPrefs, [])
      const [entries, setEntries] = React.useState([])
      const [stats, setStats] = React.useState(null)
      const [connected, setConnected] = React.useState(false)
      const [paused, setPaused] = React.useState(false)
      const [hex, setHex] = React.useState(!!prefs0.hex)
      const [dir, setDir] = React.useState(prefs0.dir || 'ALL')
      const [follow, setFollow] = React.useState(prefs0.follow !== false)
      const [search, setSearch] = React.useState('')
      const [selected, setSelected] = React.useState(null)
      const lastSeqRef = React.useRef(0)
      const pausedRef = React.useRef(paused)
      const listRef = React.useRef(null)

      React.useEffect(() => { pausedRef.current = paused }, [paused])
      React.useEffect(() => { savePrefs({ hex, dir, follow }) }, [hex, dir, follow])

      // ---- 自适应增量轮询(单条 setTimeout 链,避免固定间隔空转) ----
      React.useEffect(() => {
        let alive = true
        let timer = null

        const schedule = (delay) => {
          if (!alive) return
          timer = setTimeout(tick, delay)
        }

        const tick = async () => {
          timer = null
          if (!alive) return
          if (pausedRef.current) { schedule(POLL_PAUSED_MS); return }
          if (document.visibilityState === 'hidden') { schedule(POLL_HIDDEN_MS); return }

          let got = false
          try {
            const r = await fetch(ROUTE + '?since=' + lastSeqRef.current, { cache: 'no-store' })
            const j = await r.json()
            if (!alive) return
            setConnected(!!(j && j.ok))
            if (j && j.ok) {
              if (j.stats) setStats((prev) => (sameStats(prev, j.stats) ? prev : j.stats))
              const inc = j.entries || []
              if (inc.length) {
                got = true
                lastSeqRef.current = inc[inc.length - 1].seq
                setEntries((prev) => {
                  const next = prev.concat(inc)
                  return next.length > MAX_ROWS_KEPT ? next.slice(next.length - MAX_ROWS_KEPT) : next
                })
              }
            }
          } catch (e) { if (alive) setConnected(false) }
          schedule(got ? POLL_ACTIVE_MS : POLL_IDLE_MS)
        }

        const onVis = () => {
          if (document.visibilityState !== 'visible' || !alive) return
          if (timer !== null) { clearTimeout(timer); timer = null } // 回到前台立即补一次
          tick()
        }
        document.addEventListener('visibilitychange', onVis)
        tick()

        return () => {
          alive = false
          if (timer !== null) clearTimeout(timer)
          document.removeEventListener('visibilitychange', onVis)
        }
      }, [])

      // ---- 跟随滚动 ----
      React.useEffect(() => {
        if (follow && listRef.current) listRef.current.scrollTop = listRef.current.scrollHeight
      }, [entries, follow])

      const clear = async () => {
        if (!window.confirm('清空共享 MCP 串口日志?(GUI 流量窗口同步清空)')) return
        try {
          const r = await fetch(ROUTE_CLEAR, { method: 'POST' })
          const j = await r.json()
          if (j && j.ok) {
            setEntries([]); setSelected(null)
            if (j.stats) { setStats(j.stats); lastSeqRef.current = j.stats.lastSeq || 0 }
          }
        } catch (e) { /* 忽略 */ }
      }

      // ---- 过滤结果缓存:只在数据/方向/关键字变化时重算 ----
      const visible = React.useMemo(() => {
        const q = search.trim().toLowerCase()
        const out = []
        for (let i = 0; i < entries.length; i++) {
          const e = entries[i]
          if (dir !== 'ALL' && e.dir !== dir) continue
          if (q) {
            const hit = (e.text && e.text.toLowerCase().indexOf(q) >= 0)
              || (e.hex && e.hex.toLowerCase().indexOf(q) >= 0)
              || (e.tool && e.tool.toLowerCase().indexOf(q) >= 0)
              || (e.tag && e.tag.toLowerCase().indexOf(q) >= 0)
            if (!hit) continue
          }
          out.push(e)
        }
        return out
      }, [entries, dir, search])

      const visibleCount = visible.length
      const rendered = visibleCount > MAX_RENDER_ROWS ? visible.slice(visibleCount - MAX_RENDER_ROWS) : visible
      const counterText = (rendered.length < visibleCount ? '渲染 ' + rendered.length + ' / ' : '')
        + visibleCount + ' / ' + entries.length + ' 条'

      const btn = (label, onClick, active, title) => h('button', {
        type: 'button', title: title || '', onClick,
        style: {
          appearance: 'none', cursor: 'pointer', fontSize: 12, padding: '4px 10px',
          borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)',
          background: active ? 'var(--dsw-alias-label-primary, #222)' : 'var(--dsw-alias-bg-layer-3, #fff)',
          color: active ? 'var(--dsw-alias-bg-layer-3, #fff)' : 'var(--dsw-alias-label-primary, #222)',
          font: 'inherit',
        },
      }, label)

      const toolbar = h('div', {
        style: {
          display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap',
          padding: '10px 12px', borderBottom: '1px solid var(--dsw-alias-border-l2, #eee)',
        },
      },
        h('span', {
          title: connected ? '已连接 Host 尾随' : 'Host 未响应',
          style: { width: 8, height: 8, borderRadius: 999, background: connected ? '#10b981' : '#ef4444', flex: '0 0 auto' },
        }),
        h('span', { style: { fontSize: 12, color: 'var(--dsw-alias-label-secondary, #666)', fontVariantNumeric: 'tabular-nums' } },
          'RX ' + ((stats && stats.rx) || 0) + '(' + fmtBytes((stats && stats.rxBytes) || 0) + 'B) · TX ' + ((stats && stats.tx) || 0) + '(' + fmtBytes((stats && stats.txBytes) || 0) + 'B)'),
        btn(paused ? '▶ 继续' : '⏸ 暂停', () => setPaused(!paused), paused, '暂停后数据在 Host 侧继续累积,恢复后拉齐'),
        btn('跟随', () => setFollow(!follow), follow, '自动滚动到最新'),
        btn('HEX', () => setHex(!hex), hex, '以 HEX 显示数据'),
        h('select', {
          value: dir, onChange: (e) => setDir(e.target.value),
          style: { fontSize: 12, padding: '3px 6px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)' },
        },
          h('option', { value: 'ALL' }, '全部'),
          h('option', { value: 'RX' }, 'RX'),
          h('option', { value: 'TX' }, 'TX'),
        ),
        h('input', {
          value: search, placeholder: '过滤 text/hex/tool/port…',
          onChange: (e) => setSearch(e.target.value),
          style: { flex: '1 1 140px', minWidth: 120, height: 28, fontSize: 12, padding: '0 10px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)', boxSizing: 'border-box' },
        }),
        btn('清空', clear, false, '截断共享 mcp-traffic.jsonl'),
        h('span', { style: { fontSize: 11, color: 'var(--dsw-alias-label-tertiary, #999)' } }, counterText),
      )

      // 空态:顺带给出 shell 工具解析诊断(排查「面板连上但 shell 坏」这类环境问题)
      const diag = stats && stats.diag
      const shellLine = (label, exe, ok, hint) => h('div', { style: { color: ok ? '#059669' : '#d97706' } },
        (ok ? '✓ ' : '⚠ ') + label + ' → ' + (exe || '(PATH 中未找到)') + (ok ? '' : '  ·  ' + hint))
      const emptyBox = h('div', { style: { padding: 24, textAlign: 'center', color: 'var(--dsw-alias-label-tertiary, #999)', fontSize: 13 } },
        h('div', null,
          stats && stats.exists
            ? '日志已连接,等待 AI 串口收发…(让 Agent 调 mcp__acccom__list_ports 试试)'
            : '尚未发现 ' + ((stats && stats.logPath) || 'mcp-traffic.jsonl') + ' — 首次 AI 串口通信后自动出现'),
        diag
          ? h('div', { style: { marginTop: 8, fontSize: 11, lineHeight: 1.8 } },
              shellLine('pwsh', diag.pwsh, diag.pwshOk, 'PATH 中找不到 pwsh.exe'),
              shellLine('bash', diag.bash, diag.bashOk, 'System32 的是 WSL 存根;用 start-dsh-with-gitbash.cmd 启动可换成 Git Bash'))
          : null,
      )

      const list = h('div', { ref: listRef, style: { flex: 1, minHeight: 0, overflow: 'auto', background: 'var(--dsw-alias-bg-layer-2, #fff)' } },
        visibleCount === 0
          ? emptyBox
          : rendered.map((e) => h(Row, { key: e.seq, e, hex, selected: selected && selected.seq === e.seq, onSelect: setSelected })),
      )

      const detail = selected ? h('div', {
        style: {
          flex: '0 0 auto', maxHeight: 180, overflow: 'auto', borderTop: '1px solid var(--dsw-alias-border-l2, #eee)',
          padding: '8px 12px', fontSize: 12, background: 'var(--dsw-alias-bg-layer-3, #fafafa)',
        },
      },
        h('div', { style: { marginBottom: 4, color: 'var(--dsw-alias-label-secondary, #666)' } },
          '#' + selected.seq + ' · ' + shortTime(selected.ts) + ' · ' + selected.dir + ' · ' + selected.tool + (selected.tag ? ' · ' + selected.tag : '') + ' · ' + selected.len + 'B'),
        h('div', { style: { fontFamily: 'Consolas, Menlo, monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-all', color: 'var(--dsw-alias-label-primary, #222)' } },
          selected.text || '(空)'),
        h('div', { style: { fontFamily: 'Consolas, Menlo, monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-all', color: 'var(--dsw-alias-label-tertiary, #999)', marginTop: 4 } },
          fmtHex(selected.hex)),
      ) : null

      return h('div', { style: { height: '100%', boxSizing: 'border-box', display: 'flex', flexDirection: 'column', background: 'var(--dsw-alias-bg-layer-2, #fff)', color: 'var(--dsw-alias-label-primary, #222)' } },
        toolbar, list, detail,
      )
    }

    // ---- 组件:侧边栏面板图标 ----
    const PanelIcon = (props) => {
      const onClick = (e) => {
        if (props && typeof props.onClick === 'function') { props.onClick(e); return }
        try {
          // 槽位壳可能只渲染图标本身:兜底自己触发面板切换
          const layout = ctxRef && ctxRef.get && ctxRef.get('layout')
          if (layout && typeof layout.selectPanel === 'function') layout.selectPanel('acccom-serial')
        } catch (e2) { /* layout 服务缺席时静默 */ }
      }
      return h('span', {
        title: 'ACCCOM 串口面板',
        onClick,
        style: { display: 'inline-flex', alignItems: 'center', justifyContent: 'center', cursor: 'pointer', fontSize: 16, lineHeight: 1 },
      }, '🔌')
    }

    return {
      inject: ['slots'],
      apply: function (ctx) {
        const slots = ctx.get('slots')
        if (slots === undefined) return
        ctxRef = ctx

        // 全局面板图标(sidebar 列)
        slots.inject('sidebar.panellist', () => slots.register(
          { name: 'sidebar.panellist', id: 'acccom-serial', order: 80, label: () => 'ACCCOM 串口' },
          (props) => h(PanelIcon, props),
        ))

        // 中央面板本体(key = sidebar entry id)
        slots.inject('main', () => slots.register(
          { name: 'main', key: 'acccom-serial' },
          (props) => h(SerialPanel, props),
        ))
      },
    }
  },
})
