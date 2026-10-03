using System.IO;
using System.Text;
using DeployApp.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace DeployApp.Services
{
    /// <summary>
    /// Drives a real Chrome browser via Selenium to create ServiceNow Change Requests.
    /// In production, SSO is handled automatically by Windows domain / Kerberos (Chrome
    /// passes the logged-in user's credentials through Negotiate/NTLM).
    /// </summary>
    public class SeleniumChromeService : IDisposable
    {
        private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(60);

        private readonly string _instanceUrl;
        private IWebDriver? _driver;
        private bool _disposed;

        /// <summary>Called on the UI thread to update status text.</summary>
        public Action<string>? OnStatusChanged { get; set; }

        /// <summary>True once the Chrome window has been closed or disposed.</summary>
        public bool IsClosed => _driver == null || _disposed;

        public SeleniumChromeService(string instanceUrl)
        {
            _instanceUrl = instanceUrl.Trim().TrimEnd('/');
        }

        /// <summary>
        /// The URL that opens a blank Normal Change Request form in ServiceNow's Next Experience UI.
        /// The chg_model sys_id 007c4001c343101035ae3f52c1d3aeb2 is the standard "Normal" change model.
        /// </summary>
        private string NewFormUrl =>
            $"{_instanceUrl}/now/nav/ui/classic/params/target/change_request.do" +
            "%3Fsys_id%3D-1%26sysparm_query%3Dchg_model%3D007c4001c343101035ae3f52c1d3aeb2";

        #region Chrome lifecycle

        /// <summary>
        /// Launches Chrome with SSO / Windows auth enabled.
        /// The Chrome profile is stored persistently so cookies survive across sessions.
        /// </summary>
        public void Launch()
        {
            var options = new ChromeOptions();

            // Persistent profile so SSO cookies / sessions are remembered.
            var profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeployApp", "ChromeProfile");
            Directory.CreateDirectory(profileDir);
            options.AddArgument($"--user-data-dir={profileDir}");

            // Enable Windows Integrated Authentication (Kerberos / NTLM) for the
            // ServiceNow host. This makes SSO work transparently in production.
            var host = new Uri(_instanceUrl).Host;
            options.AddArgument($"--auth-server-whitelist=*.{GetDomain(host)}");
            options.AddArgument($"--auth-negotiate-delegate-whitelist=*.{GetDomain(host)}");

            // Start maximized so the ServiceNow form is easy to see.
            options.AddArgument("--start-maximized");

            // Suppress the "Chrome is being controlled by automated test software" bar.
            options.AddExcludedArgument("enable-automation");
            options.AddArgument("--disable-blink-features=AutomationControlled");

            // Use Selenium Manager (built into Selenium 4.6+) to auto-download chromedriver.
            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            _driver = new ChromeDriver(service, options, TimeSpan.FromMinutes(2));
            _driver.Manage().Timeouts().PageLoad = PageTimeout;
            _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        }

        /// <summary>
        /// Returns the registrable domain (e.g. "service-now.com" from "dev203974.service-now.com").
        /// Falls back to the full host if it has two or fewer parts.
        /// </summary>
        private static string GetDomain(string host)
        {
            var parts = host.Split('.');
            return parts.Length > 2
                ? string.Join(".", parts[^2..])
                : host;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _driver?.Quit(); }
            catch { /* Chrome may already be closed by the user */ }
            _driver = null;
        }

        #endregion

        #region Navigation helpers

        /// <summary>
        /// Navigates to the given URL and waits for the page to finish loading.
        /// </summary>
        private void NavigateTo(string url)
        {
            if (IsClosed) return;
            _driver!.Navigate().GoToUrl(url);
        }

        /// <summary>Set to true to skip waiting and assume the form is ready.</summary>
        public bool ForceContinue { get; set; }

        /// <summary>
        /// Waits for the ServiceNow form to be ready (g_form available on the page).
        /// Returns true when the form is loaded, false on timeout.
        /// </summary>
        private bool WaitForForm(TimeSpan timeout)
        {
            if (IsClosed) return false;

            try
            {
                var driver = _driver;
                if (driver == null) return false;
                var wait = new WebDriverWait(driver, timeout);
                wait.Until(d =>
                {
                    if (ForceContinue) return true;
                    try
                    {
                        d.SwitchTo().DefaultContent();
                        var frames = d.FindElements(By.Id("gsft_main"));
                        if (frames.Count > 0) d.SwitchTo().Frame(frames[0]);

                        var result = ((IJavaScriptExecutor)d).ExecuteScript(
                            "return (document.readyState === 'complete' && " +
                            "typeof g_form !== 'undefined' && " +
                            "g_form.getTableName && " +
                            "g_form.getTableName() === 'change_request');");
                        return result is true;
                    }
                    catch
                    {
                        return false;
                    }
                });
                // Extra pause for client scripts to finish.
                System.Threading.Thread.Sleep(1000);
                return true;
            }
            catch (WebDriverTimeoutException)
            {
                return false;
            }
            catch (WebDriverException)
            {
                return false;
            }
        }

        /// <summary>
        /// Runs JavaScript in the current page and returns the result.
        /// </summary>
        private object? RunScript(string script)
        {
            if (IsClosed) return null;
            return ((IJavaScriptExecutor)_driver!).ExecuteScript(script);
        }

        #endregion

        #region CR automation

        /// <summary>
        /// Opens ServiceNow in Chrome and waits for the form to load (SSO happens automatically).
        /// Returns true once the CR form is ready.
        /// </summary>
        public bool EnsureSignedIn()
        {
            ReportStatus("Opening ServiceNow in Chrome...");
            NavigateTo(NewFormUrl);

            ReportStatus("Waiting for the New Change Request form... (Please sign in or navigate to the form if needed)");
            
            // Wait up to 15 minutes for the user to sign in and/or navigate to the New Record form.
            if (!WaitForForm(TimeSpan.FromMinutes(15)))
            {
                return false;
            }

            ReportStatus("Change Request form detected. Ready to fill info.");
            return true;
        }

        /// <summary>
        /// Fills in a new Change Request form and clicks Submit.
        /// </summary>
        public ServiceNowResult CreateChangeRequest(DeploymentRequest request)
        {
            try
            {
                string busy = $"Creating change request for {request.RpaName}...";
                ReportStatus(busy);

                if (ForceContinue)
                {
                    ForceContinue = false; // Reset for subsequent requests
                    try 
                    {
                        _driver?.SwitchTo().DefaultContent();
                        var frames = _driver?.FindElements(By.Id("gsft_main"));
                        if (frames != null && frames.Count > 0) _driver?.SwitchTo().Frame(frames[0]);
                    } 
                    catch { /* ignore */ }
                }
                else
                {
                    // 1. Check if we are already on a new Change Request form
                    bool isAlreadyOnNewForm = false;
                    try 
                    {
                        var checkNew = RunScript("return typeof g_form !== 'undefined' ? g_form.isNewRecord() : false;");
                        isAlreadyOnNewForm = checkNew is true;
                    }
                    catch { /* ignore */ }

                    if (!isAlreadyOnNewForm)
                    {
                        NavigateTo(NewFormUrl);
                        if (!WaitForForm(PageTimeout))
                            return Fail(IsClosed
                                ? "The Chrome window was closed."
                                : "The Change Request form did not open.");
                    }
                }

                // Fetch the pre-assigned Change Number to include in the Justification
                var changeNum = RunScript("return typeof g_form !== 'undefined' ? g_form.getValue('number') : '';")?.ToString() ?? "";

                var justification = $@"RPA - {request.RpaName} - {changeNum}

•    Change Description: {request.ChangeDescription}
•    Impact Analysis: {request.ImpactAnalysis}
•    Tasks to be deployed:
{request.TasksToDeploy}
•    Test evidence :
o    Test plan Path : {request.TestPlanLogPath}
o    LogFile path : 
Is the Code pushed to Test (Yes/No): {request.CodeMovedToTest}
•    Code analysis path:
{request.CodeAnalysisPath}";

                // 2. Fill in the fields via g_form.setValue.
                int[]? changeDate = DateTime.TryParse(request.ChangeDate, out var date)
                    ? new[] { date.Year, date.Month, date.Day }
                    : null;

                var fields = new Dictionary<string, string>
                {
                    ["short_description"] = Escape(request.RpaName + " - " + request.Deployment),
                    ["description"] = Escape(request.ChangeDescription),
                    ["category"] = "Other",
                    ["risk"] = "4",   // Low
                    ["impact"] = "3", // Low
                    ["justification"] = Escape(justification),
                    ["implementation_plan"] = Escape(request.TasksToDeploy),
                    ["risk_impact_analysis"] = Escape(request.ImpactAnalysis),
                    ["test_plan"] = Escape(request.TestPlanLogPath)
                };

                var sb = new StringBuilder();
                sb.AppendLine("(function() { try {");
                foreach (var kv in fields)
                {
                    sb.AppendLine($"  if (g_form.hasField('{kv.Key}')) g_form.setValue('{kv.Key}', '{kv.Value}');");
                }

                if (changeDate != null)
                {
                    // Schedule 10:00 PM – 11:00 PM on the Change Date.
                    sb.AppendLine($"  var fmt = window.g_user_date_time_format || 'yyyy-MM-dd HH:mm:ss';");
                    sb.AppendLine($"  g_form.setValue('start_date', formatDate(new Date({changeDate[0]}, {changeDate[1] - 1}, {changeDate[2]}, 22, 0, 0), fmt));");
                    sb.AppendLine($"  g_form.setValue('end_date',   formatDate(new Date({changeDate[0]}, {changeDate[1] - 1}, {changeDate[2]}, 23, 0, 0), fmt));");
                }

                sb.AppendLine("  return JSON.stringify({ ok: true, sysId: g_form.getUniqueValue(), number: g_form.getValue('number') });");
                sb.AppendLine("} catch(e) { return JSON.stringify({ ok: false, error: String(e) }); }})()");

                var fillResult = RunScript(sb.ToString())?.ToString() ?? "";
                if (!fillResult.Contains("\"ok\":true") && !fillResult.Contains("\"ok\": true"))
                    return Fail("Could not fill in the form: " + fillResult);

                // Parse sysId from the result.
                string sysId = ExtractJsonValue(fillResult, "sysId");

                // Small delay for form onChange handlers.
                System.Threading.Thread.Sleep(1000);

                // 3. Click Submit.
                ReportStatus($"Submitting change request for {request.RpaName}...");
                var clickResult = RunScript(@"
                    (function() {
                        try {
                            var button = document.getElementById('sysverb_insert') || document.getElementById('sysverb_insert_bottom');
                            if (button) { button.click(); return 'clicked'; }
                            gsftSubmit(null, g_form.getFormElement(), 'sysverb_insert');
                            return 'submitted';
                        } catch(e) { return 'error: ' + e; }
                    })()
                ")?.ToString() ?? "error: no result";

                if (clickResult.StartsWith("error"))
                    return Fail($"Could not submit the form ({clickResult}).");

                // Wait for navigation after submit.
                System.Threading.Thread.Sleep(2000);

                // Check for page errors after submit.
                var errors = ReadPageErrors();

                // 4. Open the new record to confirm it was saved.
                NavigateTo($"{_instanceUrl}/change_request.do?sys_id={Uri.EscapeDataString(sysId)}");
                if (!WaitForForm(PageTimeout))
                    return Fail("Submitted, but could not open the record to confirm it.");

                var savedJson = RunScript(@"
                    (function() {
                        try { return JSON.stringify({ isNew: g_form.isNewRecord(), number: g_form.getValue('number') }); }
                        catch(e) { return JSON.stringify({ isNew: true, number: '' }); }
                    })()
                ")?.ToString() ?? "";

                bool isNew = !savedJson.Contains("\"isNew\":false") && !savedJson.Contains("\"isNew\": false");
                if (isNew)
                {
                    return Fail(errors.Count > 0
                        ? string.Join(" ", errors)
                        : "ServiceNow did not save the change request.");
                }

                string number = ExtractJsonValue(savedJson, "number");
                ReportStatus($"Created {number} for {request.RpaName}.");

                return new ServiceNowResult
                {
                    Success = true,
                    ChangeNumber = number,
                    SysId = sysId,
                    Message = $"Change Request {number} created successfully."
                };
            }
            catch (WebDriverException ex) when (IsClosed || ex.Message.Contains("disconnected"))
            {
                return Fail("The Chrome window was closed.");
            }
            catch (Exception ex)
            {
                return Fail($"Error: {ex.Message}");
            }
        }

        private List<string> ReadPageErrors()
        {
            var errors = new List<string>();
            try
            {
                var result = RunScript(@"
                    (function() {
                        try {
                            var nodes = document.querySelectorAll('.outputmsg_error, .fieldmsg.notification-error, .notification-error');
                            var seen = {}, list = [];
                            nodes.forEach(function(n) {
                                var t = (n.innerText || '').trim();
                                if (t && !seen[t]) { seen[t] = true; list.push(t); }
                            });
                            return JSON.stringify(list);
                        } catch(e) { return '[]'; }
                    })()
                ")?.ToString() ?? "[]";

                // Simple JSON array parse.
                if (result.StartsWith("["))
                {
                    result = result.Trim('[', ']');
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        foreach (var item in result.Split("\",\""))
                        {
                            var clean = item.Trim('"', ' ');
                            if (!string.IsNullOrEmpty(clean))
                                errors.Add(clean);
                        }
                    }
                }
            }
            catch { /* best effort */ }

            return errors;
        }

        #endregion

        #region Helpers

        private void ReportStatus(string text)
        {
            OnStatusChanged?.Invoke(text);
        }

        /// <summary>Escapes a string for safe embedding in a JavaScript single-quoted string.</summary>
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// Quick-and-dirty extraction of a JSON string value by key.
        /// Example: ExtractJsonValue("{\"number\":\"CHG001\"}", "number") → "CHG001"
        /// </summary>
        private static string ExtractJsonValue(string json, string key)
        {
            var marker = $"\"{key}\":\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                // Try with space after colon.
                marker = $"\"{key}\": \"";
                idx = json.IndexOf(marker, StringComparison.Ordinal);
            }
            if (idx < 0) return "";

            idx += marker.Length;
            var end = json.IndexOf('"', idx);
            return end > idx ? json[idx..end] : "";
        }

        private static ServiceNowResult Fail(string message) =>
            new() { Success = false, Message = message };

        #endregion
    }
}
