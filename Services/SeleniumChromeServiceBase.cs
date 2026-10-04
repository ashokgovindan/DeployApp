using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using DeployApp.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace DeployApp.Services
{
    public abstract class SeleniumChromeServiceBase : IDisposable
    {
        protected static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(60);

        protected readonly string _instanceUrl;
        protected IWebDriver? _driver;
        private bool _disposed;

        public Action<string>? OnStatusChanged { get; set; }
        public bool IsClosed => _driver == null || _disposed;

        public SeleniumChromeServiceBase(string instanceUrl)
        {
            _instanceUrl = instanceUrl.Trim().TrimEnd('/');
        }

        #region Chrome lifecycle

        public void Launch()
        {
            var options = new ChromeOptions();
            var profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeployApp", "ChromeProfile");
            Directory.CreateDirectory(profileDir);
            options.AddArgument($"--user-data-dir={profileDir}");

            var host = new Uri(_instanceUrl).Host;
            options.AddArgument($"--auth-server-whitelist=*.{GetDomain(host)}");
            options.AddArgument($"--auth-negotiate-delegate-whitelist=*.{GetDomain(host)}");
            options.AddArgument("--start-maximized");
            options.AddExcludedArgument("enable-automation");
            options.AddArgument("--disable-blink-features=AutomationControlled");

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            _driver = new ChromeDriver(service, options, TimeSpan.FromMinutes(2));
            _driver.Manage().Timeouts().PageLoad = PageTimeout;
            _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        }

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
            catch { }
            _driver = null;
        }

        #endregion

        #region Navigation helpers

        protected void NavigateTo(string url)
        {
            if (IsClosed) return;
            _driver!.Navigate().GoToUrl(url);
        }

        public bool ForceContinue { get; set; }

        protected bool WaitForForm(TimeSpan timeout)
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
                Thread.Sleep(1000);
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

        protected object? RunScript(string script)
        {
            if (IsClosed) return null;
            return ((IJavaScriptExecutor)_driver!).ExecuteScript(script);
        }

        #endregion

        #region Helpers

        protected void ReportStatus(string text)
        {
            OnStatusChanged?.Invoke(text);
        }

        protected static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        protected static string ExtractJsonValue(string json, string key)
        {
            var marker = $"\"{key}\":\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                marker = $"\"{key}\": \"";
                idx = json.IndexOf(marker, StringComparison.Ordinal);
            }
            if (idx < 0) return "";
            idx += marker.Length;
            var end = json.IndexOf('"', idx);
            return end > idx ? json[idx..end] : "";
        }

        protected static ServiceNowResult Fail(string message) =>
            new() { Success = false, Message = message };

        protected List<string> ReadPageErrors()
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

                if (result.StartsWith("["))
                {
                    result = result.Trim('[', ']');
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        foreach (var item in result.Split("\",\""))
                        {
                            var clean = item.Trim('"', ' ', ',');
                            if (!string.IsNullOrEmpty(clean))
                                errors.Add(clean);
                        }
                    }
                }
            }
            catch { }
            return errors;
        }

        #endregion
    }
}
