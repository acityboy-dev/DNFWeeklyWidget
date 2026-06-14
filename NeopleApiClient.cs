using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace DNFWeeklyWidget;

public class NeopleApiClient
{
	private readonly HttpClient _http = new()
	{
		BaseAddress = new Uri("https://api.neople.co.kr")
	};

	public async Task<List<CharacterSearchItem>> SearchCharactersByCharacterNameAsync(
		string serverId,
		string characterName,
		string apiKey)
	{
		var url =
			$"/df/servers/{serverId}/characters" +
			$"?characterName={Uri.EscapeDataString(characterName)}" +
			"&wordType=match" +
			"&limit=100" +
			$"&apikey={Uri.EscapeDataString(apiKey)}";

		using var doc = await GetJsonAsync(url);

		if (!doc.RootElement.TryGetProperty("rows", out var rows))
			return new List<CharacterSearchItem>();

		return rows.EnumerateArray()
			.Select(x => new CharacterSearchItem
			{
				CharacterId = x.GetProperty("characterId").GetString() ?? "",
				CharacterName = x.GetProperty("characterName").GetString() ?? "",
				Level = x.TryGetProperty("level", out var lv) ? lv.GetInt32() : 0,
				JobGrowName = x.TryGetProperty("jobGrowName", out var jg) ? jg.GetString() ?? "" : ""
			})
			.Where(x => !string.IsNullOrWhiteSpace(x.CharacterId))
			.ToList();
	}

	public string GetCharacterImageUrl(string serverId, string characterId)
	{
		return $"https://img-api.neople.co.kr/df/servers/{serverId}/characters/{characterId}?zoom=1";
	}

	public async Task CacheCharacterImageAsync(string serverId, string characterId, string destinationPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
		var bytes = await _http.GetByteArrayAsync(new Uri(GetCharacterImageUrl(serverId, characterId)));
		await File.WriteAllBytesAsync(destinationPath, bytes);
	}

	public async Task<CharacterDetail> GetCharacterAsync(string serverId, string characterId, string apiKey)
	{
		var url = $"/df/servers/{serverId}/characters/{characterId}?apikey={Uri.EscapeDataString(apiKey)}";
		using var doc = await GetJsonAsync(url);
		var r = doc.RootElement;

		return new CharacterDetail
		{
			CharacterId = r.GetProperty("characterId").GetString() ?? "",
			CharacterName = r.GetProperty("characterName").GetString() ?? "",
			Level = r.TryGetProperty("level", out var lv) ? lv.GetInt32() : 0,
			JobName = r.TryGetProperty("jobName", out var job) ? job.GetString() ?? "" : "",
			JobGrowName = r.TryGetProperty("jobGrowName", out var jg) ? jg.GetString() ?? "" : "",
			Fame = r.TryGetProperty("fame", out var fame) ? fame.GetInt32() : 0
		};
	}

	public async Task<int> GetTodayTimelineCountAsync(string serverId, string characterId, string apiKey)
	{
		var today = DateTime.Today;
		var endOfDay = GetSafeTimelineEnd(today.AddDays(1).AddMinutes(-1));

		var url =
			$"/df/servers/{serverId}/characters/{characterId}/timeline" +
			$"?startDate={FormatTimelineDate(today)}" +
			$"&endDate={FormatTimelineDate(endOfDay)}" +
			"&limit=100" +
			$"&apikey={Uri.EscapeDataString(apiKey)}";

		using var doc = await GetJsonAsync(url);

		if (doc.RootElement.TryGetProperty("timeline", out var timeline) &&
			timeline.TryGetProperty("rows", out var rows))
		{
			return rows.GetArrayLength();
		}

		return 0;
	}

	public async Task<CharacterDailyStatus> GetWeeklyLootStatusAsync(string serverId, string characterId, string apiKey)
	{
		try
		{
			var weekStart = GetCurrentWeeklyResetStart();
			var weekEnd = GetSafeTimelineEnd(DateTime.Now);
			var timeline = await GetTimelineRowsAsync(
				serverId,
				characterId,
				apiKey,
				weekStart,
				weekEnd);

			return CharacterDailyStatus.FromTimeline(timeline);
		}
		catch (HttpRequestException)
		{
			return CharacterDailyStatus.Unavailable();
		}
	}

