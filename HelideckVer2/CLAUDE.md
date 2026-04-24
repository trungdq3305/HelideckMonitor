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
COM Ports
  ↓ ComEngine (4 ports, ReadExisting — non-blocking)
  ↓ NmeaParserService (validate XOR checksum, dispatch by sentence type)
  ↓ HelideckDataHub (thread-safe singleton, lock)
  ↓ UI Timers (100ms update) + DataLogger (10s flush to disk)
```

### Key Components

| File | Responsibility |
|------|----------------|
| **Services/** | |
| `Services/ComEngine.cs` | Opens 4 COM ports, assembles NMEA lines in per-port `StringBuilder` buffers, fires `OnDataReceived` |
| `Services/Parsing/NmeaParserService.cs` | Validates XOR checksum, dispatches by sentence type, fires typed events. 5 consecutive failures → freeze last known values |
| `Services/AlarmEngine.cs` | Evaluates all alarms every 100ms |
| `Services/DataLogger.cs` | Enqueues lines to `ConcurrentQueue`, flushes to CSV every 10s. Auto-deletes logs older than 30 days |
| `Services/SimulationEngine.cs` | Generates valid NMEA sentences with correct checksums at 100ms intervals |
| `Services/ConfigService.cs` | Loads `config.json` at startup, applies to `SystemConfig` |
| `Services/SystemLogger.cs` | Static logger, thread-safe via `lock (_lockObj)` |
| **Core/** | |
| `Core/Data/HelideckDataHub.cs` | Thread-safe singleton (`lock _lockData`). Stores raw NMEA strings and numeric values. UI reads via `GetSnapshot()` |
| `Core/Models/AppConfig.cs` | Serializable config model — loaded from `config.json` |
| `Core/Models/SystemConfig.cs` | Runtime static accessor, applies `AppConfig` values. Alarm limits are `Func<double>` — update dynamically |
| `Core/Models/Alarm.cs` | Alarm definition, compares `Tag.Value` vs `HighLimitProvider()` |
| `Core/Models/AlarmState.cs` | Alarm state enum (Normal, Warning, etc.) |
| `Core/Models/DeviceTask.cs` | Defines COM port + sentence type per device |
| `Core/Models/Tag.cs` | Mutable value holder referenced by `Alarm` |
| **UI/** | |
| `UI/Forms/MainForm.cs` | Owns all timers: 100ms UI update, 1s snapshot log, 1s health check, 100ms chart render |
| `UI/Forms/ConfigForm.cs` | Config UI — edit alarm thresholds and COM port settings |
| `UI/Forms/DataListForm.cs` | Raw NMEA data display |
| `UI/Forms/LoginForm.cs` | Login form |
| `UI/Controls/RadarControl.cs` | GDI+ radar — call `UpdateRadar(heading, windDir)` then `Invalidate()` |
| `UI/Controls/TrendChartControl.cs` | 20-minute rolling chart — `PushMotionData` / `PushWindData`, then `Render()` |
| `UI/Theme/Palette.cs` | UI color palette |

### NMEA Sentence Types

| Sentence | Port | Fields |
|----------|------|--------|
| `$xxHDT` | COM4 (HEADING) | Heading (°) |
| `$xxMWV` | COM2 (WIND) | Wind speed (m/s), direction (°) |
| `$CNTB` | COM3 (R/P/H) | Roll (°), pitch (°), heave (cm) |
| `$PRDID` | COM3 (R/P/H) | Pitch, roll; heave = `HeaveArm × sin(pitch)` |
| `$xxGGA` | COM1 (GPS) | Lat/lon formatted as `DD°MM.MMM'D` |
| `$GPVTG` | COM1 (GPS) | Speed (knots) |

### Alarm System
- **Flow:** `Tag` (mutable value holder) → `Alarm` (compares `Tag.Value > HighLimitProvider()`) → `AlarmEngine.Evaluate()` every 100ms
- **Alarms:** `AL_WIND`, `AL_ROLL`, `AL_PITCH`, `AL_HEAVE`
- **Limits:** `Func<double>` referencing `SystemConfig` static properties — update dynamically on config change

### UI Timers (MainForm)

| Timer | Interval | Action |
|-------|----------|--------|
| `_uiUpdateTimer` | 100ms | `AlarmEngine.Evaluate()`, update labels, radar, heave period zero-crossing |
| `_snapshotTimer` | 1s | `_logger.LogSnapshot(...)` |
| `_healthTimer` | 1s | Check `SnapshotRow.Age > 2s` → set status badges to LOST |
| `_chartUpdateTimer` | 100ms | `_trendControl.Render()` |

### Thread Safety

| Component | Mechanism |
|-----------|-----------|
| `HelideckDataHub` | `lock (_lockData)` for all reads/writes |
| `ComEngine` | `_portLock` for ports, `_bufferLock` for line buffers, `ConcurrentDictionary` for timestamps |
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
trendControl.PushMotionData(roll, pitch, heave);
trendControl.PushWindData(speed, direction);
trendControl.Render();
```

## Simulation Mode

```json
// Config/config.json
{
  "IsSimulationMode": true
}
```

When enabled, `SimulationEngine` generates valid NMEA sentences with correct checksums at 100ms intervals and calls `OnComDataReceived` directly — `ComEngine` is not started.

## Common Tasks

### Add a new NMEA sentence type
1. Add parse logic in `NmeaParserService.cs`
2. Fire a new typed event (e.g. `OnNewTypeParsed`)
3. Update `HelideckDataHub` to store the value
4. Bind to UI label / chart in `MainForm.cs`

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
| Crash on startup | Check `Logs/` folder permissions; review 90-day auto-delete logic |

## Notes
- **No unit tests** — use Simulation Mode for functional testing
- **No lint step** — relies on C# nullable analysis (`<Nullable>enable</Nullable>`)
- **Log rotation:** 30-minute CSV blocks, auto-delete folders older than 30 days

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
- `Services/ConfigService.cs` — config load/save
- `Services/SystemLogger.cs` — debug logging
- `UI/Controls/RadarControl.cs` — radar display bugs
- `UI/Controls/TrendChartControl.cs` — chart bugs
- `UI/Forms/ConfigForm.cs` — config UI
- `UI/Forms/DataListForm.cs` — raw NMEA display
- `UI/Forms/LoginForm.cs` — login logic
- `UI/Theme/Palette.cs` — UI styling
- `Core/Models/Alarm.cs` — alarm model
- `Core/Models/AlarmState.cs` — alarm state enum/model
- `Core/Models/AppConfig.cs` — serializable config model
- `Core/Models/DeviceTask.cs` — COM port task definition
- `Core/Models/SystemConfig.cs` — runtime static config
- `Core/Models/Tag.cs` — mutable value holder for alarms
### Always ignore
- `*.Designer.cs` — auto-generated WinForms layout
- `*.resx` — resource files
- `bin/`, `obj/` — build output
- `Logs/` — runtime log files