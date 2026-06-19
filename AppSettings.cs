using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DNFWeeklyWidget;

public class AppSettings
{
	private static readonly object SaveLock = new();
	private const string LegacyApiKeyJsonName = "ApiKey";
	private const string EncryptedApiKeyJsonName = "EncryptedApiKey";

	[JsonIgnore]
	public string ApiKey { get; set; } = "";
	public string ServerId { get; set; } = "cain";
	public string ThemeMode { get; set; } = "system";
	public string CharacterImageMode { get; set; } = "compact";
	public bool IsCompactMode { get; set; }
	public bool FilterIncompleteOnly { get; set; }
	public bool AutoSortByFame { get; set; }
	public bool LowPerformanceMode { get; set; }
	public bool AutoRefreshOnStartup { get; set; } = true;
	public bool CheckForUpdatesOnStartup { get; set; } = true;
	public bool RunAtWindowsStartup { get; set; }
	public int AutoRefreshIntervalMinutes { get; set; } = 30;
	public bool ShowInTaskbar { get; set; }
	public bool EnableUserDataCache { get; set; } = true;
	public bool ShowCardMemos { get; set; } = true;
	public bool HideRaidContents { get; set; }
	public bool AutoHideRaidContents { get; set; }
	public int Columns { get; set; } = 4;
	public double? WindowLeft { get; set; }
	public double? WindowTop { get; set; }
	public double? WindowWidth { get; set; }
	public double? WindowHeight { get; set; }
	public List<SavedCharacter> Characters { get; set; } = new();
	public List<CachedCharacterCard> CachedCards { get; set; } = new();
	public string ActivePresetId { get; set; } = "";
	public List<CardEntryPreset> Presets { get; set; } = new();
	public WeeklyContentSettings WeeklyContents { get; set; } = new();

	[JsonIgnore]
	public CardEntryPreset ActivePreset
	{
		get
		{
			EnsurePresets();
			return Presets.FirstOrDefault(x => x.Id == ActivePresetId) ?? Presets[0];
		}
	}

	private static string Dir =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DNFWeeklyWidget");

	public static string PathFile => Path.Combine(Dir, "settings.json");
	public static string ImageCacheDir => Path.Combine(Dir, "ImageCache");

	public static AppSettings Load()
	{
		Directory.CreateDirectory(Dir);

		if (!File.Exists(PathFile))
			return new AppSettings();

		AppSettings settings;
		var migrateLegacyApiKey = false;
		try
		{
			var json = File.ReadAllText(PathFile);
			settings = DeserializeForStorage(json, out migrateLegacyApiKey);
		}
		catch (JsonException)
		{
			File.Copy(PathFile, PathFile + ".broken", true);
			settings = new AppSettings();
		}

		settings.Characters ??= new List<SavedCharacter>();
		settings.CachedCards ??= new List<CachedCharacterCard>();
		settings.Presets ??= new List<CardEntryPreset>();
		settings.WeeklyContents ??= new WeeklyContentSettings();
		settings.CharacterImageMode = CharacterRow.NormalizeImageMode(settings.CharacterImageMode);
		settings.EnsurePresets();

		if (migrateLegacyApiKey)
			TryMigrateLegacyApiKey(settings);

		return settings;
	}

	internal static AppSettings DeserializeForStorage(string json, out bool migrateLegacyApiKey)
	{
		var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
		LoadApiKey(json, settings, out migrateLegacyApiKey);
		return settings;
	}

	private static void LoadApiKey(string json, AppSettings settings, out bool migrateLegacyApiKey)
	{
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		migrateLegacyApiKey = root.TryGetProperty(LegacyApiKeyJsonName, out var legacyElement);

		if (root.TryGetProperty(EncryptedApiKeyJsonName, out var encryptedElement) &&
			encryptedElement.ValueKind == JsonValueKind.String &&
			ApiKeyProtector.TryUnprotect(encryptedElement.GetString() ?? "", out var decryptedApiKey))
		{
			settings.ApiKey = decryptedApiKey;
			return;
		}

		if (migrateLegacyApiKey && legacyElement.ValueKind == JsonValueKind.String)
			settings.ApiKey = legacyElement.GetString() ?? "";
	}

