// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// MSTEST0032 does not apply here: these asserts intentionally pin GridStatus's compile-time constant
// values against the upstream numeric contract, so they will fail if the enum's literals ever change.
#pragma warning disable MSTEST0032

namespace TeslaPowerwallLibrary.Tests;

/// <summary>
/// Unit tests pinning the public <see cref="GridStatus"/> enum integer contract, which must match the
/// numeric output produced by the upstream pypowerwall project (1 = Up, 0 = Down, -1 = Syncing).
/// </summary>
[TestClass]
public sealed class GridStatusTests
	{
	[TestMethod]
	public void WhenUpThenValueIsOne ()
		{
		Assert.AreEqual (1, (int) GridStatus.Up);
		}

	[TestMethod]
	public void WhenDownThenValueIsZero ()
		{
		Assert.AreEqual (0, (int) GridStatus.Down);
		}

	[TestMethod]
	public void WhenSyncingThenValueIsNegativeOne ()
		{
		Assert.AreEqual (-1, (int) GridStatus.Syncing);
		}
	}

#pragma warning restore MSTEST0032
