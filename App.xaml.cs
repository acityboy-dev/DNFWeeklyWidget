using System.Windows;

namespace DNFWeeklyWidget;

public partial class App : System.Windows.Application
{
	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		ShutdownMode = ShutdownMode.OnExplicitShutdown;

#if !DEBUG
		var startupSettings = AppSettings.Load();
		if (startupSettings.CheckForUpdatesOnStartup &&
			await ApplicationUpdateService.TryStartAvailableUpdateAsync(skipConfirmation: false))
		{
			Shutdown();
			return;
		}
#endif

		var mainWindow = new MainWindow();
		MainWindow = mainWindow;
		ShutdownMode = ShutdownMode.OnMainWindowClose;
		mainWindow.Show();
	}
}
