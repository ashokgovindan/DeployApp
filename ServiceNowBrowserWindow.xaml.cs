using System.Windows;
using DeployApp.Models;
using DeployApp.Services;

namespace DeployApp
{
    /// <summary>
    /// Creates ServiceNow Change Requests by launching a real Chrome browser via Selenium,
    /// filling in the Change Request form, and clicking Submit — the same way a person would.
    ///
    /// In production, SSO is handled automatically through Windows domain / Kerberos.
    /// Chrome passes the logged-in user's credentials via Negotiate/NTLM, so no manual
    /// sign-in is required.
    ///
    /// The Chrome profile is stored under %LocalAppData%\DeployApp\ChromeProfile,
    /// so cookies and sessions persist across runs.
    /// </summary>
    public partial class ServiceNowBrowserWindow : Window
    {
        private SeleniumChromeServiceBase? _chrome;
        private readonly string _instanceUrl;

        /// <summary>True once the user has closed this window.</summary>
        public bool IsClosed { get; private set; }

        public ServiceNowBrowserWindow(string instanceUrl)
        {
            InitializeComponent();
            _instanceUrl = instanceUrl.Trim().TrimEnd('/');


            // Show the ServiceNow URL so the user can copy it for testing.
            TxtUrl.Text = _instanceUrl;

            Closed += (_, _) =>
            {
                IsClosed = true;
                _chrome?.Dispose();
            };
        }

        public void SetStatus(string text) => TxtStatus.Text = text;
        public void SetProgress(string text) => TxtProgress.Text = text;

        /// <summary>
        /// Updates the URL bar to show the current page being loaded.
        /// </summary>
        public void SetUrl(string url) => TxtUrl.Text = url;

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtUrl.Text);
                // Brief visual feedback.
                BtnCopyUrl.Content = "✔ Copied";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (_, _) =>
                {
                    BtnCopyUrl.Content = "📋 Copy";
                    timer.Stop();
                };
                timer.Start();
            }
            catch { /* clipboard can fail in rare cases */ }
        }

        private void BtnStartFilling_Click(object sender, RoutedEventArgs e)
        {
            BtnStartFilling.IsEnabled = false;
            BtnStartFilling.Content = "Starting...";
            if (_chrome != null) _chrome.ForceContinue = true;
        }

        #region Chrome lifecycle

        /// <summary>
        /// Launches Chrome and opens ServiceNow. No WebView2 is used.
        /// </summary>
        public async Task InitializeAsync(bool isEpal = false)
        {
            if (isEpal)
                _chrome = new CreateEpalChromeService(_instanceUrl);
            else
                _chrome = new CreateCRChromeService(_instanceUrl);

            _chrome.OnStatusChanged = text => Dispatcher.Invoke(() => SetStatus(text));

            SetStatus("Launching Chrome...");
            await Task.Run(() => _chrome.Launch());
            SetStatus("Chrome is running.");
        }

        /// <summary>
        /// Navigates to the ServiceNow CR form and waits for sign-in / SSO.
        /// </summary>
        public async Task<bool> EnsureSignedInAsync()
        {
            var formUrl = $"{_instanceUrl}/now/nav/ui/classic/params/target/change_request.do" +
                "%3Fsys_id%3D-1%26sysparm_query%3Dchg_model%3D007c4001c343101035ae3f52c1d3aeb2";
            Dispatcher.Invoke(() => SetUrl(formUrl));
            SetStatus("Opening ServiceNow in Chrome — sign in if prompted...");
            if (_chrome is CreateCRChromeService crService)
                return await Task.Run(() => crService.EnsureSignedIn());
            return false;
        }

        /// <summary>
        /// Fills in a new Change Request form and submits it.
        /// </summary>
        public async Task<ServiceNowResult> CreateChangeRequestAsync(DeploymentRequest request)
        {
            var formUrl = $"{_instanceUrl}/now/nav/ui/classic/params/target/change_request.do" +
                "%3Fsys_id%3D-1%26sysparm_query%3Dchg_model%3D007c4001c343101035ae3f52c1d3aeb2";
            Dispatcher.Invoke(() => SetUrl(formUrl));
            if (_chrome is CreateCRChromeService crService)
                return await Task.Run(() => crService.CreateChangeRequest(request));
            return new ServiceNowResult { Success = false, Message = "Not initialized for CR." };
        }

        /// <summary>
        /// Fills in a new EPAL form and submits it.
        /// </summary>
        public async Task<ServiceNowResult> CreateEpalAsync(DeploymentRequest request, string epalUrl)
        {
            Dispatcher.Invoke(() => SetUrl(epalUrl));
            if (_chrome is CreateEpalChromeService epalService)
                return await Task.Run(() => epalService.CreateEpal(request, epalUrl));
            return new ServiceNowResult { Success = false, Message = "Not initialized for EPAL." };
        }

        #endregion
    }
}
