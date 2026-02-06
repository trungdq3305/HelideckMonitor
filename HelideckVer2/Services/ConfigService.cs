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
        private static readonly string BaseFolder = @"C:\HelideckLog\config";
        private static readonly string ConfigPath = Path.Combine(BaseFolder, "config.json");

        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(BaseFolder);
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }
}
