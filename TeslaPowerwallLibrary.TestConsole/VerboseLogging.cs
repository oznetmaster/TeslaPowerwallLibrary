// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>
/// Configures log4net to emit the library's log output to the console when verbose mode is enabled.
/// </summary>
internal static class VerboseLogging
	{
	private static bool _enabled;

	/// <summary>Enables console logging for the library's log4net repository. Safe to call more than once.</summary>
	public static void Enable ()
		{
		if (_enabled)
			return;

		_enabled = true;

		try
			{
			var layout = new log4net.Layout.PatternLayout ("%date{HH:mm:ss} %-5level %logger{1} - %message%newline");
			layout.ActivateOptions ();

			var appender = new log4net.Appender.ConsoleAppender { Layout = layout };
			appender.ActivateOptions ();

			var repository = log4net.LogManager.GetRepository (typeof (Powerwall).Assembly);
			log4net.Config.BasicConfigurator.Configure (repository, appender);
			}
		catch (Exception exc)
			{
			ConsoleHelpers.WriteError ($"Failed to enable verbose logging: {exc.Message}");
			}
		}
	}
