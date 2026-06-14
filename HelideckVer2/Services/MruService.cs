using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using HelideckVer2.Core.Data;
using HelideckVer2.Models;
using HelideckVer2.Services.Mru;

namespace HelideckVer2.Services
{
    /// <summary>
    /// Đọc dữ liệu nhị phân XBus MTData2 từ Xsens MTi qua COM port.
    /// Frame: FA FF 36 LEN [data items...] CS
    /// Trích xuất: Euler 0x2030, FreeAcceleration 0x4030, RateOfTurn 0x8020.
    /// Tính Roll/Pitch/Heading/Heave qua XsensMruProcessor, ghi vào HelideckDataHub ("R/P/H").
    /// Port lifecycle (open/retry/watchdog) do ComEngine quản lý.
    /// </summary>
    public sealed class MruService : IDisposable
    {
        // ── XBus constants ──────────────────────────────────────────────────
        private const byte Preamble = 0xFA;
        private const byte Bid = 0xFF;
        private const byte MidMTData2 = 0x36;

        // MTData2 DataID (big-endian 2-byte)
        private const ushort IdEuler = 0x2030;  // float32 roll, pitch, yaw (deg)
        private const ushort IdFreeAcc = 0x4030;  // float32 ax, ay, az (m/s²)
        private const ushort IdRoT = 0x8020;  // float32 wx, wy, wz (rad/s)

        // ── Fields ──────────────────────────────────────────────────────────
        private readonly string _portName;
        private readonly ComEngine _comEngine;
        private readonly XsensMruProcessor _processor = new XsensMruProcessor();
        public XsensMruProcessor Processor => _processor;

        private Thread _readThread;
        private volatile bool _running;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private bool _firstFrameLogged = false;

        // Throttle counters — log on 1st, 5th, then every 30 occurrences
        private int _checksumFailCount;
        private int _timeoutFailCount;

        // Debug telemetry — read by MainForm UI
        public double LastAccZ         { get; private set; }
        public double LastDt           { get; private set; }
        public double LastDtSmoothed   { get; private set; }
        public double LastHeaveVelocity{ get; private set; }
        public bool   UsingSampleFine  { get; private set; }
        public long   FrameCount       { get; private set; }
        private double _pendingDt;
        private bool   _pendingUsingSampleFine;

        // Simulation
        private readonly Random _rng = new Random();
        private System.Timers.Timer _simTimer;
        private double _simRoll, _simPitch, _simYaw;
        private double _simHeavePhase = 0;  // phase cho sinusoidal heave acc

        public MruService(string portName, int baudRate, ComEngine comEngine)
        {
            _portName = portName;
            _comEngine = comEngine;
        }

        // ── START / STOP ─────────────────────────────────────────────────────

        public void Start()
        {
            _running = true;
            if (SystemConfig.IsSimulationMode)
            {
                SystemLogger.LogInfo($"[MRU] Starting in SIMULATION mode.");
                _simTimer = new System.Timers.Timer(100) { AutoReset = true };
                _simTimer.Elapsed += (s, e) => SimulateTick();
                _simTimer.Start();
            }
            else
            {
                SystemLogger.LogInfo($"[MRU] Starting on port={_portName}.");
                _readThread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "MruService.ReadLoop"
                };
                _readThread.Start();
            }
        }

        public void Stop()
        {
            _running = false;
            _simTimer?.Stop();
            _simTimer?.Dispose();
            _simTimer = null;
        }

        /// <summary>
        /// Stops the ReadLoop then sends GoToConfig so the device returns to Config mode.
        /// Must be called synchronously (UI thread) in FormClosed before async cleanup starts.
        /// ReadLoop is stopped first to prevent it from re-sending GoToMeasurement after GoToConfig.
        /// </summary>
        public void SendGoToConfig()
        {
            if (SystemConfig.IsSimulationMode) return;

            // Stop ReadLoop first — prevents race where ReadLoop re-enters ProcessPort
            // and sends GoToMeasurement right after we send GoToConfig
            _running = false;
            _readThread?.Join(1500);  // wait up to 1.5s for ReadLoop to exit cleanly

            try
            {
                SerialPort port = _comEngine?.GetManagedPort(_portName);
                if (port != null && port.IsOpen)
                {
                    port.Write([0xFA, 0xFF, 0x30, 0x00, 0xD1], 0, 5); // GoToConfig
                    Thread.Sleep(100);  // give device time to switch to Config mode
                }
            }
            catch { }
        }

