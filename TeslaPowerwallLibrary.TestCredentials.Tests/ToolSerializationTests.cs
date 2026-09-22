using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.Login;
using TeslaPowerwallLibrary.TestConsole;

namespace TeslaPowerwallLibrary.TestCredentials.Tests;

[TestFixture]
public sealed class ToolSerializationTests
	{
	[Test]
	public void ExistingConsoleSettings_PreserveWireNamesAndProtectedValues ()
		{
		const string json = """{"host":"192.0.2.1","protectedPassword":"synthetic-protected","timeoutSeconds":25,"fleetApiClientId":"test-client","protectedFleetApiRefreshToken":"synthetic-protected-refresh","preferFleetApi":true}""";
		var settings = JsonSerializer.Deserialize<ConsoleSettings> (json)!;
		Assert.That (settings.Host, Is.EqualTo ("192.0.2.1"));
		Assert.That (settings.ProtectedPassword, Is.EqualTo ("synthetic-protected"));
		Assert.That (settings.TimeoutSeconds, Is.EqualTo (25));
		Assert.That (settings.PreferFleetApi, Is.True);
		string output = JsonSerializer.Serialize (settings);
		Assert.That (output, Does.Contain ("\"protectedFleetApiRefreshToken\":\"synthetic-protected-refresh\""));
		}

	[Test]
	public void ExistingDashboardSettings_PreserveProtectedValues ()
		{
		const string json = """{"mode":"FleetAPI","email":"unit@example.test","protectedPassword":"synthetic-protected","fleetApiClientId":"test-client","protectedFleetApiRefreshToken":"synthetic-protected-refresh","fleetApiRegion":"auto"}""";
		var settings = JsonSerializer.Deserialize<AppSettings> (json)!;
		Assert.That (settings.Email, Is.EqualTo ("unit@example.test"));
		Assert.That (settings.ProtectedPassword, Is.EqualTo ("synthetic-protected"));
		Assert.That (settings.FleetApiRegion, Is.EqualTo ("auto"));
		Assert.That (JsonSerializer.Serialize (settings), Does.Contain ("\"protectedFleetApiRefreshToken\":\"synthetic-protected-refresh\""));
		}

	[TestCase ("{\"email\":\"unit@example.test\"}")]
	[TestCase ("{\"data\":{\"email\":\"unit@example.test\"}}")]
	public void LoginIdToken_ExtractsBothSupportedEmailShapes (string body)
		{
		string token = "test." + Convert.ToBase64String (Encoding.UTF8.GetBytes (body)).TrimEnd ('=').Replace ('+', '-').Replace ('/', '_') + ".test";
		Assert.That (TeslaAuth.ExtractEmailFromToken (token), Is.EqualTo ("unit@example.test"));
		}

	[TestCase ("not-a-jwt")]
	[TestCase ("test.bm90LWpzb24.test")]
	public void MalformedLoginIdToken_ReturnsNoEmail (string token) =>
		 Assert.That (TeslaAuth.ExtractEmailFromToken (token), Is.Empty);

	[TestCase ("3600")]
	[TestCase ("\"3600\"")]
	public void OwnerTokenResponse_PreservesTokensAndExpiry (string expiry)
		{
		string json = "{\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh\",\"expires_in\":" + expiry + "}";
		var parser = typeof (TeslaAuth).GetMethod ("ParseTokenResponse", BindingFlags.Static | BindingFlags.NonPublic)!;
		var tokens = (TeslaTokens)parser.Invoke (null, new object[] { json })!;
		Assert.That (tokens.AccessToken, Is.EqualTo ("synthetic-access"));
		Assert.That (tokens.RefreshToken, Is.EqualTo ("synthetic-refresh"));
		Assert.That (tokens.ExpiresIn, Is.EqualTo (3600));
		}

	[Test]
	public void FleetTokenResponse_MapsBothTokens ()
		{
		var tokens = JsonSerializer.Deserialize<FleetTokenResponse> ("""{"access_token":"synthetic-access","refresh_token":"synthetic-refresh"}""")!;
		Assert.That (tokens.AccessToken, Is.EqualTo ("synthetic-access"));
		Assert.That (tokens.RefreshToken, Is.EqualTo ("synthetic-refresh"));
		}

	[Test]
	public void LoginRequests_UseTeslaWireNames ()
		{
		string json = JsonSerializer.Serialize (new TeslaCodeExchangeRequest { Code = "synthetic-code", CodeVerifier = "synthetic-verifier" });
		Assert.That (json, Does.Contain ("\"code_verifier\":\"synthetic-verifier\""));
		Assert.That (JsonSerializer.Serialize (new PartnerRegistrationRequest { Domain = "example.test" }), Is.EqualTo ("{\"domain\":\"example.test\"}"));
		}

	[Test, NonParallelizable]
	public void ConsoleLogger_IsExplicitlyEnabledAndWritesToStandardError ()
		{
		var old = Console.Error;
		using var output = new StringWriter ();
		try
			{
			Console.SetError (output);
			VerboseLogging.Enable ();
			VerboseLogging.Logger.LogWarning ("Synthetic diagnostic");
			Assert.That (output.ToString (), Does.Contain ("Synthetic diagnostic"));
			}
		finally { Console.SetError (old); }
		}

	[Test, NonParallelizable]
	public void CredentialLogger_OnlyEmitsSanitizedWarnings ()
		{
		var old = Console.Error;
		using var output = new StringWriter ();
		using var logger = new AuthenticationDiagnostics ();
		try
			{
			Console.SetError (output);
			logger.LogDebug ("Unable to refresh Tesla FleetAPI token: synthetic-private-value");
			Assert.That (output.ToString (), Is.Empty);
			logger.LogError ("Unable to refresh Tesla FleetAPI token: synthetic-private-value");
			Assert.That (output.ToString (), Does.Contain ("network, TLS or request timeout"));
			Assert.That (output.ToString (), Does.Not.Contain ("synthetic-private-value"));
			}
		finally { Console.SetError (old); }
		}
	}