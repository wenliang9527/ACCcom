# Web Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a real-time HTML dashboard to WPF ModbusWindow showing serial data stream + MODBUS slave registers.

**Architecture:** Single `dashboard.html` served by existing EmbedIO HTTP server via `WithStaticFolder`. WPF adds WebView2 tab pointing to `http://localhost:8899/dashboard/`. Data comes from existing REST API + WebSocket plus one new `/api/slaves` endpoint.

**Tech Stack:** EmbedIO 3.5.2, Microsoft.Web.WebView2, vanilla HTML/CSS/JS

---

### Task 1: Create dashboard.html

**Files:**
- Create: `src/ACCcom.Core/wwwroot/dashboard.html`
- Modify: `src/ACCcom.Core/ACCcom.Core.csproj` (copy wwwroot to output)

- [ ] **Step 1: Create wwwroot folder**
  Create directory `src/ACCcom.Core/wwwroot/`

- [ ] **Step 2: Write dashboard.html**

`src/ACCcom.Core/wwwroot/dashboard.html`:
```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>ACCCOM Dashboard</title>
<style>
* { margin:0; padding:0; box-sizing:border-box; }
body { font-family:'Segoe UI',sans-serif; background:#1a1a2e; color:#e0e0e0; font-size:13px; height:100vh; display:flex; flex-direction:column; }
.top-bar { display:flex; gap:16px; padding:8px 12px; background:#16213e; border-bottom:1px solid #0f3460; align-items:center; }
.top-bar .status { display:flex; align-items:center; gap:6px; }
.top-bar .dot { width:10px; height:10px; border-radius:50%; display:inline-block; }
.dot.green { background:#4ecca3; }
.dot.red { background:#e84545; }
.main { display:flex; flex:1; overflow:hidden; gap:1px; background:#0f3460; }
.panel { flex:1; display:flex; flex-direction:column; background:#1a1a2e; overflow:hidden; }
.panel-header { padding:6px 10px; background:#16213e; font-weight:600; font-size:12px; cursor:pointer; display:flex; align-items:center; gap:6px; user-select:none; border-bottom:1px solid #0f3460; }
.panel-header .arrow { transition:transform .15s; display:inline-block; }
.panel-header.collapsed .arrow { transform:rotate(-90deg); }
.panel-body { flex:1; overflow-y:auto; padding:4px 0; }
.panel-body.collapsed { display:none; }
.stream-item { padding:2px 10px; font-family:'Cascadia Code','Consolas',monospace; font-size:12px; display:flex; gap:8px; border-bottom:1px solid #16213e; }
.stream-item:hover { background:#16213e; }
.tag-rx { color:#4fc3f7; }
.tag-tx { color:#ffb74d; }
.stream-time { color:#888; min-width:60px; }
.stream-data { word-break:break-all; }
.reg-table { width:100%; border-collapse:collapse; font-family:'Cascadia Code','Consolas',monospace; font-size:12px; }
.reg-table th { text-align:left; padding:4px 8px; background:#16213e; border-bottom:1px solid #0f3460; position:sticky; top:0; }
.reg-table td { padding:3px 8px; border-bottom:1px solid #16213e; }
.reg-table tr:hover td { background:#16213e; }
.slave-list { padding:4px 8px; display:flex; gap:6px; flex-wrap:wrap; border-bottom:1px solid #0f3460; }
.slave-chip { padding:3px 10px; background:#0f3460; border-radius:12px; font-size:11px; cursor:pointer; }
.slave-chip.active { background:#4ecca3; color:#1a1a2e; font-weight:600; }
.slave-chip:hover { opacity:.8; }
.status-label { font-size:11px; color:#888; }
.status-value { font-size:12px; font-weight:600; }
.loading { text-align:center; padding:20px; color:#888; }
</style>
</head>
<body>

<div class="top-bar">
  <div class="status"><span class="dot green" id="statusDot"></span><span id="statusText">Disconnected</span></div>
  <div class="status"><span class="status-label">Port:</span><span class="status-value" id="portText">-</span></div>
  <div class="status"><span class="status-label">RX:</span><span class="status-value" id="rxText">0</span></div>
  <div class="status"><span class="status-label">TX:</span><span class="status-value" id="txText">0</span></div>
  <div style="flex:1"></div>
  <div class="status"><span class="status-label" id="versionText"></span></div>
</div>

<div class="main">
  <div class="panel" id="streamPanel">
    <div class="panel-header" onclick="toggleStream()">
      <span class="arrow">&#9660;</span> Real-Time Data Stream
    </div>
    <div class="panel-body" id="streamBody"></div>
  </div>
  <div class="panel">
    <div class="panel-header">MODBUS Slaves</div>
    <div id="slaveList" class="slave-list"></div>
    <div class="panel-body" id="slaveBody" style="padding:0;">
      <table class="reg-table">
        <thead><tr><th>Addr</th><th>Value (DEC)</th><th>Value (HEX)</th></tr></thead>
        <tbody id="regTableBody"></tbody>
      </table>
    </div>
  </div>
</div>

<script>
const BASE = '';
let ws = null;
let streamCollapsed = false;
let entries = [];
let selectedSlave = null;

function toggleStream() {
  streamCollapsed = !streamCollapsed;
  document.getElementById('streamPanel').querySelector('.panel-header').classList.toggle('collapsed', streamCollapsed);
  document.getElementById('streamBody').classList.toggle('collapsed', streamCollapsed);
}

function addStreamEntry(dir, time, data) {
  entries.unshift({dir, time, data});
  if (entries.length > 200) entries.length = 200;
  if (streamCollapsed) return;
  const el = document.getElementById('streamBody');
  const div = document.createElement('div');
  div.className = 'stream-item';
  div.innerHTML = `<span class="tag-${dir.toLowerCase()}">[${dir}]</span><span class="stream-time">${time}</span><span class="stream-data">${data}</span>`;
  el.insertBefore(div, el.firstChild);
  while (el.children.length > 100) el.removeChild(el.lastChild);
}

function updateStatus(s) {
  document.getElementById('statusDot').className = 'dot ' + (s.isOpen ? 'green' : 'red');
  document.getElementById('statusText').textContent = s.isOpen ? 'Connected' : 'Disconnected';
  document.getElementById('portText').textContent = s.currentPort || '-';
  document.getElementById('rxText').textContent = s.rxCount || 0;
  document.getElementById('txText').textContent = s.txCount || 0;
}

function updateSlaves(data) {
  if (!data || !data.slaves) return;
  const list = document.getElementById('slaveList');
  list.innerHTML = '';
  data.slaves.forEach(s => {
    const chip = document.createElement('span');
    chip.className = 'slave-chip' + (selectedSlave === s.id ? ' active' : '');
    chip.textContent = `Slave 0x${s.slaveId.toString(16).padStart(2,'0')} | ${s.transportType.toUpperCase()}`;
    chip.onclick = () => { selectedSlave = s.id; renderRegisters(data.registers, data.slaves); };
    list.appendChild(chip);
  });
  if (!selectedSlave && data.slaves.length > 0) { selectedSlave = data.slaves[0].id; }
  renderRegisters(data.registers, data.slaves);
}

function renderRegisters(regs, slaves) {
  const tbody = document.getElementById('regTableBody');
  tbody.innerHTML = '';
  if (!regs || !selectedSlave || !regs[selectedSlave]) {
    tbody.innerHTML = '<tr><td colspan="3" class="loading">No slave selected</td></tr>';
    return;
  }
  const list = regs[selectedSlave];
  list.slice(0, 32).forEach(r => {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>0x${r.address.toString(16).padStart(4,'0')}</td><td>${r.value}</td><td>0x${r.value.toString(16).padStart(4,'0').toUpperCase()}</td>`;
    tbody.appendChild(tr);
  });
  // Update chip active states
  document.querySelectorAll('.slave-chip').forEach((chip, i) => {
    chip.classList.toggle('active', slaves && slaves[i] && slaves[i].id === selectedSlave);
  });
}

