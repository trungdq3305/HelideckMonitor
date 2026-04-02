using System;
using System.Collections.Generic;
using HelideckVer2.Models;

namespace HelideckVer2.Core.Data
{
    /// <summary>
    /// KHO DỮ LIỆU TRUNG TÂM (CENTRALIZED DATA HUB)
    /// Sử dụng pattern Singleton, đảm bảo Thread-safe 100% khi chạy thực tế.
    /// Mọi Form, Service đều đọc/ghi dữ liệu qua đây.
    /// </summary>
    public class HelideckDataHub
    {
        // 1. Singleton Instance: Chỉ có 1 instance duy nhất trong toàn app
        private static readonly Lazy<HelideckDataHub> _instance =
            new Lazy<HelideckDataHub>(() => new HelideckDataHub());

        public static HelideckDataHub Instance => _instance.Value;

        // Object dùng để lock, chống xung đột luồng khi Ghi và Đọc đồng thời
        private readonly object _lockData = new object();

        // 2. KHO CHỨA DỮ LIỆU THÔ (RAW DATA STORE)
        // Chúng ta lưu DateTime? thay vì Age(s) để tự tính toán lúc đọc
        private readonly Dictionary<string, (string Value, DateTime? LastUpdate)> _sensorData;
        private readonly Dictionary<string, (string State, DateTime? LastUpdate)> _alarmStatus;

        // 3. KHO CHỨA DỮ LIỆU SỐ (NUMERIC DATA STORE - Cho Chart, Radar)
        public double Heading { get; private set; }
        public double WindSpeedMs { get; private set; }
        public double WindDirDeg { get; private set; }
        public double RollDeg { get; private set; }
        public double PitchDeg { get; private set; }
        public double HeaveCm { get; private set; }
        public double HeavePeriodSec { get; private set; }

        // Constructor private để đảm bảo Singleton
        private HelideckDataHub()
        {
            _sensorData = new Dictionary<string, (string, DateTime?)>();
            _alarmStatus = new Dictionary<string, (string, DateTime?)>();

            // Khởi tạo 4 Task cố định
            string[] tasks = { "GPS", "WIND", "R/P/H", "HEADING" };
            foreach (var task in tasks)
            {
                _sensorData[task] = ("", null);
                _alarmStatus[task] = ("Normal", null);
            }
        }

        // ==========================================
        // 4. CÁC HÀM GHI DỮ LIỆU (WRITE API - Chỉ Service gọi)
        // ==========================================

        /// <summary>
        /// Cập nhật giá trị hiển thị thô (cho Data List) và số liệu số (cho Chart/Radar)
        /// </summary>
        public void UpdateSensorData(string taskName, string displayValue, double numValue1 = 0, double numValue2 = 0, double numValue3 = 0)
        {
            lock (_lockData) // KHÓA ĐỂ ĐẢM BẢO AN TOÀN LUỒNG
            {
                if (_sensorData.ContainsKey(taskName))
                {
                    _sensorData[taskName] = (displayValue, DateTime.Now);

                    // Cập nhật dữ liệu số dựa trên TaskName
                    switch (taskName)
                    {
                        case "HEADING": Heading = numValue1; break;
                        case "WIND": WindSpeedMs = numValue1; WindDirDeg = numValue2; break;
                        case "R/P/H": RollDeg = numValue1; PitchDeg = numValue2; HeaveCm = numValue3; break;
                    }
                }
            }
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

        // ==========================================
        // 5. CÁC HÀM ĐỌC DỮ LIỆU (READ API - Chỉ UI Form gọi)
        // ==========================================

        /// <summary>
        /// Lấy toàn bộ trạng thái dữ liệu hiện tại để hiển thị trên Grid.
        /// UI Form chỉ cần gọi hàm này 1 lần mỗi giây.
        /// </summary>
        public Snapshot GetSnapshot()
        {
            lock (_lockData) // KHÓA ĐỂ ĐẢM BẢO DỮ LIỆU KHÔNG BỊ SỬA LÚC ĐANG ĐỌC
            {
                var snapshot = new Snapshot();
                DateTime now = DateTime.Now;

                // Tự tính toán Age và Stale ngay lúc đọc
                foreach (var key in _sensorData.Keys)
                {
                    var data = _sensorData[key];
                    var alarm = _alarmStatus[key];

                    double age = data.LastUpdate.HasValue ? (now - data.LastUpdate.Value).TotalSeconds : 999;
                    bool isStale = age > 2.0; // Định nghĩa Stale cứng ở đây

                    snapshot.TaskRows.Add(new SnapshotRow
                    {
                        TaskName = key,
                        Value = data.Value,
                        Age = age,
                        IsStale = isStale,
                        AlarmString = alarm.State
                    });
                }

                // Copy dữ liệu số
                snapshot.Heading = this.Heading;
                snapshot.WindSpeedMs = this.WindSpeedMs;
                snapshot.WindDirDeg = this.WindDirDeg;
                snapshot.RollDeg = this.RollDeg;
                snapshot.PitchDeg = this.PitchDeg;
                snapshot.HeaveCm = this.HeaveCm;
                snapshot.HeavePeriodSec = this.HeavePeriodSec;

                return snapshot;
            }
        }

        // --- CÁC LỚP HỖ TRỢ TRẢ VỀ DỮ LIỆU (DTOs) ---
        public class Snapshot
        {
            public List<SnapshotRow> TaskRows { get; set; } = new List<SnapshotRow>();
            public double Heading { get; set; }
            public double WindSpeedMs { get; set; }
            public double WindDirDeg { get; set; }
            public double RollDeg { get; set; }
            public double PitchDeg { get; set; }
            public double HeaveCm { get; set; }
            public double HeavePeriodSec { get; set; }
        }

        public class SnapshotRow
        {
            public string TaskName { get; set; }
            public string Value { get; set; }
            public double Age { get; set; }
            public bool IsStale { get; set; }
            public string AlarmString { get; set; }
        }
    }
}