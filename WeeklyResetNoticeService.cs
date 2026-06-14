using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace DNFWeeklyWidget;

public sealed partial class WeeklyResetNoticeService
{
	private static readonly Uri NoticeListUri = new("https://df.nexon.com/community/news/notice/list");
	private readonly HttpClient _httpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(15)
	};

	public WeeklyResetNoticeService()
	{
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 DNFWeeklyWidget");
	}

	public Task<DateTime?> GetUpcomingWeeklyResetAsync(DateTime expectedResetDate) =>
		GetMaintenanceStartAsync(expectedResetDate);

	public async Task<DateTime?> GetMaintenanceStartAsync(DateTime expectedResetDate)
	{
		try
		{
			for (var page = 1; page <= 3; page++)
			{
				var listUri = page == 1 ? NoticeListUri : new Uri($"{NoticeListUri}?page={page}");
				var listHtml = await _httpClient.GetStringAsync(listUri);
				foreach (var noticePath in FindRegularMaintenanceNoticePaths(listHtml))
				{
					var detailHtml = await _httpClient.GetStringAsync(new Uri(NoticeListUri, noticePath));
					var maintenanceStart = ParseMaintenanceStart(detailHtml);
					if (maintenanceStart?.Date == expectedResetDate.Date)
						return maintenanceStart;
					if (maintenanceStart is { } parsedStart && parsedStart.Date < expectedResetDate.Date)
						return null;
				}
			}

			return null;
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException)
		{
			return null;
		}
	}

	private static IEnumerable<string> FindRegularMaintenanceNoticePaths(string html)
	{
		foreach (Match match in NoticeLinkRegex().Matches(html))
		{
			var title = NormalizeHtmlText(match.Groups["title"].Value);
			if (title.Contains("정기점검", StringComparison.Ordinal))
				yield return WebUtility.HtmlDecode(match.Groups["path"].Value).Trim();
		}
	}

	private static DateTime? ParseMaintenanceStart(string html)
	{
		var dateMatch = MaintenanceDateRegex().Match(html);
		var timeMatch = MaintenanceTimeRegex().Match(html);
		if (!dateMatch.Success || !timeMatch.Success)
			return null;

		var dateText = NormalizeHtmlText(dateMatch.Groups["date"].Value);
		if (!DateTime.TryParseExact(
				dateText,
				"yyyy년 M월 d일",
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out var maintenanceDate))
		{
			return null;
		}

		if (!int.TryParse(timeMatch.Groups["hour"].Value, out var hour) ||
			!int.TryParse(timeMatch.Groups["minute"].Value, out var minute) ||
			hour is < 0 or > 23 ||
			minute is < 0 or > 59)
		{
			return null;
		}

		return maintenanceDate.Date.AddHours(hour).AddMinutes(minute);
	}

	private static string NormalizeHtmlText(string value)
	{
		var withoutTags = HtmlTagRegex().Replace(value, " ");
		return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
	}

	[GeneratedRegex("<a\\b[^>]*href\\s*=\\s*[\"']\\s*(?<path>/community/news/notice/\\d+(?:\\?[^\"']*)?)\\s*[\"'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex NoticeLinkRegex();

	[GeneratedRegex("<th\\b[^>]*>\\s*날짜\\s*</th>\\s*<td\\b[^>]*>(?<date>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex MaintenanceDateRegex();

	[GeneratedRegex("<th\\b[^>]*>\\s*시간\\s*</th>\\s*<td\\b[^>]*>\\s*(?<hour>\\d{1,2})\\s*:\\s*(?<minute>\\d{2})\\s*~", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex MaintenanceTimeRegex();

	[GeneratedRegex("<[^>]+>")]
	private static partial Regex HtmlTagRegex();

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespaceRegex();
}
