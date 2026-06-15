using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DNFWeeklyWidget.Updater;

internal static class Program
{
	private const string ManagedFilesName = ".update-files.json";

	[STAThread]
	private static int Main(string[] args)
	{
		if (!UpdateOptions.HasRequiredArguments(args))
		{
			MessageBox.Show("업데이트 프로그램은 단독으로 실행할 수 없습니다.", "DNFWeeklyWidget 업데이트",
				MessageBoxButton.OK, MessageBoxImage.Information);
			return 0;
		}

		try
		{
			var options = UpdateOptions.Parse(args);
			if (!options.SkipConfirmation)
			{
				var result = MessageBox.Show(
					$"새로운 버전이 발견되었습니다.\n\n현재 버전: {options.CurrentVersion}\n최신 버전: {options.LatestVersion}\n\n업데이트를 진행할까요?",
					"DNFWeeklyWidget 업데이트",
					MessageBoxButton.YesNo,
					MessageBoxImage.Information);
				if (result != MessageBoxResult.Yes)
					return 0;
			}

			File.WriteAllText(options.AcceptedFilePath, "accepted");
			var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
			application.Run(new ProgressWindow(options));
			return 0;
		}
		catch (Exception ex)
		{
			TryWriteErrorLog(args, ex);
			MessageBox.Show("업데이트를 시작하지 못했습니다.\n\n" + ex.Message,
				"DNFWeeklyWidget 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
			return 1;
		}
	}

	private sealed class ProgressWindow : Window
	{
		private readonly UpdateOptions _options;
		private readonly TextBlock _statusText;
		private readonly ProgressBar _progressBar;
		private bool _allowClose;

		public ProgressWindow(UpdateOptions options)
		{
			_options = options;
			Title = "DNFWeeklyWidget 업데이트";
			Width = 430;
			Height = 170;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			ShowInTaskbar = true;
			Background = new SolidColorBrush(Color.FromRgb(35, 37, 43));
			Foreground = Brushes.White;
			Closing += (_, e) => e.Cancel = !_allowClose;

			_statusText = new TextBlock
			{
				Text = "업데이트를 준비하고 있습니다...",
				FontSize = 14,
				Margin = new Thickness(0, 0, 0, 18)
			};
			_progressBar = new ProgressBar
			{
				Height = 18,
				Minimum = 0,
				Maximum = 100,
				IsIndeterminate = true
			};

			Content = new StackPanel
			{
				Margin = new Thickness(26),
				VerticalAlignment = VerticalAlignment.Center,
				Children = { _statusText, _progressBar }
			};
			Loaded += async (_, _) => await RunUpdateAsync();
		}

		private async Task RunUpdateAsync()
		{
			try
			{
				_statusText.Text = "앱이 종료되기를 기다리고 있습니다...";
				await Task.Run(() => WaitForProcessExit(_options.ProcessId));

				var workingDirectory = Path.GetDirectoryName(_options.AcceptedFilePath)!;
				var packagePath = Path.Combine(workingDirectory, "update.zip");
				var stagingDirectory = Path.Combine(workingDirectory, "staging");

				_statusText.Text = "업데이트를 다운로드하고 있습니다...";
				_progressBar.IsIndeterminate = false;
				await DownloadPackageAsync(_options.PackageUri, packagePath, progress =>
				{
					_progressBar.Value = progress;
					_statusText.Text = $"업데이트를 다운로드하고 있습니다... {progress:0}%";
				});

				_statusText.Text = "다운로드한 파일을 확인하고 있습니다...";
				_progressBar.IsIndeterminate = true;
				await Task.Run(() => ValidatePackage(packagePath, _options.Sha256, _options.Executable));

				_statusText.Text = "새 버전을 설치하고 있습니다...";
				await Task.Run(() => InstallPackage(packagePath, stagingDirectory, _options));

				_statusText.Text = "업데이트가 완료되었습니다. 앱을 다시 실행합니다...";
				Process.Start(new ProcessStartInfo
				{
					FileName = GetSafeDestinationPath(_options.InstallDirectory, _options.Executable),
					WorkingDirectory = _options.InstallDirectory,
					UseShellExecute = true
				});
				await Task.Delay(500);
				_allowClose = true;
				Application.Current.Shutdown();
			}
			catch (Exception ex)
			{
				TryWriteErrorLog(_options.InstallDirectory, ex);
				MessageBox.Show(this, "업데이트에 실패했습니다.\n\n" + ex.Message,
					"DNFWeeklyWidget 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
				_allowClose = true;
				Application.Current.Shutdown(1);
			}
		}

	}

	private static async Task DownloadPackageAsync(Uri uri, string destinationPath, Action<double> progress)
	{
		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd("DNFWeeklyWidget-Updater");
		using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		var totalBytes = response.Content.Headers.ContentLength;
		await using var source = await response.Content.ReadAsStreamAsync();
		await using var destination = File.Create(destinationPath);
		var buffer = new byte[81920];
		long downloaded = 0;
		int read;
		while ((read = await source.ReadAsync(buffer)) > 0)
		{
			await destination.WriteAsync(buffer.AsMemory(0, read));
			downloaded += read;
			if (totalBytes > 0)
				progress(downloaded * 100d / totalBytes.Value);
		}
	}

	private static void ValidatePackage(string packagePath, string expectedHash, string executable)
	{
		using (var stream = File.OpenRead(packagePath))
		{
			var actualHash = Convert.ToHexString(SHA256.HashData(stream));
			if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("업데이트 파일의 무결성 검증에 실패했습니다.");
		}

		using var archive = ZipFile.OpenRead(packagePath);
		if (!archive.Entries.Any(entry => string.Equals(
			entry.FullName.Replace('\\', '/'), executable.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidDataException("업데이트 패키지에 실행 파일이 없습니다.");
		}
	}

	private static void InstallPackage(string packagePath, string stagingDirectory, UpdateOptions options)
	{
		if (Directory.Exists(stagingDirectory))
			Directory.Delete(stagingDirectory, recursive: true);
		Directory.CreateDirectory(stagingDirectory);
		ExtractPackage(packagePath, stagingDirectory);
		var packageFiles = GetPackageFiles(stagingDirectory);
		DeleteObsoleteManagedFiles(options.InstallDirectory, packageFiles);
		InstallFiles(stagingDirectory, options.InstallDirectory, packageFiles);
		WriteManagedFiles(options.InstallDirectory, packageFiles);
		TryDelete(packagePath);
		TryDeleteDirectory(stagingDirectory);
	}

	private static void WaitForProcessExit(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			if (!process.WaitForExit(60_000))
				throw new TimeoutException("앱 종료를 60초 동안 기다렸지만 완료되지 않았습니다.");
		}
		catch (ArgumentException) { }
	}

	private static void ExtractPackage(string packagePath, string stagingDirectory)
	{
		using var archive = ZipFile.OpenRead(packagePath);
		foreach (var entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name)) continue;
			var destinationPath = GetSafeDestinationPath(stagingDirectory, entry.FullName);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			entry.ExtractToFile(destinationPath, overwrite: true);
		}
	}

	private static HashSet<string> GetPackageFiles(string stagingDirectory) =>
		Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(stagingDirectory, path).Replace('\\', '/'))
			.Where(path => !string.Equals(path, ManagedFilesName, StringComparison.OrdinalIgnoreCase))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	private static void DeleteObsoleteManagedFiles(string installDirectory, HashSet<string> packageFiles)
	{
		var managedFilesPath = Path.Combine(installDirectory, ManagedFilesName);
		if (!File.Exists(managedFilesPath)) return;
		var previousFiles = JsonSerializer.Deserialize<string[]>(File.ReadAllText(managedFilesPath)) ?? [];
		foreach (var relativePath in previousFiles.Where(path => !packageFiles.Contains(path)))
			TryDeleteWithRetry(GetSafeDestinationPath(installDirectory, relativePath));
	}

