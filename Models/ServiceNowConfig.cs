using System.Text.Json.Serialization;

namespace DeployApp.Models
{
    /// <summary>
    /// Stores the ServiceNow instance address.
    /// Persisted as JSON alongside the application executable.
    /// No username or password is stored: the user signs in through the
    /// ServiceNow window, which remembers the sign-in.
    /// </summary>
    public class ServiceNowConfig
    {
        [JsonPropertyName("instanceUrl")]
        public string InstanceUrl { get; set; } = string.Empty;
    }
}
