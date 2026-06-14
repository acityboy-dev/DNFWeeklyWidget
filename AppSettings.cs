using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DNFWeeklyWidget;

public class AppSettings
{
	public string ApiKey { get; set; } = "";
	public string ServerId { get; set; } = "cain";
	public string ThemeMode { get; set; } = "system";
	public string CharacterImageMode { get; set; } = "full";
	public bool IsCompactMode { get; set; }
	public bool FilterIncompleteOnly { get; set; }
	public bool AutoSortByFame { get; set; }
	public bool AutoRefreshOnStartup { get; set; } = true;
	public int AutoRefreshIntervalMinutes { get; set; } = 30;
	public bool EnableUserDataCache { get; set; } = true;
	public int Columns { get; set; } = 2;
	public double? WindowLeft { get; set; }
	public double? WindowTop { get; set; }
	public double? WindowWidth { get; set; }
	public double? WindowHeight { get; set; }
	public List<SavedCharacter> Characters { get; set; } = new();
	public List<CachedCharacterCard> CachedCards { get; set; } = new();
	public WeeklyContentSettings WeeklyContents { get; set; } = new();

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
		try
		{
			var json = File.ReadAllText(PathFile);
			settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
		}
		catch (JsonException)
		{
			File.Copy(PathFile, PathFile + ".broken", true);
			settings = new AppSettings();
		}

		settings.Characters ??= new List<SavedCharacter>();
		settings.CachedCards ??= new List<CachedCharacterCard>();
		settings.WeeklyContents ??= new WeeklyContentSettings();
		settings.CharacterImageMode = CharacterRow.NormalizeImageMode(settings.CharacterImageMode);
		return settings;
	}

	public void Save()
	{
		Directory.CreateDirectory(Dir);

		var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});

		File.WriteAllText(PathFile, json);
	}
}
