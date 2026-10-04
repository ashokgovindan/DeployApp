using System.Windows;
using System.Windows.Controls;
using DeployApp.Models;
using Microsoft.Win32;

namespace DeployApp
{
    /// <summary>
    /// Interaction logic for DeploymentRequestWindow.xaml
    /// </summary>
    public partial class DeploymentRequestWindow : Window
    {
        private readonly string _currentUser;

        /// <summary>
        /// The deployment request result. Null if the dialog was cancelled.
        /// </summary>
        public DeploymentRequest? Result { get; private set; }

        /// <summary>
        /// Creates a new deployment request form (submit mode).
        /// </summary>
        /// <param name="currentUser">The current user identifier.</param>
        /// <param name="rpaNames">List of RPA names loaded from the database.</param>
        public DeploymentRequestWindow(string currentUser, List<string> rpaNames)
        {
            InitializeComponent();
            _currentUser = currentUser;

            // Populate RPA names from the database
            CmbRpaName.ItemsSource = rpaNames;

            TxtRequestedBy.Text = currentUser;
            DpRequestDate.SelectedDate = DateTime.Today;
            SubtitleText.Text = "Choose the RPA this deployment request belongs to.";
            SubmitButtonText.Text = "Submit Request";

            DisableFridaysAndSaturdays();
        }

        private void DisableFridaysAndSaturdays()
        {
            var start = DateTime.Today.AddYears(-5);
            var end = DateTime.Today.AddYears(10);
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday)
                {
                    DpChangeDate.BlackoutDates.Add(new CalendarDateRange(d));
                }
            }
        }

        /// <summary>
        /// Creates an edit form pre-populated with an existing request.
        /// </summary>
        public DeploymentRequestWindow(string currentUser, List<string> rpaNames, DeploymentRequest existing)
            : this(currentUser, rpaNames)
        {
            SubmitButtonText.Text = "Save Changes";
            SubtitleText.Text = $"Project: {existing.RpaName}  |  Editing Request {existing.RequestDate} ({existing.Environment})";

            // Select the matching RPA name
            CmbRpaName.SelectedItem = existing.RpaName;

            if (DateTime.TryParse(existing.RequestDate, out var reqDate))
                DpRequestDate.SelectedDate = reqDate;

            if (DateTime.TryParse(existing.ChangeDate, out var chgDate))
                DpChangeDate.SelectedDate = chgDate;

            TxtChangeNumber.Text = existing.ChangeNumber;
            TxtRitmNumber.Text = existing.RitmNumber;
            TxtChangeDescription.Text = existing.ChangeDescription;
            TxtImpactAnalysis.Text = existing.ImpactAnalysis;
            TxtTasksToDeploy.Text = existing.TasksToDeploy;
            TxtTestPlanPath.Text = existing.TestPlanLogPath;
            TxtCodeReviewPath.Text = existing.CodeReviewDocumentPath;
            TxtCodeAnalysisPath.Text = existing.CodeAnalysisPath;
            SelectComboBoxItem(CmbCodeMovedToTest, existing.CodeMovedToTest);
            TxtRequestedBy.Text = existing.RequestedBy;
            SelectComboBoxItem(CmbStatus, existing.Status);
        }

        /// <summary>
        /// Selects a ComboBoxItem by matching its Content string (for ComboBoxes with ComboBoxItem children).
        /// </summary>
        private void SelectComboBoxItem(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            // Partial match fallback
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString()?.StartsWith(value) == true ||
                    value.StartsWith(item.Content?.ToString() ?? "---"))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            // Validate required fields
            if (CmbRpaName.SelectedItem == null)
            {
                MessageBox.Show("Please select an RPA Name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DpRequestDate.SelectedDate == null)
            {
                MessageBox.Show("Please select a Request Date.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new DeploymentRequest
            {
                RpaName = CmbRpaName.SelectedItem?.ToString() ?? string.Empty,
                RequestDate = DpRequestDate.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                ChangeDate = DpChangeDate.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                ChangeNumber = TxtChangeNumber.Text.Trim(),
                RitmNumber = TxtRitmNumber.Text.Trim(),
                Environment = "PROD",
                ChangeDescription = TxtChangeDescription.Text.Trim(),
                ImpactAnalysis = TxtImpactAnalysis.Text.Trim(),
                TasksToDeploy = TxtTasksToDeploy.Text.Trim(),
                TestPlanLogPath = TxtTestPlanPath.Text.Trim(),
                CodeReviewDocumentPath = TxtCodeReviewPath.Text.Trim(),
                CodeAnalysisPath = TxtCodeAnalysisPath.Text.Trim(),
                CodeMovedToTest = (CmbCodeMovedToTest.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "No",
                RequestedBy = TxtRequestedBy.Text.Trim(),
                Status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Requested"
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseTestPlan_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForFile("Select Test Plan/Log");
            if (path != null) TxtTestPlanPath.Text = path;
        }

        private void BrowseCodeReview_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForFile("Select Code Review Document");
            if (path != null) TxtCodeReviewPath.Text = path;
        }

        private void BrowseCodeAnalysis_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForFile("Select Code Analysis Report");
            if (path != null) TxtCodeAnalysisPath.Text = path;
        }

        private string? BrowseForFile(string title)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "All Files (*.*)|*.*"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
