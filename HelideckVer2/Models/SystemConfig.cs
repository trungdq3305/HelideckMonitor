using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public static class SystemConfig
    {
        public static bool IsSimulationMode { get; set; } = false;
        public static string AdminPassword { get; set; } = "123456";
        public static string ShipName { get; set; } = "FSO 01 - HELIDECK";

        // Các giới hạn báo động (Alarm Limits)
        public static double WindMax { get; set; } = 40.0;
        public static double RMax { get; set; } = 3.0;
        public static double PMax { get; set; } = 3.0;
        public static double HMax { get; set; } = 2.0;
        public static void Apply(HelideckVer2.Models.AppConfig cfg)
        {
            IsSimulationMode = cfg.IsSimulationMode;
            AdminPassword = cfg.AdminPassword ?? "123456";
            ShipName = cfg.ShipName ?? "FSO 01 - HELIDECK";

            WindMax = cfg.WindMax;
            RMax = cfg.RMax;
            PMax = cfg.PMax;
            HMax = cfg.HMax;
        }

        public static HelideckVer2.Models.AppConfig Export()
        {
            return new HelideckVer2.Models.AppConfig
            {
                IsSimulationMode = IsSimulationMode,
                AdminPassword = AdminPassword,
                ShipName = ShipName,
                WindMax = WindMax,
                RMax = RMax,
                PMax = PMax,
                HMax = HMax
            };
        }

    }
}
