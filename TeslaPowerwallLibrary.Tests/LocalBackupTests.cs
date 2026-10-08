// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Local;
using TeslaPowerwallLibrary.Tedapi;

namespace TeslaPowerwallLibrary.Tests;

public sealed partial class LocalSettingsTests
	{
	[Test]
	public async Task BackupSchedule_CancelsBeforeSchedulingAndUsesExactDuration ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		DateTimeOffset before = DateTimeOffset.UtcNow;
		Assert.That ((await rig.Powerwall.ScheduleLocalMaxBackupAsync (TimeSpan.FromSeconds (123))).Acknowledged, Is.True);
		Assert.That (rig.Handler.TegRequests, Has.Count.EqualTo (2));
		Assert.That (rig.Handler.TegRequests[0].CancelManualBackupEventRequest, Is.Not.Null);
		var schedule = rig.Handler.TegRequests[1].ScheduleManualBackupEventRequest.SchedulingInfo;
		Assert.That (schedule.DurationSeconds, Is.EqualTo (123));
		Assert.That (schedule.Priority, Is.EqualTo (ulong.MaxValue));
		Assert.That (schedule.StartTime.ToDateTimeOffset (), Is.InRange (before, DateTimeOffset.UtcNow));
		Assert.That (rig.Handler.Writes, Is.Empty, "Backup events do not overwrite config.json.");
		}

	[Test]
	public async Task BackupCancellation_IsExplicitAndSingle ()
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		Assert.That ((await rig.Powerwall.CancelLocalMaxBackupAsync ()).Acknowledged, Is.True);
		Assert.That (rig.Handler.TegRequests.Single ().CancelManualBackupEventRequest, Is.Not.Null);
		}

	[TestCase ("empty")]
	[TestCase ("unauthorized")]
	[TestCase ("timeout")]
	public async Task FailedCancellation_DoesNotScheduleOrReplay (string failure)
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		rig.Handler.TegFailure = failure;
		Assert.That (async () => await rig.Powerwall.ScheduleLocalMaxBackupAsync (TimeSpan.FromMinutes (2)), Throws.Exception);
		Assert.That (rig.Handler.TegRequests, Has.Count.EqualTo (1));
		Assert.That (rig.Handler.TegRequests.Single ().CancelManualBackupEventRequest, Is.Not.Null);
		Assert.That (rig.Handler.Logins, Is.EqualTo (1));
		}

	[TestCase (0)]
	[TestCase (59)]
	[TestCase (60.1)]
	[TestCase (4294967296)]
	public void InvalidBackupDuration_DoesNotContactGateway (double seconds)
		{
		using var rig = new Rig ();
		Assert.That (async () => await rig.Client.ScheduleMaxBackupAsync (TimeSpan.FromSeconds (seconds)), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (rig.Handler.Logins, Is.Zero);
		Assert.That (rig.Handler.TegRequests, Is.Empty);
		}

	[TestCase (false, 6, true)]
	[TestCase (true, 1, false)]
	public async Task GridControl_UsesDocumentedModeAndForceFields (bool connected, int mode, bool force)
		{
		using var rig = new Rig ();
		await rig.Client.AuthenticateAsync ();
		LocalGridCommandResult result = connected ? await rig.Powerwall.ReconnectGridAsync () : await rig.Powerwall.GoOffGridAsync ();
		Assert.That (result.ConnectToGrid, Is.EqualTo (connected));
		Assert.That (result.Acknowledged, Is.True);
		Assert.That (rig.Handler.TegRequests.Single ().SetIslandModeRequest.Mode, Is.EqualTo (mode));
		Assert.That (rig.Handler.TegRequests.Single ().SetIslandModeRequest.Force, Is.EqualTo (force));
		}

	[TestCase (0)]
	[TestCase (2)]
	[TestCase (99)]
	public async Task UnknownGridResult_IsNotReportedAsSuccess (int resultCode)
		{
		using var rig = new Rig ();
		rig.Handler.IslandResult = resultCode;
		await rig.Client.AuthenticateAsync ();
		LocalGridCommandResult result = await rig.Client.GoOffGridAsync ();
		Assert.That (result.Acknowledged, Is.False);
		Assert.That (result.ResultCode, Is.EqualTo (resultCode));
		}

	[Test]
	public async Task MissingGridReply_IsNotReportedAsSuccess ()
		{
		using var rig = new Rig ();
		rig.Handler.TegFailure = "empty";
		await rig.Client.AuthenticateAsync ();
		LocalGridCommandResult result = await rig.Client.GoOffGridAsync ();
		Assert.That (result.Acknowledged, Is.False);
		Assert.That (result.ResultCode, Is.Null);
		}

	[Test]
	public async Task AllTegWritesRequireExplicitControlPermission ()
		{
		using var rig = new Rig (allowControl: false);
		await rig.Client.AuthenticateAsync ();
		Assert.That (async () => await rig.Client.ScheduleMaxBackupAsync (TimeSpan.FromMinutes (2)), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (async () => await rig.Client.CancelMaxBackupAsync (), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (async () => await rig.Client.GoOffGridAsync (), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (async () => await rig.Client.ReconnectGridAsync (), Throws.TypeOf<PowerwallNotSupportedException> ());
		Assert.That (rig.Handler.TegRequests, Is.Empty);
		}

	[Test]
	public async Task BackupEventsRead_WorksWithControlsDisabledAndDoesNotCancel ()
		{
		using var rig = new Rig (allowControl: false);
		await rig.Client.AuthenticateAsync ();
		LocalBackupEvents result = await rig.Powerwall.GetLocalBackupEventsAsync ();
		Assert.That (result.ManualBackup, Is.Not.Null);
		Assert.That (result.ManualBackup!.IsActiveAt (DateTimeOffset.UtcNow), Is.False, "A future event is not yet active.");
		Assert.That (result.Events, Is.Empty);
		Assert.That (rig.Handler.TegRequests.Single ().GetBackupEventsRequest, Is.Not.Null);
		}

	[Test]
	public void BackupActivity_IsBoundedByStartAndEnd ()
		{
		var start = new DateTimeOffset (2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
		var schedule = new LocalBackupSchedule (start, 60, 0);
		Assert.That (schedule.IsActiveAt (start.AddSeconds (-1)), Is.False);
		Assert.That (schedule.IsActiveAt (start), Is.True);
		Assert.That (schedule.IsActiveAt (start.AddSeconds (59)), Is.True);
		Assert.That (schedule.IsActiveAt (start.AddSeconds (60)), Is.False);
		Assert.That ((schedule with { StartTime = null }).IsActiveAt (start), Is.False);
		}
	}