	private static void InstallFiles(string stagingDirectory, string installDirectory, IEnumerable<string> packageFiles)
	{
		foreach (var relativePath in packageFiles)
		{
			var sourcePath = GetSafeDestinationPath(stagingDirectory, relativePath);
			var destinationPath = GetSafeDestinationPath(installDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			ReplaceFileWithRetry(sourcePath, destinationPath);
		}
	}

	private static void ReplaceFileWithRetry(string sourcePath, string destinationPath)
	{
		var temporaryPath = destinationPath + ".update-new";
		for (var attempt = 0; attempt < 20; attempt++)
		{
			try
			{
				File.Copy(sourcePath, temporaryPath, overwrite: true);
				File.Move(temporaryPath, destinationPath, overwrite: true);
				return;
			}
			catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 19)
			{
				Thread.Sleep(250);
			}
			finally { TryDelete(temporaryPath); }
		}
	}

	private static void WriteManagedFiles(string installDirectory, HashSet<string> packageFiles) =>
		File.WriteAllText(Path.Combine(installDirectory, ManagedFilesName),
			JsonSerializer.Serialize(packageFiles.OrderBy(file => file).ToArray(), new JsonSerializerOptions { WriteIndented = true }));

	private static string GetSafeDestinationPath(string rootDirectory, string relativePath)
	{
		var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("업데이트 패키지에 허용되지 않은 경로가 포함되어 있습니다.");
		return destination;
	}

