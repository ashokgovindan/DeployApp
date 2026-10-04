using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeployApp.Helpers;
using DeployApp.Models;
using DeployApp.Services;
using DeployApp.Themes;

namespace DeployApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Logged-in Windows user, e.g. "PLS7282 - Ashok Govindan".</summary>
        private readonly string CurrentUser = UserHelper.GetCurrentUserLabel();

        /// <summary>The select-all checkbox in the grid header (created by its template).</summary>
        private CheckBox? _chkSelectAll;

        private readonly ObservableCollection<DeploymentRequest> _allRequests = new();
        private readonly DatabaseService _dbService = new();
        private List<string> _rpaNames = new();

        public MainWindow()
        {
            InitializeComponent();
            Title = $"Deployment Manager  -  {CurrentUser}";
            InitializeThemePicker();
            InitializeDatabase();
            LoadDataFromDatabase();
            RefreshGrid();
            ApplyUserRoles();
        }

        private void ApplyUserRoles()
        {
            var userId = UserHelper.GetCurrentUserId();
            if (!_dbService.IsAdminUser(userId))
            {
                BtnEdit.Visibility = Visibility.Collapsed;
                BtnRemoveSelected.Visibility = Visibility.Collapsed;
                BtnCreateCR.Visibility = Visibility.Collapsed;
                BtnCreateEPAL.Visibility = Visibility.Collapsed;
                BtnClosedReport.Visibility = Visibility.Collapsed;
                BtnDashboard.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Initializes the Access database, creating the .accdb file and tables if needed.
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                _dbService.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize the database:\n\n{ex.Message}\n\nDatabase path: {_dbService.DatabasePath}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Loads all deployment requests from the Access database into the in-memory collection.
        /// Also refreshes the RPA names list.
        /// </summary>
        private void LoadDataFromDatabase()
        {
            try
            {
                _allRequests.Clear();
                var records = _dbService.LoadAll();
                foreach (var record in records)
                {
                    _allRequests.Add(record);
                }

                _rpaNames = _dbService.LoadRpaNames();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load data from database:\n\n{ex.Message}",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Refreshes the data grid to show only open (non-closed) requests.
        /// </summary>
        private void RefreshGrid()
        {
            var closedStatuses = new[] { "Deployed", "Failed", "Rolled Back", "Cancelled" };
            var openRequests = _allRequests
                .Where(r => !closedStatuses.Contains(r.Status))
                .ToList();

            DgDeployments.ItemsSource = openRequests;
            UpdateSelectAllState();

            int openCount = openRequests.Count;
            int withoutRitm = openRequests.Count(r => string.IsNullOrWhiteSpace(r.RitmNumber));
            TxtStatusBar.Text = $"{openCount} open requests  -  {withoutRitm} without a RITM.  Closed requests are in the report.  Dates use yyyy-MM-dd.";
        }

        /// <summary>
        /// Returns the list of checked deployment requests from the data grid.
        /// </summary>
        private List<DeploymentRequest> GetCheckedRequests()
        {
            if (DgDeployments.ItemsSource is not IEnumerable<DeploymentRequest> items)
                return new List<DeploymentRequest>();

            return items.Where(r => r.IsSelected).ToList();
        }

        #region Theme

        private bool _themePickerReady;

        /// <summary>
        /// Fills the theme picker and selects the active theme.
        /// </summary>
        private void InitializeThemePicker()
        {
            CmbTheme.ItemsSource = ThemeManager.Themes;
            CmbTheme.SelectedItem = ThemeManager.Current;
            _themePickerReady = true;
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_themePickerReady && CmbTheme.SelectedItem is ThemeInfo theme)
                ThemeManager.Apply(theme.Key);
        }

        #endregion

        #region Toolbar Handlers

        private void BtnSubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DeploymentRequestWindow(CurrentUser, _rpaNames)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                try
                {
                    _dbService.Insert(dialog.Result);
                    _allRequests.Add(dialog.Result);
                    RefreshGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save to database:\n\n{ex.Message}",
                        "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedRequest();
        }

        private void DgRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ignore double-clicks on the row checkbox. Those are quick toggles, not edit requests.
            if (e.OriginalSource is DependencyObject source && FindAncestor<CheckBox>(source) != null)
                return;

            if (sender is DataGridRow row)
            {
                DgDeployments.SelectedItem = row.Item;
                EditSelectedRequest();
                e.Handled = true;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null && current is not T)
                current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            return current as T;
        }

        private void EditSelectedRequest()
        {
            if (DgDeployments.SelectedItem is not DeploymentRequest selected)
            {
                MessageBox.Show("Please select a deployment request to edit.", "Edit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DeploymentRequestWindow(CurrentUser, _rpaNames, selected)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                try
                {
                    selected.CopyFrom(dialog.Result);
                    _dbService.Update(selected);
                    RefreshGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update database:\n\n{ex.Message}",
                        "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (DgDeployments.SelectedItem is not DeploymentRequest selected)
            {
                MessageBox.Show("Please select a deployment request to remove.", "Remove",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Remove deployment request \"{selected.Deployment}\" for {selected.RpaName}?",
                "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _dbService.Delete(selected.Id);
                    _allRequests.Remove(selected);
                    RefreshGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete from database:\n\n{ex.Message}",
                        "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadDataFromDatabase();
            RefreshGrid();
        }

        private void BtnClosedReport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ClosedDeploymentsWindow(_allRequests)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        #endregion

        #region Checkbox Select All

        private void ChkSelectAll_Loaded(object sender, RoutedEventArgs e)
        {
            _chkSelectAll = sender as CheckBox;
            UpdateSelectAllState();
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && DgDeployments.ItemsSource is IEnumerable<DeploymentRequest> items)
            {
                // A click from the partly checked state clears every row.
                bool isChecked = chk.IsChecked == true;
                foreach (var item in items)
                {
                    item.IsSelected = isChecked;
                }
                UpdateSelectAllState();
            }
        }

        private void ChkRow_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectAllState();
        }

        /// <summary>
        /// Sets the header checkbox to checked, unchecked, or partly checked based on the rows.
        /// </summary>
        private void UpdateSelectAllState()
        {
            if (_chkSelectAll == null) return;

            var items = (DgDeployments.ItemsSource as IEnumerable<DeploymentRequest>)?.ToList()
                        ?? new List<DeploymentRequest>();
            int checkedCount = items.Count(r => r.IsSelected);

            _chkSelectAll.IsChecked = checkedCount == 0 ? false
                                    : checkedCount == items.Count ? true
                                    : null;
        }

        #endregion

        #region Create CR (ServiceNow Change Request)

        private async void BtnCreateCR_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = GetCheckedRequests();

            // Filter out items that already have a Change Number
            checkedItems = checkedItems.Where(r => string.IsNullOrWhiteSpace(r.ChangeNumber)).ToList();

            if (checkedItems.Count == 0)
            {
                MessageBox.Show("Please check one or more deployment requests that do not already have a Change Number.",
                    "Create CR", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validate that all checked items have a Change Date
            var missingDate = checkedItems.Where(r => string.IsNullOrWhiteSpace(r.ChangeDate)).ToList();
            if (missingDate.Count > 0)
            {
                var names = string.Join("\n", missingDate.Select(r => $"  • {r.RpaName} ({r.Deployment})"));
                MessageBox.Show(
                    $"The following requests are missing a Change Date (required to schedule at 10 PM):\n\n{names}\n\nPlease edit these requests and set a Change Date first.",
                    "Missing Change Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ensure the ServiceNow instance address is set
            var config = ServiceNowService.LoadConfig();
            if (config == null || string.IsNullOrWhiteSpace(config.InstanceUrl))
            {
                var configDialog = new ServiceNowConfigWindow { Owner = this };
                if (configDialog.ShowDialog() != true || configDialog.Result == null)
                    return;
                config = configDialog.Result;
            }

            // Confirm
            var confirmMsg = $"Create {checkedItems.Count} Change Request(s) in ServiceNow?\n\n" +
                             $"Instance: {config.InstanceUrl}\n" +
                             $"Each will be scheduled at 10:00 PM on its Change Date.\n\n" +
                             "Chrome will open and fill in the forms automatically.\n" +
                             "SSO sign-in is handled through your Windows credentials.";
            if (MessageBox.Show(confirmMsg, "Confirm CR Creation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var browser = new ServiceNowBrowserWindow(config.InstanceUrl) { Owner = this };
            browser.Show();
            IsEnabled = false;

            var results = new StringBuilder();
            int successCount = 0;

            try
            {
                try
                {
                    await browser.InitializeAsync();
                }
                catch (Exception ex)
                {
                    browser.Close();
                    MessageBox.Show(
                        "Could not launch Chrome.\n\n" +
                        $"{ex.Message}\n\nMake sure Google Chrome is installed and accessible.",
                        "Create CR", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!await browser.EnsureSignedInAsync())
                {
                    if (!browser.IsClosed) browser.Close();
                    MessageBox.Show("Not signed in to ServiceNow. No change requests were created.",
                        "Create CR", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create CRs one at a time through the form
                for (int i = 0; i < checkedItems.Count; i++)
                {
                    var request = checkedItems[i];

                    if (browser.IsClosed)
                    {
                        results.AppendLine($"✘ {request.RpaName}: Skipped. Chrome was closed.");
                        continue;
                    }

                    browser.Title = $"ServiceNow  -  Change request {i + 1} of {checkedItems.Count}";
                    browser.SetProgress($"Processing {i + 1} of {checkedItems.Count}: {request.RpaName}");
                    var result = await browser.CreateChangeRequestAsync(request);

                    if (result.Success)
                    {
                        successCount++;
                        request.ChangeNumber = result.ChangeNumber;

                        // Persist the Change # back to the database
                        try
                        {
                            _dbService.Update(request);
                        }
                        catch { /* best-effort DB update */ }

                        results.AppendLine($"✔ {request.RpaName}: {result.ChangeNumber}");
                    }
                    else
                    {
                        results.AppendLine($"✘ {request.RpaName}: {result.Message}");
                    }
                }

                if (!browser.IsClosed)
                {
                    browser.Title = "ServiceNow";
                    browser.SetStatus($"Done. {successCount} of {checkedItems.Count} change request(s) created. You can close this window.");
                    browser.SetProgress("Complete.");
                }
            }
            finally
            {
                IsEnabled = true;
                RefreshGrid();
            }

            MessageBox.Show(
                $"Results: {successCount}/{checkedItems.Count} created successfully.\n\n{results}",
                "Create CR - Results",
                MessageBoxButton.OK,
                successCount == checkedItems.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);

            // Send email if there were errors and an email is configured
            if (successCount < checkedItems.Count && !string.IsNullOrWhiteSpace(config.EmailAddress))
            {
                try
                {
                    var subject = Uri.EscapeDataString("DeployApp - Change Request Errors");
                    var body = Uri.EscapeDataString($"The following errors occurred while creating Change Requests:\n\n{results}");
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"mailto:{config.EmailAddress}?subject={subject}&body={body}",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch
                {
                    // Ignore mailto errors
                }
            }
        }

        #endregion

        private void BtnServiceNowSettings_Click(object sender, RoutedEventArgs e)
        {
            new ServiceNowConfigWindow { Owner = this }.ShowDialog();
        }

        #region Create EPAL (Placeholder)

        private async void BtnCreateEPAL_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = GetCheckedRequests();

            if (checkedItems.Count == 0)
            {
                MessageBox.Show("Please check one or more deployment requests to create an EPAL.",
                    "Create EPAL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var missingChangeNum = checkedItems.Where(r => string.IsNullOrWhiteSpace(r.ChangeNumber)).ToList();
            if (missingChangeNum.Count > 0)
            {
                var names = string.Join("\n", missingChangeNum.Select(r => $"  • {r.RpaName} ({r.Deployment})"));
                MessageBox.Show(
                    $"The following requests are missing a Change Number (required for EPAL):\n\n{names}\n\nPlease create Change Requests for these first.",
                    "Missing Change Number", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = ServiceNowService.LoadConfig();
            if (config == null || string.IsNullOrWhiteSpace(config.EpalUrl))
            {
                var configDialog = new ServiceNowConfigWindow { Owner = this };
                if (configDialog.ShowDialog() != true || configDialog.Result == null || string.IsNullOrWhiteSpace(configDialog.Result.EpalUrl))
                    return;
                config = configDialog.Result;
            }

            var confirmMsg = $"Create {checkedItems.Count} EPAL(s)?\n\nEPAL URL: {config.EpalUrl}";
            if (MessageBox.Show(confirmMsg, "Confirm EPAL Creation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var browser = new ServiceNowBrowserWindow(config.InstanceUrl) { Owner = this };
            browser.Show();
            IsEnabled = false;

            var results = new StringBuilder();
            int successCount = 0;

            try
            {
                try
                {
                    await browser.InitializeAsync();
                }
                catch (Exception ex)
                {
                    browser.Close();
                    MessageBox.Show($"Could not launch Chrome.\n\n{ex.Message}", "Create EPAL", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                for (int i = 0; i < checkedItems.Count; i++)
                {
                    var request = checkedItems[i];
                    if (browser.IsClosed)
                    {
                        results.AppendLine($"✘ {request.RpaName}: Skipped. Chrome closed.");
                        continue;
                    }

                    browser.Title = $"ServiceNow - EPAL {i + 1} of {checkedItems.Count}";
                    browser.SetProgress($"Processing {i + 1} of {checkedItems.Count}: {request.RpaName}");
                    var result = await browser.CreateEpalAsync(request, config.EpalUrl);

                    if (result.Success)
                    {
                        successCount++;
                        
                        try
                        {
                            _dbService.Update(request);
                        }
                        catch { /* best effort */ }

                        string msg = $"✔ {request.RpaName}";
                        if (!string.IsNullOrEmpty(request.RitmNumber))
                            msg += $" ({request.RitmNumber})";
                        results.AppendLine(msg);
                    }
                    else
                    {
                        results.AppendLine($"✘ {request.RpaName}: {result.Message}");
                    }
                }

                if (!browser.IsClosed)
                {
                    browser.Title = "ServiceNow";
                    browser.SetStatus($"Done. {successCount} of {checkedItems.Count} EPAL(s) created.");
                    browser.SetProgress("Complete.");
                }
            }
            finally
            {
                IsEnabled = true;
                RefreshGrid();
            }

            MessageBox.Show($"Results: {successCount}/{checkedItems.Count} created successfully.\n\n{results}", "Create EPAL - Results", MessageBoxButton.OK, successCount == checkedItems.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
            
            if (successCount < checkedItems.Count && !string.IsNullOrWhiteSpace(config.EmailAddress))
            {
                try
                {
                    var subject = Uri.EscapeDataString("DeployApp - EPAL Errors");
                    var body = Uri.EscapeDataString($"The following errors occurred while creating EPALs:\n\n{results}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"mailto:{config.EmailAddress}?subject={subject}&body={body}",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        #endregion
        private void BtnDashboard_Click(object sender, RoutedEventArgs e)
        {
            var win = new DashboardWindow { Owner = this };
            win.ShowDialog();
        }
    }
}