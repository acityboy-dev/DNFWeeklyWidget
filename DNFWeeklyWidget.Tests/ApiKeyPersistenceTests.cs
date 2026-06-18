using System.Text.Json;
using Xunit;

namespace DNFWeeklyWidget.Tests;

public sealed class ApiKeyPersistenceTests
{
	[Fact]
	public void ProtectedApiKeyRoundTripsForCurrentWindowsUser()
	{
		const string apiKey = "test-api-key-value";

		var encrypted = ApiKeyProtector.Protect(apiKey);

		Assert.NotEqual(apiKey, encrypted);
		Assert.StartsWith("dpapi-v1:", encrypted, StringComparison.Ordinal);
		Assert.True(ApiKeyProtector.TryUnprotect(encrypted, out var decrypted));
		Assert.Equal(apiKey, decrypted);
	}

	[Fact]
	public void StorageJsonContainsOnlyEncryptedApiKey()
	{
		const string apiKey = "plain-api-key-must-not-be-serialized";
		var settings = new AppSettings { ApiKey = apiKey };

		var json = settings.SerializeForStorage();
		using var document = JsonDocument.Parse(json);

		Assert.False(document.RootElement.TryGetProperty("ApiKey", out _));
		Assert.True(document.RootElement.TryGetProperty("EncryptedApiKey", out var encrypted));
		Assert.DoesNotContain(apiKey, json, StringComparison.Ordinal);
		Assert.StartsWith("dpapi-v1:", encrypted.GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void LegacyPlaintextApiKeyMigratesToEncryptedStorage()
	{
		const string apiKey = "legacy-plain-api-key";
		var legacyJson = $$"""
		{
		  "ApiKey": "{{apiKey}}",
		  "Columns": 4
		}
		""";

		var settings = AppSettings.DeserializeForStorage(legacyJson, out var migrationRequired);
		var migratedJson = settings.SerializeForStorage();
		var reloaded = AppSettings.DeserializeForStorage(migratedJson, out var secondMigrationRequired);

		Assert.True(migrationRequired);
		Assert.Equal(apiKey, settings.ApiKey);
		Assert.DoesNotContain(apiKey, migratedJson, StringComparison.Ordinal);
		Assert.Equal(apiKey, reloaded.ApiKey);
		Assert.False(secondMigrationRequired);
	}
}
