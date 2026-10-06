'use strict'
// ============================================================
//  dsh-serial-panel — Client 半区(浏览器面板,永久 cordis 插件)  v1.7.0
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
//
//  ── v1.7.0 设计改版:仪器读数台(SIGNAL DESK) ──
//  纯表现层重构,行为与数据契约零变化。设计概念:把面板从「一列灰字」改成
//  一台 bench instrument 的读数台——
//   * 顶部读数条(readout strip):呼吸灯 + RX/TX 账目(等宽 tabular-nums,
//      engraved 小标签)+ 右侧行数计数器,一眼是「仪器」而不是「网页」;
//   * 控制条(control strip):全部控件收成 24px 高的 engraved chip,engaged
//     状态用品牌 9% 淡染(暂停=琥珀、清空确认=错误红),hover/聚焦有态;
//   * 行解剖:左侧 2px「信号脊」按方向着色(RX 绿 / TX 琥珀 / SYS 中性),
//     选中时脊线全亮 + 品牌淡染底;方向徽章从实色块改为描边 chip;
//   * 字阶分层:标签 10px/0.1em 字距大写,数据 12px 等宽 tabular-nums,
//     全程走 DSH 设计 token(--dsw-alias-*),深浅主题由 body[data-ds-dark-theme]
//     切换校准值,token 缺失时十六进制作兜底;
//   * 动效收敛在三处:首屏行错峰淡入(前 12 行 25ms 阶梯)、呼吸灯、
//     新数据浮钮上浮;prefers-reduced-motion 全部关闭。
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

    // ---- 一次性样式表 ----
    // inline style 写不了 :hover / 动画 / 容器查询 / 深色覆盖,整panel的视觉
    // 都收在这里;类名前缀 sp-(serial panel),与 DSH 其余插件无碰撞。
    // 命名约定:sp- 根与骨架 / sp-row 行 / sp-dir 方向徽章 / sp-btn chip /
    // sp-ledger 读数账目 / sp-mark 搜索命中 / sp-gap 断层 / sp-detail 详情。
    const injectStyles = () => {
      try {
        if (document.getElementById('dsh-serial-panel-styles')) return
        const el = document.createElement('style')
        el.id = 'dsh-serial-panel-styles'
        el.textContent = [
          // ---------- 根与设计 token ----------
          // 方向色(RX 绿 / TX 琥珀)在浅色下取校准深值保证 4.5:1 对比,
          // 深色主题换亮值;品牌淡染用 color-mix,Chromium 111+(DSH 同款写法)。
          '.sp-root {',
          '  --sp-rx: #15803d; --sp-rx-soft: rgba(22, 163, 74, 0.12); --sp-rx-line: rgba(22, 163, 74, 0.5);',
          '  --sp-tx: #b45309; --sp-tx-soft: rgba(217, 119, 6, 0.12); --sp-tx-line: rgba(217, 119, 6, 0.5);',
          '  --sp-hair: var(--dsw-alias-border-l2, #ececec);',
          '  --sp-sans: var(--dsw-font-family, "Segoe UI", system-ui, sans-serif);',
          '  --sp-mono: var(--ds-font-family-code, var(--dsw-font-mono, Consolas, Menlo, monospace));',
          '  --sp-brand: var(--dsw-alias-brand-primary, #0f1115);',
          '  --sp-ink-1: var(--dsw-alias-label-primary, #1a1a1a);',
          '  --sp-ink-2: var(--dsw-alias-label-secondary, #5f6368);',
          '  --sp-ink-3: var(--dsw-alias-label-tertiary, #81858c);',
          '  --sp-sunken: var(--dsw-alias-bg-layer-3, #f5f5f5);',
          '  --sp-wash: color-mix(in srgb, var(--sp-brand) 9%, transparent);',
          '  --sp-wash-line: color-mix(in srgb, var(--sp-brand) 38%, transparent);',
          '  --sp-err: var(--dsw-alias-state-error-primary, #dc2626);',
          '  --sp-ok: var(--dsw-alias-state-success-primary, #16a34a);',
          '  --sp-warn: var(--dsw-alias-state-warn-primary, #d97706);',
          '}',
          'body[data-ds-dark-theme] .sp-root {',
          '  --sp-rx: #4ed17e; --sp-rx-soft: rgba(78, 209, 126, 0.16); --sp-rx-line: rgba(78, 209, 126, 0.55);',
          '  --sp-tx: #f7ad31; --sp-tx-soft: rgba(247, 173, 49, 0.16); --sp-tx-line: rgba(247, 173, 49, 0.55);',
          '  --sp-wash: color-mix(in srgb, var(--sp-brand) 16%, transparent);',
          '  --sp-wash-line: color-mix(in srgb, var(--sp-brand) 42%, transparent);',
          '}',

          // ---------- 面板骨架 ----------
          '.sp-root {',
          '  container-type: inline-size; container-name: sp-panel;',
          '  width: 100%; max-width: 100%; min-width: 0; overflow: hidden;',
          '  height: 100%; box-sizing: border-box; display: flex; flex-direction: column;',
          '  background: var(--dsw-alias-bg-layer-2, #fff); color: var(--sp-ink-1);',
          '  font-family: var(--sp-sans); font-size: 12px;',
          '}',

          // ---------- 读数条:呼吸灯 + RX/TX 账目 + 行数 ----------
          '.sp-read {',
          '  flex: none; display: flex; align-items: center; gap: 10px;',
          '  padding: 7px 10px 6px; border-bottom: 1px solid var(--sp-hair);',
          '  background: var(--dsw-alias-bg-layer-2, #fff);',
          '}',
          '.sp-lamp { position: relative; width: 7px; height: 7px; border-radius: 999px; flex: none; }',
          '.sp-lamp[data-s="live"] { background: var(--sp-ok); }',
          '.sp-lamp[data-s="live"]::after {',
          '  content: ""; position: absolute; inset: -3px; border-radius: 999px;',
          '  border: 1px solid var(--sp-ok); animation: sp-pulse 2.6s ease-out infinite;',
          '}',
          '.sp-lamp[data-s="wait"] { background: var(--sp-warn); }',
          '.sp-lamp[data-s="dead"] { background: var(--sp-err); }',
          '@keyframes sp-pulse { 0% { transform: scale(0.55); opacity: 0.9; } 70%, 100% { transform: scale(1.7); opacity: 0; } }',
          '.sp-ledger { display: flex; align-items: baseline; gap: 5px; font-family: var(--sp-mono); font-variant-numeric: tabular-nums; }',
          '.sp-k { font-size: 10px; font-weight: 600; letter-spacing: 0.1em; text-transform: uppercase; }',
          '.sp-k.rx { color: var(--sp-rx); }',
          '.sp-k.tx { color: var(--sp-tx); }',
          '.sp-v { font-size: 12px; font-weight: 600; color: var(--sp-ink-1); }',
          '.sp-u { font-size: 10px; color: var(--sp-ink-3); }',
          '.sp-vsep { width: 1px; height: 12px; background: var(--sp-hair); margin: 0 3px; align-self: center; }',
          '.sp-count { margin-left: auto; font-family: var(--sp-mono); font-size: 10px; color: var(--sp-ink-3); font-variant-numeric: tabular-nums; white-space: nowrap; }',
          '.sp-pausetag {',
          '  font-size: 10px; font-weight: 600; letter-spacing: 0.1em;',
          '  color: var(--sp-tx); border: 1px solid var(--sp-tx-line); background: var(--sp-tx-soft);',
          '  border-radius: 4px; padding: 1px 5px;',
          '}',

          // ---------- 控制条 ----------
          '.sp-ctl {',
          '  flex: none; display: flex; align-items: center; gap: 6px; flex-wrap: wrap;',
          '  padding: 7px 10px; border-bottom: 1px solid var(--sp-hair);',
          '}',
          '.sp-btn {',
          '  --sp-wash: color-mix(in srgb, var(--sp-brand) 9%, transparent);',
          '  --sp-wash-line: color-mix(in srgb, var(--sp-brand) 38%, transparent);',
          '  --sp-wash-ink: var(--sp-brand);',
          '  appearance: none; -webkit-appearance: none; cursor: pointer; font: inherit; font-size: 11px;',
          '  display: inline-flex; align-items: center; gap: 5px; height: 24px; padding: 0 9px;',
          '  border-radius: 6px; border: 1px solid var(--sp-hair); background: transparent;',
          '  color: var(--sp-ink-2); white-space: nowrap;',
          '  transition: background-color 0.12s ease, color 0.12s ease, border-color 0.12s ease;',
          '}',
          '.sp-btn:hover { background: var(--dsw-alias-interactive-bg-hover, rgba(15, 17, 21, 0.05)); color: var(--sp-ink-1); }',
          '.sp-btn:focus-visible { outline: 2px solid var(--dsw-focus-ring-color, var(--sp-brand)); outline-offset: 1px; }',
          '.sp-btn[data-on="1"] { background: var(--sp-wash); border-color: var(--sp-wash-line); color: var(--sp-wash-ink); }',
          '.sp-btn.sp-warn { --sp-wash: var(--sp-tx-soft); --sp-wash-line: var(--sp-tx-line); --sp-wash-ink: var(--sp-tx); }',
          '.sp-btn.sp-danger { --sp-wash: color-mix(in srgb, var(--sp-err) 12%, transparent); --sp-wash-line: color-mix(in srgb, var(--sp-err) 45%, transparent); --sp-wash-ink: var(--sp-err); }',
          '.sp-select {',
          '  height: 24px; font: inherit; font-size: 11px; border-radius: 6px;',
          '  border: 1px solid var(--sp-hair); background: var(--sp-sunken); color: var(--sp-ink-1);',
          '  padding: 0 2px; cursor: pointer;',
          '}',
          '.sp-select:focus-visible { outline: 2px solid var(--dsw-focus-ring-color, var(--sp-brand)); outline-offset: 1px; }',
          '.sp-search {',
          '  flex: 1 1 150px; min-width: 110px; height: 24px; font: inherit; font-size: 11px;',
          '  padding: 0 8px; border-radius: 6px; box-sizing: border-box;',
          '  border: 1px solid var(--sp-hair); background: var(--sp-sunken); color: var(--sp-ink-1);',
          '}',
          '.sp-search::placeholder { color: var(--sp-ink-3); }',
          '.sp-search:focus { outline: none; border-color: var(--sp-wash-line); }',
          '.sp-search:focus-visible { outline: 2px solid var(--dsw-focus-ring-color, var(--sp-brand)); outline-offset: 1px; }',

          // ---------- 列表与行 ----------
          '.sp-listwrap {',
          '  flex: 1; min-height: 0; position: relative; display: flex; flex-direction: column;',
          '  background: var(--dsw-alias-bg-layer-2, #fff);',
          '}',
          '.sp-listwrap[data-paused="1"] .sp-list { box-shadow: inset 0 2px 0 var(--sp-tx); }',
          '.sp-list {',
          '  flex: 1; min-height: 0; width: 100%; overflow-x: hidden; overflow-y: auto;',
          '  --dsh-scrollbar-thumb: var(--dsw-alias-scrollbar-bg-l2, rgba(15, 17, 21, 0.18));',
          '  --dsh-scrollbar-thumb-hover: var(--dsw-alias-scrollbar-hover-l2, rgba(15, 17, 21, 0.3));',
          '  scrollbar-width: thin;',
          '}',
          '.sp-row {',
          '  position: relative; display: flex; align-items: baseline; gap: 8px;',
          '  min-width: 0; width: 100%; box-sizing: border-box;',
          '  padding: 3px 10px 3px 12px; cursor: pointer;',
          '  border-bottom: 0.5px solid var(--sp-hair);',
          '  font-size: 12px; line-height: 1.7;',
          '  transition: background-color 0.12s ease;',
          '  animation: sp-in 0.22s ease both;',
          '}',
          '.sp-row::before {',
          '  content: ""; position: absolute; left: 0; top: 0; bottom: 0; width: 2px;',
          '  background: transparent; transition: background-color 0.12s ease;',
          '}',
          '.sp-row[data-dir="RX"]::before { background: var(--sp-rx-line); }',
          '.sp-row[data-dir="TX"]::before { background: var(--sp-tx-line); }',
          '.sp-row[data-dir="SYS"]::before { background: var(--sp-hair); }',
          '.sp-row:hover { background: var(--dsw-alias-interactive-bg-hover, rgba(15, 17, 21, 0.045)); }',
          '.sp-row.sp-sel { background: var(--sp-wash); }',
          '.sp-row.sp-sel[data-dir="RX"]::before { background: var(--sp-rx); }',
          '.sp-row.sp-sel[data-dir="TX"]::before { background: var(--sp-tx); }',
          '.sp-row.sp-sel[data-dir="SYS"]::before { background: var(--sp-ink-3); }',
          '@keyframes sp-in { from { opacity: 0; transform: translateY(3px); } to { opacity: 1; transform: none; } }',
          // 首屏错峰:只给前 12 行加阶梯延迟,长列表不做人浪
          '.sp-list > *:nth-child(1) { animation-delay: 0ms; }',
          '.sp-list > *:nth-child(2) { animation-delay: 24ms; }',
          '.sp-list > *:nth-child(3) { animation-delay: 48ms; }',
          '.sp-list > *:nth-child(4) { animation-delay: 72ms; }',
          '.sp-list > *:nth-child(5) { animation-delay: 96ms; }',
          '.sp-list > *:nth-child(6) { animation-delay: 118ms; }',
          '.sp-list > *:nth-child(7) { animation-delay: 140ms; }',
          '.sp-list > *:nth-child(8) { animation-delay: 162ms; }',
          '.sp-list > *:nth-child(9) { animation-delay: 184ms; }',
          '.sp-list > *:nth-child(10) { animation-delay: 206ms; }',
          '.sp-list > *:nth-child(11) { animation-delay: 228ms; }',
          '.sp-list > *:nth-child(12) { animation-delay: 250ms; }',
          '@media (prefers-reduced-motion: reduce) {',
          '  .sp-row, .sp-lamp[data-s="live"]::after, .sp-jump, .sp-port, .sp-btn, .sp-copy { animation: none; transition: none; }',
          '}',

          // 行内单元
          '.sp-time { flex: 0 0 84px; color: var(--sp-ink-3); font-family: var(--sp-mono); font-variant-numeric: tabular-nums; font-size: 11px; }',
          '.sp-dir {',
          '  flex: 0 0 26px; display: inline-flex; align-items: center; justify-content: center;',
          '  height: 16px; border-radius: 4px; font-size: 10px; font-weight: 700; letter-spacing: 0.04em;',
          '  border: 1px solid var(--sp-hair); background: var(--sp-sunken); color: var(--sp-ink-3);',
          '}',
          '.sp-dir[data-dir="RX"] { color: var(--sp-rx); border-color: var(--sp-rx-line); background: var(--sp-rx-soft); }',
          '.sp-dir[data-dir="TX"] { color: var(--sp-tx); border-color: var(--sp-tx-line); background: var(--sp-tx-soft); }',
          '.sp-port {',
          '  flex: 0 0 8px; height: 8px; border-radius: 999px; align-self: center;',
          '  cursor: pointer; border: 0; padding: 0;',
          '  box-shadow: 0 0 0 2px transparent;',
          '  transition: transform 0.12s ease, box-shadow 0.12s ease;',
          '}',
          '.sp-port:hover { transform: scale(1.3); box-shadow: 0 0 0 3px var(--dsw-alias-interactive-bg-hover, rgba(15, 17, 21, 0.14)); }',
          '.sp-tool {',
          '  flex: 0 0 110px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;',
          '  color: var(--sp-ink-2); font-size: 11px;',
          '}',
          '.sp-len { flex: 0 0 44px; text-align: right; color: var(--sp-ink-3); font-family: var(--sp-mono); font-variant-numeric: tabular-nums; font-size: 11px; }',
          '.sp-dt {',
          '  flex: 0 0 48px; text-align: right; color: var(--sp-ink-3);',
          '  font-family: var(--sp-mono); font-variant-numeric: tabular-nums; font-size: 10px;',
          '}',
          '.sp-payload {',
          '  flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;',
          '  font-family: var(--sp-mono); font-size: 12px; color: var(--sp-ink-1);',
          '}',
          '.sp-payload[data-dir="RX"] { color: var(--sp-rx); }',
          '.sp-payload[data-dir="TX"] { color: var(--sp-tx); }',
          '.sp-mark {',
          '  background: color-mix(in srgb, var(--sp-warn) 32%, transparent);',
          '  color: inherit; border-radius: 2px; padding: 0 1px;',
          '}',

          // 断层标记
          '.sp-gap {',
          '  display: flex; align-items: center; gap: 8px; padding: 4px 10px;',
          '  font-family: var(--sp-mono); font-size: 10px; color: var(--sp-ink-3);',
          '  border-bottom: 0.5px solid var(--sp-hair); white-space: nowrap;',
          '}',
          '.sp-gap::before, .sp-gap::after {',
          '  content: ""; flex: 1; height: 1px;',
          '  background: repeating-linear-gradient(90deg, var(--sp-hair) 0 4px, transparent 4px 8px);',
          '}',

          // 新数据浮钮
          '.sp-jump {',
          '  position: absolute; bottom: 12px; right: 12px; z-index: 5;',
          '  appearance: none; -webkit-appearance: none; cursor: pointer; border: 0;',
          '  border-radius: 999px; padding: 5px 11px; font: inherit; font-size: 11px; font-weight: 600;',
          '  display: inline-flex; align-items: center; gap: 5px;',
          '  color: var(--dsw-alias-label-primary-inverted, #fff);',
          '  background: var(--sp-brand);',
          '  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.22);',
          '  animation: sp-rise 0.18s ease both;',
          '}',
          '.sp-jump:focus-visible { outline: 2px solid var(--sp-brand); outline-offset: 2px; }',
          '@keyframes sp-rise { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }',

          // ---------- 详情区 ----------
          '.sp-detail {',
          '  flex: none; max-height: 180px; overflow: auto;',
          '  border-top: 1px solid var(--sp-hair);',
          '  background: var(--sp-sunken); padding: 8px 10px;',
          '}',
          '.sp-meta { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; margin-bottom: 6px; }',
          '.sp-seq { font-family: var(--sp-mono); font-size: 11px; font-weight: 600; color: var(--sp-ink-1); }',
          '.sp-metacap { font-size: 10px; color: var(--sp-ink-3); font-variant-numeric: tabular-nums; }',
          '.sp-metaacts { margin-left: auto; display: flex; gap: 6px; }',
          '.sp-copy {',
          '  appearance: none; -webkit-appearance: none; cursor: pointer; font: inherit; font-size: 10px;',
          '  height: 20px; padding: 0 8px; border-radius: 5px;',
          '  border: 1px solid var(--sp-hair); background: transparent; color: var(--sp-ink-2);',
          '  transition: background-color 0.12s ease, color 0.12s ease;',
          '}',
          '.sp-copy:hover { background: var(--dsw-alias-interactive-bg-hover, rgba(15, 17, 21, 0.05)); color: var(--sp-ink-1); }',
          '.sp-copy[data-done="1"] {',
          '  color: var(--dsw-alias-label-primary-inverted, #fff);',
          '  background: var(--sp-ok); border-color: var(--sp-ok);',
          '}',
          '.sp-copy:focus-visible { outline: 2px solid var(--dsw-focus-ring-color, var(--sp-brand)); outline-offset: 1px; }',
          '.sp-text {',
          '  font-family: var(--sp-mono); font-size: 12px; line-height: 1.6;',
          '  white-space: pre-wrap; word-break: break-all; color: var(--sp-ink-1);',
          '}',
          '.sp-hex {',
          '  font-family: var(--sp-mono); font-size: 11px; line-height: 1.6;',
          '  white-space: pre-wrap; word-break: break-all; color: var(--sp-ink-3); margin-top: 4px;',
          '}',

          // ---------- 空态 ----------
          '.sp-empty {',
          '  flex: 1; min-height: 100%; display: flex; flex-direction: column;',
          '  align-items: center; justify-content: center; gap: 9px;',
          '  padding: 28px 20px; text-align: center;',
          '}',
          '.sp-empty-glyph { opacity: 0.14; color: var(--sp-ink-1); }',
          '.sp-empty-title { font-size: 12px; line-height: 1.7; color: var(--sp-ink-2); max-width: 420px; }',
          '.sp-api {',
          '  display: inline-flex; align-items: center; gap: 8px; max-width: 100%;',
          '  font-family: var(--sp-mono); font-size: 11px; color: var(--sp-ink-2);',
          '  background: var(--sp-sunken); border: 1px solid var(--sp-hair);',
          '  border-radius: 6px; padding: 3px 5px 3px 10px;',
          '}',
          '.sp-api code { overflow: hidden; text-overflow: ellipsis; }',
          '.sp-diag { font-size: 11px; line-height: 1.9; text-align: left; }',
          '.sp-diag .ok { color: var(--sp-ok); }',
          '.sp-diag .bad { color: var(--sp-warn); }',

          // ---------- 窄宽度降级(容器查询跟随面板实际宽度;media 作小窗口兜底) ----------
          // 读数账目是面板的「仪器脸」,只在极窄停靠才让位给行数据:
          // ≤400px 收起右侧行数计数器,RX/TX 账目保留到最后一刻。
          '@container sp-panel (max-width: 640px) { .sp-tool { display: none; } }',
          '@container sp-panel (max-width: 560px) { .sp-len { display: none; } }',
          '@container sp-panel (max-width: 400px) { .sp-count { display: none; } }',
          '@media (max-width: 640px) { .sp-tool { display: none; } }',
          '@media (max-width: 560px) { .sp-len { display: none; } }',
          '@media (max-width: 420px) { .sp-count { display: none; } }',
        ].join('\n')
        document.head.appendChild(el)
      } catch (e) { /* 注入失败仅失去增强样式,布局由内联兜底 */ }
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
        out.push(h('mark', { key: hit, className: 'sp-mark' }, s.slice(hit, hit + needle.length)))
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
        out.push(h('mark', { key: i + ':' + m.index, className: 'sp-mark' }, m[0]))
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
        type: 'button', className: 'sp-copy', 'data-done': done ? '1' : '0', onClick: onCopy,
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
    // 视觉走 class(脊线/徽章/等宽),布局参数留在内联(列宽是运行时手势)。
    const Row = React.memo(function Row({ e, hex, selected, onSelect, matcher, onPort }) {
      const raw = hex ? (fmtHex(e.hex) || e.text) : (e.text || fmtHex(e.hex))
      const payload = raw.length > ROW_PAYLOAD_MAX ? raw.slice(0, ROW_PAYLOAD_MAX) + ' …' : raw
      const payloadChildren = !matcher ? payload
        : matcher instanceof RegExp ? highlightRe(payload, matcher)
        : highlight(payload, matcher)
      return h('div', {
        className: 'sp-row' + (selected ? ' sp-sel' : ''),
        'data-dir': e.dir || 'SYS',
        onClick: () => onSelect(e),
      },
        h('span', { className: 'sp-time' }, shortTime(e.ts)),
        h('span', { className: 'sp-dir', 'data-dir': e.dir || 'SYS' }, e.dir),
        e.tag ? h('span', {
          className: 'sp-port',
          title: '按端口 ' + e.tag + ' 过滤(再点切回全部)',
          onClick: (ev) => {
            ev.stopPropagation()
            if (typeof onPort === 'function') onPort(e.tag)
          },
          style: { background: 'hsl(' + hueOf(e.tag) + ', 55%, 45%)' },
        }) : null,
        h('span', { className: 'sp-tool' }, e.tool + (e.tag ? ' · ' + e.tag : '')),
        h('span', { className: 'sp-len' }, e.len ? e.len : ''),
        h('span', { className: 'sp-dt', title: '距上一条' }, fmtDt(e.dtMs)),
        h('span', { className: 'sp-payload', 'data-dir': e.dir || 'SYS' }, payloadChildren),
      )
    })

    // ---- 组件:断层标记(增量 seq 跳变 / Host 环滚出) ----
    const GapRow = React.memo(function GapRow({ count }) {
      return h('div', { className: 'sp-gap' }, '… 已省略 ' + count + ' 条(超出 Host 保留窗口)…')
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

      // ---- 控制条 chip:统一 24px 仪器按键,engaged 态走 data-on 淡染 ----
      const chip = (label, onClick, on, title, variant) => h('button', {
        type: 'button', onClick,
        className: 'sp-btn' + (variant ? ' sp-' + variant : ''),
        'data-on': on ? '1' : '0',
        title: title || '',
      }, label)

      // 连接态灯:dead=Host 未响应 / wait=等日志文件 / live=尾随中
      const lampState = !connected ? 'dead' : (stats && stats.exists === false) ? 'wait' : 'live'
      const lampTitle = !connected ? 'Host 未响应'
        : (stats && stats.exists === false) ? '已连接,等待日志文件出现'
        : '已连接 Host 尾随'

      // 读数账目:0 值的字节单位不占位(未开始通信时读数条保持干净)
      const rxN = (stats && stats.rx) || 0
      const txN = (stats && stats.tx) || 0
      const rxB = (stats && stats.rxBytes) || 0
      const txB = (stats && stats.txBytes) || 0
      const head = h('div', { className: 'sp-read' },
        h('span', { className: 'sp-lamp', 'data-s': lampState, title: lampTitle }),
        h('span', { className: 'sp-ledger', title: 'RX/TX 帧数与字节账目' },
          h('span', { className: 'sp-k rx' }, 'RX'),
          h('span', { className: 'sp-v' }, String(rxN)),
          rxB > 0 ? h('span', { className: 'sp-u' }, '· ' + fmtBytes(rxB) + 'B') : null,
          h('span', { className: 'sp-vsep' }),
          h('span', { className: 'sp-k tx' }, 'TX'),
          h('span', { className: 'sp-v' }, String(txN)),
          txB > 0 ? h('span', { className: 'sp-u' }, '· ' + fmtBytes(txB) + 'B') : null,
        ),
        paused ? h('span', { className: 'sp-pausetag' }, '已暂停') : null,
        h('span', { className: 'sp-count' }, counterText),
      )

      const ctl = h('div', { className: 'sp-ctl' },
        chip('HEX', () => setHex(!hex), hex, '以 HEX 显示数据'),
        h('select', {
          value: dir, onChange: (e) => setDir(e.target.value), className: 'sp-select',
          'aria-label': '方向过滤',
        },
          h('option', { value: 'ALL' }, '全部'),
          h('option', { value: 'RX' }, 'RX'),
          h('option', { value: 'TX' }, 'TX'),
        ),
        h('select', {
          value: port, onChange: (e) => setPort(e.target.value), className: 'sp-select',
          title: '按端口标签过滤', 'aria-label': '端口过滤',
        },
          h('option', { value: 'ALL' }, '全部端口'),
          portOptions.map((t) => h('option', { key: t, value: t }, t)),
        ),
        chip('.*', () => setUseRegex(!useRegex), useRegex, '正则模式(非法表达式自动回退子串匹配)'),
        h('input', {
          ref: searchRef,
          value: search,
          placeholder: useRegex ? '正则过滤 text/hex/tool/port…' : '过滤 text/hex/tool/port…',
          onChange: (e) => setSearch(e.target.value),
          className: 'sp-search', 'aria-label': '过滤',
        }),
        chip(paused ? '▶ 继续' : '⏸ 暂停', () => setPaused(!paused), paused,
          '暂停后数据在 Host 侧继续累积,恢复后拉齐', 'warn'),
        chip(follow && autoSuspended ? '跟随·挂起' : '跟随', () => setFollow(!follow), effectiveFollow,
          autoSuspended ? '翻看历史中已自动挂起,回到底部或再点恢复' : '自动滚动到最新'),
        chip(confirmClear ? '确认清空?' : '清空', clear, confirmClear,
          '截断共享 mcp-traffic.jsonl(再点一次生效)', 'danger'),
        chip('导出', () => downloadJsonl(visible), false, '下载当前过滤视图为 JSONL(整个内存环用 Host /export 路由)'),
      )

      // 空态:API 地址(agent 可直接 curl)+ 链路状态 + shell 工具解析诊断
      const diag = stats && stats.diag
      const shellLine = (label, exe, ok, hint) => h('div', {
        className: ok ? 'ok' : 'bad',
      },
        (ok ? '✓ ' : '⚠ ') + label + ' → ' + (exe || '(PATH 中未找到)') + (ok ? '' : '  ·  ' + hint))
      const apiFull = (typeof location !== 'undefined' && location.origin ? location.origin : '') + ROUTE
      const emptyBox = h('div', { className: 'sp-empty' },
        h('div', { className: 'sp-empty-glyph' }, h(PlugGlyph, { size: 40 })),
        h('div', { className: 'sp-empty-title' },
          stats && stats.exists
            ? '日志已连接,等待 AI 串口收发…(让 Agent 调 mcp__acccom__list_ports 试试)'
            : '尚未发现 ' + ((stats && stats.logPath) || 'mcp-traffic.jsonl') + ' — 首次 AI 串口通信后自动出现'),
        h('span', { className: 'sp-api' },
          h('span', null, 'API'),
          h('code', null, apiFull),
          h(CopyBtn, { text: apiFull, label: '复制' })),
        stats && stats.exists && typeof stats.logSize === 'number'
          ? h('div', { className: 'sp-metacap' },
              '日志 ' + fmtBytes(stats.logSize) + 'B · 坏行 ' + (stats.badLines || 0)
              + ' · 轮转文件' + (stats.rotated ? '已生成' : '未生成'))
          : null,
        diag
          ? h('div', { className: 'sp-diag' },
              shellLine('pwsh', diag.pwsh, diag.pwshOk, 'PATH 中找不到 pwsh.exe'),
              shellLine('bash', diag.bash, diag.bashOk, 'System32 的是 WSL 存根;用 start-dsh-with-gitbash.cmd 启动可换成 Git Bash'))
          : null,
      )

      const list = h('div', {
        ref: listRef, onScroll,
        className: 'sp-list',
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
      const listWrap = h('div', { className: 'sp-listwrap', 'data-paused': paused ? '1' : '0' },
        list,
        (autoSuspended && newCount > 0) ? h('button', {
          type: 'button', onClick: jumpToLatest, title: '跳到最新并恢复跟随',
          className: 'sp-jump',
        }, '↓ ' + newCount + ' 条新数据') : null,
      )

      const detail = selected ? h('div', { className: 'sp-detail' },
        h('div', { className: 'sp-meta' },
          h('span', { className: 'sp-seq' }, '#' + selected.seq),
          h('span', { className: 'sp-dir', 'data-dir': selected.dir || 'SYS' }, selected.dir),
          h('span', { className: 'sp-metacap' },
            (typeof selected.ts === 'string' && selected.ts.length >= 10 ? selected.ts.slice(0, 10) + ' · ' : '')
            + shortTime(selected.ts) + ' · ' + selected.tool + (selected.tag ? ' · ' + selected.tag : '')
            + ' · ' + selected.len + 'B'
            + (selected.dtMs != null ? ' · Δ' + fmtDt(selected.dtMs) : '')),
          h('span', { className: 'sp-metaacts' },
            selected.text ? h(CopyBtn, { text: selected.text, label: '复制text' }) : null,
            selected.hex ? h(CopyBtn, { text: fmtHex(selected.hex), label: '复制HEX' }) : null),
        ),
        h('div', { className: 'sp-text' }, selected.text || '(空)'),
        h('div', { className: 'sp-hex' }, fmtHex(selected.hex)),
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
        className: 'sp-root',
      },
        head, ctl, listWrap, detail,
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
