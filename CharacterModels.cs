using System.Text.Json;
using System.ComponentModel;
using System.Windows;

namespace DNFWeeklyWidget;

public class CharacterRow : INotifyPropertyChanged
{
    private const double WideLayoutThreshold = 320;
    private const double DetailedLootLayoutThreshold = 340;

    private double _cardWidth = 150;
    private string _jobSummary = "";
    private string _fameSummary = "";
    private string _metaSummary = "";
    private string _summary = "";
    private string _imageUrl = "";
    private string _imageMode = "full";
    private Thickness _compactImageMargin = new(-85, -78, 0, 0);
    private IReadOnlyList<SummaryLine> _summaryLines = [];
    private bool _isDragging;

    public bool IsDropIndicator { get; set; }
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value)
                return;

            _isDragging = value;
            OnPropertyChanged(nameof(IsDragging));
        }
    }
    public string ServerId { get; set; } = "cain";
    public string ServerName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string JobName { get; set; } = "";
    public int Fame { get; set; }
    public string BaseSummary { get; set; } = "";
    public CharacterWeeklyStatus? WeeklyStatus { get; set; }
    public string JobSummary
    {
        get => _jobSummary;
        set
        {
            var normalized = NormalizeJobSummary(value);
            if (_jobSummary == normalized)
                return;

            _jobSummary = normalized;
            UpdateMetaSummary();
            OnPropertyChanged(nameof(JobSummary));
        }
    }
    public string FameSummary
    {
        get => _fameSummary;
        set
        {
            if (_fameSummary == value)
                return;

            _fameSummary = value;
            UpdateMetaSummary();
            OnPropertyChanged(nameof(FameSummary));
        }
    }
    public string MetaSummary
    {
        get => _metaSummary;
        set
        {
            if (_metaSummary == value)
                return;

            _metaSummary = value;
            OnPropertyChanged(nameof(MetaSummary));
        }
    }
    public string Summary
    {
        get => _summary;
        set
        {
            if (_summary == value)
                return;

            _summary = value;
            _summaryLines = BuildPlainSummaryLines(value);
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(SummaryParagraphs));
            OnPropertyChanged(nameof(SummaryLines));
        }
    }
    public bool IsWideLayout => CardWidth >= WideLayoutThreshold;
    public bool IsDetailedLootLayout => CardWidth >= DetailedLootLayoutThreshold;
    public bool IsCompactLayout => !IsWideLayout;
    public IReadOnlyList<SummaryLine> SummaryLines => _summaryLines;
    public IReadOnlyList<SummaryLine> SummaryParagraphs => SummaryLines;
    public string ImageUrl
    {
        get => _imageUrl;
        set
        {
            if (_imageUrl == value)
                return;

            _imageUrl = value;
            OnPropertyChanged(nameof(ImageUrl));
            OnPropertyChanged(nameof(HasCharacterImage));
            OnPropertyChanged(nameof(ShowFullCharacterImage));
            OnPropertyChanged(nameof(ShowCompactCharacterImage));
            OnPropertyChanged(nameof(ShowFullImagePlaceholder));
            OnPropertyChanged(nameof(ShowCompactImagePlaceholder));
        }
    }
    public string ImageMode
    {
        get => _imageMode;
        set
        {
            var normalized = NormalizeImageMode(value);
            if (_imageMode == normalized)
                return;

            _imageMode = normalized;
            OnPropertyChanged(nameof(ImageMode));
            OnPropertyChanged(nameof(ShowFullImage));
            OnPropertyChanged(nameof(ShowCompactImage));
            OnPropertyChanged(nameof(ShowStandardHeader));
            OnPropertyChanged(nameof(ShowFullCharacterImage));
            OnPropertyChanged(nameof(ShowCompactCharacterImage));
            OnPropertyChanged(nameof(ShowFullImagePlaceholder));
            OnPropertyChanged(nameof(ShowCompactImagePlaceholder));
        }
    }
    public bool HasCharacterImage => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool ShowFullImage => ImageMode == "full";
    public bool ShowCompactImage => ImageMode == "compact";
    public bool ShowStandardHeader => ImageMode != "compact";
    public bool ShowFullCharacterImage => ShowFullImage && HasCharacterImage;
    public bool ShowCompactCharacterImage => ShowCompactImage && HasCharacterImage;
    public bool ShowFullImagePlaceholder => ShowFullImage && !HasCharacterImage;
    public bool ShowCompactImagePlaceholder => ShowCompactImage && !HasCharacterImage;
    public Thickness CompactImageMargin
    {
        get => _compactImageMargin;
        set
        {
            if (_compactImageMargin == value)
                return;

            _compactImageMargin = value;
            OnPropertyChanged(nameof(CompactImageMargin));
        }
    }

    public double CardWidth
    {
        get => _cardWidth;
        set
        {
            if (Math.Abs(_cardWidth - value) < 0.5)
                return;

            _cardWidth = value;
            OnPropertyChanged(nameof(CardWidth));
            OnPropertyChanged(nameof(IsWideLayout));
            OnPropertyChanged(nameof(IsDetailedLootLayout));
            OnPropertyChanged(nameof(IsCompactLayout));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyWeeklyContentSettings(WeeklyContentSettings settings, WeeklyDisplayContext? context = null)
    {
        if (WeeklyStatus is null)
            return;

        var lines = BuildPlainSummaryLines(BaseSummary)
            .Where(line => IsWeeklyLootLineVisible(line.Text, settings))
            .ToList();
        if (lines.Count > 0)
            lines.Add(SummaryLine.Separator());
        lines.AddRange(WeeklyStatus.ToSummaryLines(settings, context ?? WeeklyDisplayContext.Empty, Fame));

        Summary = string.Join("\n", lines.Where(x => !x.IsSeparator).Select(x => x.Text));
        _summaryLines = lines;
        OnPropertyChanged(nameof(SummaryLines));
        OnPropertyChanged(nameof(SummaryParagraphs));
    }

    private static bool IsWeeklyLootLineVisible(string line, WeeklyContentSettings settings)
    {
        var separatorIndex = line.IndexOf(':');
        var title = separatorIndex >= 0
            ? line[..separatorIndex].Trim()
            : line.Trim();

        return title switch
        {
            "주간 중천장비" => settings.ShowWeeklyEquipmentLoot,
            "주간 서약" => settings.ShowWeeklyOathLoot,
            "주간 결정" => settings.ShowWeeklyCrystalLoot,
            "주간 획득" => settings.ShowAnyWeeklyLoot,
            _ => true
        };
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateMetaSummary()
    {
        MetaSummary = string.Join("  ", new[] { JobSummary, FameSummary }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string NormalizeJobSummary(string value)
    {
        var normalized = value.Trim();
        while (normalized.StartsWith("\u771e", StringComparison.Ordinal))
            normalized = normalized[1..].TrimStart();

        return normalized;
    }

    public static string NormalizeImageMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "hidden" => "hidden",
            "compact" => "compact",
            _ => "full"
        };
    }

    public static Thickness GetCompactImageMargin(string jobName, string jobGrowName)
    {
        var key = GetDfGearSmallIconKey(jobName, jobGrowName);
        var (x, y) = key switch
        {
            "gh-m" => (-88, -78),
            "gh-f" => (-85, -90),
            "gn-m" => (-85, -60),
            "gn-f" => (-85, -78),
            "mg-m" => (-85, -96),
            "mg-f" => (-82, -110),
            "pr-m" => (-85, -68),
            "pr-f" => (-85, -85),
            "fi-m" => (-84, -78),
            "fi-f" => (-86, -90),
            "th" => (-84, -79),
            "kng" => (-82, -100),
            "mc" => (-80, -77),
            "gs" => (-85, -74),
            "ac" => (-78, -100),
            "ek" => (-85, -90),
            _ => (-85, -78)
        };

        return new Thickness(x, y, 0, 0);
    }

    private static string GetDfGearSmallIconKey(string jobName, string jobGrowName)
    {
        _ = jobGrowName;
        return jobName.Trim() switch
        {
            "\uadc0\uac80\uc0ac(\ub0a8)" or "\ub2e4\ud06c\ub098\uc774\ud2b8" => "gh-m",
            "\uadc0\uac80\uc0ac(\uc5ec)" => "gh-f",
            "\uac70\ub108(\ub0a8)" => "gn-m",
            "\uac70\ub108(\uc5ec)" => "gn-f",
            "\ub9c8\ubc95\uc0ac(\ub0a8)" => "mg-m",
            "\ub9c8\ubc95\uc0ac(\uc5ec)" or "\ud06c\ub9ac\uc5d0\uc774\ud130" => "mg-f",
            "\ud504\ub9ac\uc2a4\ud2b8(\ub0a8)" => "pr-m",
            "\ud504\ub9ac\uc2a4\ud2b8(\uc5ec)" => "pr-f",
            "\uaca9\ud22c\uac00(\ub0a8)" => "fi-m",
            "\uaca9\ud22c\uac00(\uc5ec)" => "fi-f",
            "\ub3c4\uc801" => "th",
            "\ub098\uc774\ud2b8" => "kng",
            "\ub9c8\ucc3d\uc0ac" => "mc",
            "\ucd1d\uac80\uc0ac" => "gs",
            "\uc544\ucc98" => "ac",
            "\uc81c\uad6d\uae30\uc0ac" => "ek",
            _ => ""
        };
    }

    private static IReadOnlyList<SummaryLine> BuildPlainSummaryLines(string text)
    {
        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new SummaryLine
            {
                Text = line,
                Body = line,
                DetailedBody = BuildDetailedLootSummaryLine(line)
            })
            .Select(ApplyLootLineParts)
            .ToList();
    }

    private static SummaryLine ApplyLootLineParts(SummaryLine line)
    {
        var separatorIndex = line.Text.IndexOf(':');
        if (separatorIndex < 0)
            return line;

        var title = line.Text[..separatorIndex].Trim();
        if (title is not ("주간 중천장비" or "주간 서약" or "주간 결정"))
            return line;

        var values = line.Text[(separatorIndex + 1)..]
            .Trim()
            .Split('/', StringSplitOptions.TrimEntries);
        if (values.Length != 2 ||
            !int.TryParse(values[0], out var primevalCount) ||
            !int.TryParse(values[1], out var epicCount))
        {
            return line;
        }

        line.IsWeeklyLoot = true;
        line.LootTitle = title;
        line.PrimevalCount = primevalCount;
        line.EpicCount = epicCount;
        return line;
    }

    private static string BuildDetailedLootSummaryLine(string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0)
            return line;

        var title = line[..separatorIndex].Trim();
        if (title is not ("주간 중천장비" or "주간 서약" or "주간 결정"))
            return line;

        var values = line[(separatorIndex + 1)..]
            .Trim()
            .Split('/', StringSplitOptions.TrimEntries);
        if (values.Length != 2 ||
            !int.TryParse(values[0], out var primevalCount) ||
            !int.TryParse(values[1], out var epicCount))
        {
            return line;
        }

        return $"{title}: 태초 {primevalCount}/에픽 {epicCount}";
    }

    private static void SplitMetaSummary(string metaSummary, ref string jobSummary, ref string fameSummary)
    {
        if (!string.IsNullOrWhiteSpace(jobSummary) || !string.IsNullOrWhiteSpace(fameSummary))
            return;

        var normalized = metaSummary
            .Replace("Lv.", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        var fameIndex = normalized.IndexOf("명성", StringComparison.OrdinalIgnoreCase);
        if (fameIndex < 0)
        {
            jobSummary = normalized;
            return;
        }

        jobSummary = normalized[..fameIndex].Trim();
        fameSummary = normalized[fameIndex..].Trim();
    }

    private static int ParseFame(string fameSummary)
    {
        var digits = new string(fameSummary.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var fame) ? fame : 0;
    }

    public static CharacterRow DropIndicator(double cardWidth)
    {
        return new CharacterRow
        {
            IsDropIndicator = true,
            CardWidth = cardWidth
        };
    }

    public static CharacterRow NotFound(string serverId, string characterName, double cardWidth, string? imageMode = null)
    {
        return new CharacterRow
        {
            ServerId = serverId,
            ServerName = ServerOptions.GetName(serverId),
            CharacterName = characterName,
            Summary = "캐릭터를 찾지 못했습니다.",
            ImageMode = NormalizeImageMode(imageMode),
            CardWidth = cardWidth
        };
    }

    public static CharacterRow FromCache(CachedCharacterCard cache, double cardWidth)
    {
        var metaSummary = cache.MetaSummary;
        var jobSummary = cache.JobSummary;
        var fameSummary = cache.FameSummary;
        var summary = cache.Summary;
        if (string.IsNullOrWhiteSpace(metaSummary) && !string.IsNullOrWhiteSpace(summary))
        {
            var lines = summary.Split('\n');
            if (lines.Length >= 4 &&
                lines[0].StartsWith("Lv.", StringComparison.OrdinalIgnoreCase) &&
                lines[2].StartsWith("명성 ", StringComparison.OrdinalIgnoreCase))
            {
                jobSummary = lines[1];
                fameSummary = lines[2];
                metaSummary = $"{jobSummary}  {fameSummary}";
                summary = string.Join("\n", lines.Skip(3));
            }
            else
            {
                metaSummary = lines[0];
                summary = lines.Length > 1 ? string.Join("\n", lines.Skip(1)) : "";
            }
        }
        SplitMetaSummary(metaSummary, ref jobSummary, ref fameSummary);

        return new CharacterRow
        {
            ServerId = cache.ServerId,
            ServerName = string.IsNullOrWhiteSpace(cache.ServerName)
                ? ServerOptions.GetName(cache.ServerId)
                : cache.ServerName,
            CharacterName = cache.CharacterName,
            JobName = cache.JobName,
            Fame = cache.Fame > 0 ? cache.Fame : ParseFame(fameSummary),
            JobSummary = jobSummary,
            FameSummary = fameSummary,
            BaseSummary = summary,
            Summary = summary,
            ImageUrl = cache.ImagePath,
            CompactImageMargin = GetCompactImageMargin(cache.JobName, jobSummary),
            CardWidth = cardWidth
        };
    }

    public CachedCharacterCard ToCache()
    {
        return new CachedCharacterCard
        {
            ServerId = ServerId,
            ServerName = ServerName,
            CharacterName = CharacterName,
            JobName = JobName,
            Fame = Fame,
            JobSummary = JobSummary,
            FameSummary = FameSummary,
            MetaSummary = MetaSummary,
            Summary = Summary,
            ImagePath = ImageUrl,
            CompactImageOffsetX = CompactImageMargin.Left,
            CompactImageOffsetY = CompactImageMargin.Top
        };
    }
}

public class SavedCharacter
{
	public string ServerId { get; set; } = "cain";
	public string CharacterName { get; set; } = "";
}

public class SummaryLine
{
	public string Text { get; set; } = "";
	public string Marker { get; set; } = "";
	public string Body { get; set; } = "";
	public string DetailedBody { get; set; } = "";
	public bool IsWeeklyLoot { get; set; }
	public string LootTitle { get; set; } = "";
	public int PrimevalCount { get; set; }
	public int EpicCount { get; set; }
	public bool IsCleared { get; set; }
	public bool IsLimitedOut { get; set; }
	public bool IsFameLocked { get; set; }
	public bool IsSeparator { get; set; }

	public static SummaryLine Separator()
	{
		return new SummaryLine { IsSeparator = true };
	}
}

public class WeeklyDisplayContext
{
	public static WeeklyDisplayContext Empty { get; } = new();

	public int TwilightOfInaeClearedCount { get; set; }
	public int HardNabelClearedCount { get; set; }
}

public class IncompleteContentGroup
{
	public string ContentName { get; set; } = "";
	public List<string> Characters { get; set; } = new();
	public string TitleOverride { get; set; } = "";
	public int Count => Characters.Count;
	public string Title => string.IsNullOrWhiteSpace(TitleOverride)
		? $"{ContentName} 미완료 {Count}명"
		: TitleOverride;
	public string CharacterSummary => string.Join(", ", Characters);
	public bool HasCharacterSummary => Characters.Count > 0;
}

public class WeeklyLootSummaryGroup
{
	public string Title { get; set; } = "";
	public int PrimevalCount { get; set; }
	public int EpicCount { get; set; }
	public string Summary => $"태초 {PrimevalCount}/에픽 {EpicCount}";
}

public class CachedCharacterCard
{
	public string ServerId { get; set; } = "cain";
	public string ServerName { get; set; } = "";
	public string CharacterName { get; set; } = "";
	public string JobName { get; set; } = "";
	public int Fame { get; set; }
	public string JobSummary { get; set; } = "";
	public string FameSummary { get; set; } = "";
	public string MetaSummary { get; set; } = "";
	public string Summary { get; set; } = "";
	public string ImagePath { get; set; } = "";
	public double? CompactImageOffsetX { get; set; }
	public double? CompactImageOffsetY { get; set; }
}

public class CharacterSearchItem
{
	public string CharacterId { get; set; } = "";
	public string CharacterName { get; set; } = "";
	public int Level { get; set; }
	public string JobGrowName { get; set; } = "";
}

public class CharacterDetail
{
	public string CharacterId { get; set; } = "";
	public string CharacterName { get; set; } = "";
	public int Level { get; set; }
	public string JobName { get; set; } = "";
	public string JobGrowName { get; set; } = "";
	public int Fame { get; set; }
}

public class CharacterDailyStatus
{
	public bool IsAvailable { get; set; } = true;
	public int EpicEquipmentCount { get; set; }
	public int EpicOathCount { get; set; }
	public int EpicCrystalCount { get; set; }
	public int PrimevalEquipmentCount { get; set; }
	public int PrimevalOathCount { get; set; }
	public int PrimevalCrystalCount { get; set; }

	public static CharacterDailyStatus FromTimeline(IEnumerable<TimelineRow> rows)
	{
		var status = new CharacterDailyStatus();

		foreach (var row in rows)
		{
			var rarity = GetLootRarity(row);
			if (rarity != "에픽" && rarity != "태초")
				continue;

			var category = GetLootCategory(row);
			if (rarity == "에픽")
				status.AddEpic(category);
			else
				status.AddPrimeval(category);
		}

		return status;
	}

	public static CharacterDailyStatus Unavailable()
	{
		return new CharacterDailyStatus
		{
			IsAvailable = false
		};
	}

	public string ToSummaryText()
	{
		if (!IsAvailable)
			return "주간 획득: 타임라인 조회 실패";

		return
			$"주간 중천장비: {PrimevalEquipmentCount}/{EpicEquipmentCount}\n" +
			$"주간 서약: {PrimevalOathCount}/{EpicOathCount}\n" +
			$"주간 결정: {PrimevalCrystalCount}/{EpicCrystalCount}";
	}

	private void AddEpic(LootCategory category)
	{
		if (category == LootCategory.Oath)
			EpicOathCount++;
		else if (category == LootCategory.Crystal)
			EpicCrystalCount++;
		else
			EpicEquipmentCount++;
	}

	private void AddPrimeval(LootCategory category)
	{
		if (category == LootCategory.Oath)
			PrimevalOathCount++;
		else if (category == LootCategory.Crystal)
			PrimevalCrystalCount++;
		else
			PrimevalEquipmentCount++;
	}

	private static LootCategory GetLootCategory(TimelineRow row)
	{
		var itemName = row.ItemName.Trim();

		if (itemName.EndsWith(" 서약", StringComparison.OrdinalIgnoreCase))
			return LootCategory.Oath;

		if (itemName.EndsWith(" 결정", StringComparison.OrdinalIgnoreCase))
			return LootCategory.Crystal;

		return LootCategory.Equipment;
	}

	private static string GetLootRarity(TimelineRow row)
	{
		var itemName = row.ItemName.Trim();

		if (itemName.EndsWith("태초의 광휘 결정", StringComparison.OrdinalIgnoreCase))
			return "태초";

		if (itemName.EndsWith("완전한 광휘 결정", StringComparison.OrdinalIgnoreCase))
			return "에픽";

		return row.ItemRarity;
	}
}

public class CharacterWeeklyStatus
{
	public bool IsAvailable { get; set; } = true;
	public List<WeeklyContentResult> Contents { get; set; } = new();

    public static CharacterWeeklyStatus FromTimeline(IEnumerable<TimelineRow> rows)
    {
        var timelineRows = rows.ToList();
        return new CharacterWeeklyStatus
        {
            Contents = WeeklyContentDefinition.All
                .Select(content => new WeeklyContentResult
                {
                    Id = content.Id,
                    Name = content.Name,
                    IsCleared = timelineRows.Any(content.IsClearedBy)
                })
                .ToList()
        };
    }

    public static CharacterWeeklyStatus Unavailable()
    {
        return new CharacterWeeklyStatus
        {
            IsAvailable = false,
            Contents = WeeklyContentDefinition.All
                .Select(content => new WeeklyContentResult
                {
                    Id = content.Id,
                    Name = content.Name,
                    IsCleared = null
                })
                .ToList()
        };
    }

    public IReadOnlyList<SummaryLine> ToSummaryLines(WeeklyContentSettings settings, WeeklyDisplayContext context, int fame)
    {
        var hardNabelCleared = Contents.Any(content =>
            string.Equals(content.Id, WeeklyContentDefinition.HardNabelId, StringComparison.OrdinalIgnoreCase) &&
            content.IsCleared == true);
        var apocalypseCleared = Contents.Any(content =>
            string.Equals(content.Id, WeeklyContentDefinition.ApocalypseId, StringComparison.OrdinalIgnoreCase) &&
            content.IsCleared == true);

        var enabledIds = WeeklyContentDefinition.GetEnabled(settings)
            .Select(content => content.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var visibleContents = Contents
            .Where(content => enabledIds.Contains(content.Id))
            .ToList();

        if (visibleContents.Count == 0)
            return [new SummaryLine
            {
                Text = "표시할 주간 콘텐츠 없음",
                Body = "표시할 주간 콘텐츠 없음",
                DetailedBody = "표시할 주간 콘텐츠 없음"
            }];

        return visibleContents
            .Select(x =>
            {
                var definition = WeeklyContentDefinition.FindById(x.Id);
                var isFameLocked = x.IsCleared != true && definition?.IsFameLocked(fame) == true;
                var statusText = isFameLocked ? "진행불가 명성" : x.StatusText;
                var statusMark = isFameLocked ? "!" : x.StatusMark;
                var body = $"{x.Name} {statusText}";

                return new SummaryLine
                {
                    Text = $"{statusMark} {x.Name} {statusText}",
                    Marker = statusMark,
                    Body = body,
                    DetailedBody = body,
                    IsCleared = x.IsCleared == true,
                    IsLimitedOut = x.IsLimitedOut(settings, context, hardNabelCleared, apocalypseCleared),
                    IsFameLocked = isFameLocked
                };
            })
            .ToList();
    }
}

public class WeeklyContentResult
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public bool? IsCleared { get; set; }
	public string StatusMark => IsCleared switch
	{
		true => "✓",
		false => "·",
		_ => "?"
	};
	public string StatusText => IsCleared switch
	{
		true => "완료",
		false => "미완료",
		_ => "확인 실패"
	};

	public bool IsLimitedOut(
		WeeklyContentSettings settings,
		WeeklyDisplayContext context,
		bool hardNabelCleared,
		bool apocalypseCleared)
	{
		return IsCleared == false &&
			((Id == WeeklyContentDefinition.VenusId &&
			  apocalypseCleared) ||
			 (Id == WeeklyContentDefinition.TwilightOfInaeId &&
				context.TwilightOfInaeClearedCount >= 8) ||
			 (Id == WeeklyContentDefinition.NormalNabelId &&
			  hardNabelCleared) ||
			 (Id == WeeklyContentDefinition.HardNabelId &&
			  context.HardNabelClearedCount >= 4));
	}
}

public class TimelineRow
{
	public int Code { get; set; }
	public string CodeName { get; set; } = "";
	public string Date { get; set; } = "";
	public string DungeonName { get; set; } = "";
	public string RaidName { get; set; } = "";
	public string ModeName { get; set; } = "";
	public string PhaseName { get; set; } = "";
	public string ItemName { get; set; } = "";
	public string ItemRarity { get; set; } = "";
	public string ItemType { get; set; } = "";
	public string ItemTypeDetail { get; set; } = "";
	public bool IsHard { get; set; }
	public string SearchText { get; set; } = "";
	public string UniqueKey => $"{Code}:{Date}:{SearchText}";

	public static TimelineRow FromJson(JsonElement row)
	{
		var data = row.TryGetProperty("data", out var dataElement)
			? dataElement
			: row;

		return new TimelineRow
		{
			Code = GetInt(row, "code"),
			CodeName = GetString(row, "name"),
			Date = GetString(row, "date"),
			DungeonName = GetString(data, "dungeonName"),
			RaidName = GetString(data, "raidName"),
			ModeName = GetString(data, "modeName"),
			PhaseName = GetString(data, "phaseName"),
			ItemName = GetString(data, "itemName"),
			ItemRarity = GetString(data, "itemRarity"),
			ItemType = GetString(data, "itemType"),
			ItemTypeDetail = GetString(data, "itemTypeDetail"),
			IsHard = GetBool(data, "hard"),
			SearchText = BuildSearchText(row, data)
		};
	}

	public bool ContainsAny(params string[] keywords)
	{
		return keywords.Any(keyword => SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
	}

	public bool IsWeeklyClearCode()
	{
		return WeeklyContentDefinition.TimelineCodes.Contains(Code);
	}

	public bool IsNabelRaid()
	{
		return ContainsAny("나벨", "nabel");
	}

	public bool IsHardMistRaid()
	{
		return RaidName.Contains("아스라한", StringComparison.OrdinalIgnoreCase) &&
			ModeName.Contains("안개의 신, 무", StringComparison.OrdinalIgnoreCase) &&
			IsHard;
	}

	private static int GetInt(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property))
			return 0;

		if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
			return value;

		if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
			return value;

		return 0;
	}

	private static string GetString(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property))
			return "";

		return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
	}

	private static bool GetBool(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property))
			return false;

		if (property.ValueKind == JsonValueKind.True)
			return true;

		if (property.ValueKind == JsonValueKind.False)
			return false;

		return property.ValueKind == JsonValueKind.String &&
			bool.TryParse(property.GetString(), out var value) &&
			value;
	}

	private static string BuildSearchText(JsonElement row, JsonElement data)
	{
		var values = new List<string>
		{
			GetString(row, "name"),
			GetString(row, "date"),
			GetString(data, "dungeonName"),
			GetString(data, "raidName"),
			GetString(data, "modeName"),
			GetString(data, "phaseName"),
			GetString(data, "raidPartyName"),
			GetString(data, "channelName"),
			GetString(data, "monsterName"),
			GetString(data, "itemName"),
			GetString(data, "itemType"),
			GetString(data, "itemTypeDetail"),
			FlattenStrings(data)
		};

		return string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x)));
	}

	private static string FlattenStrings(JsonElement element)
	{
		var values = new List<string>();
		CollectStrings(element, values);
		return string.Join(" ", values);
	}

	private static void CollectStrings(JsonElement element, List<string> values)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
					CollectStrings(property.Value, values);
				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
					CollectStrings(item, values);
				break;
			case JsonValueKind.String:
				var value = element.GetString();
				if (!string.IsNullOrWhiteSpace(value))
					values.Add(value);
				break;
			case JsonValueKind.Number:
			case JsonValueKind.True:
			case JsonValueKind.False:
				values.Add(element.ToString());
				break;
		}
	}
}

