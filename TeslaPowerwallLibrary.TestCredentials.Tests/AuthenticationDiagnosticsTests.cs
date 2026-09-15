// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using NUnit.Framework;

namespace TeslaPowerwallLibrary.TestCredentials.Tests;

[TestFixture]
public sealed class AuthenticationDiagnosticsTests
	{
	private const string Logger = "TeslaPowerwallLibrary.FleetApi.FleetApiConnection";

	[Test]
	public void DataStatus_DoesNotExposeRequestUrl () =>
		Assert.That (AuthenticationDiagnostics.Summarize (Logger, "Tesla FleetAPI https://private-host/private-token returned HTTP 403"), Is.EqualTo ("Fleet data request returned HTTP 403. Request and response details withheld."));

	[Test]
	public void UnknownException_DoesNotExposeMessage () =>
		Assert.That (AuthenticationDiagnostics.DescribeFailure (new InvalidOperationException ("private-token")), Is.EqualTo ("Failure category: InvalidOperationException. Private details withheld."));

	[Test]
	public void ProductFailure_IsDistinguishedFromTokenExchange () =>
		Assert.That (AuthenticationDiagnostics.DescribeFailure (new InvalidOperationException ("Unable to retrieve the Tesla FleetAPI product list. The access token may be expired or rejected.")), Does.Contain ("product/site lookup failed"));

	[Test]
	public void RefreshStatus_ReportsOnlyStatusNumber () =>
		Assert.That (AuthenticationDiagnostics.Summarize (Logger, "Tesla FleetAPI token refresh failed (HTTP 401)."), Does.Contain ("HTTP 401"));

	[TestCase ("Unable to refresh Tesla FleetAPI token: private-token")]
	[TestCase ("Unable to parse Tesla FleetAPI token refresh response: private-token")]
	[TestCase ("Unable to connect to Tesla FleetAPI https://private-host/private-token")]
	public void PrivateExceptionDetails_AreReplacedWithFixedCategory (string message)
		{
		string? summary = AuthenticationDiagnostics.Summarize (Logger, message);
		Assert.That (summary, Is.Not.Null);
		Assert.That (summary, Does.Not.Contain ("private-token"));
		Assert.That (summary, Does.Not.Contain ("private-host"));
		}

	[TestCase ("private-token")]
	[TestCase ("Tesla FleetAPI token refresh failed (HTTP 401).\nprivate-token")]
	public void UnexpectedMessage_IsSuppressed (string message) =>
		Assert.That (AuthenticationDiagnostics.Summarize (Logger, message), Is.Null);

	[Test]
	public void UnrelatedLogger_IsSuppressed () =>
		Assert.That (AuthenticationDiagnostics.Summarize ("Other", "Tesla FleetAPI token refresh failed (HTTP 401)."), Is.Null);
	}