// Polling
async function pollStatus() {
  try {
    const r = await fetch(BASE + '/api/status');
    const j = await r.json();
    if (j.success !== false) updateStatus(j);
  } catch {}
}
async function pollSlaves() {
  try {
    const r = await fetch(BASE + '/api/slaves');
    const j = await r.json();
    if (j.success !== false) updateSlaves(j);
  } catch {}
}

setInterval(pollStatus, 2000);
setInterval(pollSlaves, 1000);

// WebSocket
function connectWs() {
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  ws = new WebSocket(proto + '//' + location.host + '/ws');
  ws.onmessage = e => {
    try {
      const entry = JSON.parse(e.data);
      addStreamEntry(entry.direction, entry.timestamp ? entry.timestamp.substr(11,8) : '', entry.rawHex || entry.text || '');
    } catch {}
  };
  ws.onclose = () => { setTimeout(connectWs, 2000); };
  ws.onerror = () => {};
}
connectWs();

// Initial load
pollStatus();
pollSlaves();
</script>
</body>
</html>
```

- [ ] **Step 3: Add Content copy to ACCcom.Core.csproj**

Insert after the closing `</AssemblyAttribute>` (line 13):
```xml
  </ItemGroup>
  <ItemGroup>
    <Content Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4: Verify the file exists in output**

Run: `dir src\ACCcom.Core\bin\Debug\net8.0\wwwroot\dashboard.html` or equivalent.
Expected: file exists

- [ ] **Step 5: Commit**
```bash
git add src/ACCcom.Core/wwwroot/dashboard.html src/ACCcom.Core/ACCcom.Core.csproj
git commit -m "feat: add dashboard HTML page"
```

---

### Task 2: Add HTTP routes for dashboard + slaves API

**Files:**
- Modify: `src/ACCcom.Core/Services/HttpService.cs`

- [ ] **Step 1: Modify HttpService constructor to accept `ModbusSlaveService` and add routes**

