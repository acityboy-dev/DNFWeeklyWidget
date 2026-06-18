using Xunit;

namespace DNFWeeklyWidget.Tests;

public sealed class PresetServiceTests
{
	[Fact]
	public void RemoveCurrentCharactersRemovesMatchingEntriesCacheAndRowsOnlyFromCurrentPreset()
	{
		var currentPreset = new CardEntryPreset
		{
			Id = "current",
			Characters =
			[
				new SavedCharacter { ServerId = "cain", CharacterName = "제거 대상" },
				new SavedCharacter { ServerId = "bakal", CharacterName = "유지 대상" }
			],
			CachedCards =
			[
				new CachedCharacterCard { ServerId = "cain", CharacterName = "제거 대상" },
				new CachedCharacterCard { ServerId = "bakal", CharacterName = "유지 대상" }
			]
		};
		var otherPreset = new CardEntryPreset
		{
			Id = "other",
			Characters =
			[
				new SavedCharacter { ServerId = "cain", CharacterName = "제거 대상" }
			]
		};
		var settings = new AppSettings
		{
			ActivePresetId = currentPreset.Id,
			Presets = [currentPreset, otherPreset]
		};
		var service = new PresetService(settings);
		service.SetRows(currentPreset.Id,
		[
			new CharacterRow { ServerId = "cain", CharacterName = "제거 대상" },
			new CharacterRow { ServerId = "bakal", CharacterName = "유지 대상" }
		]);

		var removedCount = service.RemoveCurrentCharacters(
		[
			new SavedCharacter { ServerId = "CAIN", CharacterName = "제거 대상" }
		]);

		Assert.Equal(1, removedCount);
		Assert.Equal("유지 대상", Assert.Single(currentPreset.Characters).CharacterName);
		Assert.Equal("유지 대상", Assert.Single(currentPreset.CachedCards).CharacterName);
		Assert.True(service.TryGetRows(currentPreset.Id, out var rows));
		Assert.Equal("유지 대상", Assert.Single(rows).CharacterName);
		Assert.Single(otherPreset.Characters);
		Assert.Same(currentPreset.Characters, settings.Characters);
		Assert.Same(currentPreset.CachedCards, settings.CachedCards);
	}

	[Fact]
	public void PersistingEmptyRowsKeepsEmptyPresetIsolated()
	{
		var emptyPreset = new CardEntryPreset { Id = "empty", Name = "빈 프리셋" };
		var populatedPreset = new CardEntryPreset
		{
			Id = "populated",
			Name = "카드 프리셋",
			Characters =
			[
				new SavedCharacter { ServerId = "cain", CharacterName = "카드 캐릭터" }
			]
		};
		var settings = new AppSettings
		{
			ActivePresetId = emptyPreset.Id,
			Presets = [emptyPreset, populatedPreset]
		};
		var service = new PresetService(settings);

		service.PersistRows(Array.Empty<CharacterRow>());
		service.SelectPreset(populatedPreset.Id);
		service.SelectPreset(emptyPreset.Id);

		Assert.Empty(emptyPreset.Characters);
		Assert.Empty(emptyPreset.CachedCards);
		Assert.True(service.TryGetRows(emptyPreset.Id, out var emptyRows));
		Assert.Empty(emptyRows);
		Assert.Single(populatedPreset.Characters);
		Assert.Same(emptyPreset.Characters, settings.Characters);
	}

	[Fact]
	public void ReplaceCurrentCharactersKeepsExistingDefaultBehavior()
	{
		var currentPreset = new CardEntryPreset
		{
			Id = "current",
			Characters =
			[
				new SavedCharacter { ServerId = "cain", CharacterName = "기존 캐릭터" }
			]
		};
		var settings = new AppSettings
		{
			ActivePresetId = currentPreset.Id,
			Presets = [currentPreset]
		};
		var service = new PresetService(settings);

		service.ReplaceCurrentCharacters(
		[
			new SavedCharacter { ServerId = "bakal", CharacterName = "가져온 캐릭터" }
		]);

		var character = Assert.Single(currentPreset.Characters);
		Assert.Equal("bakal", character.ServerId);
		Assert.Equal("가져온 캐릭터", character.CharacterName);
		Assert.Same(currentPreset.Characters, settings.Characters);
	}

	[Fact]
	public void AppendCurrentCharactersPreservesCurrentEntriesCacheAndOtherPresets()
	{
		var currentPreset = new CardEntryPreset
		{
			Id = "current",
			Name = "현재",
			Characters =
			[
				new SavedCharacter { ServerId = "cain", CharacterName = "기존 캐릭터" }
			],
			CachedCards =
			[
				new CachedCharacterCard { ServerId = "cain", CharacterName = "기존 캐릭터" }
			]
		};
		var otherPreset = new CardEntryPreset
		{
			Id = "other",
			Name = "다른 프리셋",
			Characters =
			[
				new SavedCharacter { ServerId = "diregie", CharacterName = "다른 캐릭터" }
			]
		};
		var settings = new AppSettings
		{
			ActivePresetId = currentPreset.Id,
			Presets = [currentPreset, otherPreset]
		};
		var service = new PresetService(settings);

		var addedCount = service.AppendCurrentCharacters(
		[
			new SavedCharacter { ServerId = "CAIN", CharacterName = "기존 캐릭터" },
			new SavedCharacter { ServerId = "bakal", CharacterName = "새 캐릭터" },
			new SavedCharacter { ServerId = "BAKAL", CharacterName = "새 캐릭터" }
		]);

		Assert.Equal(1, addedCount);
		Assert.Collection(
			currentPreset.Characters,
			character => Assert.Equal("기존 캐릭터", character.CharacterName),
			character => Assert.Equal("새 캐릭터", character.CharacterName));
		Assert.Single(currentPreset.CachedCards);
		Assert.Single(otherPreset.Characters);
		Assert.Same(currentPreset.Characters, settings.Characters);
		Assert.Same(currentPreset.CachedCards, settings.CachedCards);
	}
}
