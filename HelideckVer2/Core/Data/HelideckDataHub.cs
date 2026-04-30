using System;
using System.Collections.Generic;

namespace HelideckVer2.Core.Data
{
    public class HelideckDataHub
    {
        private static readonly Lazy<HelideckDataHub> _instance =
            new Lazy<HelideckDataHub>(() => new HelideckDataHub());

        public static HelideckDataHub Instance => _instance.Value;

        private readonly object _lockData = new object();

        // Dữ liệu thô (raw NMEA string) cho DataList / Bảng quét
        private readonly Dictionary<string, (string Value, DateTime? LastUpdate)> _sensorData;
        private readonly Dictionary<string, (string State, DateTime? LastUpdate)> _alarmStatus;

        // Dữ liệu số cho chart / radar / UI
        public double Heading      { get; private set; }
        public double WindSpeedMs  { get; private set; }
        public double WindDirDeg   { get; private set; }
        public double RollDeg      { get; private set; }
        public double PitchDeg     { get; private set; }
        public double HeaveCm      { get; private set; }
        public double HeavePeriodSec { get; private set; }

        // GPS numeric
        public double  GpsSpeedKnot { get; private set; }
        public string  GpsLat       { get; private set; } = "NO FIX";
        public string  GpsLon       { get; private set; } = "NO FIX";

        // METEO (COM5) — populated when NMEA format is known
        public double TempCelsius { get; private set; }
        public double HumidityPct { get; private set; }
        public double PressureHPa { get; private set; }

        private HelideckDataHub()
        {
            _sensorData   = new Dictionary<string, (string, DateTime?)>();
            _alarmStatus  = new Dictionary<string, (string, DateTime?)>();

            string[] tasks = { "GPS", "WIND", "R/P/H", "HEADING", "METEO" };
            foreach (var t in tasks)
            {
                _sensorData[t]  = ("", null);
                _alarmStatus[t] = ("Normal", null);
            }
        }

        // ── WRITE API ────────────────────────────────────────────────────────

        /// <summary>Lưu chuỗi NMEA RAW vào bảng quét (DataList).</summary>
        public void UpdateRawString(string taskName, string rawString)
        {
            lock (_lockData)
            {
                if (_sensorData.ContainsKey(taskName))
                    _sensorData[taskName] = (rawString, DateTime.Now);
            }
        }

        /// <summary>Cập nhật dữ liệu số cho chart / radar.</summary>
        public void UpdateNumericData(string taskName, double v1 = 0, double v2 = 0, double v3 = 0)
        {
            lock (_lockData)
            {
                switch (taskName)
                {
                    case "HEADING": Heading     = v1; break;
                    case "WIND":    WindSpeedMs = v1; WindDirDeg = v2; break;
                    case "R/P/H":   RollDeg     = v1; PitchDeg   = v2; HeaveCm    = v3; break;
                }
            }
        }

        /// <summary>Cập nhật GPS numeric (tốc độ + vị trí).</summary>
        public void UpdateGpsData(double speedKnot, string lat, string lon)
        {
            lock (_lockData)
            {
                GpsSpeedKnot = speedKnot;
                GpsLat       = lat ?? "NO FIX";
                GpsLon       = lon ?? "NO FIX";
            }
        }

        public void UpdateMeteoData(double temp, double humidity, double pressure)
        {
            lock (_lockData) { TempCelsius = temp; HumidityPct = humidity; PressureHPa = pressure; }
        }

        public void UpdateHeavePeriod(double period)
        {
            lock (_lockData) HeavePeriodSec = period;
        }

        public void UpdateAlarmState(string taskName, string alarmString)
        {
            lock (_lockData)
            {
                if (_alarmStatus.ContainsKey(taskName))
                    _alarmStatus[taskName] = (alarmString, DateTime.Now);
            }
        }

        // ── READ API ─────────────────────────────────────────────────────────

        public Snapshot GetSnapshot()
        {
            lock (_lockData)
            {
                var snap = new Snapshot();
                DateTime now = DateTime.Now;

                foreach (var key in _sensorData.Keys)
                {
                    var data  = _sensorData[key];
                    var alarm = _alarmStatus[key];
                    double age = data.LastUpdate.HasValue
                        ? (now - data.LastUpdate.Value).TotalSeconds
                        : 999;

                    snap.TaskRows.Add(new SnapshotRow
                    {
                        TaskName    = key,
                        Value       = data.Value,
                        Age         = age,
                        IsStale     = age > 2.0,
                        AlarmString = alarm.State
                    });
                }

                snap.Heading       = Heading;
                snap.WindSpeedMs   = WindSpeedMs;
                snap.WindDirDeg    = WindDirDeg;
                snap.RollDeg       = RollDeg;
                snap.PitchDeg      = PitchDeg;
                snap.HeaveCm       = HeaveCm;
                snap.HeavePeriodSec= HeavePeriodSec;
                snap.GpsSpeedKnot  = GpsSpeedKnot;
                snap.GpsLat        = GpsLat;
                snap.GpsLon        = GpsLon;
                snap.TempCelsius   = TempCelsius;
                snap.HumidityPct   = HumidityPct;
                snap.PressureHPa   = PressureHPa;

                return snap;
            }
        }

        // ── DTOs ─────────────────────────────────────────────────────────────

        public class Snapshot
        {
            public List<SnapshotRow> TaskRows { get; set; } = new List<SnapshotRow>();
            public double Heading        { get; set; }
            public double WindSpeedMs    { get; set; }
            public double WindDirDeg     { get; set; }
            public double RollDeg        { get; set; }
            public double PitchDeg       { get; set; }
            public double HeaveCm        { get; set; }
            public double HeavePeriodSec { get; set; }
            public double GpsSpeedKnot   { get; set; }
            public string GpsLat         { get; set; } = "NO FIX";
            public string GpsLon         { get; set; } = "NO FIX";
            public double TempCelsius    { get; set; }
            public double HumidityPct    { get; set; }
            public double PressureHPa    { get; set; }
        }

        public class SnapshotRow
        {
            public string TaskName    { get; set; }
            public string Value       { get; set; }
            public double Age         { get; set; }
            public bool   IsStale     { get; set; }
            public string AlarmString { get; set; }
        }
    }
}
