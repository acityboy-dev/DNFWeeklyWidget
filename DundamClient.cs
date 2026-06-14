using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DNFWeeklyWidget;

public class DundamClient
{
	private readonly HttpClient _http = new()
	{
		BaseAddress = new Uri("https://dundam.xyz")
	};

	public DundamClient()
	{
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 DNFWeeklyWidget");
		_http.DefaultRequestHeaders.Referrer = new Uri("https://dundam.xyz/");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
	}

	public async Task<List<SavedCharacter>> SearchAdventureCharactersAsync(string adventureName)
	{
		var encodedName = Uri.EscapeDataString(adventureName);
		var url = $"/dat/searchData.jsp?name={encodedName}&server=adven";

		using var body = new StringContent("{}", Encoding.UTF8, "application/json");
		using var res = await _http.PostAsync(url, body);
		var text = await res.Content.ReadAsStringAsync();

		if (!res.IsSuccessStatusCode)
			throw new Exception($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");

		if (string.IsNullOrWhiteSpace(text))
			return new List<SavedCharacter>();

		using var doc = JsonDocument.Parse(text);
		if (!doc.RootElement.TryGetProperty("characters", out var characters) ||
			characters.ValueKind != JsonValueKind.Array)
		{
			return new List<SavedCharacter>();
		}

		return characters.EnumerateArray()
			.Select(ParseCharacter)
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId) && !string.IsNullOrWhiteSpace(x.CharacterName))
			.GroupBy(x => $"{x.ServerId}:{x.CharacterName}", StringComparer.OrdinalIgnoreCase)
			.Select(x => x.First())
			.ToList();
	}

	private static SavedCharacter ParseCharacter(JsonElement character)
	{
		return new SavedCharacter
		{
			ServerId = GetString(character, "server") ?? "",
			CharacterName =
				GetString(character, "name")
				?? GetString(character, "characterName")
				?? GetString(character, "nickname")
				?? ""
		};
	}

	private static string? GetString(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property))
			return null;

		return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
	}
}
