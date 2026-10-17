// Copyright (c) 2026 Neil Colvin. MIT License; see TeslaPowerwallLibrary/LICENSE.
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tests;

// Read-only access-token session: refresh credentials remain with the credential helper.
[TestFixture, Category ("Live"), NonParallelizable, FixtureLifeCycle (LifeCycle.SingleInstance)]
public sealed partial class LiveSiteTests
	{
	private Powerwall? _client;
	private LiveSettings? _settings;
	private CancellationTokenSource? _deadline;
	private RSA? _signingKey;

	[OneTimeSetUp]
	public async Task ConnectSite ()
		{
		string flag = TestContext.Parameters.Get ("EnableLiveTests", "");
		if (flag.Length != 0 && !bool.TryParse (flag, out _))
			throw new InvalidDataException ("EnableLiveTests must be true or false.");
		if (flag.Equals ("false", StringComparison.OrdinalIgnoreCase))
			Assert.Ignore ("Live tests are disabled for this run.");
		string directory = TestContext.Parameters.Get ("TestDataDirectory", "");
		if (string.IsNullOrWhiteSpace (directory))
			directory = Environment.GetEnvironmentVariable ("TESLA_LIVE_TEST_DATA_DIRECTORY") ?? "";
		if (!string.IsNullOrWhiteSpace (directory))
			{
			string path = Path.Combine (directory, "LiveTestSettings.json");
			if (File.Exists (path))
				{
				try
					{
					_settings = JsonSerializer.Deserialize<LiveSettings> (File.ReadAllText (path));
					}
				catch (JsonException) { throw new InvalidDataException ("Live settings must contain a valid settings object."); }
				}
			}
		if (!flag.Equals ("true", StringComparison.OrdinalIgnoreCase) && _settings?.Enabled != true)
			Assert.Ignore ("Live library tests require private settings and explicit enablement.");
		if (_settings?.Mode == "local")
			{
			_settings.ValidateLocal ();
			}
		else if (_settings is null || string.IsNullOrWhiteSpace (_settings.AccessToken) || string.IsNullOrWhiteSpace (_settings.SiteId)
			 || !_settings.SiteId.All (char.IsDigit) || _settings.Mode is not ("fleet" or "cloud")
			 || (_settings.Mode == "fleet" && (string.IsNullOrWhiteSpace (_settings.ClientId)
				  || _settings.Region is not ("auto" or "na" or "eu" or "cn"))))
			throw new InvalidDataException ("Live settings require accessToken, numeric siteId, mode (fleet/cloud), and clientId/region for Fleet API.");
		bool fleet = _settings.Mode == "fleet";
		_deadline = new CancellationTokenSource (TimeSpan.FromMinutes (3));
		_client = _settings!.Mode == "local"
			 ? new Powerwall (_settings.LocalOptions (out _signingKey))
			 : new Powerwall (new PowerwallOptions
				 {
				 FleetApi = fleet,
				 CloudMode = !fleet,
				 SiteId = _settings.SiteId,
				 FleetApiClientId = fleet ? _settings.ClientId : null,
				 FleetApiRegion = _settings.Region ?? "na",
				 FleetApiAccessToken = fleet ? _settings.AccessToken : null,
				 AccessToken = fleet ? null : _settings.AccessToken,
				 NoFleetApiTokenPersistence = true,
				 NoCloudTokenPersistence = true,
				 Timeout = TimeSpan.FromSeconds (20)
				 });
		Assert.That (await _client.ConnectAsync (_deadline.Token), Is.True, "The dedicated live API session must connect.");
		}

	[OneTimeTearDown]
	public void DisposeSite ()
		{
		_client?.Dispose ();
		_signingKey?.Dispose ();
		_deadline?.Dispose ();
		_signingKey = null;
		_client = null;
		_deadline = null;
		_settings = null;
		}

	[Test]
	public async Task RealSite_SelectsSiteAndReadsName ()
		{
		string? selected = _settings!.Mode == "fleet" ? _client!.FleetApiSiteId : _client!.CloudSiteId;
		if (_settings.Mode != "local")
			Assert.That (selected == _settings.SiteId, Is.True, "The requested site must be selected.");
		if (_settings.Mode == "local")
			Assert.That (_client.LocalHost, Is.EqualTo (_settings.Host),
				 "The configured local endpoint must be retained without cloud fallback.");
		else
			Assert.That (!string.IsNullOrWhiteSpace (await _client.SiteNameAsync (_deadline!.Token)), Is.True);
		}

	[Test]
	public async Task RealSite_ReadsTypedPowerAndBattery ()
		{
		var power = await _client!.GetPowerReadingsAsync (_deadline!.Token);
		Assert.That (new[] { power.Site, power.Solar, power.Battery, power.Load }
			 .All (value => value is double reading && !double.IsNaN (reading) && !double.IsInfinity (reading)), Is.True);
		Assert.That (await _client.LevelAsync (cancellationToken: _deadline.Token), Is.InRange (0d, 100d));
		}

	/// <summary>Cloud status must not invent a gateway boot time or firmware hash from installation metadata.</summary>
	[Test]
	public async Task RealSite_StatusPreservesMetadataMeaning ()
		{
		var status = await _client!.StatusAsync (_deadline!.Token);
		Assert.That (status, Is.Not.Null);
		if (_settings!.Mode is "fleet" or "cloud")
			{
			Assert.That (status!.StartTime, Is.Null);
			Assert.That (status.UpTimeSeconds, Is.Null);
			Assert.That (status.GitHash, Is.Null);
			Assert.That (status.SyncType, Is.EqualTo ("unknown"));
			TestContext.Progress.WriteLine ("Cloud installation date available: " + (status.InstallationDate is not null));
			}
		}

	[Test]
	public async Task RealSite_RefreshesOperatingConfiguration ()
		{
		double? reserve = await _client!.GetReserveAsync (scale: false, force: true, cancellationToken: _deadline!.Token);
		string? mode = await _client.GetModeAsync (force: true, cancellationToken: _deadline.Token);
		if (_settings!.Mode == "local" && _settings.LocalProtocol == "Gateway")
			{
			TestContext.Progress.WriteLine ("Gateway operating settings available: " + (reserve.HasValue && !string.IsNullOrWhiteSpace (mode)));
			if (reserve.HasValue)
				Assert.That (reserve, Is.InRange (0d, 100d));
			}
		else
			{
			Assert.That (reserve, Is.Not.Null.And.InRange (0d, 100d));
			Assert.That (mode, Is.Not.Null.And.Not.Empty);
			}
		Assert.That (await _client.IsConnectedAsync (_deadline.Token), Is.True);
		}

	public sealed partial class LiveSettings
		{
		[JsonPropertyName ("enabled")]
		public bool Enabled
			{
			get; set;
			}
		[JsonPropertyName ("mode")]
		public string? Mode
			{
			get; set;
			}
		[JsonPropertyName ("accessToken")]
		public string? AccessToken
			{
			get; set;
			}
		[JsonPropertyName ("clientId")]
		public string? ClientId
			{
			get; set;
			}
		[JsonPropertyName ("siteId")]
		public string? SiteId
			{
			get; set;
			}
		[JsonPropertyName ("region")]
		public string? Region
			{
			get; set;
			}
		}
	}