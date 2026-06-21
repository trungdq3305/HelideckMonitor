using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public class Alarm
    {
        public string Id { get; }
        public Tag Tag { get; }

        public Func<double> HighLimitProvider { get; }

        // ===== STATE =====
        public bool IsActive { get; private set; }
        public bool IsAcked { get; private set; }

        // ===== TIME =====
        public DateTime? RaisedTime { get; private set; }
        public DateTime? ClearedTime { get; private set; }
        public DateTime? AckTime { get; private set; }

        public Alarm(string id, Tag tag, Func<double> highLimitProvider)
        {
            Id = id;
            Tag = tag;
            HighLimitProvider = highLimitProvider;
        }

        /// <summary>
        /// Evaluate condition with 5% hysteresis deadband.
        /// Raises when Tag.Value exceeds limit; clears only when value drops below 95% of limit.
        /// Prevents rapid raise/clear chatter (chattering) when value oscillates near threshold.
        /// Returns true if the Active/Cleared state changed.
        /// </summary>
        public bool Evaluate()
        {
            bool prevActive = IsActive;
            double limit = HighLimitProvider();

            if (Tag.Value > limit)
                IsActive = true;
            else if (Tag.Value <= limit * 0.95)
                IsActive = false;
            // else: inside hysteresis band — hold current state

            if (IsActive && !prevActive)
            {
                RaisedTime = DateTime.Now;
                IsAcked = false;   // newly raised — clear ACK flag
            }
            else if (!IsActive && prevActive)
            {
                ClearedTime = DateTime.Now;

                // reset ACK on clear so the next raise is treated as a new alarm
                IsAcked = false;
                AckTime = null;
            }


            return prevActive != IsActive;
        }

        /// <summary>
        /// User ACK alarm
        /// </summary>
        public bool Ack()
        {
            if (IsAcked) return false;

            IsAcked = true;
            AckTime = DateTime.Now;
            return true;
        }
        public AlarmState State =>
    !IsActive ? AlarmState.Normal :
    (IsAcked ? AlarmState.Acknowledged : AlarmState.Active);

    }
}
