using Xunit;

namespace DNFWeeklyWidget.Tests;

public sealed class CardMemoAndRaidPolicyTests
{
	[Fact]
	public void SameCharacterKeepsIndependentMemoPerPreset()
	{
		var first = new CardEntryPreset { Id = "first", Name = "1번" };
		var second = new CardEntryPreset { Id = "second", Name = "2번" };
		var settings = new AppSettings
		{
			ActivePresetId = first.Id,
			Presets = [first, second]
		};
		var service = new PresetService(settings);
		var firstRow = new CharacterRow { ServerId = "cain", CharacterName = "동일 캐릭터", Memo = "첫 메모" };

		service.UpdateCurrentMemo(firstRow);
		service.SelectPreset(second.Id);
		var secondRow = new CharacterRow { ServerId = "cain", CharacterName = "동일 캐릭터", Memo = "둘째 메모" };
		service.UpdateCurrentMemo(secondRow);
		service.SelectPreset(first.Id);
		var restored = new CharacterRow { ServerId = "cain", CharacterName = "동일 캐릭터" };
		service.ApplyCurrentMemos([restored]);

		Assert.Equal("첫 메모", restored.Memo);
		Assert.Equal("둘째 메모", Assert.Single(second.CardMemos).Memo);
	}

	[Fact]
	public void PresetMemosSerializeSeparatelyFromCardCache()
	{
		var settings = new AppSettings
		{
			ActivePresetId = "preset",
			Presets =
			[
				new CardEntryPreset
				{
					Id = "preset",
					CachedCards = [new CachedCharacterCard { ServerId = "cain", CharacterName = "캐릭터" }],
					CardMemos =
					[
						new CardMemoEntry { ServerId = "cain", CharacterName = "캐릭터", Memo = "독립 메모" }
					]
				}
			]
		};

		var json = settings.SerializeForStorage();
		var restored = AppSettings.DeserializeForStorage(json, out _);
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var presetJson = document.RootElement.GetProperty("Presets")[0];

		Assert.False(presetJson.GetProperty("CachedCards")[0].TryGetProperty("Memo", out _));
		Assert.Equal("독립 메모", Assert.Single(restored.Presets[0].CardMemos).Memo);
	}

	[Theory]
	[InlineData(2026, 6, 18, 5, 59, false)]
	[InlineData(2026, 6, 18, 6, 0, true)]
	[InlineData(2026, 6, 19, 23, 59, true)]
	[InlineData(2026, 6, 20, 5, 59, true)]
	[InlineData(2026, 6, 20, 6, 0, false)]
	public void AutoHideWindowUsesThursdaySixToSaturdaySix(
		int year, int month, int day, int hour, int minute, bool expected)
	{
		Assert.Equal(expected, RaidDisplayPolicy.IsAutoHideWindow(
			new DateTime(year, month, day, hour, minute, 0)));
	}

	[Fact]
	public void EffectiveRaidSettingsKeepLegionsAndDisableEveryRaid()
	{
		var configured = new WeeklyContentSettings
		{
			ShowVenus = true,
			ShowApocalypse = true,
			ShowBakalRaid = true,
			ShowNabelRaid = true,
			ShowNormalNabelRaid = true,
			ShowHardNabelRaid = true,
			ShowTwilightOfInae = true,
			ShowDiregieRaid = true,
			ShowHardMistRaid = true
		};

		var effective = RaidDisplayPolicy.CreateEffectiveSettings(configured, hideRaids: true);

		Assert.True(effective.ShowVenus);
		Assert.True(effective.ShowApocalypse);
		Assert.False(effective.ShowBakalRaid);
		Assert.False(effective.ShowNabelRaid);
		Assert.False(effective.ShowNormalNabelRaid);
		Assert.False(effective.ShowHardNabelRaid);
		Assert.False(effective.ShowTwilightOfInae);
		Assert.False(effective.ShowDiregieRaid);
		Assert.False(effective.ShowHardMistRaid);
	}
}
