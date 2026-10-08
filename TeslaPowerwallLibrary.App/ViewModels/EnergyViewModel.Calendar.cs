// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;

namespace TeslaPowerwallLibrary.App.ViewModels;

public sealed partial class EnergyViewModel
	{
	/// <summary>Advances a graph following the current calendar period, preserving deliberately selected history.</summary>
	/// <param name="now">Current time, interpreted using the graph's local time zone.</param>
	/// <returns>Whether the displayed period changed. No cloud or device request is made.</returns>
	internal bool AdvanceCurrentPeriod (DateTimeOffset now)
		{
		if (_disposed)
			return false;
		NextPeriodCommand.NotifyCanExecuteChanged ();
		if (!_followCurrentPeriod || SelectedPeriod == LifetimePeriod
			|| GetPeriodRange (SelectedPeriod, _anchor).Start == GetPeriodRange (SelectedPeriod, now).Start)
			return false;
		_generation++; // An outstanding history response belongs to the previous period.
		_anchor = now;
		var (start, end) = GetPeriodRange ("day", _anchor);
		_localSamples.RemoveAll (point => point.ReceivedAt < start || point.ReceivedAt >= end.AddSeconds (1));
		ResolveRange ();
		ClearHistory ();
		HistorySourceLabel = IsLocal ? "Live readings: LAN. Cloud history has not been loaded for this period."
			: "History has not been loaded for this period.";
		PreviousPeriodCommand.NotifyCanExecuteChanged ();
		return true;
		}

	private void OnCalendarBoundary (object? sender, EventArgs e)
		{
		_calendarTimer?.Stop ();
		if (_disposed)
			return;
		if (AdvanceCurrentPeriod (_now ()) && IsLocal)
			_ = LoadLocalHistoryAsync ();
		ScheduleCalendarBoundary ();
		}

	private void ScheduleCalendarBoundary ()
		{
		if (_calendarTimer is null || _disposed)
			return;
		var now = _now ();
		var tomorrow = now.ToLocalTime ().Date.AddDays (1);
		var delay = LocalMidnight (tomorrow.Year, tomorrow.Month, tomorrow.Day) - now;
		_calendarTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds (1);
		_calendarTimer.Start ();
		}
	}
