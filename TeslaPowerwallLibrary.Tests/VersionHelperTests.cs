// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests for <see cref="VersionHelper"/>.
/// </summary>
[TestFixture]
public sealed class VersionHelperTests
	{
	[Test]
	public void WhenVersionIsNullThenParseReturnsNull ()
		{
		var result = VersionHelper.ParseVersion (null);

		Assert.That (result, Is.Null);
		}

	[Test]
	public void WhenVersionIsDottedTripletThenParseCombinesBaseOneHundred ()
		{
		var result = VersionHelper.ParseVersion ("23.44.1");

		Assert.That (result, Is.EqualTo (234401L));
		}

	[Test]
	public void WhenVersionHasTrailingTokenThenOnlyFirstTokenIsParsed ()
		{
		var result = VersionHelper.ParseVersion ("23.44.1 27c790c5");

		Assert.That (result, Is.EqualTo (234401L));
		}

	[Test]
	public void WhenVersionHasFewerThanThreePartsThenItIsPaddedWithZeros ()
		{
		var result = VersionHelper.ParseVersion ("23.44");

		Assert.That (result, Is.EqualTo (234400L));
		}

	[Test]
	public void WhenVersionContainsNonNumericCharactersThenTheyAreStripped ()
		{
		var result = VersionHelper.ParseVersion ("v23.44.1");

		Assert.That (result, Is.EqualTo (234401L));
		}
	}