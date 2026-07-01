// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for <see cref="VersionHelper"/>.
/// </summary>
[TestClass]
public sealed class VersionHelperTests
	{
	[TestMethod]
	public void WhenVersionIsNullThenParseReturnsNull ()
		{
		var result = VersionHelper.ParseVersion (null);

		Assert.IsNull (result);
		}

	[TestMethod]
	public void WhenVersionIsDottedTripletThenParseCombinesBaseOneHundred ()
		{
		var result = VersionHelper.ParseVersion ("23.44.1");

		Assert.AreEqual (234401L, result);
		}

	[TestMethod]
	public void WhenVersionHasTrailingTokenThenOnlyFirstTokenIsParsed ()
		{
		var result = VersionHelper.ParseVersion ("23.44.1 27c790c5");

		Assert.AreEqual (234401L, result);
		}

	[TestMethod]
	public void WhenVersionHasFewerThanThreePartsThenItIsPaddedWithZeros ()
		{
		var result = VersionHelper.ParseVersion ("23.44");

		Assert.AreEqual (234400L, result);
		}

	[TestMethod]
	public void WhenVersionContainsNonNumericCharactersThenTheyAreStripped ()
		{
		var result = VersionHelper.ParseVersion ("v23.44.1");

		Assert.AreEqual (234401L, result);
		}
	}
