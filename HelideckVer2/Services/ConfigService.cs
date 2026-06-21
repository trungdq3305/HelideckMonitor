using HelideckVer2.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HelideckVer2.Services
{
    public static class ConfigService
    {
        private static readonly string BaseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        private static readonly string ConfigPath = Path.Combine(BaseFolder, "config.json");

        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(BaseFolder);
            cfg.Validate(); // ensure limits are sane before persisting
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });

            // Write to temp file then atomically replace — prevents corrupt config on power loss mid-write.
            string tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(ConfigPath))
                File.Replace(tempPath, ConfigPath, ConfigPath + ".bak");
            else
                File.Move(tempPath, ConfigPath);
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                var json = File.ReadAllText(ConfigPath);
                var cfg  = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                cfg.Validate(); // clamp any out-of-range values from manual edits or corruption
                return cfg;
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("ConfigService.Load: failed to parse config.json, using defaults", ex);
                return new AppConfig();
            }
        }
    }
}