        public void Dispose() => Stop();

        // ── SIMULATION ───────────────────────────────────────────────────────

        private void SimulateTick()
        {
            double dt = 0.1;
            _simRoll += (_rng.NextDouble() - 0.5) * 0.4 * dt;
            _simPitch += (_rng.NextDouble() - 0.5) * 0.4 * dt;
            _simYaw += (_rng.NextDouble() - 0.5) * 0.5 * dt;
            _simHeavePhase += dt * 2.0 * Math.PI / 6.0;  // chu kỳ 6 giây

            _simRoll = Math.Clamp(_simRoll, -5.0, 5.0);
            _simPitch = Math.Clamp(_simPitch, -5.0, 5.0);
            _simYaw = XsensMruProcessor.Normalize360(_simYaw);

            // Gia tốc thẳng đứng hình sin ~0.22 m/s² → tích phân đôi → heave ~20cm biên độ
            double heaveAcc = 0.22 * Math.Sin(_simHeavePhase) + (_rng.NextDouble() - 0.5) * 0.02;
            var acc = new Vec3(0, 0, heaveAcc);
            var rot = new Vec3(0, 0, 0);
            var out_ = _processor.UpdateEuler(_simRoll, _simPitch, _simYaw, acc, rot, dt);
            if (!out_.Valid) return;

            _pendingDt              = dt;
            _pendingUsingSampleFine = false;
            PublishOutput(out_);
            HelideckDataHub.Instance.UpdateRawString("R/P/H",
                $"SIM MRU R={out_.RollDeg:0.00}° P={out_.PitchDeg:0.00}° H={out_.HeadingDeg:0.0}° Heave={out_.HeaveInstantCg * 100:0.0}cm");
        }

        // ── READ LOOP ─────────────────────────────────────────────────────────

        private void ReadLoop()
        {
            bool portLoggedOnce = false;
            while (_running)
            {
                SerialPort port = _comEngine?.GetManagedPort(_portName);
                if (port == null || !port.IsOpen)
                {
                    portLoggedOnce    = false;
                    _firstFrameLogged = false;
                    Thread.Sleep(500);
                    continue;
                }

                if (!portLoggedOnce)
                {
                    SystemLogger.LogInfo($"[MRU] Port {_portName} open — entering read loop.");
                    portLoggedOnce = true;
                }

                try
                {
                    ProcessPort(port);
                }
                catch (TimeoutException)
                {
                    _timeoutFailCount++;
                    if (ShouldLogThrottled(_timeoutFailCount))
                        SystemLogger.LogInfo($"[MRU] {_portName}: read timeout count={_timeoutFailCount}.");

                    // Every 5 consecutive timeouts (~25s): close the port completely.
                    // Closing forces the RS-232 hardware driver to flush its state and drop DTR/RTS,
                    // which often unsticks an MTi that stopped responding after a power cycle.
                    // ComEngine watchdog will reopen the port within 5s.
                    if (_timeoutFailCount % 5 == 0)
                    {
                        SystemLogger.LogInfo($"[MRU] {_portName}: {_timeoutFailCount} consecutive timeouts — closing port for RS-232 line reset.");
                        try { port.Close(); } catch { }
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Thread.Sleep(200);
                    }
                }
                catch (IOException ex)
                {
                    // USB/serial hardware disconnect: SerialPort.IsOpen may stay true even after disconnect.
                    // Explicitly close so ComEngine watchdog detects isClosed=true and reopens.
                    try { port.Close(); } catch { }
                    _timeoutFailCount = 0;
                    SystemLogger.LogInfo($"[MRU] {_portName}: hardware error ({ex.GetType().Name}) — port closed, waiting for watchdog retry.");
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    _timeoutFailCount = 0;
                    SystemLogger.LogInfo($"[MRU] ProcessPort exception on {_portName}: {ex.GetType().Name} — {ex.Message}");
                    Thread.Sleep(200);
                }
            }
            SystemLogger.LogInfo($"[MRU] ReadLoop exited.");
        }

