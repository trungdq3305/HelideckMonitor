using HelideckVer2.Models;
using System;
using System.Collections.Generic;

namespace HelideckVer2.Services
{
    public class AlarmEngine
    {
        private readonly List<Alarm> _alarms = new();

        public event Action<Alarm> AlarmRaised;
        public event Action<Alarm> AlarmCleared;
        public event Action<Alarm> AlarmAcked;

        public void Register(Alarm alarm)
        {
            _alarms.Add(alarm);
        }

        public void Evaluate()
        {
            foreach (var alarm in _alarms)
            {
                bool changed = alarm.Evaluate();
                if (!changed) continue;

                if (alarm.IsActive)
                    AlarmRaised?.Invoke(alarm);
                else
                    AlarmCleared?.Invoke(alarm);
            }
        }

        public void Ack(string alarmId)
        {
            var alarm = _alarms.Find(a => a.Id == alarmId);
            if (alarm == null) return;

            if (alarm.Ack())
                AlarmAcked?.Invoke(alarm);
        }

        public IEnumerable<Alarm> GetAll() => _alarms;
        public IEnumerable<Alarm> GetActive() => _alarms.FindAll(a => a.IsActive);

    }
}
