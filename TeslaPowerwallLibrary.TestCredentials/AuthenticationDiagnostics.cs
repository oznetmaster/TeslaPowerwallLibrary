// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace TeslaPowerwallLibrary.TestCredentials;

// Only fixed categories and HTTP status numbers may leave the credential process.
internal sealed class AuthenticationDiagnostics : AppenderSkeleton, IDisposable
	{
	private readonly Hierarchy _repository;
	internal AuthenticationDiagnostics ()
		{
		Threshold = Level.Warn;
		_repository = (Hierarchy)LogManager.GetRepository (typeof (Powerwall).Assembly);
		_repository.Root.AddAppender (this);
		_repository.Configured = true;
		}

	protected override void Append (LoggingEvent loggingEvent)
		{
		string? summary = Summarize (loggingEvent.LoggerName, loggingEvent.RenderedMessage);
		if (summary is not null)
			Console.Error.WriteLine (summary);
		}

	internal static string? Summarize (string? logger, string? message)
		{
		if (logger != "TeslaPowerwallLibrary.FleetApi.FleetApiConnection" || message is null)
			return null;
		var status = Regex.Match (message, @"\ATesla FleetAPI token refresh failed \(HTTP ([1-5][0-9]{2})\)\.\z");
		if (status.Success)
			return "Fleet token refresh returned HTTP " + status.Groups[1].Value + ". No token retry was made by the test helper.";
		var dataStatus = Regex.Match (message, @"\ATesla FleetAPI [^\r\n]+ returned HTTP ([1-5][0-9]{2})(?: \(Gone\) - endpoint permanently removed)?\z");
		if (dataStatus.Success)
			return "Fleet data request returned HTTP " + dataStatus.Groups[1].Value + ". Request and response details withheld.";
		if (message.StartsWith ("Unable to refresh Tesla FleetAPI token:", StringComparison.Ordinal))
			return "Fleet token refresh encountered a network, TLS or request timeout error. Response details withheld.";
		if (message.StartsWith ("Unable to parse Tesla FleetAPI token refresh response:", StringComparison.Ordinal)
			|| message == "Tesla FleetAPI token refresh response did not contain an access token.")
			return "Fleet token refresh returned an invalid or incomplete response. Response details withheld.";
		if (message.StartsWith ("Unable to connect to Tesla FleetAPI ", StringComparison.Ordinal)
			|| message.StartsWith ("Timeout waiting for Tesla FleetAPI ", StringComparison.Ordinal))
			return "A Fleet data request encountered a connection or timeout error. Request details withheld.";
		return null;
		}

	internal static string DescribeFailure (Exception exception) => exception.Message switch
		{
			"Unable to obtain a Tesla FleetAPI access token from the supplied refresh token." => "Fleet token exchange did not complete.",
			"Unable to retrieve the Tesla FleetAPI product list. The access token may be expired or rejected." => "Fleet token exchange completed, but the product/site lookup failed.",
			"The authenticated site did not match the configured test site." => "The connected Fleet site did not match the configured test site.",
			_ => "Failure category: " + exception.GetType ().Name + ". Private details withheld."
			};

	public void Dispose () => Close ();

	protected override void OnClose ()
		{
		_repository.Root.RemoveAppender (this);
		base.OnClose ();
		}
	}