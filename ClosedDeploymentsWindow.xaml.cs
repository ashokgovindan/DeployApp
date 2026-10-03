using System.Collections.ObjectModel;
using System.Windows;
using DeployApp.Models;

namespace DeployApp
{
    /// <summary>
    /// Interaction logic for ClosedDeploymentsWindow.xaml
    /// Shows deployment requests with closed statuses: Deployed, Failed, Rolled Back, Cancelled.
    /// </summary>
    public partial class ClosedDeploymentsWindow : Window
    {
        private readonly ObservableCollection<DeploymentRequest> _allRequests;

        public ClosedDeploymentsWindow(ObservableCollection<DeploymentRequest> allRequests)
        {
            InitializeComponent();
            _allRequests = allRequests;
            RefreshData();
        }

        private void RefreshData()
        {
            var closedStatuses = new[] { "Deployed", "Failed", "Rolled Back", "Cancelled" };
            var closedItems = _allRequests
                .Where(r => closedStatuses.Contains(r.Status))
                .ToList();

            DgClosedDeployments.ItemsSource = closedItems;
            TxtClosedCount.Text = $"Closed  -  deployed, failed, rolled back or cancelled  ({closedItems.Count})";
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
