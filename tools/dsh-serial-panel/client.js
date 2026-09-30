'use strict'
// ============================================================
//  dsh-serial-panel — Client 半区(浏览器面板,永久 cordis 插件)  v1.6.0
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
//
//  ── v1.4.0 易用性 ──
//   * 跟随挂起时列表右下角浮出「↓ N 条新数据」按钮,点击回底并恢复跟随
//     (标准 log-viewer 模式:翻历史期间不再错过新数据)
//   * 行内端口色块可点击 = 过滤该端口(再点同端口切回全部)
//   * 搜索命中在行内高亮(payload/mark)
//   * 语义色接入 DSH 主题 token(state-success/warn/error、brand,
//     原十六进制作兜底值,token 缺失时渲染不变)
//
//  ── v1.5.0 感知与效率 ──
//   * 左侧图标「有新流量」圆点:图标自带 ?stats=1 零载荷轮询(5s,页面隐藏 8s),
//     面板可见且未挂起时即视为已读;tab 体未挂载时徽章仍工作
//   * 正则搜索:.* 开关,非法表达式自动回退子串;命中高亮同样支持正则
//   * 快捷键(仅面板获得焦点时,绝不影响对话输入):空格=暂停,/ =聚焦搜索,
//     Esc=取消选中
//
//  ── v1.6.0 按需导出与自发现 ──
//   * 工具栏「导出」:当前过滤视图(可见条目)一键下载 JSONL,Blob 本地生成,
//     不经 Host(Host /export 仍负责整个内存环)
//   * Host 新增 GET /help 返回 AGENTS.md:agent 发现端点后可自助,无需先读仓库
//   * 详情区补完整日期(跨天会话只有时分秒会对不上)
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
    const MARK_STYLE = { background: 'rgba(245, 158, 11, 0.35)', color: 'inherit', borderRadius: 2, padding: '0 1px' }

    // ---- 流量徽章共享态:图标轮询推进 lastSeq,面板可见时标记已读 ----
    const badgeStore = {
      lastSeq: 0,
      lastSeenSeq: 0,
      subs: new Set(),
      update(seq) { if (seq > this.lastSeq) { this.lastSeq = seq; this.notify() } },
      markSeen(seq) { if (seq > this.lastSeenSeq) { this.lastSeenSeq = seq; this.notify() } },
      unread() { return this.lastSeq > this.lastSeenSeq },
      notify() { for (const fn of this.subs) { try { fn() } catch (e) { /* 订阅者异常不扩散 */ } } },
      subscribe(fn) { this.subs.add(fn); return () => this.subs.delete(fn) },
    }

    // 搜索匹配器:正则开关开启且表达式合法 → RegExp;否则小写子串(空查询 → null)
    const compileMatcher = (query, useRegex) => {
      const q = String(query || '').trim()
      if (!q) return null
      if (useRegex) {
        try { return new RegExp(q, 'i') } catch (e) { return q.toLowerCase() } // 非法正则回退子串
      }
      return q.toLowerCase()
    }

    const matcherHit = (matcher, e) => (matcher instanceof RegExp
      ? matcher.test(e.text || '') || matcher.test(e.hex || '') || matcher.test(e.tool || '') || matcher.test(e.tag || '')
      : (e.text && e.text.toLowerCase().indexOf(matcher) >= 0)
        || (e.hex && e.hex.toLowerCase().indexOf(matcher) >= 0)
        || (e.tool && e.tool.toLowerCase().indexOf(matcher) >= 0)
        || (e.tag && e.tag.toLowerCase().indexOf(matcher) >= 0))

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

    // 搜索命中高亮:大小写不敏感,返回字符串/mark 混合的 children 数组
    const highlight = (text, q) => {
      if (!q) return [text]
      const s = String(text)
      const lower = s.toLowerCase()
      const needle = q.toLowerCase()
      if (!needle) return [s]
      const out = []
      let i = 0
      for (;;) {
        const hit = lower.indexOf(needle, i)
        if (hit < 0) { if (i < s.length) out.push(s.slice(i)); break }
        if (hit > i) out.push(s.slice(i, hit))
        out.push(h('mark', { key: hit, style: MARK_STYLE }, s.slice(hit, hit + needle.length)))
        i = hit + needle.length
      }
      return out.length ? out : [s]
    }

    // 正则版高亮:exec 循环需要 g 标志(非 g 的 exec 永远返回首个命中 → 死循环),
    // 内部克隆加 g;零长匹配 lastIndex 前进 1 位防空转
    const highlightRe = (text, re) => {
      const s = String(text)
      const g = re.flags.indexOf('g') >= 0 ? re : new RegExp(re.source, re.flags + 'g')
      g.lastIndex = 0
      const out = []
      let i = 0
      let m
      let guard = 0
      while ((m = g.exec(s)) !== null && guard++ < 1000) {
        if (m.index > i) out.push(s.slice(i, m.index))
        out.push(h('mark', { key: i + ':' + m.index, style: MARK_STYLE }, m[0]))
        i = m.index + (m[0].length || 1)
        if (!m[0].length) g.lastIndex = i
      }
      if (i < s.length) out.push(s.slice(i))
      return out.length ? out : [s]
    }

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

    // 当前过滤视图 → JSONL 下载(Blob 本地生成;gap 标记不是数据,剔除)
    const downloadJsonl = (entries, name) => {
      try {
        const lines = entries.filter((e) => !e.kind).map((e) => JSON.stringify(e))
        const blob = new Blob([lines.join('\n') + (lines.length ? '\n' : '')], { type: 'application/x-ndjson' })
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = (name || 'acccom-serial-view-') + new Date().toISOString().replace(/[:.]/g, '-') + '.jsonl'
        document.body.appendChild(a)
        a.click()
        document.body.removeChild(a)
        setTimeout(() => URL.revokeObjectURL(url), 1000)
        return true
      } catch (e) { return false }
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
          background: done ? 'var(--dsw-alias-state-success-primary, #10b981)' : 'var(--dsw-alias-bg-layer-3, #fff)',
          color: done ? 'var(--dsw-alias-bg-layer-1, #fff)' : 'var(--dsw-alias-label-secondary, #666)', font: 'inherit',
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
    const Row = React.memo(function Row({ e, hex, selected, onSelect, matcher, onPort }) {
      const raw = hex ? (fmtHex(e.hex) || e.text) : (e.text || fmtHex(e.hex))
      const payload = raw.length > ROW_PAYLOAD_MAX ? raw.slice(0, ROW_PAYLOAD_MAX) + ' …' : raw
      const payloadChildren = !matcher ? payload
        : matcher instanceof RegExp ? highlightRe(payload, matcher)
        : highlight(payload, matcher)
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
          title: '按端口 ' + e.tag + ' 过滤(再点切回全部)',
          onClick: (ev) => {
            ev.stopPropagation()
            if (typeof onPort === 'function') onPort(e.tag)
          },
          style: {
            flex: '0 0 8px', height: 8, borderRadius: 999, alignSelf: 'center',
            cursor: 'pointer',
            boxShadow: '0 0 0 2px transparent',
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
        }, payloadChildren),
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
      const [useRegex, setUseRegex] = React.useState(!!prefs0.useRegex)
      const [autoSuspended, setAutoSuspended] = React.useState(false) // 向上翻历史时自动挂起跟随(临时态)
      const [newCount, setNewCount] = React.useState(0) // 挂起期间到达的新条数(浮出提示)
      const [confirmClear, setConfirmClear] = React.useState(false)
      const [search, setSearch] = React.useState('')
      const [selected, setSelected] = React.useState(null)
      const lastSeqRef = React.useRef(0)
      const pausedRef = React.useRef(paused)
      const listRef = React.useRef(null)
      const searchRef = React.useRef(null)
      const rootRef = React.useRef(null)
      const confirmTimerRef = React.useRef(null)
      const inFlightRef = React.useRef(false)
      const autoSuspendedRef = React.useRef(false)

      React.useEffect(() => { pausedRef.current = paused }, [paused])
      React.useEffect(() => { autoSuspendedRef.current = autoSuspended }, [autoSuspended])
      React.useEffect(() => { savePrefs({ hex, dir, follow, port, useRegex }) }, [hex, dir, follow, port, useRegex])
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
                // 跟随挂起期间到达的数据计数,浮出「新数据」提示
                if (autoSuspendedRef.current) setNewCount((c) => c + inc.length)
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
        autoSuspendedRef.current = !atBottom // ref 同步赋值:事件与轮询之间不留渲染空窗
        setAutoSuspended(!atBottom)          // 值未变时 React 跳过重渲染
        if (atBottom) setNewCount(0)
      }

      // 浮出提示点击:回底部(随后的 scroll 事件会自动恢复跟随并清零计数)
      const jumpToLatest = () => {
        const el = listRef.current
        if (el) el.scrollTop = el.scrollHeight
        setNewCount(0)
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

      // ---- 过滤结果缓存:只在数据/方向/端口/匹配器变化时重算 ----
      const matcher = React.useMemo(() => compileMatcher(search, useRegex), [search, useRegex])
      const visible = React.useMemo(() => {
        const out = []
        for (let i = 0; i < entries.length; i++) {
          const e = entries[i]
          if (e.kind === 'gap') {
            if (dir === 'ALL' && !matcher) out.push(e) // 断层标记仅在无过滤时显示
            continue
          }
          if (dir !== 'ALL' && e.dir !== dir) continue
          if (port !== 'ALL' && e.tag !== port) continue
          if (matcher && !matcherHit(matcher, e)) continue
          out.push(e)
        }
        return out
      }, [entries, dir, port, matcher])

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
          style: {
            width: 8, height: 8, borderRadius: 999, flex: '0 0 auto',
            background: !connected
              ? 'var(--dsw-alias-state-error-primary, #ef4444)'
              : (stats && stats.exists === false)
                ? 'var(--dsw-alias-state-warn-primary, #f59e0b)'
                : 'var(--dsw-alias-state-success-primary, #10b981)',
          },
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
        btn('.*', () => setUseRegex(!useRegex), useRegex, '正则模式(非法表达式自动回退子串匹配)'),
        h('input', {
          ref: searchRef,
          value: search,
          placeholder: useRegex ? '正则过滤 text/hex/tool/port…' : '过滤 text/hex/tool/port…',
          onChange: (e) => setSearch(e.target.value),
          style: { flex: '1 1 140px', minWidth: 120, height: 28, fontSize: 12, padding: '0 10px', borderRadius: 8, border: '1px solid var(--dsw-alias-border-l2, #ddd)', background: 'var(--dsw-alias-bg-layer-3, #fff)', color: 'var(--dsw-alias-label-primary, #222)', boxSizing: 'border-box' },
        }),
        btn(confirmClear ? '确认清空?' : '清空', clear, confirmClear, '截断共享 mcp-traffic.jsonl(再点一次生效)'),
        btn('导出', () => downloadJsonl(visible), false, '下载当前过滤视图为 JSONL(整个内存环用 Host /export 路由)'),
        h('span', { style: { fontSize: 11, color: 'var(--dsw-alias-label-tertiary, #999)' } }, counterText),
      )

      // 空态:API 地址(agent 可直接 curl)+ 链路状态 + shell 工具解析诊断
      const diag = stats && stats.diag
      const shellLine = (label, exe, ok, hint) => h('div', {
        style: { color: ok ? 'var(--dsw-alias-state-success-primary, #059669)' : 'var(--dsw-alias-state-warn-primary, #d97706)' },
      },
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
        style: { flex: 1, minHeight: 0, width: '100%', overflowX: 'hidden', overflowY: 'auto' },
      },
        visibleCount === 0
          ? emptyBox
          : rendered.map((e) => (e.kind === 'gap'
              ? h(GapRow, { key: 'gap-' + e.seq, count: e.count })
              : h(Row, {
                  key: e.seq, e, hex,
                  matcher,
                  onPort: (tag) => setPort(port === tag ? 'ALL' : tag),
                  selected: selected && selected.seq === e.seq, onSelect: setSelected,
                }))),
      )

      // 跟随挂起期间有新数据 → 右下角浮出提示,点击回底并恢复跟随
      const listWrap = h('div', {
        style: { flex: 1, minHeight: 0, position: 'relative', display: 'flex', flexDirection: 'column', background: 'var(--dsw-alias-bg-layer-2, #fff)' },
      },
        list,
        (autoSuspended && newCount > 0) ? h('button', {
          type: 'button', onClick: jumpToLatest, title: '跳到最新并恢复跟随',
          style: {
            position: 'absolute', bottom: 14, right: 14, zIndex: 5,
            appearance: 'none', cursor: 'pointer', border: 'none', borderRadius: 999,
            padding: '6px 12px', fontSize: 12, fontWeight: 600, font: 'inherit',
            color: 'var(--dsw-alias-bg-layer-1, #fff)',
            background: 'var(--dsw-alias-brand-primary, #4f6bed)',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.18)',
          },
        }, '↓ ' + newCount + ' 条新数据') : null,
      )

      const detail = selected ? h('div', {
        style: {
          flex: '0 0 auto', maxHeight: 180, overflow: 'auto', borderTop: '1px solid var(--dsw-alias-border-l2, #eee)',
          padding: '8px 12px', fontSize: 12, background: 'var(--dsw-alias-bg-layer-3, #fafafa)',
        },
      },
        h('div', { style: { marginBottom: 4, display: 'flex', alignItems: 'center', gap: 8, color: 'var(--dsw-alias-label-secondary, #666)' } },
          h('span', { style: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } },
            '#' + selected.seq + ' · ' + shortTime(selected.ts)
              + (typeof selected.ts === 'string' && selected.ts.length >= 10 ? ' · ' + selected.ts.slice(0, 10) : '')
              + ' · ' + selected.dir + ' · '
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

      // ---- 快捷键(仅面板持有焦点时;对话输入框的事件不经过本 DOM,不受影响) ----
      const isFormField = (t) => {
        const tag = t && t.tagName
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable)
      }
      const onKeyDown = (e) => {
        if (isFormField(e.target) || e.target.tagName === 'BUTTON') return // Space 让按钮保持原生点击
        if (e.key === ' ') { e.preventDefault(); setPaused(!pausedRef.current) }
        else if (e.key === '/') { e.preventDefault(); if (searchRef.current) searchRef.current.focus() }
        else if (e.key === 'Escape') setSelected(null)
      }
      // 点击面板任意非表单区域即聚焦根节点,让快捷键随手可用
      const onMouseDown = (e) => {
        if (isFormField(e.target) || e.target.tagName === 'BUTTON') return
        if (rootRef.current) rootRef.current.focus()
      }

      // 面板可见且未暂停未挂起 = 正在实时看 → 标记已读,清掉左侧图标圆点
      React.useEffect(() => {
        if (tabVisible && !paused && !autoSuspended && stats) badgeStore.markSeen(stats.lastSeq || 0)
      }, [tabVisible, paused, autoSuspended, stats])

      return h('div', {
        ref: rootRef, tabIndex: -1, onKeyDown, onMouseDown,
        style: {
          // 停靠 pane 内必须锁宽:内容(hex 行不换行)会把无宽约束的根撑到比 pane 宽,
          // 被 tabBody 的 overflow:hidden 直接裁掉——拖拽调宽时表现为"数据不跟随"
          outline: 'none',
          width: '100%', maxWidth: '100%', minWidth: 0, overflow: 'hidden',
          containerType: 'inline-size', containerName: 'sp-panel',
          height: '100%', boxSizing: 'border-box', display: 'flex', flexDirection: 'column',
          background: 'var(--dsw-alias-bg-layer-2, #fff)',
          color: 'var(--dsw-alias-label-primary, #222)',
        },
      },
        toolbar, listWrap, detail,
      )
    }

    // ---- 组件:侧边栏面板入口(点击打开面板;优先右栏 tab,兜底整屏) ----
    // 自带 ?stats=1 轻量轮询驱动「有新流量」圆点:tab 体未挂载时徽章的唯一数据源
    const PanelIcon = (props) => {
      const [unread, setUnread] = React.useState(badgeStore.unread())
      React.useEffect(() => badgeStore.subscribe(() => setUnread(badgeStore.unread())), [])
      React.useEffect(() => {
        let alive = true
        let timer = null
        const tick = async () => {
          timer = null
          if (!alive) return
          if (document.visibilityState === 'hidden') { timer = setTimeout(tick, 8000); return }
          try {
            const r = await fetch(ROUTE + '?stats=1', { cache: 'no-store' })
            const j = await r.json()
            if (alive && j && j.ok && j.stats) badgeStore.update(j.stats.lastSeq || 0)
          } catch (e) { /* Host 未起:静默 */ }
          if (alive) timer = setTimeout(tick, 5000)
        }
        tick()
        return () => { alive = false; if (timer !== null) clearTimeout(timer) }
      }, [])
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
        style: { position: 'relative', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', cursor: 'pointer', lineHeight: 1 },
      },
        h(PlugGlyph, { size: 16 }),
        unread ? h('span', {
          title: '有新的串口流量',
          style: {
            position: 'absolute', top: -3, right: -5, width: 7, height: 7,
            borderRadius: 999,
            background: 'var(--dsw-alias-state-error-primary, #ef4444)',
            border: '1px solid var(--dsw-alias-bg-layer-2, #fff)',
          },
        }) : null,
      )
    }

    return {
      inject: ['slots', 'sidebarRight', 'sidebarRightTabs'],
      // 纯函数仅供 test.js client 模式断言,生产逻辑不依赖
      _test: { highlight, highlightRe, compileMatcher, matcherHit, hueOf, fmtDt, sameStats, badgeStore },
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
