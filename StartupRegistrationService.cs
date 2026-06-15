using Microsoft.Win32;

namespace DNFWeeklyWidget;

internal static class StartupRegistrationService
{
	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "DNFWeeklyWidget";

	public static bool TryApply(bool enabled, out string errorMessage)
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
				Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

			if (!enabled)
			{
				key.DeleteValue(ValueName, throwOnMissingValue: false);
				errorMessage = "";
				return true;
			}

			var executablePath = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				errorMessage = "현재 실행 파일 경로를 확인할 수 없습니다.";
				return false;
			}

			key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
			errorMessage = "";
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}
}
