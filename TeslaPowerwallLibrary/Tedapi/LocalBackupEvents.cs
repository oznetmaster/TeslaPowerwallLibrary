// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using TeslaPowerwallLibrary.Local;

using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Timing and priority reported for a local backup event.</summary>
/// <param name="StartTime">UTC start time, or null when omitted.</param>
/// <param name="DurationSeconds">Reported event duration in seconds.</param>
/// <param name="Priority">Gateway scheduling priority.</param>
public sealed record LocalBackupSchedule (
	[property: JsonPropertyName ("start_time")] DateTimeOffset? StartTime,
	[property: JsonPropertyName ("duration_seconds")] long DurationSeconds,
	[property: JsonPropertyName ("priority")] decimal Priority)
	{
	/// <summary>Determines whether the reported interval contains the supplied time.</summary>
	/// <param name="time">Time at which to evaluate the schedule.</param>
	/// <returns>True only while the reported schedule has started and has not expired.</returns>
	public bool IsActiveAt (DateTimeOffset time) => StartTime is DateTimeOffset start
		&& time >= start && time - start < TimeSpan.FromSeconds (DurationSeconds);
	}

/// <summary>A gateway-reported scheduled backup event.</summary>
/// <param name="Id">Gateway event identifier.</param>
/// <param name="Name">Event name.</param>
/// <param name="Schedule">Event timing, or null if omitted.</param>
public sealed record LocalBackupEvent (
	[property: JsonPropertyName ("id")] string Id,
	[property: JsonPropertyName ("name")] string Name,
	[property: JsonPropertyName ("schedule")] LocalBackupSchedule? Schedule);

/// <summary>Manual and scheduled backup events read from the local gateway.</summary>
/// <param name="ManualBackup">Manual backup timing, or null when none was reported.</param>
/// <param name="Events">Other reported backup events.</param>
public sealed record LocalBackupEvents (
	[property: JsonPropertyName ("manual_backup")] LocalBackupSchedule? ManualBackup,
	[property: JsonPropertyName ("events")] IReadOnlyList<LocalBackupEvent> Events);

