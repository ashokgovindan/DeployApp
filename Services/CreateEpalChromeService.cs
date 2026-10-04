using System;
using System.Threading;
using DeployApp.Models;

namespace DeployApp.Services
{
    public class CreateEpalChromeService : SeleniumChromeServiceBase
    {
        public CreateEpalChromeService(string instanceUrl) : base(instanceUrl)
        {
        }

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

                bool found = false;
                for (int i = 0; i < 30; i++)
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

                SetSelect2Field("Alternative Point of Contact", "Mohanvel");
                SetSelect2Field("Application Support Type", "New RPA Code Deployments");
                SetSelect2Field("Team", "PnC");
                SetSelect2Field("Cloud - Environment", "Production");

                ReportStatus($"Please complete the form and Submit for {request.RpaName}. Waiting...");

                string initialUrl = _driver!.Url;
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
                        if (currentUrl != initialUrl && !currentUrl.Contains(epalUrl))
                        {
                            break;
                        }

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

                bool reqClicked = false;
                for (int i = 0; i < 15; i++)
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
                return;

            Thread.Sleep(500);

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

            Thread.Sleep(1500);

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
