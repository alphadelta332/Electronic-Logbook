using System.Configuration;
using System.Data;
using System.Windows;
using ElectronicLogbook.Updater;

namespace ElectronicLogbook.Updater.Wizard;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--validate-hosted-configuration", StringComparer.Ordinal))
        {
            Shutdown(SupabaseHostedSyncConfiguration.TryLoad(out _, out _) ? 0 : 2);
            return;
        }

        // Do not put the wizard in front of Excel while the source workbook is
        // being saved and closed. If Excel needs to show anything, the user must
        // be able to see it rather than having to discover a hidden dialog.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Closed += (_, _) => Shutdown();
        if (mainWindow.IsHostedConnectionMode)
        {
            mainWindow.BeginHostedConnectionMode();
        }
        else
        {
            mainWindow.BeginAvailabilityCheck();
        }
    }
}

