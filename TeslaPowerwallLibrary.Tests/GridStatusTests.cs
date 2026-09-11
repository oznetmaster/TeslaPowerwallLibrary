// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.


namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests pinning the public <see cref="GridStatus"/> enum integer contract, which must match the
/// numeric output produced by the upstream pypowerwall project (1 = Up, 0 = Down, -1 = Syncing).
/// </summary>
[TestFixture]
public sealed class GridStatusTests
	{
	[Test]
	public void WhenUpThenValueIsOne ()
		{
		Assert.That ((int) GridStatus.Up, Is.EqualTo (1));
		}

	[Test]
	public void WhenDownThenValueIsZero ()
		{
		Assert.That ((int) GridStatus.Down, Is.EqualTo (0));
		}

	[Test]
	public void WhenSyncingThenValueIsNegativeOne ()
		{
		Assert.That ((int) GridStatus.Syncing, Is.EqualTo (-1));
		}
	}