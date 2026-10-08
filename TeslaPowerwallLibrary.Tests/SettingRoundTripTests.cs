// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Offline recovery checks for explicitly authorized live settings tests.</summary>
[TestFixture]
public sealed class SettingRoundTripTests
	{
	/// <summary>Restores after success, a lost acknowledgement, or failed change confirmation.</summary>
	/// <param name="failure">Simulated request or confirmation failure.</param>
	[TestCase ("none"), TestCase ("change-response"), TestCase ("change-confirmation"), TestCase ("restore-response"), TestCase ("restore-confirmation")]
	public async Task AlwaysRestoresAndIndependentlyConfirms (string failure)
		{
		int state = 20;
		var writes = new List<int> ();
		var confirmations = new List<int> ();
		bool restored = false, blocked = false;
		Task Run () => SettingRoundTrip.RunAsync (20, 19, () => Task.FromResult (state), value =>
			{
			writes.Add (value);
			state = value;
			if ((value == 19 && failure == "change-response") || (value == 20 && failure == "restore-response"))
				throw new InvalidOperationException ("Synthetic lost acknowledgement");
			return Task.CompletedTask;
			}, (a, b) => a == b, async (expected, read) =>
			{
			confirmations.Add (expected);
			if ((expected == 19 && failure == "change-confirmation") || (expected == 20 && failure == "restore-confirmation"))
				throw new InvalidOperationException ("Synthetic unavailable confirmation");
			Assert.That (await read (), Is.EqualTo (expected));
			}, () => restored = true, () => blocked = true);
		if (failure is "none" or "restore-response") await Run ();
		else Assert.That (async () => await Run (), Throws.TypeOf<InvalidOperationException> ());
		Assert.Multiple (() =>
			{
			Assert.That (state, Is.EqualTo (20));
			Assert.That (writes, Is.EqualTo (new[] { 19, 20 }));
			Assert.That (confirmations.Last (), Is.EqualTo (20));
			Assert.That (restored, Is.EqualTo (failure != "restore-confirmation"));
			Assert.That (blocked, Is.EqualTo (failure == "restore-confirmation"));
			});
		}

	/// <summary>A rejected change leaves the original value intact and does not cause a redundant write.</summary>
	[Test]
	public void RejectedChange_DoesNotWriteAgainWhenOriginalIsConfirmed ()
		{
		int writes = 0;
		bool restored = false;
		Assert.That (async () => await SettingRoundTrip.RunAsync (true, false, () => Task.FromResult (true), _ =>
			{ writes++; throw new InvalidOperationException ("Rejected"); }, (a, b) => a == b,
			async (expected, read) => Assert.That (await read (), Is.EqualTo (expected)),
			() => restored = true, () => Assert.Fail ("Original should be confirmed")), Throws.TypeOf<InvalidOperationException> ());
		Assert.That (writes, Is.EqualTo (1));
		Assert.That (restored, Is.True);
		}
	}
