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
        /// Evaluate condition
        /// return true nếu Active/Clear đổi
        /// </summary>
        public bool Evaluate()
        {
            bool prevActive = IsActive;

            if (Tag.Value > HighLimitProvider())
                IsActive = true;
            else
                IsActive = false;

            if (IsActive && !prevActive)
            {
                RaisedTime = DateTime.Now;
                IsAcked = false;   // Alarm mới → chưa ACK
            }
            else if (!IsActive && prevActive)
            {
                ClearedTime = DateTime.Now;

                // reset ACK khi clear (để lần sau active là alarm mới)
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
