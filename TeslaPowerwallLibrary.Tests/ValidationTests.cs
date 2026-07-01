// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for <see cref="Validation"/>.
/// </summary>
[TestClass]
public sealed class ValidationTests
	{
	[TestMethod]
	[DataRow ("10.0.1.99")]
	[DataRow ("powerwall.local")]
	[DataRow ("gateway.example.com")]
	public void WhenHostIsValidThenIsValidHostReturnsTrue (string host)
		{
		Assert.IsTrue (Validation.IsValidHost (host));
		}

	[TestMethod]
	[DataRow ("not a host")]
	[DataRow ("")]
	public void WhenHostIsInvalidThenIsValidHostReturnsFalse (string host)
		{
		Assert.IsFalse (Validation.IsValidHost (host));
		}

	[TestMethod]
	public void WhenHostIsNullThenIsValidHostReturnsFalse ()
		{
		Assert.IsFalse (Validation.IsValidHost (null));
		}

	[TestMethod]
	[DataRow ("10.0.1.99")]
	[DataRow ("192.168.91.1")]
	[DataRow ("::1")]
	public void WhenValueIsIpAddressThenIsValidIpAddressReturnsTrue (string value)
		{
		Assert.IsTrue (Validation.IsValidIpAddress (value));
		}

	[TestMethod]
	[DataRow ("powerwall.local")]
	[DataRow ("not-an-ip")]
	public void WhenValueIsNotIpAddressThenIsValidIpAddressReturnsFalse (string value)
		{
		Assert.IsFalse (Validation.IsValidIpAddress (value));
		}

	[TestMethod]
	[DataRow ("user@example.com")]
	[DataRow ("nobody@nowhere.com")]
	public void WhenEmailIsValidThenIsValidEmailReturnsTrue (string email)
		{
		Assert.IsTrue (Validation.IsValidEmail (email));
		}

	[TestMethod]
	[DataRow ("not-an-email")]
	[DataRow ("missing@domain")]
	[DataRow ("@nowhere.com")]
	public void WhenEmailIsInvalidThenIsValidEmailReturnsFalse (string email)
		{
		Assert.IsFalse (Validation.IsValidEmail (email));
		}
	}
