# CLAUDE.md

Guidance for Claude Code when working with this repository.

## Quick Start

### Prerequisites
- .NET 8.0+ SDK
- Visual Studio 2022 / VS Code
- Windows (WinForms only)

### Setup & Run
```bash
# Clone & restore dependencies
git clone <repo>
cd HelideckVer2
dotnet restore

# Build
dotnet build HelideckVer2.sln

# Run
dotnet run --project HelideckVer2.csproj

# Release build
dotnet build -c Release HelideckVer2.sln
```

### Configuration
- Copy `Config/config.json.example` → `Config/config.json`
- Edit COM port assignments and alarm thresholds in `config.json`
- Set `"IsSimulationMode": true` to test without hardware

## Architecture

### Data Pipeline (Main Flow)
```
COM Ports (COM1–COM5)
  ↓ ComEngine (manages all 5 ports, watchdog + retry)
  │   COM1–COM4: ReadExisting (non-blocking NMEA stream)
  │   COM5:      Modbus RTU — MeteoService borrows port via GetManagedPort()
  ↓ NmeaParserService (validate XOR checksum, dispatch by sentence type)
  ↓ HelideckDataHub (thread-safe singleton, lock)
  ↓ UI Timers (100ms update) + DataLogger (10s flush to disk)
```

### Key Components

