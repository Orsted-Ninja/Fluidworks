using System.IO;
using UnityEngine;

namespace FluidWorks.Settings
{
    public static class SettingsSerializer
    {
        private static string ConfigPath => Path.Combine(Application.persistentDataPath, "FluidWorksConfig.fwconfig");

        public static void Save(SettingsData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(ConfigPath, json);
                Debug.Log($"[Settings] Configuration saved to {ConfigPath}");
            }
            catch (IOException e)
            {
                Debug.LogError($"[Settings] Failed to save configuration: {e.Message}");
            }
        }

        public static SettingsData Load()
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.Log("[Settings] No configuration file found. Creating defaults.");
                var defaults = new SettingsData();
                defaults.ResetToDefaults();
                Save(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                SettingsData data = JsonUtility.FromJson<SettingsData>(json);
                if (data == null)
                {
                    Debug.LogWarning("[Settings] Configuration file is empty or corrupted. Resetting to defaults.");
                    var defaults = new SettingsData();
                    defaults.ResetToDefaults();
                    return defaults;
                }
                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Settings] Failed to load configuration: {e.Message}");
                var defaults = new SettingsData();
                defaults.ResetToDefaults();
                return defaults;
            }
        }
    }
}
