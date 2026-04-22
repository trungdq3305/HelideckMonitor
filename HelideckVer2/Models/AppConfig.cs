using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public class AppConfig
    {
        public List<DeviceTask> Tasks { get; set; } = new List<DeviceTask> {
        new DeviceTask { TaskName = "GPS", PortName = "COM1", BaudRate = 9600 },
        new DeviceTask { TaskName = "WIND", PortName = "COM2", BaudRate = 4800 },
        new DeviceTask { TaskName = "R/P/H", PortName = "COM3", BaudRate = 9600 },
        new DeviceTask { TaskName = "HEADING", PortName = "COM4", BaudRate = 4800 }
    };
        public bool IsSimulationMode { get; set; } = true;
        public string AdminPassword { get; set; } = "123456";
        public string ShipName { get; set; } = "FSO 01 - HELIDECK";

        // Limits
        public double WindMax { get; set; } = 40.0;
        public double RMax { get; set; } = 3.0;
        public double PMax { get; set; } = 3.0;
        public double HMax { get; set; } = 2.0; // cm (theo spec bạn chọn nội bộ cm)

    }
}
