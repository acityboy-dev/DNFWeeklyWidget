using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DNFWeeklyWidget;

internal static class ApplicationUpdateService
{
	private const string ManifestUrl =
		"https://raw.githubusercontent.com/acityboy-dev/DNFWeeklyWidget.Release/main/update.json";
	private const string UpdaterFileName = "update.exe";
	private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

	public static string CurrentVersionText => GetCurrentVersion().ToString(3);

	public static async Task<UpdateInfo?> CheckForUpdateAsync()
	{
		try
		{
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DNFWeeklyWidget-Updater");
			var manifestUri = new Uri(ManifestUrl);
			var manifestJson = await HttpClient.GetStringAsync(manifestUri);
			var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
			if (manifest is null ||
				!TryParseVersion(manifest.Version, out var remoteVersion) ||
				string.IsNullOrWhiteSpace(manifest.PackageUrl) ||
				string.IsNullOrWhiteSpace(manifest.Sha256) ||
				string.IsNullOrWhiteSpace(manifest.Executable))
			{
				return null;
			}

			return new UpdateInfo(
				GetCurrentVersion(),
				remoteVersion,
				new Uri(manifestUri, manifest.PackageUrl),
				manifest.Sha256.Trim(),
				manifest.Executable);
		}
		catch
		{
			return null;
		}
	}

	public static async Task<bool> TryStartAvailableUpdateAsync(bool skipConfirmation)
	{
#if DEBUG
		return false;
#else
		var update = await CheckForUpdateAsync();
		return update is not null && update.IsUpdateAvailable &&
			await TryStartUpdateAsync(update, skipConfirmation);
#endif
	}

	public static async Task<bool> TryStartUpdateAsync(UpdateInfo update, bool skipConfirmation)
	{
		try
		{
			var executablePath = Environment.ProcessPath;
			var installDirectory = Path.GetDirectoryName(executablePath);
			if (string.IsNullOrWhiteSpace(installDirectory))
				return false;

			var installedUpdaterPath = Path.Combine(installDirectory, UpdaterFileName);
			if (!File.Exists(installedUpdaterPath))
				return false;

			var updateDirectory = Path.Combine(Path.GetTempPath(), "DNFWeeklyWidget", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(updateDirectory);
			var temporaryUpdaterPath = Path.Combine(updateDirectory, UpdaterFileName);
			var acceptedPath = Path.Combine(updateDirectory, "accepted.flag");
			File.Copy(installedUpdaterPath, temporaryUpdaterPath, overwrite: true);

			var startInfo = new ProcessStartInfo
			{
				FileName = temporaryUpdaterPath,
				UseShellExecute = false,
				WorkingDirectory = updateDirectory
			};
			startInfo.ArgumentList.Add("--pid");
			startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
			startInfo.ArgumentList.Add("--install-dir");
			startInfo.ArgumentList.Add(installDirectory);
			startInfo.ArgumentList.Add("--package-url");
			startInfo.ArgumentList.Add(update.PackageUri.AbsoluteUri);
			startInfo.ArgumentList.Add("--sha256");
			startInfo.ArgumentList.Add(update.Sha256);
			startInfo.ArgumentList.Add("--executable");
			startInfo.ArgumentList.Add(update.Executable);
			startInfo.ArgumentList.Add("--current-version");
			startInfo.ArgumentList.Add(update.CurrentVersion.ToString(3));
			startInfo.ArgumentList.Add("--latest-version");
			startInfo.ArgumentList.Add(update.LatestVersion.ToString(3));
			startInfo.ArgumentList.Add("--accepted-file");
			startInfo.ArgumentList.Add(acceptedPath);
			if (skipConfirmation)
				startInfo.ArgumentList.Add("--skip-confirmation");

			using var process = Process.Start(startInfo);
			if (process is null)
				return false;

			while (!process.HasExited)
			{
				if (File.Exists(acceptedPath))
					return true;
				await Task.Delay(100);
			}

			return File.Exists(acceptedPath);
		}
		catch
		{
			return false;
		}
	}

	private static Version GetCurrentVersion()
	{
		var versionText = Assembly.GetExecutingAssembly()
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;
		return TryParseVersion(versionText, out var version) ? version : new Version(0, 0, 0);
	}

	private static bool TryParseVersion(string? value, out Version version)
	{
		var normalized = value?.Split('+', 2)[0].Split('-', 2)[0];
		return Version.TryParse(normalized, out version!);
	}

	internal sealed record UpdateInfo(
		Version CurrentVersion,
		Version LatestVersion,
		Uri PackageUri,
		string Sha256,
		string Executable)
	{
		public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
	}

	private sealed class UpdateManifest
	{
		public string Version { get; set; } = "";
		public string PackageUrl { get; set; } = "";
		public string Sha256 { get; set; } = "";
		public string Executable { get; set; } = "DNFWeeklyWidget.exe";
	}
}
