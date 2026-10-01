# acccom-zcode — ACCCOM 串口工具链 ZCode 插件

把 ACCCOM 对 agent 的**知识层**打包成 ZCode 插件(纯 JSON + Markdown,无需改 ZCode 本身)。
装好后 ZCode 会话自动获得串口操作的操作纪律与两个快捷命令。

## 组件

| 组件 | 内容 |
|------|------|
| skill | `acccom-serial`:三层接口选择(行动=MCP 工具/审计=traffic_log 飞行记录仪/人在环=桌面端)、read_data 实时缓冲 vs 飞行记录仪的判断、干净复现工作流、端口纪律(COM 独占、COM9 保留)、DSH bash WSL 存根陷阱 |
| command | `/acccom-status`:飞行记录仪水位 + 链路自检 |
| command | `/acccom-repro`:干净复现三步(记水位 → 操作 → 读回本次全部收发) |

**刻意不包含 MCP server**:MCP 由用户级配置(`~/.zcode/cli/config.json` 的 `acccom`)持有。
ZCode 对插件 MCP 键名做命名空间处理,若插件再声明一份会出现双进程 + 工具重复,
还可能抢 COM 口——所以插件只带知识层,装/卸不影响 MCP。

## 安装(本地市场)

1. ZCode → Settings → Plugin Management → Discover → `+` → 添加本地目录
   `D:\WORK_VSCODE\Vibe-coding\Xcom\plugins`(含 `marketplace.json`)
2. 安装并启用 `acccom-zcode`(会话自动重载)
3. 启用后删除用户级散装副本,让插件版本接管(避免双副本漂移):
   `~/.zcode/skills/acccom-serial/`
4. 验证:新会话里输入 `/acccom-status`;或说"用串口发点数据"看 skill 是否触发

## 与其他副本的关系

| 副本 | 作用域 |
|------|--------|
| 本插件 `skills/acccom-serial/` | ZCode(仓库版本化,真源) |
| `~/.dsh/skills/acccom-serial/SKILL.md` | DeepSeek Harness(独立副本) |
| `tools/dsh-serial-panel/AGENTS.md` | API 参考(GET /help 实时取),两 harness 共用 |

改 skill 内容:改本插件副本 → `marketplace.json` 与 `plugin.json` 同步 bump version →
在 ZCode 里 Refresh marketplace + 更新插件;DSH 副本手动同步。