        private static void TrySend(SerialPort port, byte[] frame)
        {
            try { port.Write(frame, 0, frame.Length); } catch { }
        }

        private static void InitDevice(SerialPort port)
        {
            // GoToConfig first — ensures device is in a known Config state regardless of power-cycle history.
            // Without this, rapid GoToMeasurement sends can toggle the device Config↔Measurement indefinitely.
            // FA FF 30 00 D1
            TrySend(port, [0xFA, 0xFF, 0x30, 0x00, 0xD1]);
            Thread.Sleep(300);
            try { port.DiscardInBuffer(); } catch { }

            // GoToMeasurement — output config already stored in flash (set via MT Manager).
            // FA FF 10 00 F1
            SystemLogger.LogInfo($"[MRU] Sending GoToMeasurement on {port.PortName}.");
            TrySend(port, [0xFA, 0xFF, 0x10, 0x00, 0xF1]);
            Thread.Sleep(600);  // 600ms: RS-232 needs extra settle time vs USB before first frame
            try { port.DiscardInBuffer(); } catch { }
        }

        private void ProcessPort(SerialPort port)
        {
            InitDevice(port);

            // Đồng bộ frame: tìm preamble FA
            while (_running)
            {
                int b = port.ReadByte();
                if (b != Preamble) continue;

                // Đọc BID
                int bid = port.ReadByte();
                if (bid != Bid) continue;

                // Đọc MID
                int mid = port.ReadByte();
                if (mid != MidMTData2) continue;

                // Đọc LEN
                int lenByte = port.ReadByte();
                int len;
                int lenSum;  // bytes LEN đóng góp vào checksum
                if (lenByte == 0xFF)
                {
                    // Extended length: 2 bytes big-endian, checksum bao gồm cả 0xFF + hi + lo
                    int hi = port.ReadByte();
                    int lo = port.ReadByte();
                    len = (hi << 8) | lo;
                    lenSum = 0xFF + hi + lo;
                }
                else
                {
                    len = lenByte;
                    lenSum = lenByte;
                }

                // Guard: max XBus MTData2 frame is ~512 bytes at 100Hz with all data items.
                // A larger value means corrupted sync — re-scan for next preamble.
                if (len > 512) continue;

                // Đọc DATA
                byte[] data = ReadExact(port, len);

                // Đọc checksum
                int cs = port.ReadByte();

                // Xác minh: sum(BID..CS) & 0xFF == 0
                int sum = Bid + MidMTData2 + lenSum;
                foreach (byte d in data) sum += d;
                sum += cs;
                if ((sum & 0xFF) != 0)
                {
                    _checksumFailCount++;
                    if (ShouldLogThrottled(_checksumFailCount))
                        SystemLogger.LogInfo($"[MRU] {_portName}: checksum fail count={_checksumFailCount}. Check baud rate / cable / device output config.");
                    continue;
                }

                // Valid frame received — reset error counters
                _checksumFailCount = 0;
                _timeoutFailCount  = 0;

                // Parse và tính toán
                ParseAndUpdate(data);
            }
        }

        // ── MTData2 PARSER ────────────────────────────────────────────────────

        private float _roll, _pitch, _yaw;
        private float _ax, _ay, _az;
        private float _wx, _wy, _wz;
        private bool _hasEuler, _hasFreeAcc, _hasRoT, _hasSampleFine;
        private uint _curSampleFine, _prevSampleFine;

        // SampleTimeFine chạy ở 10kHz → 1 tick = 0.1ms
        private const double SampleFineHz = 10000.0;

