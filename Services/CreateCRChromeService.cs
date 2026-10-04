using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DeployApp.Models;
using OpenQA.Selenium;

namespace DeployApp.Services
{
    public class CreateCRChromeService : SeleniumChromeServiceBase
    {
        public CreateCRChromeService(string instanceUrl) : base(instanceUrl)
        {
        }

        private string NewFormUrl =>
            $"{_instanceUrl}/now/nav/ui/classic/params/target/change_request.do" +
            "%3Fsys_id%3D-1%26sysparm_query%3Dchg_model%3D007c4001c343101035ae3f52c1d3aeb2";

        public bool EnsureSignedIn()
        {
            ReportStatus("Opening ServiceNow in Chrome...");
            NavigateTo(NewFormUrl);

            ReportStatus("Waiting for the New Change Request form... (Please sign in or navigate to the form if needed)");

            if (!WaitForForm(TimeSpan.FromMinutes(15)))
            {
                return false;
            }

            ReportStatus("Change Request form detected. Ready to fill info.");
            return true;
        }

        public ServiceNowResult CreateChangeRequest(DeploymentRequest request)
        {
            try
            {
                string busy = $"Creating change request for {request.RpaName}...";
                ReportStatus(busy);

                if (ForceContinue)
                {
                    ForceContinue = false;
                    if (!WaitForForm(TimeSpan.FromSeconds(15)))
                    {
                        return Fail("Could not detect the form on the current page. Please ensure the page is fully loaded.");
                    }
                }
                else
                {
                    bool isAlreadyOnNewForm = false;
                    try
                    {
                        var checkNew = RunScript("return typeof g_form !== 'undefined' ? g_form.isNewRecord() : false;");
                        isAlreadyOnNewForm = checkNew is true;
                    }
                    catch { }

                    if (!isAlreadyOnNewForm)
                    {
                        NavigateTo(NewFormUrl);
                        if (!WaitForForm(PageTimeout))
                            return Fail(IsClosed
                                ? "The Chrome window was closed."
                                : "The Change Request form did not open.");
                    }
                }

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

                int[]? changeDate = DateTime.TryParse(request.ChangeDate, out var date)
                    ? new[] { date.Year, date.Month, date.Day }
                    : null;

                var fields = new Dictionary<string, string>
                {
                    ["short_description"] = Escape(request.RpaName + " - " + request.Deployment),
                    ["description"] = Escape(request.ChangeDescription),
                    ["category"] = "Other",
                    ["risk"] = "4",
                    ["impact"] = "3",
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
                    sb.AppendLine($"  var fmt = window.g_user_date_time_format || 'yyyy-MM-dd HH:mm:ss';");
                    sb.AppendLine($"  g_form.setValue('start_date', formatDate(new Date({changeDate[0]}, {changeDate[1] - 1}, {changeDate[2]}, 22, 0, 0), fmt));");
                    sb.AppendLine($"  g_form.setValue('end_date',   formatDate(new Date({changeDate[0]}, {changeDate[1] - 1}, {changeDate[2]}, 23, 0, 0), fmt));");
                }

                sb.AppendLine("  return JSON.stringify({ ok: true, sysId: g_form.getUniqueValue(), number: g_form.getValue('number') });");
                sb.AppendLine("} catch(e) { return JSON.stringify({ ok: false, error: String(e) }); }})()");

                var fillResult = RunScript(sb.ToString())?.ToString() ?? "";
                if (!fillResult.Contains("\"ok\":true") && !fillResult.Contains("\"ok\": true"))
                    return Fail("Could not fill in the form: " + fillResult);

                string sysId = ExtractJsonValue(fillResult, "sysId");
                Thread.Sleep(1000);

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

                Thread.Sleep(2000);
                var errors = ReadPageErrors();

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
    }
}
