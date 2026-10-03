using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Runtime.InteropServices;
using DeployApp.Models;

namespace DeployApp.Services
{
    /// <summary>
    /// Provides CRUD operations against a Microsoft Access (.accdb) database
    /// for deployment requests. Creates the database file and table on first use.
    /// </summary>
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService()
        {
            _dbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "DeployApp.accdb");
            _connectionString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_dbPath};";
        }

        /// <summary>
        /// The full path to the Access database file.
        /// </summary>
        public string DatabasePath => _dbPath;

        /// <summary>
        /// Ensures the database file and required tables exist.
        /// Call this once at application startup.
        /// </summary>
        public void Initialize()
        {
            if (!File.Exists(_dbPath))
            {
                CreateDatabase();
            }
            EnsureTablesExist();
        }

        /// <summary>
        /// Creates a new, empty .accdb file using ADOX COM automation.
        /// Requires the Microsoft Access Database Engine (ACE) to be installed.
        /// </summary>
        private void CreateDatabase()
        {
            var catalogType = Type.GetTypeFromProgID("ADOX.Catalog");
            if (catalogType == null)
            {
                throw new InvalidOperationException(
                    "Microsoft Access Database Engine is not installed.\n\n" +
                    "Please download and install it from:\n" +
                    "https://www.microsoft.com/en-us/download/details.aspx?id=54920");
            }

            object? catalog = null;
            try
            {
                catalog = Activator.CreateInstance(catalogType);
                // Use reflection to call Create method on the ADOX.Catalog COM object
                catalogType.InvokeMember("Create",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null, catalog, new object[] { _connectionString });
            }
            finally
            {
                if (catalog != null)
                {
                    Marshal.ReleaseComObject(catalog);
                }
            }
        }

        /// <summary>
        /// Creates the DeploymentRequests and RpaNames tables if they don't already exist.
        /// Seeds RpaNames with default entries on first creation.
        /// </summary>
        private void EnsureTablesExist()
        {
            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            var schema = conn.GetSchema("Tables");
            var existingTables = schema.Rows.Cast<DataRow>()
                .Select(row => row["TABLE_NAME"]?.ToString())
                .ToHashSet();

            // --- DeploymentRequests table ---
            if (!existingTables.Contains("DeploymentRequests"))
            {
                const string sql = """
                    CREATE TABLE DeploymentRequests (
                        Id AUTOINCREMENT PRIMARY KEY,
                        RpaName TEXT(200),
                        RequestDate TEXT(20),
                        ChangeDate TEXT(20),
                        ChangeNumber TEXT(20),
                        RitmNumber TEXT(20),
                        Environment TEXT(20),
                        Status TEXT(50),
                        RequestedBy TEXT(100),
                        DeployedDate TEXT(20),
                        ChangeDescription MEMO,
                        ImpactAnalysis MEMO,
                        TasksToDeploy MEMO,
                        TestPlanLogPath MEMO,
                        CodeReviewDocumentPath MEMO,
                        CodeAnalysisPath MEMO,
                        CodeMovedToTest TEXT(10)
                    )
                    """;
                using var cmd = new OleDbCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }

            // --- RpaNames table ---
            if (!existingTables.Contains("RpaNames"))
            {
                const string createSql = """
                    CREATE TABLE RpaNames (
                        Id AUTOINCREMENT PRIMARY KEY,
                        RpaName TEXT(200)
                    )
                    """;
                using var createCmd = new OleDbCommand(createSql, conn);
                createCmd.ExecuteNonQuery();

                // Seed with default RPA names
                var defaultNames = new[]
                {
                    "Agency - Florida Mailbox",
                    "Agency User Role - Sec Grp Review",
                    "Schedule Creation",
                    "Case Management",
                    "Document Processing"
                };

                foreach (var name in defaultNames)
                {
                    using var insertCmd = new OleDbCommand(
                        "INSERT INTO RpaNames (RpaName) VALUES (@RpaName)", conn);
                    insertCmd.Parameters.AddWithValue("@RpaName", name);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Loads all deployment requests from the database.
        /// </summary>
        public List<DeploymentRequest> LoadAll()
        {
            var requests = new List<DeploymentRequest>();

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            using var cmd = new OleDbCommand("SELECT * FROM DeploymentRequests ORDER BY Id", conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                requests.Add(MapFromReader(reader));
            }

            return requests;
        }

        /// <summary>
        /// Inserts a new deployment request and sets its Id from the auto-generated key.
        /// </summary>
        public void Insert(DeploymentRequest request)
        {
            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            const string sql = """
                INSERT INTO DeploymentRequests (
                    RpaName, RequestDate, ChangeDate, ChangeNumber, RitmNumber,
                    Environment, Status, RequestedBy, DeployedDate,
                    ChangeDescription, ImpactAnalysis, TasksToDeploy,
                    TestPlanLogPath, CodeReviewDocumentPath, CodeAnalysisPath,
                    CodeMovedToTest
                ) VALUES (
                    @RpaName, @RequestDate, @ChangeDate, @ChangeNumber, @RitmNumber,
                    @Environment, @Status, @RequestedBy, @DeployedDate,
                    @ChangeDescription, @ImpactAnalysis, @TasksToDeploy,
                    @TestPlanLogPath, @CodeReviewDocumentPath, @CodeAnalysisPath,
                    @CodeMovedToTest
                )
                """;

            using var cmd = new OleDbCommand(sql, conn);
            AddParameters(cmd, request);
            cmd.ExecuteNonQuery();

            // Retrieve the auto-generated Id
            using var idCmd = new OleDbCommand("SELECT @@IDENTITY", conn);
            var result = idCmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                request.Id = Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// Updates an existing deployment request identified by its Id.
        /// </summary>
        public void Update(DeploymentRequest request)
        {
            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            const string sql = """
                UPDATE DeploymentRequests SET
                    RpaName = @RpaName,
                    RequestDate = @RequestDate,
                    ChangeDate = @ChangeDate,
                    ChangeNumber = @ChangeNumber,
                    RitmNumber = @RitmNumber,
                    Environment = @Environment,
                    Status = @Status,
                    RequestedBy = @RequestedBy,
                    DeployedDate = @DeployedDate,
                    ChangeDescription = @ChangeDescription,
                    ImpactAnalysis = @ImpactAnalysis,
                    TasksToDeploy = @TasksToDeploy,
                    TestPlanLogPath = @TestPlanLogPath,
                    CodeReviewDocumentPath = @CodeReviewDocumentPath,
                    CodeAnalysisPath = @CodeAnalysisPath,
                    CodeMovedToTest = @CodeMovedToTest
                WHERE Id = @Id
                """;

            using var cmd = new OleDbCommand(sql, conn);
            AddParameters(cmd, request);
            cmd.Parameters.AddWithValue("@Id", request.Id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes a deployment request by its Id.
        /// </summary>
        public void Delete(int id)
        {
            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            using var cmd = new OleDbCommand("DELETE FROM DeploymentRequests WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        #region RpaNames Table

        /// <summary>
        /// Loads all RPA names from the RpaNames table, sorted alphabetically.
        /// </summary>
        public List<string> LoadRpaNames()
        {
            var names = new List<string>();

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            using var cmd = new OleDbCommand("SELECT RpaName FROM RpaNames ORDER BY RpaName", conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Inserts a new RPA name if it doesn't already exist. Returns true if inserted.
        /// </summary>
        public bool InsertRpaName(string rpaName)
        {
            if (string.IsNullOrWhiteSpace(rpaName))
                return false;

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            // Check for duplicates (case-insensitive)
            using var checkCmd = new OleDbCommand(
                "SELECT COUNT(*) FROM RpaNames WHERE RpaName = @RpaName", conn);
            checkCmd.Parameters.AddWithValue("@RpaName", rpaName.Trim());
            var count = Convert.ToInt32(checkCmd.ExecuteScalar());

            if (count > 0)
                return false;

            using var insertCmd = new OleDbCommand(
                "INSERT INTO RpaNames (RpaName) VALUES (@RpaName)", conn);
            insertCmd.Parameters.AddWithValue("@RpaName", rpaName.Trim());
            insertCmd.ExecuteNonQuery();
            return true;
        }

        /// <summary>
        /// Deletes an RPA name from the lookup table.
        /// </summary>
        public void DeleteRpaName(string rpaName)
        {
            using var conn = new OleDbConnection(_connectionString);
            conn.Open();

            using var cmd = new OleDbCommand(
                "DELETE FROM RpaNames WHERE RpaName = @RpaName", conn);
            cmd.Parameters.AddWithValue("@RpaName", rpaName);
            cmd.ExecuteNonQuery();
        }

        #endregion

        /// <summary>
        /// Maps a data reader row to a DeploymentRequest object.
        /// </summary>
        private static DeploymentRequest MapFromReader(OleDbDataReader reader)
        {
            return new DeploymentRequest
            {
                Id = GetInt(reader, "Id"),
                RpaName = GetString(reader, "RpaName"),
                RequestDate = GetString(reader, "RequestDate"),
                ChangeDate = GetString(reader, "ChangeDate"),
                ChangeNumber = GetString(reader, "ChangeNumber"),
                RitmNumber = GetString(reader, "RitmNumber"),
                Environment = GetString(reader, "Environment"),
                Status = GetString(reader, "Status"),
                RequestedBy = GetString(reader, "RequestedBy"),
                DeployedDate = GetString(reader, "DeployedDate"),
                ChangeDescription = GetString(reader, "ChangeDescription"),
                ImpactAnalysis = GetString(reader, "ImpactAnalysis"),
                TasksToDeploy = GetString(reader, "TasksToDeploy"),
                TestPlanLogPath = GetString(reader, "TestPlanLogPath"),
                CodeReviewDocumentPath = GetString(reader, "CodeReviewDocumentPath"),
                CodeAnalysisPath = GetString(reader, "CodeAnalysisPath"),
                CodeMovedToTest = GetString(reader, "CodeMovedToTest")
            };
        }

        /// <summary>
        /// Adds all field parameters to a command.
        /// OleDb uses positional parameters — the order must match the SQL placeholders exactly.
        /// </summary>
        private static void AddParameters(OleDbCommand cmd, DeploymentRequest r)
        {
            cmd.Parameters.AddWithValue("@RpaName", r.RpaName ?? string.Empty);
            cmd.Parameters.AddWithValue("@RequestDate", r.RequestDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@ChangeDate", r.ChangeDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@ChangeNumber", r.ChangeNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("@RitmNumber", r.RitmNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("@Environment", r.Environment ?? string.Empty);
            cmd.Parameters.AddWithValue("@Status", r.Status ?? string.Empty);
            cmd.Parameters.AddWithValue("@RequestedBy", r.RequestedBy ?? string.Empty);
            cmd.Parameters.AddWithValue("@DeployedDate", r.DeployedDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@ChangeDescription", r.ChangeDescription ?? string.Empty);
            cmd.Parameters.AddWithValue("@ImpactAnalysis", r.ImpactAnalysis ?? string.Empty);
            cmd.Parameters.AddWithValue("@TasksToDeploy", r.TasksToDeploy ?? string.Empty);
            cmd.Parameters.AddWithValue("@TestPlanLogPath", r.TestPlanLogPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@CodeReviewDocumentPath", r.CodeReviewDocumentPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@CodeAnalysisPath", r.CodeAnalysisPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@CodeMovedToTest", r.CodeMovedToTest ?? string.Empty);
        }

        private static string GetString(OleDbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static int GetInt(OleDbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }
    }
}
