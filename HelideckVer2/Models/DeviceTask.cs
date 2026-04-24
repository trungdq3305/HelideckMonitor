using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public class DeviceTask
    {
        public string TaskName { get; set; }
        public string PortName { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public int BaudRate { get; set; }
    }
}