/// <summary>Response to a grid contactor command; acknowledgement does not prove physical grid state.</summary>
/// <param name="ConnectToGrid">True for a reconnection request; false for intentional islanding.</param>
/// <param name="ResultCode">Gateway result code, or null when no matching response was returned. One is the upstream-observed success code.</param>
public sealed record LocalGridCommandResult (
	[property: JsonPropertyName ("connect_to_grid")] bool ConnectToGrid,
	[property: JsonPropertyName ("result_code")] int? ResultCode)
	{
	/// <summary>Whether the gateway returned the observed successful acknowledgement code.</summary>
	[JsonPropertyName ("acknowledged")]
	public bool Acknowledged => ResultCode == 1;
	}

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Reads manual and scheduled backup events over signed local access without changing them.</summary>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Typed reported backup events.</returns>
	public async Task<LocalBackupEvents> GetBackupEventsAsync (CancellationToken cancellationToken = default)
		{
		RequireSignedAccess ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			Signed.TEGMessages result = await SendTegAsync (new Signed.TEGMessages
				{ GetBackupEventsRequest = new Signed.TEGAPIGetBackupEventsRequest () }, false, cancellationToken).ConfigureAwait (false);
			Signed.TEGAPIGetBackupEventsResponse reply = result.GetBackupEventsResponse
				?? throw new PowerwallConnectionException ("The gateway did not return backup events.");
			return new LocalBackupEvents (Schedule (reply.ManualBackupEvent?.SchedulingInfo),
				reply.BackupEvents.Select (static item => new LocalBackupEvent (item.Id, item.Name, Schedule (item.SchedulingInfo))).ToArray ());
			}
		finally { _gate.Release (); }
		}

	/// <summary>Replaces the manual backup event with a new maximum-backup interval starting now.</summary>
	/// <remarks>Requires AllowLocalControl. Cancels the previous event first. If scheduling fails, the cancellation may already have taken effect; read events before retrying.</remarks>
	/// <param name="duration">Whole-second duration of at least one minute, representable as an unsigned 32-bit seconds value.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The gateway's scheduling acknowledgement.</returns>
	public async Task<LocalCommandResult> ScheduleMaxBackupAsync (TimeSpan duration, CancellationToken cancellationToken = default)
		{
		double seconds = duration.TotalSeconds;
		if (seconds < 60 || seconds > uint.MaxValue || seconds != Math.Truncate (seconds))
			throw new ArgumentOutOfRangeException (nameof (duration), "Backup duration must be whole seconds, at least 60, within the protocol range.");
		RequireSignedControl ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			await CancelMaxBackupCoreAsync (cancellationToken).ConfigureAwait (false);
			Signed.TEGMessages reply = await SendTegAsync (new Signed.TEGMessages
				{
				ScheduleManualBackupEventRequest = new Signed.TEGAPIScheduleManualBackupEventRequest
					{
					SchedulingInfo = new Signed.ControlEventSchedulingInfo
						{ StartTime = Timestamp.FromDateTimeOffset (DateTimeOffset.UtcNow), DurationSeconds = (uint)seconds, Priority = ulong.MaxValue }
					}
				}, true, cancellationToken).ConfigureAwait (false);
			if (reply.ScheduleManualBackupEventResponse is null)
				throw new PowerwallConnectionException ("The previous backup event was cancelled, but the new schedule was not acknowledged. Read events before retrying.");
			return new LocalCommandResult (true);
			}
		finally
			{
			_cache.Clear ();
			_gate.Release ();
			}
		}

	/// <summary>Cancels the manual maximum-backup event over signed local access.</summary>
	/// <remarks>Requires AllowLocalControl. This may change battery charging and discharge behavior.</remarks>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The gateway's cancellation acknowledgement.</returns>
	public async Task<LocalCommandResult> CancelMaxBackupAsync (CancellationToken cancellationToken = default)
		{
		RequireSignedControl ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			await CancelMaxBackupCoreAsync (cancellationToken).ConfigureAwait (false);
			return new LocalCommandResult (true);
			}
		finally
			{
			_cache.Clear ();
			_gate.Release ();
			}
		}

	/// <summary>Requests intentional islanding by opening the grid contactor.</summary>
	/// <remarks>Requires AllowLocalControl. This physically changes the household's grid connection; acknowledgement is not physical confirmation.</remarks>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The reported contactor command result.</returns>
	public Task<LocalGridCommandResult> GoOffGridAsync (CancellationToken cancellationToken = default) =>
		SetGridConnectionAsync (false, cancellationToken);

	/// <summary>Requests reconnection to the grid by closing the grid contactor.</summary>
	/// <remarks>Requires AllowLocalControl. This physically changes the household's grid connection; acknowledgement is not physical confirmation.</remarks>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The reported contactor command result.</returns>
	public Task<LocalGridCommandResult> ReconnectGridAsync (CancellationToken cancellationToken = default) =>
		SetGridConnectionAsync (true, cancellationToken);

	private async Task<LocalGridCommandResult> SetGridConnectionAsync (bool connected, CancellationToken cancellationToken)
		{
		RequireSignedControl ();
		await _gate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			EnsureConnected ();
			Signed.TEGMessages reply = await SendTegAsync (new Signed.TEGMessages
				{ SetIslandModeRequest = new Signed.TEGAPISetIslandModeRequest { Mode = connected ? 1 : 6, Force = !connected } }, true, cancellationToken).ConfigureAwait (false);
			return new LocalGridCommandResult (connected, reply.SetIslandModeResponse?.Result);
			}
		finally
			{
			_cache.Clear ();
			_gate.Release ();
			}
		}

	private async Task CancelMaxBackupCoreAsync (CancellationToken cancellationToken)
		{
		Signed.TEGMessages reply = await SendTegAsync (new Signed.TEGMessages
			{ CancelManualBackupEventRequest = new Signed.TEGAPICancelManualBackupEventRequest () }, true, cancellationToken).ConfigureAwait (false);
		if (reply.CancelManualBackupEventResponse is null)
			throw new PowerwallConnectionException ("The gateway did not acknowledge cancellation of the manual backup event. Read events before retrying.");
		}

	private async Task<Signed.TEGMessages> SendTegAsync (Signed.TEGMessages message, bool control, CancellationToken cancellationToken)
		{
		Signed.MessageEnvelope envelope = CommandEnvelope ();
		envelope.Teg = message;
		byte[] reply = await ExchangeEnvelopeAsync (envelope.ToByteArray (), cancellationToken, allowAuthenticationRetry: !control).ConfigureAwait (false);
		return Signed.MessageEnvelope.Parser.ParseFrom (reply).Teg
			?? throw new PowerwallConnectionException ("The gateway did not return a matching local command response.");
		}

	private static LocalBackupSchedule? Schedule (Signed.ControlEventSchedulingInfo? value) => value is null ? null :
		new LocalBackupSchedule (value.StartTime?.ToDateTimeOffset (), value.DurationSeconds, value.Priority);

	private void RequireSignedAccess ()
		{
		if (!IsSigned)
			throw new PowerwallNotSupportedException ("This operation requires signed LAN access.");
		}
	}
