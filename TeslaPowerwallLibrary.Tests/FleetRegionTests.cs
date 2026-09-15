// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using Newtonsoft.Json.Linq;

using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class FleetRegionTests
	{
	[Test]
	public void DefaultOptions_DiscoverRegion () => Assert.That (new PowerwallOptions ().FleetApiRegion, Is.EqualTo ("auto"));

	[TestCase ("auto", true)]
	[TestCase (null, true)]
	[TestCase ("  ", true)]
	[TestCase ("na", false)]
	[TestCase ("eu", false)]
	[TestCase ("cn", false)]
	public void RegionOverrides_AreDistinguishedFromAutomatic (string? region, bool automatic) =>
		Assert.That (FleetApiRegions.IsAutomatic (region), Is.EqualTo (automatic));

	[TestCase ("na", FleetApiRegions.NORTH_AMERICA)]
	[TestCase ("eu", FleetApiRegions.EUROPE)]
	public async Task Discovery_RoutesProductsToAccountRegion_AndRunsOncePerConnection (string region, string baseUrl)
		{
		var handler = new RegionHandler (region, baseUrl);
		using var connection = new FleetApiConnection ("client", "synthetic-access", null, FleetApiRegions.NORTH_AMERICA, TimeSpan.FromSeconds (5), handler);
		Assert.That (await connection.DiscoverRegionAsync (CancellationToken.None), Is.True);
		Assert.That (await connection.DiscoverRegionAsync (CancellationToken.None), Is.True);
		await connection.GetProductsAsync ();
		Assert.That (handler.Urls, Is.EqualTo (new[] { FleetApiRegions.NORTH_AMERICA + "/api/1/users/region", baseUrl + "/api/1/products" }));
		}

	[TestCase ("eu", "https://unrelated.test")]
	[TestCase ("eu", "http://fleet-api.prd.eu.vn.cloud.tesla.com")]
	[TestCase ("eu", "https://fleet-api.prd.eu.vn.cloud.tesla.com/other")]
	[TestCase ("eu", FleetApiRegions.NORTH_AMERICA)]
	[TestCase ("unknown", FleetApiRegions.NORTH_AMERICA)]
	[TestCase (null, null)]
	public async Task Discovery_RejectsMissingMismatchedOrUntrustedEndpoint (string? region, string? baseUrl)
		{
		var handler = new RegionHandler (region, baseUrl);
		using var connection = new FleetApiConnection ("client", "synthetic-access", null, FleetApiRegions.NORTH_AMERICA, TimeSpan.FromSeconds (5), handler);
		Assert.That (await connection.DiscoverRegionAsync (CancellationToken.None), Is.False);
		Assert.That (handler.Urls, Has.Count.EqualTo (1));
		}

	private sealed class RegionHandler (string? region, string? baseUrl) : HttpMessageHandler
		{
		internal List<string> Urls { get; } = [];
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Urls.Add (request.RequestUri!.AbsoluteUri);
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Get));
			Assert.That (request.Headers.Authorization!.Parameter, Is.EqualTo ("synthetic-access"));
			var body = request.RequestUri.AbsolutePath.EndsWith ("/region", StringComparison.Ordinal)
				? new JObject { ["response"] = new JObject { ["region"] = region, ["fleet_api_base_url"] = baseUrl } }
				: new JObject { ["response"] = new JArray () };
			return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (body.ToString ()) });
			}
		}
	}