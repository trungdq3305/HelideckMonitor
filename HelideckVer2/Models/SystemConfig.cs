using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public static class SystemConfig
    {
        public static event Action ThemeChanged;
        public static event Action VesselImageChanged;

        public static bool IsSimulationMode { get; set; } = false;
        public static void RaiseVesselImageChanged() => VesselImageChanged?.Invoke();

        private static bool _isLightTheme = false;
        public static bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                if (_isLightTheme == value) return;
                _isLightTheme = value;
                ThemeChanged?.Invoke();
            }
        }
        public static string AdminPassword { get; set; } = "123456";
        public static string ShipName { get; set; } = "FSO 01 - HELIDECK";

        // Alarm limits
        public static double WindMax { get; set; } = 40.0;
        public static double RMax { get; set; } = 2.0;
        public static double PMax { get; set; } = 3.0;
        public static double HMax { get; set; } = 200.0;
        public static void Apply(HelideckVer2.Models.AppConfig cfg)
        {
            IsSimulationMode = cfg.IsSimulationMode;
            IsLightTheme = cfg.IsLightTheme;
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
                IsLightTheme = IsLightTheme,
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
