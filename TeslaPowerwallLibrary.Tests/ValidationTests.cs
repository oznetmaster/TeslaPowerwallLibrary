// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for <see cref="Validation"/>.
/// </summary>
[TestFixture]
public sealed class ValidationTests
	{
	[Test]
	[TestCase ("10.0.1.99")]
	[TestCase ("powerwall.local")]
	[TestCase ("gateway.example.com")]
	public void WhenHostIsValidThenIsValidHostReturnsTrue (string host)
		{
		Assert.That (Validation.IsValidHost (host), Is.True);
		}

	[Test]
	[TestCase ("not a host")]
	[TestCase ("")]
	public void WhenHostIsInvalidThenIsValidHostReturnsFalse (string host)
		{
		Assert.That (Validation.IsValidHost (host), Is.False);
		}

	[Test]
	public void WhenHostIsNullThenIsValidHostReturnsFalse ()
		{
		Assert.That (Validation.IsValidHost (null), Is.False);
		}

	[Test]
	[TestCase ("10.0.1.99")]
	[TestCase ("192.168.91.1")]
	[TestCase ("::1")]
	public void WhenValueIsIpAddressThenIsValidIpAddressReturnsTrue (string value)
		{
		Assert.That (Validation.IsValidIpAddress (value), Is.True);
		}

	[Test]
	[TestCase ("powerwall.local")]
	[TestCase ("not-an-ip")]
	public void WhenValueIsNotIpAddressThenIsValidIpAddressReturnsFalse (string value)
		{
		Assert.That (Validation.IsValidIpAddress (value), Is.False);
		}

	[Test]
	[TestCase ("user@example.com")]
	[TestCase ("nobody@nowhere.com")]
	public void WhenEmailIsValidThenIsValidEmailReturnsTrue (string email)
		{
		Assert.That (Validation.IsValidEmail (email), Is.True);
		}

	[Test]
	[TestCase ("not-an-email")]
	[TestCase ("missing@domain")]
	[TestCase ("@nowhere.com")]
	public void WhenEmailIsInvalidThenIsValidEmailReturnsFalse (string email)
		{
		Assert.That (Validation.IsValidEmail (email), Is.False);
		}
	}