public enum LootCategory
{
	Equipment,
	Oath,
	Crystal
}

public class WeeklyContentSettings
{
	public bool ShowWeeklyEquipmentLoot { get; set; } = true;
	public bool ShowWeeklyOathLoot { get; set; } = true;
	public bool ShowWeeklyCrystalLoot { get; set; } = true;
	public bool ShowVenus { get; set; } = false;
	public bool ShowApocalypse { get; set; } = true;
	public bool ShowBakalRaid { get; set; } = true;
	public bool ShowNabelRaid { get; set; } = true;
	public bool ShowNormalNabelRaid { get; set; } = true;
	public bool ShowHardNabelRaid { get; set; } = true;
	public bool ShowTwilightOfInae { get; set; } = true;
	public bool ShowDiregieRaid { get; set; } = true;
	public bool ShowHardMistRaid { get; set; } = false;

	public bool ShowAnyWeeklyLoot =>
		ShowWeeklyEquipmentLoot ||
		ShowWeeklyOathLoot ||
		ShowWeeklyCrystalLoot;
}

public class WeeklyContentDefinition
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public int RequiredFame { get; init; }
	public Func<WeeklyContentSettings, bool> IsEnabled { get; init; } = _ => true;
	public Func<TimelineRow, bool> IsClearedBy { get; init; } = _ => false;

	public const string VenusId = "venus";
	public const string ApocalypseId = "apocalypse";
	public const string TwilightOfInaeId = "inae";
	public const string NormalNabelId = "nabel-normal";
	public const string HardNabelId = "nabel-hard";
	public const string BakalId = "bakal";
	public const string DiregieId = "diregie";
	public const string HardMistId = "mist-hard";

	public static IReadOnlyList<int> TimelineCodes { get; } = [201, 209, 210];

	public static IReadOnlyList<WeeklyContentDefinition> All { get; } =
	[
		new()
		{
			Id = VenusId,
			Name = "베누스",
			RequiredFame = 41929,
			IsEnabled = settings => settings.ShowVenus,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.ContainsAny("베누스", "venus")
		},
		new()
		{
			Id = ApocalypseId,
			Name = "아포칼립스",
			RequiredFame = 73993,
			IsEnabled = settings => settings.ShowApocalypse,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.ContainsAny("아포칼립스", "apocalypse")
		},
		new()
		{
			Id = BakalId,
			Name = "바칼레이드",
			RequiredFame = 18774,
			IsEnabled = settings => settings.ShowBakalRaid,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.ContainsAny("바칼", "bakal")
		},
		new()
		{
			Id = HardMistId,
			Name = "하드 안개신",
			RequiredFame = 0,
			IsEnabled = settings => settings.ShowHardMistRaid,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.IsHardMistRaid()
		},
		new()
		{
			Id = NormalNabelId,
			Name = "일반 나벨",
			RequiredFame = 47684,
			IsEnabled = settings => settings.ShowNabelRaid && settings.ShowNormalNabelRaid,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.IsNabelRaid() && row.IsHard == false
		},
		new()
		{
			Id = HardNabelId,
			Name = "하드 나벨",
			RequiredFame = 47684,
			IsEnabled = settings => settings.ShowNabelRaid && settings.ShowHardNabelRaid,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.IsNabelRaid() && row.IsHard == true
		},
		new()
		{
			Id = TwilightOfInaeId,
			Name = "이내 황혼전",
			RequiredFame = 72688,
			IsEnabled = settings => settings.ShowTwilightOfInae,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.ContainsAny("이내", "황혼", "inae")
		},
		new()
		{
			Id = DiregieId,
			Name = "디레지에 레이드",
			RequiredFame = 63257,
			IsEnabled = settings => settings.ShowDiregieRaid,
			IsClearedBy = row => row.IsWeeklyClearCode() && row.ContainsAny("디레지에", "diregie")
		},
	];

	public bool IsFameLocked(int fame)
	{
		return RequiredFame > 0 && fame > 0 && fame <= RequiredFame;
	}

	public static WeeklyContentDefinition? FindById(string contentId)
	{
		return All.FirstOrDefault(content =>
			string.Equals(content.Id, contentId, StringComparison.OrdinalIgnoreCase));
	}

	public static IEnumerable<WeeklyContentDefinition> GetEnabled(WeeklyContentSettings settings)
	{
		return All.Where(content => content.IsEnabled(settings));
	}
}

public class ServerOption
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
}

public static class ServerOptions
{
	public static IReadOnlyList<ServerOption> All { get; } =
	[
		new() { Id = "cain", Name = "카인" },
		new() { Id = "diregie", Name = "디레지에" },
		new() { Id = "siroco", Name = "시로코" },
		new() { Id = "prey", Name = "프레이" },
		new() { Id = "casillas", Name = "카시야스" },
		new() { Id = "hilder", Name = "힐더" },
		new() { Id = "anton", Name = "안톤" },
		new() { Id = "bakal", Name = "바칼" }
	];

	public static string GetName(string serverId)
	{
		return All.FirstOrDefault(x => string.Equals(x.Id, serverId, StringComparison.OrdinalIgnoreCase))?.Name
			?? serverId;
	}
}
