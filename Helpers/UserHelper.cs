using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace DeployApp.Helpers
{
    /// <summary>
    /// Resolves the current Windows logged-in user's ID and display name.
    /// Uses the Win32 GetUserNameEx API to retrieve the display name without
    /// requiring additional NuGet packages.
    /// </summary>
    public static class UserHelper
    {
        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern byte GetUserNameExW(int nameFormat, StringBuilder lpNameBuffer, ref int lpnSize);

        /// <summary>NameDisplay = 3 retrieves the user's display name (e.g., "Ashok Govindan").</summary>
        private const int NameDisplay = 3;

        /// <summary>
        /// Returns the Windows login username (e.g., "kirit").
        /// </summary>
        public static string GetCurrentUserId()
        {
            return Environment.UserName;
        }

        /// <summary>
        /// Returns the user's display name from Active Directory or local account.
        /// Falls back to the login username if unavailable.
        /// </summary>
        public static string GetCurrentUserDisplayName()
        {
            try
            {
                var buffer = new StringBuilder(256);
                int size = buffer.Capacity;
                if (GetUserNameExW(NameDisplay, buffer, ref size) != 0)
                {
                    var name = buffer.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch
            {
                // Fall through to fallback
            }

            return Environment.UserName;
        }

        /// <summary>
        /// Returns a formatted label like "PLS7282 - Ashok Govindan".
        /// If the display name matches the login ID, returns just the ID.
        /// </summary>
        public static string GetCurrentUserLabel()
        {
            var id = GetCurrentUserId();
            var displayName = GetCurrentUserDisplayName();

            if (!string.IsNullOrEmpty(displayName) &&
                !string.Equals(displayName, id, StringComparison.OrdinalIgnoreCase))
            {
                return $"{id} - {displayName}";
            }

            return id;
        }
        /// <summary>
        /// Checks if the current user is an admin.
        /// </summary>
        public static bool IsCurrentUserAdmin()
        {
            var adminUsers = new[] { "admin", "kirit" }; // Mock admin list, can be updated from config
            var id = GetCurrentUserId();
            return adminUsers.Contains(id, StringComparer.OrdinalIgnoreCase);
        }
    }
}