	public async Task<CharacterWeeklyStatus> GetWeeklyStatusAsync(
		string serverId,
		string characterId,
		string apiKey)
	{
		try
		{
			var weekStart = GetCurrentWeeklyResetStart();
			var weekEnd = GetSafeTimelineEnd(DateTime.Now);
			var timeline = await GetWeeklyClearTimelineRowsAsync(
				serverId,
				characterId,
				apiKey,
				weekStart,
				weekEnd);

			return CharacterWeeklyStatus.FromTimeline(timeline);
		}
		catch (HttpRequestException)
		{
			return CharacterWeeklyStatus.Unavailable();
		}
	}

	private async Task<List<TimelineRow>> GetTimelineRowsAsync(
		string serverId,
		string characterId,
		string apiKey,
		DateTime startDate,
		DateTime endDate,
		int? code = null)
	{
		var url =
			$"/df/servers/{serverId}/characters/{characterId}/timeline" +
			$"?startDate={FormatTimelineDate(startDate)}" +
			$"&endDate={FormatTimelineDate(endDate)}" +
			"&limit=100" +
			(code is null ? "" : $"&code={code.Value}") +
			$"&apikey={Uri.EscapeDataString(apiKey)}";

		using var doc = await GetJsonAsync(url);

		if (!doc.RootElement.TryGetProperty("timeline", out var timeline) ||
			!timeline.TryGetProperty("rows", out var rows))
		{
			return new List<TimelineRow>();
		}

		return rows.EnumerateArray()
			.Select(TimelineRow.FromJson)
			.ToList();
	}

	private async Task<List<TimelineRow>> GetWeeklyClearTimelineRowsAsync(
		string serverId,
		string characterId,
		string apiKey,
		DateTime startDate,
		DateTime endDate)
	{
		var rows = new List<TimelineRow>();

		try
		{
			foreach (var code in WeeklyContentDefinition.TimelineCodes)
			{
				rows.AddRange(await GetTimelineRowsAsync(
					serverId,
					characterId,
					apiKey,
					startDate,
					endDate,
					code));
			}
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
		{
			rows = await GetTimelineRowsAsync(
				serverId,
				characterId,
				apiKey,
				startDate,
				endDate);
		}

		return rows
			.Where(x => x.IsWeeklyClearCode())
			.GroupBy(x => x.UniqueKey, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.First())
			.ToList();
	}

	private async Task<JsonDocument> GetJsonAsync(string url)
	{
		var res = await _http.GetAsync(url);
		var text = await res.Content.ReadAsStringAsync();

		if (!res.IsSuccessStatusCode)
			throw new HttpRequestException($"API 실패: {(int)res.StatusCode} {res.ReasonPhrase}", null, res.StatusCode);

		return JsonDocument.Parse(text);
	}

	private static string FormatTimelineDate(DateTime date)
	{
		return Uri.EscapeDataString(date.ToString("yyyy-MM-dd HH:mm"));
	}

	private static DateTime GetSafeTimelineEnd(DateTime requestedEnd)
	{
		var now = DateTime.Now;
		var safeEnd = requestedEnd <= now ? requestedEnd : now;
		return new DateTime(
			safeEnd.Year,
			safeEnd.Month,
			safeEnd.Day,
			safeEnd.Hour,
			safeEnd.Minute,
			0);
	}

	private static DateTime GetCurrentWeeklyResetStart()
	{
		var now = DateTime.Now;
		var todayReset = now.Date.AddHours(6);
		var daysSinceThursday = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Thursday) % 7;
		var weekStart = todayReset.AddDays(-daysSinceThursday);

		if (now < weekStart)
			weekStart = weekStart.AddDays(-7);

		return weekStart;
	}
}
