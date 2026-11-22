// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

[TestFixture]
public sealed class FleetReadCooldownTests
	{
	[TestCase ("delta", 45)]
	[TestCase ("date", 120)]
	[TestCase ("no header", 10)]
	[TestCase ("past date", 0)]
	public async Task RetryAfter_SuppressesReadsAcrossEndpointsUntilExpiry (string format, int delay)
		{
		var header = format switch
			{
			"delta" => new RetryConditionHeaderValue (TimeSpan.FromSeconds (delay)),
			"date" => new RetryConditionHeaderValue (DateTimeOffset.UtcNow.AddSeconds (delay)),
			"past date" => new RetryConditionHeaderValue (DateTimeOffset.UtcNow.AddMinutes (-1)),
			_ => null
			};
		long elapsed = 0;
		using var handler = new RateLimitedFirstRead (header);
		using var fleet = new FleetApiConnection ("test-app", "test-token", null, FleetApiRegions.NORTH_AMERICA,
			TimeSpan.FromSeconds (5), handler, elapsedMilliseconds: () => elapsed);
		Assert.That (await fleet.GetSiteConfigAsync ("123"), Is.Null);

		if (delay > 0)
			{
			elapsed = (delay - (format == "date" ? 5 : 1)) * 1000;
			Assert.That (await fleet.GetSiteSummaryAsync ("123"), Is.Null);
			Assert.That (handler.Requests, Is.EqualTo (1));
			}
		elapsed = delay * 1000;
		Assert.That (await fleet.GetSiteSummaryAsync ("123"), Is.Not.Null);
		Assert.That (handler.Requests, Is.EqualTo (2));
		}

	private sealed class RateLimitedFirstRead (RetryConditionHeaderValue? retryAfter) : HttpMessageHandler
		{
		internal int Requests
			{
			get; private set;
			}

		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Get));
			var result = ++Requests == 1 ? new HttpResponseMessage ((HttpStatusCode)429) : new HttpResponseMessage (HttpStatusCode.OK)
				{
				Content = new StringContent ("""{"response":{"percentage_charged":45.0}}""")
				};
			if (Requests == 1)
				result.Headers.RetryAfter = retryAfter;
			return Task.FromResult (result);
			}
		}
	}