	private static void TryDeleteWithRetry(string path)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			try { if (File.Exists(path)) File.Delete(path); return; }
			catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 19) { Thread.Sleep(250); }
		}
	}

	private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
	private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

	private static void TryWriteErrorLog(string[] args, Exception exception) =>
		TryWriteErrorLog(UpdateOptions.TryGetValue(args, "--install-dir") ?? AppContext.BaseDirectory, exception);

	private static void TryWriteErrorLog(string installDirectory, Exception exception)
	{
		try { File.WriteAllText(Path.Combine(installDirectory, "update-error.log"), $"{DateTime.Now:O}{Environment.NewLine}{exception}"); }
		catch { }
	}

	private sealed class UpdateOptions
	{
		public int ProcessId { get; init; }
		public string InstallDirectory { get; init; } = "";
		public Uri PackageUri { get; init; } = null!;
		public string Sha256 { get; init; } = "";
		public string Executable { get; init; } = "";
		public string CurrentVersion { get; init; } = "";
		public string LatestVersion { get; init; } = "";
		public string AcceptedFilePath { get; init; } = "";
		public bool SkipConfirmation { get; init; }

		private static readonly string[] RequiredArguments =
		[
			"--pid", "--install-dir", "--package-url", "--sha256", "--executable",
			"--current-version", "--latest-version", "--accepted-file"
		];

		public static bool HasRequiredArguments(string[] args) =>
			RequiredArguments.All(name => !string.IsNullOrWhiteSpace(TryGetValue(args, name)));

		public static UpdateOptions Parse(string[] args)
		{
			if (!int.TryParse(GetRequiredValue(args, "--pid"), out var processId) || processId <= 0)
				throw new ArgumentException("올바른 프로세스 ID가 필요합니다.");
			var installDirectory = Path.GetFullPath(GetRequiredValue(args, "--install-dir"));
			if (!Directory.Exists(installDirectory))
				throw new DirectoryNotFoundException("설치 경로를 확인할 수 없습니다.");
			return new UpdateOptions
			{
				ProcessId = processId,
				InstallDirectory = installDirectory,
				PackageUri = new Uri(GetRequiredValue(args, "--package-url")),
				Sha256 = GetRequiredValue(args, "--sha256"),
				Executable = GetRequiredValue(args, "--executable"),
				CurrentVersion = GetRequiredValue(args, "--current-version"),
				LatestVersion = GetRequiredValue(args, "--latest-version"),
				AcceptedFilePath = Path.GetFullPath(GetRequiredValue(args, "--accepted-file")),
				SkipConfirmation = args.Contains("--skip-confirmation", StringComparer.OrdinalIgnoreCase)
			};
		}

		public static string? TryGetValue(string[] args, string name)
		{
			for (var index = 0; index < args.Length - 1; index++)
				if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
			return null;
		}

		private static string GetRequiredValue(string[] args, string name) =>
			TryGetValue(args, name) ?? throw new ArgumentException($"필수 인자 누락: {name}");
	}
}
