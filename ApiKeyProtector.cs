using System.Security.Cryptography;
using System.Text;

namespace DNFWeeklyWidget;

internal static class ApiKeyProtector
{
	private const string Prefix = "dpapi-v1:";
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DNFWeeklyWidget.ApiKey.v1");

	public static string Protect(string apiKey)
	{
		if (string.IsNullOrEmpty(apiKey))
			return "";

		var plaintext = Encoding.UTF8.GetBytes(apiKey);
		try
		{
			var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
			return Prefix + Convert.ToBase64String(protectedBytes);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(plaintext);
		}
	}

	public static bool TryUnprotect(string protectedApiKey, out string apiKey)
	{
		apiKey = "";
		if (!protectedApiKey.StartsWith(Prefix, StringComparison.Ordinal))
			return false;

		byte[] protectedBytes;
		try
		{
			protectedBytes = Convert.FromBase64String(protectedApiKey[Prefix.Length..]);
		}
		catch (FormatException)
		{
			return false;
		}

		try
		{
			var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
			try
			{
				apiKey = Encoding.UTF8.GetString(plaintext);
				return true;
			}
			finally
			{
				CryptographicOperations.ZeroMemory(plaintext);
			}
		}
		catch (CryptographicException)
		{
			return false;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(protectedBytes);
		}
	}
}