	private static void TryMigrateLegacyApiKey(AppSettings settings)
	{
		try
		{
			settings.Save(sanitizeLegacyCopies: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
		{
			// Keep the in-memory key usable and retry migration on the next launch.
		}
	}

	public void EnsurePresets()
	{
		if (Presets.Count == 0)
		{
			Presets.Add(new CardEntryPreset
			{
				Id = Guid.NewGuid().ToString("N"),
				Name = "기본",
				Characters = Characters ?? new List<SavedCharacter>(),
				CachedCards = CachedCards ?? new List<CachedCharacterCard>(),
				CardMemos = new List<CardMemoEntry>()
			});
		}

		foreach (var preset in Presets)
		{
			if (string.IsNullOrWhiteSpace(preset.Id))
				preset.Id = Guid.NewGuid().ToString("N");
			if (string.IsNullOrWhiteSpace(preset.Name))
				preset.Name = "프리셋";
			preset.Characters ??= new List<SavedCharacter>();
			preset.CachedCards ??= new List<CachedCharacterCard>();
			preset.CardMemos ??= new List<CardMemoEntry>();
		}

		if (string.IsNullOrWhiteSpace(ActivePresetId) ||
			Presets.All(x => x.Id != ActivePresetId))
		{
			ActivePresetId = Presets[0].Id;
		}

		var activePreset = Presets.FirstOrDefault(x => x.Id == ActivePresetId) ?? Presets[0];
		Characters = activePreset.Characters;
		CachedCards = activePreset.CachedCards;
	}

	public void Save()
	{
		Save(sanitizeLegacyCopies: false);
	}

	private void Save(bool sanitizeLegacyCopies)
	{
		Directory.CreateDirectory(Dir);

		var json = SerializeForStorage();

		lock (SaveLock)
		{
			var tempPath = PathFile + ".tmp";
			var backupPath = PathFile + ".bak";

			File.WriteAllText(tempPath, json);
			if (File.Exists(PathFile))
				File.Replace(tempPath, PathFile, backupPath, ignoreMetadataErrors: true);
			else
				File.Move(tempPath, PathFile);

			if (sanitizeLegacyCopies)
			{
				try
				{
					File.Copy(PathFile, backupPath, overwrite: true);
				}
				catch
				{
					try
					{
						File.Delete(backupPath);
					}
					catch
					{
					}

					throw;
				}

				try
				{
					File.Delete(PathFile + ".broken");
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
	}

	internal string SerializeForStorage()
	{
		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};
		var root = JsonSerializer.SerializeToNode(this, options)?.AsObject() ?? new JsonObject();
		if (!string.IsNullOrEmpty(ApiKey))
			root[EncryptedApiKeyJsonName] = ApiKeyProtector.Protect(ApiKey);
		return root.ToJsonString(options);
	}
}

public class CardEntryPreset : INotifyPropertyChanged
{
	private string _name = "프리셋";
	private bool _isEditing;

	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public string Name
	{
		get => _name;
		set
		{
			var name = string.IsNullOrWhiteSpace(value) ? "프리셋" : value.Trim();
			if (_name == name)
				return;

			_name = name;
			OnPropertyChanged();
		}
	}
	public List<SavedCharacter> Characters { get; set; } = new();
	public List<CachedCharacterCard> CachedCards { get; set; } = new();
	public List<CardMemoEntry> CardMemos { get; set; } = new();

	[JsonIgnore]
	public bool IsEditing
	{
		get => _isEditing;
		set
		{
			if (_isEditing == value)
				return;

			_isEditing = value;
			OnPropertyChanged();
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

public class CardMemoEntry
{
	public string ServerId { get; set; } = "cain";
	public string CharacterName { get; set; } = "";
	public string Memo { get; set; } = "";
}
