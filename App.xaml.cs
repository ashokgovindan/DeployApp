using System.Windows;
using DeployApp.Themes;

namespace DeployApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load the saved theme before the first window opens.
            ThemeManager.Initialize();
        }
    }
}
