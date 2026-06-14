using System.IO;
using System.Net.Http;

namespace DNFWeeklyWidget;

internal sealed class CharacterCardService
{
	private readonly AppSettings _settings;
	private readonly NeopleApiClient _api;

	public CharacterCardService(AppSettings settings, NeopleApiClient? api = null)
	{
		_settings = settings;
		_api = api ?? new NeopleApiClient();
	}

	public async Task<List<CharacterRow>> CreateRowsAsync(
		IEnumerable<SavedCharacter> characters,
		double cardWidth)
	{
		var rows = new List<CharacterRow>();
		foreach (var character in characters)
			rows.Add(await CreateRowAsync(character, cardWidth));

		return rows;
	}

	public async Task<CharacterRow> CreateRowAsync(SavedCharacter saved, double cardWidth)
	{
		var serverId = string.IsNullOrWhiteSpace(saved.ServerId) ? _settings.ServerId : saved.ServerId;
		var found = await _api.SearchCharactersByCharacterNameAsync(
			serverId,
			saved.CharacterName,
			_settings.ApiKey);

		var character = found.FirstOrDefault();
		if (character is null)
			return CharacterRow.NotFound(serverId, saved.CharacterName, cardWidth, _settings.CharacterImageMode);

		var detail = await _api.GetCharacterAsync(serverId, character.CharacterId, _settings.ApiKey);
		var weeklyLootStatus = await _api.GetWeeklyLootStatusAsync(serverId, character.CharacterId, _settings.ApiKey);
		var weeklyStatus = await _api.GetWeeklyStatusAsync(
			serverId,
			character.CharacterId,
			_settings.ApiKey);
		var imageMode = CharacterRow.NormalizeImageMode(_settings.CharacterImageMode);
		var imageSource = imageMode == "hidden"
			? GetExistingImageSource(serverId, character.CharacterId)
			: await GetCachedImageSourceAsync(serverId, character.CharacterId);

		return new CharacterRow
		{
			CharacterName = character.CharacterName,
			ServerId = serverId,
			ServerName = ServerOptions.GetName(serverId),
			CardWidth = cardWidth,
			ImageMode = imageMode,
			ImageUrl = imageSource,
			JobName = detail.JobName,
			Fame = detail.Fame,
			JobSummary = detail.JobGrowName,
			CompactImageMargin = CharacterRow.GetCompactImageMargin(detail.JobName, detail.JobGrowName),
			FameSummary = $"명성 {detail.Fame:N0}",
			BaseSummary = weeklyLootStatus.ToSummaryText(),
			WeeklyStatus = weeklyStatus
		};
	}

	private async Task<string> GetCachedImageSourceAsync(string serverId, string characterId)
	{
		var imageUrl = _api.GetCharacterImageUrl(serverId, characterId);
		if (!_settings.EnableUserDataCache)
			return imageUrl;

		var imagePath = GetCachedImagePath(serverId, characterId);
		var fallback = File.Exists(imagePath) ? imagePath : imageUrl;

		try
		{
			await _api.CacheCharacterImageAsync(serverId, characterId, imagePath);
			return imagePath;
		}
		catch (HttpRequestException)
		{
			return fallback;
		}
		catch (IOException)
		{
			return fallback;
		}
		catch (UnauthorizedAccessException)
		{
			return fallback;
		}
		catch (TaskCanceledException)
		{
			return fallback;
		}
	}

	private string GetExistingImageSource(string serverId, string characterId)
	{
		var imagePath = GetCachedImagePath(serverId, characterId);
		return File.Exists(imagePath)
			? imagePath
			: _api.GetCharacterImageUrl(serverId, characterId);
	}

	private static string GetCachedImagePath(string serverId, string characterId)
	{
		var fileName = $"{SanitizeCacheFileName(serverId)}_{SanitizeCacheFileName(characterId)}.png";
		return Path.Combine(AppSettings.ImageCacheDir, fileName);
	}

	private static string SanitizeCacheFileName(string value)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		var chars = value
			.Select(character => invalidChars.Contains(character) ? '_' : character)
			.ToArray();

		return new string(chars);
	}
}
