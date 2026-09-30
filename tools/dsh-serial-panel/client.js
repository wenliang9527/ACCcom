'use strict'
// ============================================================
//  dsh-serial-panel — Client 半区(浏览器面板,永久 cordis 插件)  v1.3.1
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
//
//  ── v1.2.0 新增 ──
//   * Δt 帧间隔列(Host 算好 dtMs,行内直读)、详情区 text/hex 一键复制
//   * 跟随自动挂起:向上翻历史自动暂停跟随,回到底部自动恢复
//   * 端口下拉过滤 + portTag 稳定色块;断层标记(seq 跳变/Host 重启守卫)
//   * 连接态三色(绿=尾随中/黄=等日志文件/红=Host 未响应)
//   * 清空两段式确认;回前台补拉加在途守卫;空态显示 API 地址可复制
//
//  ── v1.3.0 布局改造 ──
//   * 面板从整屏 main 槽迁到右侧停靠栏(rightbar)tab:对话始终可见
//     (sidebarRightTabs.register 类型 + sidebar.right.pane.tab 挂体,keepMounted)
//   * 左侧图标 = 打开右侧 tab;右栏 guide 默认页加入口卡片
//   * DSH 无 rightbar 服务时自动回退整屏 main 模式(行为同 v1.2.0)
//   * tab 不可见时轮询降为 2s;≤640px 隐藏工具列(停靠宽度下更从容)
//
//  ── v1.3.1 修复:停靠栏拖拽时数据不跟随 ──
//   * 根元素锁宽(width:100%/min-width:0/overflow:hidden):面板体无宽约束时
//     会被不换行的 hex 行撑到比 pane 宽,遭 tabBody overflow:hidden 裁切,
//     表现为拖拽调宽后内容不重排、右缘被切
//   * 列降级从视口 @media 换成容器 @container(1920 视口下面板拖到 300px,
//     media query 永不触发);≤520px 额外隐藏 RX/TX 统计文本
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

    // ---- 一次性样式:hover/窄宽度降级(inline style 写不了 :hover 与媒体查询) ----
    // 行背景整体走 class:内联 background 会压过样式表,导致 hover 失效
    const injectStyles = () => {
      try {
        if (document.getElementById('dsh-serial-panel-styles')) return
        const el = document.createElement('style')
        el.id = 'dsh-serial-panel-styles'
        el.textContent = [
          '.sp-row { background: transparent; }',
          '.sp-row:hover { background: var(--dsw-alias-bg-layer-3, #f5f5f5); }',
          '.sp-row.sp-sel { background: var(--dsw-alias-bg-layer-3, #f0f0f0); }',
          // 容器查询跟随面板实际宽度(停靠栏拖拽时列降级实时生效);
          // 旧版 Chromium 不支持时仅失去列降级,media query 作小窗口兜底
          '@container sp-panel (max-width: 640px) { .sp-tool { display: none; } }',
          '@container sp-panel (max-width: 560px) { .sp-len { display: none; } }',
          '@container sp-panel (max-width: 520px) { .sp-stats { display: none; } }',
          '@media (max-width: 640px) { .sp-tool { display: none; } }',
          '@media (max-width: 560px) { .sp-len { display: none; } }',
        ].join('\n')
        document.head.appendChild(el)
      } catch (e) { /* 注入失败仅降级 hover 与窄宽度适配 */ }
    }
    injectStyles()

    const fmtDt = (ms) => (ms == null ? '—' : ms < 1000 ? ms + 'ms' : (ms / 1000).toFixed(2) + 's')

    // portTag → 稳定色相:多端口流量交错时一眼区分
    const hueOf = (tag) => {
      let hsh = 0
      for (let i = 0; i < tag.length; i++) hsh = (hsh * 31 + tag.charCodeAt(i)) >>> 0
      return hsh % 360
    }

    const copyText = async (s) => {
      try {
        await navigator.clipboard.writeText(s)
        return true
      } catch (e) {
        try {
          const ta = document.createElement('textarea')
          ta.value = s
          ta.style.position = 'fixed'
          ta.style.opacity = '0'
          document.body.appendChild(ta)
          ta.select()
          const done = document.execCommand('copy')
          document.body.removeChild(ta)
          return done
        } catch (e2) { return false }
      }
    }

    // ---- 组件:复制按钮(成功后短暂显示「已复制」) ----
    const CopyBtn = function CopyBtn({ text, label }) {
      const [done, setDone] = React.useState(false)
      const timerRef = React.useRef(null)
      React.useEffect(() => () => { if (timerRef.current) clearTimeout(timerRef.current) }, [])
      const onCopy = async () => {
        if (!(await copyText(text))) return
        setDone(true)
        if (timerRef.current) clearTimeout(timerRef.current)
        timerRef.current = setTimeout(() => setDone(false), 1500)
      }
      return h('button', {
        type: 'button', onClick: onCopy,
        style: {
          appearance: 'none', cursor: 'pointer', fontSize: 11, padding: '2px 8px',
          borderRadius: 6, border: '1px solid var(--dsw-alias-border-l2, #ddd)',
          background: done ? '#10b981' : 'var(--dsw-alias-bg-layer-3, #fff)',
          color: done ? '#fff' : 'var(--dsw-alias-label-secondary, #666)', font: 'inherit',
        },
      }, done ? '已复制' : (label || '复制'))
    }

    // stats 浅比较:Host 每轮都回一个全新对象,不比较就会每轮触发重渲染
    const STATS_KEYS = ['tx', 'rx', 'txBytes', 'rxBytes', 'lastAt', 'exists', 'kept', 'lastSeq',
      'badLines', 'logSize', 'logMtimeMs', 'rotated']
    const sameStats = (a, b) => {
      if (!a || !b) return false
      for (let i = 0; i < STATS_KEYS.length; i++) {
        const k = STATS_KEYS[i]
        if (a[k] !== b[k]) return false
      }
      return true
    }

    let ctxRef = null // apply 时捕获,供图标兜底切换面板用

    // ---- 组件:串口插头图标(guide 入口卡与侧边栏共用;接受 {size, className}) ----
    const PlugGlyph = ({ size, className }) => h('svg', {
      width: size || 16, height: size || 16, viewBox: '0 0 16 16', className,
      fill: 'none', stroke: 'currentColor', strokeWidth: 1.5,
      strokeLinecap: 'round', strokeLinejoin: 'round', style: { display: 'block' },
    },
      h('path', { d: 'M5.5 1.5v3' }),
      h('path', { d: 'M10.5 1.5v3' }),
      h('path', { d: 'M3.5 4.5h9v3.2a4.5 4.5 0 0 1-9 0V4.5z' }),
      h('path', { d: 'M8 12.2v2.3' }),
    )

    // ---- 组件:行条目(memo:引用未变的行不重渲染) ----
    const Row = React.memo(function Row({ e, hex, selected, onSelect }) {
      const raw = hex ? (fmtHex(e.hex) || e.text) : (e.text || fmtHex(e.hex))
      const payload = raw.length > ROW_PAYLOAD_MAX ? raw.slice(0, ROW_PAYLOAD_MAX) + ' …' : raw
      return h('div', {
        className: 'sp-row' + (selected ? ' sp-sel' : ''),
        onClick: () => onSelect(e),
        style: {
          display: 'flex', alignItems: 'baseline', gap: 8,
          minWidth: 0, width: '100%', boxSizing: 'border-box',
          padding: '2px 10px', cursor: 'pointer',
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
        e.tag ? h('span', {
          title: e.tag,
          style: {
            flex: '0 0 8px', height: 8, borderRadius: 999, alignSelf: 'center',
            background: 'hsl(' + hueOf(e.tag) + ', 55%, 45%)',
          },
        }) : null,
        h('span', { className: 'sp-tool', style: { flex: '0 0 110px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--dsw-alias-label-secondary, #666)' } }, e.tool + (e.tag ? ' · ' + e.tag : '')),
        h('span', { className: 'sp-len', style: { flex: '0 0 44px', textAlign: 'right', color: 'var(--dsw-alias-label-tertiary, #999)', fontVariantNumeric: 'tabular-nums' } }, e.len),
        h('span', {
          className: 'sp-dt', title: '距上一条',
          style: { flex: '0 0 48px', textAlign: 'right', fontSize: 11, color: 'var(--dsw-alias-label-tertiary, #999)', fontVariantNumeric: 'tabular-nums' },
        }, fmtDt(e.dtMs)),
        h('span', {
          style: {
            flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            fontFamily: 'Consolas, Menlo, monospace', color: 'var(--dsw-alias-label-primary, #222)',
          },
        }, payload),
      )
    })

    // ---- 组件:断层标记(增量 seq 跳变 / Host 环滚出) ----
    const GapRow = React.memo(function GapRow({ count }) {
      return h('div', {
        style: {
          padding: '3px 10px', textAlign: 'center', fontSize: 11,
          color: 'var(--dsw-alias-label-tertiary, #999)',
          background: 'var(--dsw-alias-bg-layer-3, #fafafa)',
          borderBottom: '1px solid var(--dsw-alias-border-l2, #f0f0f0)',
        },
      }, '… 已省略 ' + count + ' 条(超出 Host 保留窗口)…')
    })

    // ---- 组件:串口实时面板(右侧停靠栏 tab 体 / 旧版整屏 main 面板) ----
    const EMPTY_TAB_INFO = { tab: null }
    const SerialPanel = (props) => {
      const prefs0 = React.useMemo(loadPrefs, [])
      // 右栏 tab 体:框架注入 useTabInfo;缺席(旧 main 路径)用恒等兜底,hook 仍无条件调用
      const useTabInfoSafe = props && typeof props.useTabInfo === 'function' ? props.useTabInfo : () => EMPTY_TAB_INFO
      const tabInfo = useTabInfoSafe()
      const tabVisible = !(tabInfo && tabInfo.tab && tabInfo.tab.visible === false)
      const tabVisibleRef = React.useRef(tabVisible)
      React.useEffect(() => { tabVisibleRef.current = tabVisible }, [tabVisible])
      const [entries, setEntries] = React.useState([])
      const [stats, setStats] = React.useState(null)
      const [connected, setConnected] = React.useState(false)
      const [paused, setPaused] = React.useState(false)
      const [hex, setHex] = React.useState(!!prefs0.hex)
      const [dir, setDir] = React.useState(prefs0.dir || 'ALL')
      const [port, setPort] = React.useState(prefs0.port || 'ALL')
      const [follow, setFollow] = React.useState(prefs0.follow !== false)
      const [autoSuspended, setAutoSuspended] = React.useState(false) // 向上翻历史时自动挂起跟随(临时态)
      const [confirmClear, setConfirmClear] = React.useState(false)
      const [search, setSearch] = React.useState('')
      const [selected, setSelected] = React.useState(null)
      const lastSeqRef = React.useRef(0)
      const pausedRef = React.useRef(paused)
      const listRef = React.useRef(null)
      const confirmTimerRef = React.useRef(null)
      const inFlightRef = React.useRef(false)

      React.useEffect(() => { pausedRef.current = paused }, [paused])
      React.useEffect(() => { savePrefs({ hex, dir, follow, port }) }, [hex, dir, follow, port])
      React.useEffect(() => () => { if (confirmTimerRef.current) clearTimeout(confirmTimerRef.current) }, [])

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
          if (!tabVisibleRef.current) { schedule(POLL_HIDDEN_MS); return } // tab 在后台:降频,keepMounted 下仍在积累
          if (inFlightRef.current) { schedule(POLL_ACTIVE_MS); return } // 上一轮仍在途:保持链活,稍后续拉

          let got = false
          inFlightRef.current = true
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
                const prevLast = lastSeqRef.current
                const firstSeq = inc[0].seq
                lastSeqRef.current = inc[inc.length - 1].seq
                setEntries((prev) => {
                  let next
                  if (firstSeq <= prevLast) {
                    next = inc.slice() // Host 重启(seq 归零):整体替换,避免 seq 回退与 key 重复
                  } else if (firstSeq > prevLast + 1) {
                    // 断层:中间条目已滚出 Host 保留窗口(首屏环内更早的历史同此)
                    next = prev.concat([{ kind: 'gap', seq: firstSeq, count: firstSeq - prevLast - 1 }], inc)
                  } else {
                    next = prev.concat(inc)
                  }
                  return next.length > MAX_ROWS_KEPT ? next.slice(next.length - MAX_ROWS_KEPT) : next
                })
              }
            }
          } catch (e) { if (alive) setConnected(false) } finally {
            inFlightRef.current = false
          }
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

      // ---- 跟随滚动:用户向上翻历史时自动挂起,回到底部自动恢复 ----
      // 程序性滚到底部同样落在"底部"判定内,无需区分滚动来源
      const effectiveFollow = follow && !autoSuspended
      React.useEffect(() => {
        const el = listRef.current
        if (el && effectiveFollow) el.scrollTop = el.scrollHeight
      }, [entries, effectiveFollow])

      const onScroll = () => {
        const el = listRef.current
        if (!el) return
        const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 24
        setAutoSuspended(!atBottom) // 值未变时 React 跳过重渲染
      }

      // ---- 清空(两段式确认:webview 可能吞掉原生 confirm 对话框) ----
      const clear = async () => {
        if (!confirmClear) {
          setConfirmClear(true)
          if (confirmTimerRef.current) clearTimeout(confirmTimerRef.current)
          confirmTimerRef.current = setTimeout(() => setConfirmClear(false), 3000)
          return
        }
        if (confirmTimerRef.current) clearTimeout(confirmTimerRef.current)
        setConfirmClear(false)
        try {
          const r = await fetch(ROUTE_CLEAR, { method: 'POST' })
          const j = await r.json()
          if (j && j.ok) {
            setEntries([]); setSelected(null)
            if (j.stats) { setStats(j.stats); lastSeqRef.current = j.stats.lastSeq || 0 }
          }
        } catch (e) { /* 忽略 */ }
      }

      // ---- 端口清单(用于下拉;选中项被裁出窗口时保留该选项防值失效) ----
      const ports = React.useMemo(() => {
        const out = []
        for (let i = 0; i < entries.length; i++) {
          const t = entries[i].tag
          if (t && out.indexOf(t) < 0) out.push(t)
        }
        return out
      }, [entries])
      const portOptions = port !== 'ALL' && ports.indexOf(port) < 0 ? ports.concat(port) : ports

      // ---- 过滤结果缓存:只在数据/方向/端口/关键字变化时重算 ----
      const visible = React.useMemo(() => {
        const q = search.trim().toLowerCase()
        const out = []
        for (let i = 0; i < entries.length; i++) {
          const e = entries[i]
          if (e.kind === 'gap') {
            if (dir === 'ALL' && !q) out.push(e) // 断层标记仅在无过滤时显示
            continue
          }
          if (dir !== 'ALL' && e.dir !== dir) continue
          if (port !== 'ALL' && e.tag !== port) continue
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
      }, [entries, dir, port, search])

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
          title: !connected ? 'Host 未响应' : (stats && stats.exists === false) ? '已连接,等待日志文件出现' : '已连接 Host 尾随',
          style: { width: 8, height: 8, borderRadius: 999, background: !connected ? '#ef4444' : (stats && stats.exists === false) ? '#f59e0b' : '#10b981', flex: '0 0 auto' },
        }),
        h('span', { className: 'sp-stats', style: { fontSize: 12, color: 'var(--dsw-alias-label-secondary, #666)', fontVariantNumeric: 'tabular-nums' } },
          'RX ' + ((stats && stats.rx) || 0) + '(' + fmtBytes((stats && stats.rxBytes) || 0) + 'B) · TX ' + ((stats && stats.tx) || 0) + '(' + fmtBytes((stats && stats.txBytes) || 0) + 'B)'),
        btn(paused ? '▶ 继续' : '⏸ 暂停', () => setPaused(!paused), paused, '暂停后数据在 Host 侧继续累积,恢复后拉齐'),
        btn(follow && autoSuspended ? '跟随·挂起' : '跟随', () => setFollow(!follow), effectiveFollow,
          autoSuspended ? '翻看历史中已自动挂起,回到底部或再点恢复' : '自动滚动到最新'),
        btn('HEX', () => setHex(!hex), hex, '以 HEX 显示数据'),
        h('select', {
          value: dir, onChange: (e) => setDir(e.target.value),
          style: { fontSize: 12, padding: '3px 6px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)' },
        },
          h('option', { value: 'ALL' }, '全部'),
          h('option', { value: 'RX' }, 'RX'),
          h('option', { value: 'TX' }, 'TX'),
        ),
        h('select', {
          value: port, onChange: (e) => setPort(e.target.value), title: '按端口标签过滤',
          style: { fontSize: 12, padding: '3px 6px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)' },
        },
          h('option', { value: 'ALL' }, '全部端口'),
          portOptions.map((t) => h('option', { key: t, value: t }, t)),
        ),
        h('input', {
          value: search, placeholder: '过滤 text/hex/tool/port…',
          onChange: (e) => setSearch(e.target.value),
          style: { flex: '1 1 140px', minWidth: 120, height: 28, fontSize: 12, padding: '0 10px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)', boxSizing: 'border-box' },
        }),
        btn(confirmClear ? '确认清空?' : '清空', clear, confirmClear, '截断共享 mcp-traffic.jsonl(再点一次生效)'),
        h('span', { style: { fontSize: 11, color: 'var(--dsw-alias-label-tertiary, #999)' } }, counterText),
      )

      // 空态:API 地址(agent 可直接 curl)+ 链路状态 + shell 工具解析诊断
      const diag = stats && stats.diag
      const shellLine = (label, exe, ok, hint) => h('div', { style: { color: ok ? '#059669' : '#d97706' } },
        (ok ? '✓ ' : '⚠ ') + label + ' → ' + (exe || '(PATH 中未找到)') + (ok ? '' : '  ·  ' + hint))
      const apiFull = (typeof location !== 'undefined' && location.origin ? location.origin : '') + ROUTE
      const emptyBox = h('div', { style: { padding: 24, textAlign: 'center', color: 'var(--dsw-alias-label-tertiary, #999)', fontSize: 13 } },
        h('div', null,
          stats && stats.exists
            ? '日志已连接,等待 AI 串口收发…(让 Agent 调 mcp__acccom__list_ports 试试)'
            : '尚未发现 ' + ((stats && stats.logPath) || 'mcp-traffic.jsonl') + ' — 首次 AI 串口通信后自动出现'),
        h('div', { style: { marginTop: 6, fontSize: 11, display: 'inline-flex', alignItems: 'center', gap: 6 } },
          'API:',
          h('span', { style: { fontFamily: 'Consolas, Menlo, monospace' } }, apiFull),
          h(CopyBtn, { text: apiFull, label: '复制' })),
        stats && stats.exists && typeof stats.logSize === 'number'
          ? h('div', { style: { marginTop: 4, fontSize: 11 } },
              '日志 ' + fmtBytes(stats.logSize) + 'B · 坏行 ' + (stats.badLines || 0)
              + ' · 轮转文件' + (stats.rotated ? '已生成' : '未生成'))
          : null,
        diag
          ? h('div', { style: { marginTop: 8, fontSize: 11, lineHeight: 1.8 } },
              shellLine('pwsh', diag.pwsh, diag.pwshOk, 'PATH 中找不到 pwsh.exe'),
              shellLine('bash', diag.bash, diag.bashOk, 'System32 的是 WSL 存根;用 start-dsh-with-gitbash.cmd 启动可换成 Git Bash'))
          : null,
      )

      const list = h('div', {
        ref: listRef, onScroll,
        style: { flex: 1, minHeight: 0, width: '100%', overflowX: 'hidden', overflowY: 'auto', background: 'var(--dsw-alias-bg-layer-2, #fff)' },
      },
        visibleCount === 0
          ? emptyBox
          : rendered.map((e) => (e.kind === 'gap'
              ? h(GapRow, { key: 'gap-' + e.seq, count: e.count })
              : h(Row, { key: e.seq, e, hex, selected: selected && selected.seq === e.seq, onSelect: setSelected }))),
      )

      const detail = selected ? h('div', {
        style: {
          flex: '0 0 auto', maxHeight: 180, overflow: 'auto', borderTop: '1px solid var(--dsw-alias-border-l2, #eee)',
          padding: '8px 12px', fontSize: 12, background: 'var(--dsw-alias-bg-layer-3, #fafafa)',
        },
      },
        h('div', { style: { marginBottom: 4, display: 'flex', alignItems: 'center', gap: 8, color: 'var(--dsw-alias-label-secondary, #666)' } },
          h('span', { style: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
            '#' + selected.seq + ' · ' + shortTime(selected.ts) + ' · ' + selected.dir + ' · '
              + selected.tool + (selected.tag ? ' · ' + selected.tag : '') + ' · ' + selected.len + 'B'
              + (selected.dtMs != null ? ' · Δ' + fmtDt(selected.dtMs) : '')),
          selected.text ? h(CopyBtn, { text: selected.text, label: '复制text' }) : null,
          selected.hex ? h(CopyBtn, { text: fmtHex(selected.hex), label: '复制HEX' }) : null,
        ),
        h('div', { style: { fontFamily: 'Consolas, Menlo, monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-all', color: 'var(--dsw-alias-label-primary, #222)' } },
          selected.text || '(空)'),
        h('div', { style: { fontFamily: 'Consolas, Menlo, monospace', whiteSpace: 'pre-wrap', wordBreak: 'break-all', color: 'var(--dsw-alias-label-tertiary, #999)', marginTop: 4 } },
          fmtHex(selected.hex)),
      ) : null

      return h('div', {
        style: {
          // 停靠 pane 内必须锁宽:内容(hex 行不换行)会把无宽约束的根撑到比 pane 宽,
          // 被 tabBody 的 overflow:hidden 直接裁掉——拖拽调宽时表现为"数据不跟随"
          width: '100%', maxWidth: '100%', minWidth: 0, overflow: 'hidden',
          containerType: 'inline-size', containerName: 'sp-panel',
          height: '100%', boxSizing: 'border-box', display: 'flex', flexDirection: 'column',
          background: 'var(--dsw-alias-bg-layer-2, #fff)',
          color: 'var(--dsw-alias-label-primary, #222)',
        },
      },
        toolbar, list, detail,
      )
    }

    // ---- 组件:侧边栏面板入口(点击打开面板;优先右栏 tab,兜底整屏) ----
    const PanelIcon = (props) => {
      const onClick = (e) => {
        if (props && typeof props.onOpen === 'function') { props.onOpen(e); return }
        if (props && typeof props.onClick === 'function') { props.onClick(e); return }
        try {
          // 槽位壳可能只渲染图标本身:兜底自己触发面板切换
          const layout = ctxRef && ctxRef.get && ctxRef.get('layout')
          if (layout && typeof layout.selectPanel === 'function') layout.selectPanel('acccom-serial')
        } catch (e2) { /* layout 服务缺席时静默 */ }
      }
      return h('span', {
        title: props && props.title ? props.title : 'ACCCOM 串口面板',
        onClick,
        style: { display: 'inline-flex', alignItems: 'center', justifyContent: 'center', cursor: 'pointer', lineHeight: 1 },
      }, h(PlugGlyph, { size: 16 }))
    }

    return {
      inject: ['slots', 'sidebarRight', 'sidebarRightTabs'],
      apply: function (ctx) {
        const slots = ctx.get('slots')
        if (slots === undefined) return
        ctxRef = ctx

        const rightTabs = ctx.get('sidebarRightTabs')
        const right = ctx.get('sidebarRight')
        const hasRightbar = !!(rightTabs && typeof rightTabs.register === 'function'
          && right && typeof right.openTab === 'function')

        if (hasRightbar) {
          // 主路径:右侧停靠栏 tab —— 对话始终可见;keepMounted 让轮询跨切换/收起存活
          ctx.effect(() => rightTabs.register({
            id: 'dsh-serial-panel',
            kind: 'acccom-serial',
            title: () => 'ACCCOM 串口',
            keepMounted: true,
            guide: [{
              id: 'acccom-serial',
              kind: 'acccom-serial',
              title: () => 'ACCCOM 串口',
              description: () => 'AI 串口收发实时面板',
              icon: PlugGlyph,
            }],
          }), 'dsh-serial-panel: tab type')

          ctx.effect(() => slots.inject('sidebar.right.pane.tab', () => slots.register(
            { name: 'sidebar.right.pane.tab', key: 'dsh-serial-panel' },
            (props) => h(SerialPanel, props),
          )), 'dsh-serial-panel: pane body')

          // 左侧图标 = 打开右侧 tab(无会话时 openTab 会抛,吞掉即可)
          slots.inject('sidebar.panellist', () => slots.register(
            { name: 'sidebar.panellist', id: 'acccom-serial', order: 80, label: () => 'ACCCOM 串口' },
            (props) => h(PanelIcon, {
              ...props,
              title: '打开 ACCCOM 串口面板(右侧停靠,对话保留)',
              onOpen: () => { try { right.openTab('acccom-serial') } catch (e) { /* 无会话/面板未挂 */ } },
            }),
          ))
        } else {
          // 兜底:DSH 缺 rightbar 服务(版本差异)→ 整屏 main 模式,行为同 v1.2.0
          slots.inject('sidebar.panellist', () => slots.register(
            { name: 'sidebar.panellist', id: 'acccom-serial', order: 80, label: () => 'ACCCOM 串口' },
            (props) => h(PanelIcon, props),
          ))

          slots.inject('main', () => slots.register(
            { name: 'main', key: 'acccom-serial' },
            (props) => h(SerialPanel, props),
          ))
        }
      },
    }
  },
})
