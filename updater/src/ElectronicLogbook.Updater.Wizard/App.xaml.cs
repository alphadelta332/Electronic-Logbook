using System.Configuration;
using System.Data;
using System.Windows;

namespace ElectronicLogbook.Updater.Wizard;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Do not put the wizard in front of Excel while the source workbook is
        // being saved and closed. If Excel needs to show anything, the user must
        // be able to see it rather than having to discover a hidden dialog.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Closed += (_, _) => Shutdown();
        mainWindow.BeginAvailabilityCheck();
    }
}