        private void ParseAndUpdate(byte[] data)
        {
            int pos = 0;
            _hasEuler = _hasFreeAcc = _hasRoT = _hasSampleFine = false;

            while (pos + 3 <= data.Length)
            {
                ushort dataId = (ushort)((data[pos] << 8) | data[pos + 1]);
                byte itemLen = data[pos + 2];
                pos += 3;

                if (pos + itemLen > data.Length) break;

                switch (dataId)
                {
                    case IdEuler when itemLen >= 12:
                        _roll = ParseFloat(data, pos);
                        _pitch = ParseFloat(data, pos + 4);
                        _yaw = ParseFloat(data, pos + 8);
                        _hasEuler = true;
                        break;

                    case IdFreeAcc when itemLen >= 12:
                        _ax = ParseFloat(data, pos);
                        _ay = ParseFloat(data, pos + 4);
                        _az = ParseFloat(data, pos + 8);
                        _hasFreeAcc = true;
                        break;

                    case ushort rotId when (rotId & 0xFFF0) == (IdRoT & 0xFFF0) && itemLen >= 12:
                        _wx = ParseFloat(data, pos);
                        _wy = ParseFloat(data, pos + 4);
                        _wz = ParseFloat(data, pos + 8);
                        _hasRoT = true;
                        break;

                    case 0x1060 when itemLen >= 4:  // SampleTimeFine (10kHz hardware clock)
                        _curSampleFine = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                        _hasSampleFine = true;
                        break;
                }

                pos += itemLen;
            }

            if (!_hasEuler) return;

            // dt: dùng SampleTimeFine (0.1ms resolution) nếu có, fallback DateTime (~15ms jitter)
            double dt;
            if (_hasSampleFine && _lastFrameTime != DateTime.MinValue)
            {
                // uint rollover được xử lý tự động bởi phép trừ uint32
                uint ticks = _curSampleFine - _prevSampleFine;
                dt = Math.Clamp(ticks / SampleFineHz, 0.001, 0.2);
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                dt = _lastFrameTime == DateTime.MinValue
                    ? 0.01
                    : Math.Clamp((now - _lastFrameTime).TotalSeconds, 0.001, 0.2);
            }
            _prevSampleFine          = _curSampleFine;
            _lastFrameTime           = DateTime.UtcNow;
            _pendingDt               = dt;
            _pendingUsingSampleFine  = _hasSampleFine && _lastFrameTime != DateTime.MinValue;

            var freeAcc = _hasFreeAcc ? new Vec3(_ax, _ay, _az) : new Vec3(0, 0, 0);
            var rotBody = _hasRoT ? new Vec3(_wx, _wy, _wz) : new Vec3(0, 0, 0);

            var out_ = _processor.UpdateEuler(
                _roll, _pitch, _yaw,
                freeAcc, rotBody,
                dt);  // MTi RateOfTurn xuất rad/s

            if (!out_.Valid) return;

            if (!_firstFrameLogged)
            {
                SystemLogger.LogInfo($"[MRU] First valid frame received on {_portName} — R={out_.RollDeg:0.00}° P={out_.PitchDeg:0.00}°");
                _firstFrameLogged = true;
            }

            PublishOutput(out_);
            HelideckDataHub.Instance.UpdateRawString("R/P/H",
                $"MRU R={out_.RollDeg:0.00}° P={out_.PitchDeg:0.00}° H={out_.HeadingDeg:0.0}° Heave={out_.HeaveInstantCg * 100:0.0}cm");
        }

        private void PublishOutput(MruOutput o)
        {
            LastAccZ          = o.VerticalAcceleration;
            LastDt            = _pendingDt;
            LastDtSmoothed    = LastDtSmoothed < 0.0001
                                ? _pendingDt
                                : 0.9 * LastDtSmoothed + 0.1 * _pendingDt;
            LastHeaveVelocity = o.HeaveVelocity;
            UsingSampleFine   = _pendingUsingSampleFine;
            FrameCount++;

            HelideckDataHub.Instance.UpdateNumericData("R/P/H",
                o.RollDeg,
                o.PitchDeg,
                o.HeaveInstantCg * 100.0);
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        // Log on 1st occurrence, 5th, then every 30 — avoids spam while keeping signal visible
        private static bool ShouldLogThrottled(int count) =>
            count == 1 || count == 5 || count % 30 == 0;

        private byte[] ReadExact(SerialPort port, int count)
        {
            var buf = new byte[count];
            int read = 0;
            while (read < count && _running)
            {
                int n = port.Read(buf, read, count - read);
                if (n > 0) read += n;
                // n==0: rare on some RS-232 drivers — retry without sleeping
            }
            return buf;
        }

        // XBus dùng big-endian IEEE 754 float32
        private static float ParseFloat(byte[] buf, int offset)
        {
            var b = new byte[4] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset + 0] };
            return BitConverter.ToSingle(b, 0);
        }
    }
}
