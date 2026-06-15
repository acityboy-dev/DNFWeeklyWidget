using System.Windows;

namespace DNFWeeklyWidget;

public partial class App : System.Windows.Application
{
	private const string SingleInstanceMutexName = @"Local\DNFWeeklyWidget.SingleInstance";
	private Mutex? _singleInstanceMutex;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		ShutdownMode = ShutdownMode.OnExplicitShutdown;

		_singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
		if (!isFirstInstance)
		{
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
			return;
		}

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

	protected override void OnExit(ExitEventArgs e)
	{
		_singleInstanceMutex?.ReleaseMutex();
		_singleInstanceMutex?.Dispose();
		_singleInstanceMutex = null;
		base.OnExit(e);
	}
}