Replace the constructor (lines 17-25) and add two new methods after `Dispose()`:

```csharp
    private readonly ModbusSlaveService? _slaveService;

    public HttpService(ISerialService? serialService = null, ParserManager? parserManager = null,
        ModbusSlaveService? slaveService = null, string url = DefaultUrl, int bufferCapacity = 10000)
    {
        Buffer = new DataBufferService(bufferCapacity);
        _serialService = serialService;
        _parserManager = parserManager;
        _slaveService = slaveService;
        var asmDir = Path.GetDirectoryName(typeof(HttpService).Assembly.Location)!;
        var wwwroot = Path.Combine(asmDir, "wwwroot");
        _server = new WebServer(o => o.WithUrlPrefix(url).WithMode(HttpListenerMode.EmbedIO))
            .WithWebApi("/api", m => m.WithController(() => new SerialController(this)))
            .WithModule(new SerialWebSocketHandler("/ws", this));
        if (Directory.Exists(wwwroot))
            _server = _server.WithStaticFolder("/dashboard", wwwroot, true);
    }
```

Add these methods after `Dispose()` (after line 161):
```csharp
    public object GetSlaves()
    {
        if (_slaveService == null) return new { slaves = Array.Empty<object>(), registers = new object() };
        var slaves = _slaveService.GetActiveSlaves().ToList();
        var regs = new Dictionary<string, List<object>>();
        foreach (var s in slaves)
        {
            var device = _slaveService.GetDevice(s.Id);
            if (device == null) continue;
            var list = new List<object>();
            var count = Math.Min(device.HoldingRegisterCount, 32);
            for (int i = 0; i < count; i++)
            {
                var v = device.GetHoldingRegister((ushort)i);
                list.Add(new { address = i, value = (int)v, hex = $"0x{v:X4}" });
            }
            regs[s.Id] = list;
        }
        return new { slaves, registers = regs };
    }
```

- [ ] **Step 2: Add ModbusSlaveService parameter to all callers**

In `MainViewModel.cs`, find the HttpService constructor call and add the slaveService parameter:

```csharp
        _http = new HttpService(_serial, _parserManager, _slaveService, bufferCapacity: _settings.BufferCapacity);
```

But first check what `MainViewModel.cs` actually has. Read `D:\WORK_VSCODE\Vibe-coding\Xcom\src\ACCcom\ViewModels\MainViewModel.cs` to confirm the exact call site.

- [ ] **Step 3: Build and verify**
Run: `dotnet build src/ACCcom.Core 2>&1`
Expected: Build succeeded

- [ ] **Step 4: Commit**
```bash
git add src/ACCcom.Core/Services/HttpService.cs
git commit -m "feat: add dashboard route and slaves API to HttpService"
```

---

### Task 3: Add WebView2 Dashboard Tab to WPF

**Files:**
- Modify: `src/ACCcom/ACCcom.csproj` (add WebView2 package)
- Modify: `src/ACCcom/ModbusWindow.xaml` (add Dashboard tab)
- Modify: `src/ACCcom/ViewModels/MainViewModel.cs` (pass slaveService to HttpService)

- [ ] **Step 1: Add Microsoft.Web.WebView2 NuGet package**

Run:
```bash
dotnet add src/ACCcom/ACCcom.csproj package Microsoft.Web.WebView2
```

- [ ] **Step 2: Write MainViewModel.cs change**

Read the current `MainViewModel.cs` to find the exact HttpService constructor call, then add the `_slaveService` parameter.

The current call site (from exploration) is likely:
```csharp
_http = new HttpService(_serial, _parserManager, bufferCapacity: _settings.BufferCapacity);
```

Change to:
```csharp
_http = new HttpService(_serial, _parserManager, _slaveService, bufferCapacity: _settings.BufferCapacity);
```

Also add a `_slaveService` field to the class if not already present:
```csharp
private readonly ModbusSlaveService? _slaveService;
```

And initialize it in the constructor.

- [ ] **Step 3: Add Dashboard tab to ModbusWindow.xaml**

Insert a third TabItem after the Slave tab's closing `</TabItem>`:

```xml
        <TabItem Header="Dashboard">
            <Grid>
                <WebView2 x:Name="DashboardWebView" Source="http://localhost:8899/dashboard/" />
            </Grid>
        </TabItem>
```

Add the WebView2 namespace to the Window element:
```xml
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Core"
```

And change `<WebView2>` to `<wv2:WebView2>`.

- [ ] **Step 4: Build and verify**
Run: `dotnet build src/ACCcom 2>&1`
Expected: Build succeeded

- [ ] **Step 5: Run all tests**
Run: `dotnet test tests/ACCcom.Core.Tests 2>&1`
Expected: 325 tests pass

- [ ] **Step 6: Commit**
```bash
git add src/ACCcom/ModbusWindow.xaml src/ACCcom/ViewModels/MainViewModel.cs src/ACCcom/ACCcom.csproj
git commit -m "feat: add WebView2 dashboard tab to ModbusWindow"
```
