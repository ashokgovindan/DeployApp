using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeployApp.Models;

namespace DeployApp.Services
{
    /// <summary>
    /// Loads and saves the ServiceNow connection settings.
    /// Change Requests are created through the ServiceNow web form
    /// (see ServiceNowBrowserWindow), not through the REST API.
    /// </summary>
    public static class ServiceNowService
    {
        private static readonly string ConfigFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "servicenow_config.json");

        /// <summary>
        /// Loads the saved ServiceNow configuration, or returns null if none exists.
        /// Removes any username or password left over from the old API version.
        /// </summary>
        public static ServiceNowConfig? LoadConfig()
        {
            if (!File.Exists(ConfigFilePath))
                return null;

            try
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<ServiceNowConfig>(json);

                var node = JsonNode.Parse(json) as JsonObject;
                if (config != null && node != null && (node.ContainsKey("password") || node.ContainsKey("username")))
                    SaveConfig(config); // rewrite without the stored credentials

                return config;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves the ServiceNow configuration to disk.
        /// </summary>
        public static void SaveConfig(ServiceNowConfig config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
    }

    /// <summary>
    /// Result of creating one Change Request.
    /// </summary>
    public class ServiceNowResult
    {
        public bool Success { get; set; }
        public string ChangeNumber { get; set; } = string.Empty;
        public string SysId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
