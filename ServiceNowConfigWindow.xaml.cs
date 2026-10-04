using System.Windows;
using DeployApp.Models;
using DeployApp.Services;

namespace DeployApp
{
    /// <summary>
    /// Interaction logic for ServiceNowConfigWindow.xaml
    /// Lets the user set the ServiceNow instance address.
    /// </summary>
    public partial class ServiceNowConfigWindow : Window
    {
        /// <summary>
        /// The saved configuration result, or null if cancelled.
        /// </summary>
        public ServiceNowConfig? Result { get; private set; }

        public ServiceNowConfigWindow()
        {
            InitializeComponent();

            var existing = ServiceNowService.LoadConfig();
            TxtInstanceUrl.Text = existing?.InstanceUrl ?? "https://dev203974.service-now.com";
            TxtEmail.Text = existing?.EmailAddress ?? "";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var url = TxtInstanceUrl.Text.Trim().TrimEnd('/');
            var email = TxtEmail.Text.Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show("Please enter the full instance address, starting with https://",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtInstanceUrl.Focus();
                return;
            }

            Result = new ServiceNowConfig { InstanceUrl = $"{uri.Scheme}://{uri.Host}", EmailAddress = email };
            ServiceNowService.SaveConfig(Result);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
