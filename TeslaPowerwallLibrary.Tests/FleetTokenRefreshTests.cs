// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http;

using TeslaPowerwallLibrary.FleetApi;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Verifies the Fleet OAuth request and rotation contract without contacting Tesla.</summary>
[TestFixture]
public sealed class FleetTokenRefreshTests
	{
	[Test]
	public async Task Refresh_UsesFleetTokenEndpointAndEncodedForm_AndPublishesRotation ()
		{
		var handler = new TokenHandler (HttpStatusCode.OK, "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\"}");
		using var connection = CreateConnection (handler);
		var notifications = 0;
		connection.TokensRefreshed += (_, tokens) =>
			{
				notifications++;
				Assert.That (tokens.AccessToken, Is.EqualTo ("new-access"));
				Assert.That (tokens.RefreshToken, Is.EqualTo ("new-refresh"));
				Assert.That (tokens.RefreshTokenChanged, Is.True);
			};

		Assert.That (await connection.RefreshAccessTokenAsync (), Is.True);
		Assert.That (handler.Url, Is.EqualTo ("https://fleet-auth.prd.vn.cloud.tesla.com/oauth2/v3/token"));
		Assert.That (handler.Method, Is.EqualTo (HttpMethod.Post));
		Assert.That (handler.MediaType, Is.EqualTo ("application/x-www-form-urlencoded"));
		var fields = handler.Body!.Split ('&').Select (field => field.Split ('='))
			.ToDictionary (pair => Uri.UnescapeDataString (pair[0].Replace ('+', ' ')), pair => Uri.UnescapeDataString (pair[1].Replace ('+', ' ')));
		Assert.That (fields.Count, Is.EqualTo (3));
		Assert.That (fields["grant_type"], Is.EqualTo ("refresh_token"));
		Assert.That (fields["client_id"], Is.EqualTo ("client+id"));
		Assert.That (fields["refresh_token"], Is.EqualTo ("refresh+/=&"));
		Assert.That (notifications, Is.EqualTo (1));
		Assert.That (connection.RefreshToken, Is.EqualTo ("new-refresh"));
		}

	[TestCase (HttpStatusCode.Unauthorized, "{\"error\":\"login_required\"}")]
	[TestCase (HttpStatusCode.OK, "{}")]
	[TestCase (HttpStatusCode.OK, "invalid-json")]
	public async Task Refresh_RejectedOrInvalidResponse_DoesNotRetryOrReplaceTokens (HttpStatusCode status, string body)
		{
		var handler = new TokenHandler (status, body);
		using var connection = CreateConnection (handler);
		var notifications = 0;
		connection.TokensRefreshed += (_, _) => notifications++;
		Assert.That (await connection.RefreshAccessTokenAsync (), Is.False);
		Assert.That (connection.RefreshToken, Is.EqualTo ("refresh+/=&"));
		Assert.That (connection.AccessToken, Is.Null);
		Assert.That (notifications, Is.Zero);
		Assert.That (handler.RequestCount, Is.EqualTo (1));
		}

	private static FleetApiConnection CreateConnection (HttpMessageHandler handler) =>
		new ("client+id", null, "refresh+/=&", "https://fleet-api.prd.eu.vn.cloud.tesla.com", TimeSpan.FromSeconds (5), handler);

	private sealed class TokenHandler (HttpStatusCode status, string response) : HttpMessageHandler
		{
		internal string? Url
			{
			get; private set;
			}
		internal HttpMethod? Method
			{
			get; private set;
			}
		internal string? MediaType
			{
			get; private set;
			}
		internal string? Body
			{
			get; private set;
			}
		internal int RequestCount
			{
			get; private set;
			}

		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			RequestCount++;
			Url = request.RequestUri!.AbsoluteUri;
			Method = request.Method;
			MediaType = request.Content!.Headers.ContentType!.MediaType;
			Body = await request.Content.ReadAsStringAsync ();
			return new HttpResponseMessage (status) { Content = new StringContent (response) };
			}
		}
	}