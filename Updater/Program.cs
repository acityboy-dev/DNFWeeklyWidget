using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DNFWeeklyWidget.Updater;

internal static class Program
{
	private const string ManagedFilesName = ".update-files.json";

	private static int Main(string[] args)
	{
		if (!UpdateOptions.HasRequiredArguments(args))
		{
			ShowStandaloneWarning();
			return 0;
		}

		try
		{
			var options = UpdateOptions.Parse(args);
			WaitForProcessExit(options.ProcessId);

			var stagingDirectory = Path.Combine(
				Path.GetDirectoryName(options.PackagePath)!,
				"staging");
			if (Directory.Exists(stagingDirectory))
				Directory.Delete(stagingDirectory, recursive: true);
			Directory.CreateDirectory(stagingDirectory);

			ExtractPackage(options.PackagePath, stagingDirectory);
			var packageFiles = GetPackageFiles(stagingDirectory);
			DeleteObsoleteManagedFiles(options.InstallDirectory, packageFiles);
			InstallFiles(stagingDirectory, options.InstallDirectory, packageFiles);
			WriteManagedFiles(options.InstallDirectory, packageFiles);

			var executablePath = GetSafeDestinationPath(options.InstallDirectory, options.Executable);
			Process.Start(new ProcessStartInfo
			{
				FileName = executablePath,
				WorkingDirectory = options.InstallDirectory,
				UseShellExecute = true
			});

			TryDelete(options.PackagePath);
			TryDeleteDirectory(stagingDirectory);
			return 0;
		}
		catch (Exception ex)
		{
			TryWriteErrorLog(args, ex);
			return 1;
		}
	}

	private static void ShowStandaloneWarning()
	{
		MessageBox(
			IntPtr.Zero,
			"업데이트 프로그램은 단독으로 실행할 수 없습니다.",
			"DNFWeeklyWidget 업데이트",
			0x00000040);
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
	private static extern int MessageBox(IntPtr windowHandle, string text, string caption, uint type);

	private static void WaitForProcessExit(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			if (!process.WaitForExit(60_000))
				throw new TimeoutException("앱 종료를 60초 동안 기다렸지만 완료되지 않았습니다.");
		}
		catch (ArgumentException)
		{
			// The application already exited.
		}
	}

	private static void ExtractPackage(string packagePath, string stagingDirectory)
	{
		using var archive = ZipFile.OpenRead(packagePath);
		foreach (var entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name))
				continue;

			var destinationPath = GetSafeDestinationPath(stagingDirectory, entry.FullName);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			entry.ExtractToFile(destinationPath, overwrite: true);
		}
	}

	private static HashSet<string> GetPackageFiles(string stagingDirectory)
	{
		return Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(stagingDirectory, path).Replace('\\', '/'))
			.Where(path => !string.Equals(path, ManagedFilesName, StringComparison.OrdinalIgnoreCase))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	private static void DeleteObsoleteManagedFiles(string installDirectory, HashSet<string> packageFiles)
	{
		var managedFilesPath = Path.Combine(installDirectory, ManagedFilesName);
		if (!File.Exists(managedFilesPath))
			return;

		var previousFiles = JsonSerializer.Deserialize<string[]>(File.ReadAllText(managedFilesPath)) ?? [];
		foreach (var relativePath in previousFiles.Where(path => !packageFiles.Contains(path)))
		{
			var obsoletePath = GetSafeDestinationPath(installDirectory, relativePath);
			TryDeleteWithRetry(obsoletePath);
			DeleteEmptyParentDirectories(Path.GetDirectoryName(obsoletePath), installDirectory);
		}
	}

	private static void InstallFiles(
		string stagingDirectory,
		string installDirectory,
		IEnumerable<string> packageFiles)
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
			catch (IOException) when (attempt < 19)
			{
				Thread.Sleep(250);
			}
			catch (UnauthorizedAccessException) when (attempt < 19)
			{
				Thread.Sleep(250);
			}
			finally
			{
				TryDelete(temporaryPath);
			}
		}
	}

	private static void WriteManagedFiles(string installDirectory, HashSet<string> packageFiles)
	{
		var path = Path.Combine(installDirectory, ManagedFilesName);
		var json = JsonSerializer.Serialize(packageFiles.OrderBy(file => file).ToArray(), new JsonSerializerOptions
		{
			WriteIndented = true
		});
		File.WriteAllText(path, json);
	}

	private static string GetSafeDestinationPath(string rootDirectory, string relativePath)
	{
		var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("업데이트 패키지에 허용되지 않은 경로가 포함되어 있습니다.");

		return destination;
	}

	private static void DeleteEmptyParentDirectories(string? directory, string rootDirectory)
	{
		var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar);
		while (!string.IsNullOrWhiteSpace(directory) &&
			!string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
		{
			if (Directory.EnumerateFileSystemEntries(directory).Any())
				return;

			var parent = Directory.GetParent(directory)?.FullName;
			Directory.Delete(directory);
			directory = parent;
		}
	}

	private static void TryDeleteWithRetry(string path)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			try
			{
				if (File.Exists(path))
					File.Delete(path);
				return;
			}
			catch (IOException) when (attempt < 19)
			{
				Thread.Sleep(250);
			}
			catch (UnauthorizedAccessException) when (attempt < 19)
			{
				Thread.Sleep(250);
			}
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
		catch
		{
		}
	}

	private static void TryWriteErrorLog(string[] args, Exception exception)
	{
		try
		{
			var installDirectory = UpdateOptions.TryGetValue(args, "--install-dir") ?? AppContext.BaseDirectory;
			File.WriteAllText(
				Path.Combine(installDirectory, "update-error.log"),
				$"{DateTime.Now:O}{Environment.NewLine}{exception}");
		}
		catch
		{
		}
	}

	private sealed class UpdateOptions
	{
		public int ProcessId { get; init; }
		public string InstallDirectory { get; init; } = "";
		public string PackagePath { get; init; } = "";
		public string Executable { get; init; } = "";

		public static bool HasRequiredArguments(string[] args) =>
			!string.IsNullOrWhiteSpace(TryGetValue(args, "--pid")) &&
			!string.IsNullOrWhiteSpace(TryGetValue(args, "--install-dir")) &&
			!string.IsNullOrWhiteSpace(TryGetValue(args, "--package")) &&
			!string.IsNullOrWhiteSpace(TryGetValue(args, "--executable"));

		public static UpdateOptions Parse(string[] args)
		{
			var pidText = GetRequiredValue(args, "--pid");
			if (!int.TryParse(pidText, out var processId) || processId <= 0)
				throw new ArgumentException("올바른 프로세스 ID가 필요합니다.");

			var installDirectory = Path.GetFullPath(GetRequiredValue(args, "--install-dir"));
			var packagePath = Path.GetFullPath(GetRequiredValue(args, "--package"));
			var executable = GetRequiredValue(args, "--executable");
			if (!Directory.Exists(installDirectory) || !File.Exists(packagePath))
				throw new DirectoryNotFoundException("업데이트 경로를 확인할 수 없습니다.");

			return new UpdateOptions
			{
				ProcessId = processId,
				InstallDirectory = installDirectory,
				PackagePath = packagePath,
				Executable = executable
			};
		}

		public static string? TryGetValue(string[] args, string name)
		{
			for (var index = 0; index < args.Length - 1; index++)
			{
				if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
					return args[index + 1];
			}

			return null;
		}

		private static string GetRequiredValue(string[] args, string name) =>
			TryGetValue(args, name) ?? throw new ArgumentException($"필수 인자 누락: {name}");
	}
}
