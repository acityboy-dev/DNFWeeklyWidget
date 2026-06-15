using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DNFWeeklyWidget;

internal static class ApplicationUpdateService
{
	private const string ManifestUrl =
		"https://raw.githubusercontent.com/acityboy-dev/DNFWeeklyWidget.Release/main/update.json";
	private const string UpdaterFileName = "update.exe";
	private static readonly HttpClient HttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(15)
	};

	public static async Task<bool> TryStartUpdateAsync()
	{
#if DEBUG
		return false;
#else
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
				remoteVersion <= GetCurrentVersion())
			{
				return false;
			}

			var executablePath = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(executablePath))
				return false;

			var installDirectory = Path.GetDirectoryName(executablePath);
			if (string.IsNullOrWhiteSpace(installDirectory))
				return false;

			var installedUpdaterPath = Path.Combine(installDirectory, UpdaterFileName);
			if (!File.Exists(installedUpdaterPath))
				return false;

			var packageUri = new Uri(manifestUri, manifest.PackageUrl);
			var updateDirectory = Path.Combine(Path.GetTempPath(), "DNFWeeklyWidget", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(updateDirectory);
			var packagePath = Path.Combine(updateDirectory, "update.zip");
			var temporaryUpdaterPath = Path.Combine(updateDirectory, UpdaterFileName);

			await DownloadFileAsync(packageUri, packagePath);
			if (!HasExpectedSha256(packagePath, manifest.Sha256))
			{
				Directory.Delete(updateDirectory, recursive: true);
				return false;
			}

			if (!PackageContainsExecutable(packagePath, manifest.Executable))
			{
				Directory.Delete(updateDirectory, recursive: true);
				return false;
			}

			File.Copy(installedUpdaterPath, temporaryUpdaterPath, overwrite: true);
			var process = Process.Start(new ProcessStartInfo
			{
				FileName = temporaryUpdaterPath,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = updateDirectory,
				ArgumentList =
				{
					"--pid", Environment.ProcessId.ToString(),
					"--install-dir", installDirectory,
					"--package", packagePath,
					"--executable", manifest.Executable
				}
			});

			return process is not null;
		}
		catch (HttpRequestException)
		{
			return false;
		}
		catch (TaskCanceledException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (JsonException)
		{
			return false;
		}
		catch (Exception)
		{
			// Update failures must never prevent the application from starting.
			return false;
		}
#endif
	}

	private static async Task DownloadFileAsync(Uri uri, string destinationPath)
	{
		using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		await using var source = await response.Content.ReadAsStreamAsync();
		await using var destination = File.Create(destinationPath);
		await source.CopyToAsync(destination);
	}

	private static bool HasExpectedSha256(string path, string expectedHash)
	{
		if (string.IsNullOrWhiteSpace(expectedHash))
			return false;

		using var stream = File.OpenRead(path);
		var actualHash = Convert.ToHexString(SHA256.HashData(stream));
		return string.Equals(actualHash, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static bool PackageContainsExecutable(string packagePath, string executable)
	{
		if (string.IsNullOrWhiteSpace(executable))
			return false;

		using var archive = ZipFile.OpenRead(packagePath);
		return archive.Entries.Any(entry =>
			string.Equals(entry.FullName.Replace('\\', '/'), executable.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
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

	private sealed class UpdateManifest
	{
		public string Version { get; set; } = "";
		public string PackageUrl { get; set; } = "";
		public string Sha256 { get; set; } = "";
		public string Executable { get; set; } = "DNFWeeklyWidget.exe";
	}
}
