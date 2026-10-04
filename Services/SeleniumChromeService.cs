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
                        var frameElement = ((IJavaScriptExecutor)d).ExecuteScript(@"
                            function findFrame(root) {
                                var iframes = root.querySelectorAll ? root.querySelectorAll('iframe') : [];
                                for (var i = 0; i < iframes.length; i++) {
                                    if (iframes[i].id === 'gsft_main') return iframes[i];
                                }
                                var all = root.querySelectorAll ? root.querySelectorAll('*') : [];
                                for (var i = 0; i < all.length; i++) {
                                    if (all[i].shadowRoot) {
                                        var found = findFrame(all[i].shadowRoot);
                                        if (found) return found;
                                    }
                                }
                                return null;
                            }
                            return findFrame(document);
                        ") as OpenQA.Selenium.IWebElement;

                        if (frameElement != null) d.SwitchTo().Frame(frameElement);

                        if (frameElement != null) d.SwitchTo().Frame(frameElement);

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

                    // Since the user manually skipped the long wait, we just wait a short
                    // time (e.g. 15 seconds) for the current page's form to fully load.
                    if (!WaitForForm(TimeSpan.FromSeconds(15)))
                    {
                        return Fail("Could not detect the form on the current page. Please ensure the page is fully loaded.");
                    }
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
                sb.AppendLine("return (function() { try {");
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
                    return (function() {
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
                    return (function() {
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
                    return (function() {
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
        public ServiceNowResult CreateEpal(DeploymentRequest request, string epalUrl)
        {
            try
            {
                ReportStatus($"Opening EPAL form for {request.RpaName}...");

                if (!ForceContinue)
                {
                    NavigateTo(epalUrl);
                }
                else
                {
                    ForceContinue = false;
                }

                // Wait for the textarea to appear
                bool found = false;
                for (int i = 0; i < 30; i++) // up to 15 seconds
                {
                    if (IsClosed) return Fail("Chrome window closed.");
                    var check = RunScript("return document.querySelector('textarea') !== null;");
                    if (check is true)
                    {
                        found = true;
                        break;
                    }
                    Thread.Sleep(500);
                }

                if (!found)
                    return Fail("Could not detect the EPAL form (textarea not found).");

                var changeNum = request.ChangeNumber ?? "";

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

                var script = $@"
                    return (function() {{
                        var target = null;
                        var labels = document.querySelectorAll('label');
                        for(var i=0; i<labels.length; i++) {{
                            if(labels[i].innerText.indexOf('Additional Information') > -1) {{
                                var id = labels[i].getAttribute('for');
                                if(id) target = document.getElementById(id);
                                break;
                            }}
                        }}
                        if (!target) target = document.querySelector('textarea');
                        
                        if (target) {{
                            target.value = {System.Text.Json.JsonSerializer.Serialize(justification)};
                            target.dispatchEvent(new Event('input', {{ bubbles: true }}));
                            target.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            return 'ok';
                        }}
                        return 'not_found';
                    }})();
                ";

                var res = RunScript(script);
                if (res?.ToString() != "ok")
                    return Fail("Failed to set 'Additional Information' text.");

                // Automatically fill known dropdowns based on user request
                SetSelect2Field("Alternative Point of Contact", "Mohanvel");
                SetSelect2Field("Application Support Type", "New RPA Code Deployments");
                SetSelect2Field("Team", "PnC");
                SetSelect2Field("Cloud - Environment", "Production");

                ReportStatus($"Please complete the form and Submit for {request.RpaName}. Waiting...");

                // Wait for user to submit (URL changes) or click Start Filling to force continue
                string initialUrl = _driver.Url;
                while (!IsClosed)
                {
                    if (ForceContinue)
                    {
                        ForceContinue = false;
                        break;
                    }

                    try
                    {
                        string currentUrl = _driver.Url;
                        // If url changed significantly (e.g. from catalog item to req/ritm)
                        if (currentUrl != initialUrl && !currentUrl.Contains(epalUrl))
                        {
                            break;
                        }

                        // Check for order success message in Service Portal
                        var successMsg = RunScript("return document.body.innerText.indexOf('Thank you, your request has been submitted') > -1;");
                        if (successMsg is true)
                        {
                            break;
                        }
                    }
                    catch { }

                    Thread.Sleep(1000);
                }

                ReportStatus($"Capturing REQ and RITM for {request.RpaName}...");

                // Find a link containing "REQ" and click it
                bool reqClicked = false;
                for (int i = 0; i < 15; i++) // up to 15 seconds
                {
                    if (IsClosed) return Fail("Chrome window closed.");

                    var reqScript = @"
                        return (function() {
                            var links = document.querySelectorAll('a');
                            for(var i=0; i<links.length; i++) {
                                if(links[i].innerText && links[i].innerText.indexOf('REQ') > -1) {
                                    if(/REQ\d{5,}/.test(links[i].innerText)) {
                                        links[i].click();
                                        return 'clicked';
                                    }
                                }
                            }
                            return 'not_found';
                        })();
                    ";
                    var reqRes = RunScript(reqScript)?.ToString();
                    if (reqRes == "clicked")
                    {
                        reqClicked = true;
                        break;
                    }
                    Thread.Sleep(1000);
                }

                if (reqClicked)
                {
                    // Wait for RITM page to load and capture RITM
                    for (int i = 0; i < 15; i++)
                    {
                        if (IsClosed) return Fail("Chrome window closed.");
                        var ritmScript = @"
                            return (function() {
                                var bodyText = document.body.innerText || '';
                                var match = bodyText.match(/RITM\d{5,}/);
                                if (match) return match[0];
                                return '';
                            })();
                        ";
                        var ritmRes = RunScript(ritmScript)?.ToString() ?? "";
                        if (ritmRes.StartsWith("RITM"))
                        {
                            request.RitmNumber = ritmRes;
                            break;
                        }
                        Thread.Sleep(1000);
                    }
                }
                else
                {
                    ReportStatus("Could not find a clickable REQ link.");
                }

                return new ServiceNowResult { Success = true };
            }
            catch (Exception ex)
            {
                return Fail($"Exception: {ex.Message}");
            }
        }

        private void SetSelect2Field(string labelText, string searchText)
        {
            var openScript = $@"
                return (function() {{
                    var labels = document.querySelectorAll('label');
                    for(var i=0; i<labels.length; i++) {{
                        if(labels[i].innerText.indexOf('{labelText}') > -1) {{
                            var container = labels[i].parentElement.querySelector('.select2-container');
                            if (!container) {{
                                // Try standard select if select2 isn't used
                                var sel = labels[i].parentElement.querySelector('select');
                                if (sel) {{
                                    for(var j=0; j<sel.options.length; j++) {{
                                        if(sel.options[j].innerText.indexOf('{searchText}') > -1) {{
                                            sel.selectedIndex = j;
                                            sel.dispatchEvent(new Event('change', {{ bubbles: true }}));
                                            return 'select_set';
                                        }}
                                    }}
                                }}
                                return 'no_select2';
                            }}
                            
                            var choice = container.querySelector('.select2-choice, .select2-selection');
                            if (choice) {{
                                var ev = new MouseEvent('mousedown', {{ bubbles: true }});
                                choice.dispatchEvent(ev);
                                choice.click();
                                return 'opened';
                            }}
                        }}
                    }}
                    return 'not_found';
                }})();
            ";

            var openRes = RunScript(openScript)?.ToString();
            if (openRes == "select_set" || openRes == "not_found" || openRes == "no_select2")
                return; // already handled or failed

            Thread.Sleep(500); // give select2 a moment to open and render search field

            var typeScript = $@"
                return (function() {{
                    var input = document.querySelector('#select2-drop input.select2-input') || 
                                document.querySelector('.select2-search__field') ||
                                document.querySelector('.select2-container--open input.select2-search__field');
                    if (input) {{
                        input.value = '{searchText}';
                        input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                        var ke = new KeyboardEvent('keyup', {{ bubbles: true }});
                        input.dispatchEvent(ke);
                        return 'typed';
                    }}
                    return 'no_input';
                }})();
            ";
            RunScript(typeScript);

            Thread.Sleep(1500); // give it time to fetch AJAX results

            var clickScript = $@"
                return (function() {{
                    var results = document.querySelectorAll('.select2-result-selectable, .select2-results__option');
                    for(var i=0; i<results.length; i++) {{
                        var text = results[i].innerText;
                        if (text.indexOf('{searchText}') > -1 || text.indexOf('{searchText.Split(' ')[0]}') > -1) {{
                            var ev = new MouseEvent('mouseup', {{ bubbles: true }});
                            results[i].dispatchEvent(ev);
                            results[i].click();
                            return 'selected';
                        }}
                    }}
                    if (results.length > 0) {{
                        var ev = new MouseEvent('mouseup', {{ bubbles: true }});
                        results[0].dispatchEvent(ev);
                        results[0].click();
                        return 'selected_first';
                    }}
                    return 'no_results';
                }})();
            ";
            RunScript(clickScript);
            Thread.Sleep(500);
        }
    }
}