| File | Responsibility |
|------|----------------|
| **Services/** | |
| `Services/ComEngine.cs` | Opens all COM ports (COM1–COM5), watchdog + exponential backoff retry. NMEA ports: `DataReceived` event + `ReadExisting`. METEO port: no `DataReceived` — skips inactivity timeout, exposes `GetManagedPort()` for MeteoService |
| `Services/Parsing/NmeaParserService.cs` | Validates XOR checksum, dispatches by sentence type, fires typed events. 5 consecutive failures → freeze last known values |
| `Services/AlarmEngine.cs` | Evaluates all alarms every 100ms |
| `Services/DataLogger.cs` | Enqueues lines to `ConcurrentQueue`, flushes to CSV every 10s. Auto-deletes logs older than 30 days |
| `Services/SimulationEngine.cs` | Generates valid NMEA sentences with correct checksums at 100ms intervals |
| `Services/MeteoService.cs` | Modbus RTU polling (RK330-01) via RS485/COM5 every 2s — temp, humidity, pressure. **Port lifecycle delegated to ComEngine** — MeteoService calls `_comEngine.GetManagedPort("COM5")` each poll; if null (port not yet open) poll silently skips. Simulation mode generates random values |
| `Services/ConfigService.cs` | Loads `config.json` at startup, applies to `SystemConfig` |
| `Services/SystemLogger.cs` | Static logger, thread-safe via `lock (_lockObj)` |
| **Core/** | |
| `Core/Data/HelideckDataHub.cs` | Thread-safe singleton (`lock _lockData`). Stores raw NMEA strings and numeric values. UI reads via `GetSnapshot()` |
| `Core/Models/AppConfig.cs` | Serializable config model — loaded from `config.json` |
| `Core/Models/SystemConfig.cs` | Runtime static accessor, applies `AppConfig` values. Fires `ThemeChanged` and `VesselImageChanged` events. Alarm limits are `Func<double>` — update dynamically |
| `Core/Models/Alarm.cs` | Alarm definition, compares `Tag.Value` vs `HighLimitProvider()` |
| `Core/Models/AlarmState.cs` | Alarm state enum (Normal, Active, Acknowledged) |
| `Core/Models/DeviceTask.cs` | Defines COM port + sentence type per device |
| `Core/Models/Tag.cs` | Mutable value holder referenced by `Alarm` |
| **UI/** | |
| `UI/Forms/MainForm.cs` | Owns all timers: 100ms UI update, 1s snapshot log, 1s health check, 100ms chart render. Contains inner class `CardPanel` for theme-aware backgrounds |
| `UI/Forms/ConfigForm.cs` | 4-tab config UI: Alarm Limits, COM Configuration, Vessel Image (live reload), Alarm History. Live DAY/NIGHT preview with revert-on-cancel |
| `UI/Forms/DataListForm.cs` | Raw NMEA data display |
| `UI/Forms/LoginForm.cs` | Login form |
| `UI/Controls/RadarControl.cs` | GDI+ radar — call `UpdateRadar(heading, windDir)` then `Invalidate()` |
| `UI/Controls/TrendChartControl.cs` | 20-minute rolling chart — `PushMotionData` / `PushWindData` / `PushEnvData`, then `Render()`. Dual Y-axis for all modes. CursorX disabled — cursor drawn in PostPaint |
| `UI/Theme/Palette.cs` | UI color palette — all colors are computed properties reading `IsLight` flag |

### NMEA Sentence Types

| Sentence | Port | Fields |
|----------|------|--------|
| `$xxHDT` | COM4 (HEADING) | Heading (°) |
| `$xxMWV` | COM2 (WIND) | Wind speed (m/s), direction (°) |
| `$CNTB` | COM3 (R/P/H) | Roll (°), pitch (°), heave (cm) — Teledyne/Kongsberg format |
| `$PRDID` | COM3 (R/P/H) | Pitch, roll; heave = `HeaveArm × sin(pitch)` |
| `$PASHR` | COM3 (R/P/H) | **Xsens Sirius AHRS** — roll, pitch; heave direct from SDI (m→cm) if field present, else HeaveArm fallback. Supports both with-timestamp and without-timestamp variants |
| `$PHTRO` | COM3 (R/P/H) | Xsens proprietary pitch/roll; heave = `HeaveArm × sin(pitch)` |
| `$xxGGA` | COM1 (GPS) | Lat/lon formatted as `DD°MM.MMM'D` |
| `$xxVTG` | COM1 (GPS) | Speed (knots) — field p[5] |

### Alarm System
- **Flow:** `Tag` (mutable value holder) → `Alarm` (compares `Tag.Value > HighLimitProvider()`) → `AlarmEngine.Evaluate()` every 100ms
- **Alarms:** `AL_WIND`, `AL_ROLL`, `AL_PITCH`, `AL_HEAVE`
- **Limits:** `Func<double>` referencing `SystemConfig` static properties — update dynamically on config change
- **States:** Normal → Active (unacked) → Acknowledged → Normal (reset on clear)
- **User ACK:** click on the value label (lblWindSpeed, lblRoll, lblPitch, lblHeave)

### UI Timers (MainForm)

| Timer | Interval | Action |
|-------|----------|--------|
| `_uiUpdateTimer` | 100ms | `AlarmEngine.Evaluate()`, update labels, radar, heave period zero-crossing |
| `_snapshotTimer` | 1s | `_logger.LogSnapshot(...)` |
| `_healthTimer` | 1s | Check `SnapshotRow.Age > 2s` → set status badges to LOST; update clock |
| `_chartUpdateTimer` | 100ms | `_trendControl.Render()` |

### Thread Safety

| Component | Mechanism |
|-----------|-----------|
| `HelideckDataHub` | `lock (_lockData)` for all reads/writes |
| `ComEngine` | `_portLock` for ports, `_bufferLock` for line buffers, `ConcurrentDictionary` for timestamps |
| `MeteoService` | `System.Timers.Timer` callback (threadpool). Reads COM5 via `GetManagedPort()` which acquires `_portLock` inside ComEngine |
| `DataLogger` | `ConcurrentQueue` enqueue from any thread, flush via `System.Timers.Timer` |
| `SystemLogger` | `lock (_lockObj)` |
| UI updates | Windows Forms timer only (UI thread) |

## Custom Controls

**RadarControl** (`UI/Controls/RadarControl.cs`) — GDI+ drawn
```csharp
radarControl.UpdateRadar(heading, windDir);
radarControl.Invalidate();
```

**TrendChartControl** (`UI/Controls/TrendChartControl.cs`) — 20-minute rolling buffer
```csharp
trendControl.PushMotionData(roll, pitch, heave);   // Motion mode
trendControl.PushWindData(speed, direction);        // Wind mode
trendControl.PushEnvData(temp, humidity);           // Env mode
trendControl.SetMode(TrendMode.Motion, isSeparate); // rebuild areas+series+legend colors
trendControl.SetViewWindow(minutes);                // 2 or 20
trendControl.Render();                              // call every 100ms
```

Chart Y-axes (dual-axis on all combined modes):
| Mode | Y left | Y right |
|------|--------|---------|
| Motion | Roll/Pitch (°) | Heave (cm) |
| Wind | Wind Speed (m/s) | Direction (°), fixed 0–360, interval 90 |
| Env | Temperature (°C) | Humidity (%), fixed 0–100 |

**Anti-jitter rule:** `CursorX.IsUserEnabled = false` on all ChartAreas. Cursor line and tooltip are drawn together in `Chart_PostPaint`. `Chart_MouseMove` only stores `_hoverXValue` / `_hoverText` — never calls `Invalidate()`. Repaint is driven solely by `Render()` every 100ms.

## Live Theme Switch (Day / Night)

Theme switches **without restart**. The flow:

1. `ConfigForm` — DAY/NIGHT buttons apply immediately via `SystemConfig.IsLightTheme = value`
2. `SystemConfig.IsLightTheme` setter fires `ThemeChanged` event
3. `MainForm.ApplyTheme()` receives event (marshals to UI thread via `InvokeRequired`)
4. Snapshot old palette → switch `Palette.IsLight` → snapshot new palette → build `Dictionary<Color,Color>` old→new → `SwapControlColors(this, map)` (recursive)
5. `CardPanel` controls are invalidated separately (`card.Invalidate(true)`) — they override `OnPaintBackground` to always read `Palette.CardFace` directly, bypassing BackColor
6. `_trendControl.SetMode(...)` rebuilds chart areas/series/legend with new colors
7. `_radarControl.Invalidate()` triggers GDI+ repaint

**Revert-on-cancel:** ConfigForm stores `_originalTheme` on open. `FormClosing` reverts `SystemConfig.IsLightTheme` if `DialogResult != OK`.

**CardPanel inner class** (in MainForm):
```csharp
private sealed class CardPanel : Panel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new SolidBrush(Palette.CardFace);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
// Child labels must use BackColor = Color.Transparent so they paint through CardPanel
```

## Live Vessel Image Reload

Vessel image (`Images/picture1.png`) can be changed without restart:
1. Settings → Vessel Image tab → Browse → Save
2. `ConfigForm.BtnSave_Click` copies file then calls `SystemConfig.RaiseVesselImageChanged()`
3. `MainForm` subscriber reloads `pictureBox1` via `LoadImageFromFile`

`SystemConfig` exposes the event through a raise method (C# events can only be invoked from declaring class):
```csharp
public static event Action VesselImageChanged;
public static void RaiseVesselImageChanged() => VesselImageChanged?.Invoke();
```

`LoadImageFromFile` clones the image from a FileStream so the file is not locked after loading:
```csharp
using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
using var tmp    = Image.FromStream(stream);
picBox.Image     = (Image)tmp.Clone();
```

## Simulation Mode

```json
// Config/config.json
{
  "IsSimulationMode": true
}
```

When enabled, `SimulationEngine` generates valid NMEA sentences with correct checksums at 100ms intervals and calls `OnComDataReceived` directly — `ComEngine.Initialize()` is not called (COM ports stay closed). `MeteoService` detects `IsSimulationMode = true` and generates random drift values; `GetManagedPort()` returns null but is never reached in simulation path.

## MeteoService — RK330-01 Modbus RTU

| Parameter | Value |
|-----------|-------|
| Interface | RS485 qua cổng COM vật lý trên PC/IPC |
| Port | COM5 (Task "METEO" in config) |
| Protocol | Modbus RTU, Slave ID 1, FC 0x03 |
| Registers | 0x0000, count 3 |
| CRC | CRC-16/IBM (0xA001), low byte first |
| Request | `01 03 00 00 00 03 05 CB` |
| Decode | reg[0]/100 → °C, reg[1]/100 → %, reg[2]/10 → mbar |
| Poll interval | 2s |
| ReadTimeout | 600ms, WriteTimeout 300ms (set by ComEngine on port open) |

**Port lifecycle**: ComEngine opens COM5 and handles watchdog/retry exactly like COM1–4. For METEO task, ComEngine skips `DataReceived` event subscription and skips 10-second inactivity timeout (Modbus is request-response, not streaming). MeteoService constructor takes `ComEngine` reference and calls `GetManagedPort("COM5")` on each poll — if null (port not ready), poll is silently skipped.

**Constructor**: `new MeteoService(portName, baudRate, _comEngine)` — pass the running ComEngine instance.

Data flows: `MeteoService.Poll()` → `HelideckDataHub.UpdateMeteoData()` → `_uiUpdateTimer` reads via `GetSnapshot()` → `_lblTemp` / `_lblHumidity` + `TrendChartControl.PushEnvData()`.

## Common Tasks

### Add a new NMEA sentence type
1. Add parse logic in `NmeaParserService.cs`
2. Fire a new typed event (e.g. `OnNewTypeParsed`)
3. Update `HelideckDataHub` to store the value
4. Bind to UI label / chart in `MainForm.cs`

### Connect Xsens Sirius AHRS
Configure Xsens MT Manager to output one of (priority order):
1. `$PASHR` — gives direct SDI heave (most accurate)
2. `$PRDID` — heave approximated from HeaveArm × sin(pitch)
3. `$PHTRO` — heave approximated

Leave `SentenceType` empty in Settings for COM3 (auto-detect). No code changes needed.

### Change vessel image live
Settings (requires login) → Vessel Image tab → Browse → Save. No restart needed.

### Debug data flow
1. Check `Logs/` folder (auto-created on first run)
2. Enable `SystemLogger` debug output if available
3. Inspect `GetSnapshot()` return values at runtime

### Change alarm limits
Edit `SystemConfig` static properties or update `config.json` — limits are `Func<double>` so they apply immediately without restart.

## Troubleshooting

| Issue | Fix |
|-------|-----|
| COM port fails to open | Check COM port assignments in `config.json` + Windows Device Manager |
| Data shows LOST after 2s | Trace checksum failures in `NmeaParserService` (5-fail freeze threshold) |
| Incorrect heave values | Verify `HeaveArm` config value + `HeaveArm × sin(pitch)` formula |
| Crash on startup | Check `Logs/` folder permissions; review 30-day auto-delete logic |
| Chart jitter on mouse hover | Do NOT re-enable `CursorX.IsUserEnabled` — cursor must stay in PostPaint |
| Theme not updating a control | Check if it uses `CardPanel` + `Color.Transparent` labels; avoid captured color variables |
| Vessel image not reloading | Ensure `SystemConfig.RaiseVesselImageChanged()` is called after file copy in `BtnSave_Click` |
| METEO badge stays WAIT | Check COM5 assignment in Settings; RS485 A/B polarity; try swapping wires. ComEngine watchdog opens COM5 — check SystemLogger for `[COM] COM5 offline` entries |

## Notes
- **No unit tests** — use Simulation Mode for functional testing
- **No lint step** — relies on C# nullable analysis (`<Nullable>enable</Nullable>`)
- **Log rotation:** 30-minute CSV blocks, auto-delete folders older than 30 days
- **Theme:** All Palette colors are computed properties — never cache a `Palette.XxxColor` into a local variable inside a method that survives theme switches
- **Chart legend:** `SetMode()` must update `_chart.Legends[0].BackColor/ForeColor` — legend is NOT rebuilt when ChartAreas/Series are cleared

## File Reading Strategy

### Read first (critical path)
- `Config/config.json`
- `Services/ComEngine.cs`
- `Services/Parsing/NmeaParserService.cs`
- `Core/Data/HelideckDataHub.cs`
- `UI/Forms/MainForm.cs`

### Read when relevant
- `Services/AlarmEngine.cs` — alarm logic
- `Services/DataLogger.cs` — logging issues
- `Services/SimulationEngine.cs` — simulation mode
- `Services/MeteoService.cs` — Modbus RTU / temperature-humidity-pressure
- `Services/ConfigService.cs` — config load/save
- `Services/SystemLogger.cs` — debug logging
- `UI/Controls/RadarControl.cs` — radar display bugs
- `UI/Controls/TrendChartControl.cs` — chart bugs
- `UI/Forms/ConfigForm.cs` — config UI (4 tabs: Alarm, COM, Vessel Image, Alarm History)
- `UI/Forms/DataListForm.cs` — raw NMEA display
- `UI/Forms/LoginForm.cs` — login logic
- `UI/Theme/Palette.cs` — UI styling
- `Core/Models/Alarm.cs` — alarm model
- `Core/Models/AlarmState.cs` — alarm state enum/model
- `Core/Models/AppConfig.cs` — serializable config model
- `Core/Models/DeviceTask.cs` — COM port task definition
- `Core/Models/SystemConfig.cs` — runtime static config + ThemeChanged/VesselImageChanged events
- `Core/Models/Tag.cs` — mutable value holder for alarms

### Always ignore
- `*.Designer.cs` — auto-generated WinForms layout
- `*.resx` — resource files
- `bin/`, `obj/` — build output
- `Logs/` — runtime log files
