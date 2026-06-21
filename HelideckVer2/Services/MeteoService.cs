using System;
using System.IO.Ports;
using HelideckVer2.Core.Data;
using HelideckVer2.Models;

namespace HelideckVer2.Services
{
    /// <summary>
    /// Polls RK330-01 (Temp / Humidity / Pressure) via Modbus RTU over RS-485.
    /// Frame: FC 03, Slave 1, Start 0x0000, Count 3.
    /// Register mapping: [0]=Temp×10 (°C), [1]=Humidity×10 (%), [2]=Pressure×10 (mbar).
    /// Port lifecycle (open/close/retry) is delegated to ComEngine — MeteoService only polls.
    /// No NuGet required — CRC-16/IBM implemented inline.
    /// </summary>
    public sealed class MeteoService : IDisposable
    {
        private readonly string     _portName;
        private readonly ComEngine  _comEngine;

        private System.Timers.Timer _timer;

        // Throttle counters — log on 1st, 5th, then every 30 occurrences
        private int  _pollExceptionCount;
        private int  _badResponseCount;
        private bool _firstPollLogged;

        // Simulation drift state
        private readonly Random _rng = new Random();
        private double _simTemp     = 25.0;
        private double _simHumidity = 60.0;
        private double _simPressure = 1013.0;

        /// <param name="comEngine">
        /// Pass the running ComEngine so MeteoService can borrow its managed SerialPort.
        /// Pass null only in unit-test scenarios — real hardware requires a valid ComEngine.
        /// </param>
        public MeteoService(string portName, int baudRate, ComEngine comEngine)
        {
            _portName  = portName;
            _comEngine = comEngine;
        }

        // ── START / STOP ──────────────────────────────────────────────────────

        public void Start()
        {
            // AutoReset=false: next poll starts only after current poll completes,
            // preventing concurrent Modbus requests that corrupt response framing.
            _timer = new System.Timers.Timer(2000) { AutoReset = false };
            _timer.Elapsed += (s, e) => Poll();
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();

        // ── POLLING ───────────────────────────────────────────────────────────

        private void Poll()
        {
            try
            {
                if (SystemConfig.IsSimulationMode)
                {
                    PollSimulated();
                    return;
                }

                // Borrow the port that ComEngine is managing — null means port is not yet open
                SerialPort port = _comEngine?.GetManagedPort(_portName);
                if (port == null || !port.IsOpen) return;

                try
                {
                    byte[] req = BuildRequest(slaveId: 1, startAddr: 0x0000, count: 3);
                    port.DiscardInBuffer();
                    port.Write(req, 0, req.Length);

                    // Expected response: SlaveID(1) + FC(1) + ByteCount(1) + Data(6) + CRC(2) = 11 bytes
                    byte[] resp = ReadExact(port, 11);
                    if (!ValidateResponse(resp, slaveId: 1, fc: 0x03, dataBytes: 6))
                    {
                        _badResponseCount++;
                        if (ShouldLogThrottled(_badResponseCount))
                            SystemLogger.LogInfo($"[METEO] {_portName}: invalid Modbus response count={_badResponseCount}. Check slave ID, wiring, RS-485 polarity.");
                        return;
                    }

                    // Cast to short for correct signed decode — sub-zero temps use two's complement
                    double temp     = (short)((resp[3] << 8) | resp[4]) / 10.0;
                    double humidity = ((resp[5] << 8) | resp[6]) / 10.0;
                    double pressure = ((resp[7] << 8) | resp[8]) / 10.0;

                    // Reset error counters and log first successful poll
                    _pollExceptionCount = 0;
                    _badResponseCount   = 0;
                    if (!_firstPollLogged)
                    {
                        SystemLogger.LogInfo($"[METEO] {_portName}: first successful poll — T={temp:0.0}°C RH={humidity:0.0}% P={pressure:0.0}mbar");
                        _firstPollLogged = true;
                    }

                    HelideckDataHub.Instance.UpdateMeteoData(temp, humidity, pressure);
                    HelideckDataHub.Instance.UpdateRawString("METEO",
                        $"T={temp:0.00}°C  RH={humidity:0.00}%  P={pressure:0.0}mbar");
                }
                catch (Exception ex)
                {
                    // Port may have disconnected — ComEngine watchdog will detect and reopen it
                    _pollExceptionCount++;
                    if (ShouldLogThrottled(_pollExceptionCount))
                        SystemLogger.LogInfo($"[METEO] {_portName}: poll exception count={_pollExceptionCount} — {ex.GetType().Name}: {ex.Message}");
                }
            }
            finally
            {
                // Restart timer after poll completes (AutoReset=false) to ensure sequential non-overlapping polls
                try { _timer?.Start(); } catch { }
            }
        }

        private void PollSimulated()
        {
            _simTemp     += (_rng.NextDouble() - 0.5) * 0.3;
            _simHumidity += (_rng.NextDouble() - 0.5) * 0.5;
            _simPressure += (_rng.NextDouble() - 0.5) * 0.2;

            _simTemp     = Math.Clamp(_simTemp,     18.0, 40.0);
            _simHumidity = Math.Clamp(_simHumidity, 30.0, 95.0);
            _simPressure = Math.Clamp(_simPressure, 995.0, 1035.0);

            HelideckDataHub.Instance.UpdateMeteoData(_simTemp, _simHumidity, _simPressure);
            HelideckDataHub.Instance.UpdateRawString("METEO",
                $"SIM T={_simTemp:0.00}°C  RH={_simHumidity:0.00}%  P={_simPressure:0.0}mbar");
        }

        // ── SERIAL HELPERS ────────────────────────────────────────────────────

        private static byte[] ReadExact(SerialPort port, int count)
        {
            var buf  = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = port.Read(buf, read, count - read);
                if (n == 0) break;
                read += n;
            }
            return buf;
        }

        // ── MODBUS RTU ────────────────────────────────────────────────────────

        private static byte[] BuildRequest(byte slaveId, ushort startAddr, ushort count)
        {
            var f = new byte[8];
            f[0] = slaveId;
            f[1] = 0x03;
            f[2] = (byte)(startAddr >> 8);
            f[3] = (byte)(startAddr & 0xFF);
            f[4] = (byte)(count >> 8);
            f[5] = (byte)(count & 0xFF);
            ushort crc = Crc16(f, 6);
            f[6] = (byte)(crc & 0xFF);   // CRC low byte first (Modbus RTU)
            f[7] = (byte)(crc >> 8);
            return f;
        }

        private static bool ValidateResponse(byte[] r, byte slaveId, byte fc, int dataBytes)
        {
            int total = 3 + dataBytes + 2;
            if (r.Length < total)      return false;
            if (r[0] != slaveId)       return false;
            if (r[1] != fc)            return false;
            if (r[2] != dataBytes)     return false;
            ushort crcCalc = Crc16(r, total - 2);
            ushort crcRecv = (ushort)(r[total - 2] | (r[total - 1] << 8));
            return crcCalc == crcRecv;
        }

        // Log on 1st occurrence, 5th, then every 30 — avoids spam while keeping signal visible
        private static bool ShouldLogThrottled(int count) =>
            count == 1 || count == 5 || count % 30 == 0;

        // CRC-16/IBM (polynomial 0xA001, Modbus standard)
        private static ushort Crc16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }
    }
}
