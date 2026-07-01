// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>
/// Interaction logic for the Tesla Powerwall setup application. This Windows tool adapts the
/// upstream <c>python -m pypowerwall authtoken</c> command, performing the Tesla OAuth 2.0 PKCE
/// login in an embedded browser and presenting the resulting refresh and access tokens.
/// </summary>
public partial class App : Application
	{
	/// <summary>
	/// Gets a value indicating whether the app was launched in non-interactive "emit" mode
	/// (via <c>--emit</c>), in which it auto-starts the login and writes the captured tokens to
	/// standard output for a calling process to consume.
	/// </summary>
	public static bool EmitMode { get; private set; }

	/// <summary>Gets the Tesla region requested on the command line (<c>us</c> or <c>cn</c>); defaults to <c>us</c>.</summary>
	public static string Region { get; private set; } = "us";

	/// <inheritdoc/>
	protected override void OnStartup (StartupEventArgs e)
		{
		ParseArguments (e.Args);
		base.OnStartup (e);
		}

	private static void ParseArguments (string[] args)
		{
		for (var index = 0; index < args.Length; index++)
			{
			var arg = args[index];
			if (string.Equals (arg, "--emit", StringComparison.OrdinalIgnoreCase))
				{
				EmitMode = true;
				}
			else if (string.Equals (arg, "--region", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
				{
				var region = args[++index];
				if (string.Equals (region, "us", StringComparison.OrdinalIgnoreCase)
					|| string.Equals (region, "cn", StringComparison.OrdinalIgnoreCase))
					{
					Region = region.ToLowerInvariant ();
					}
				}
			}
		}
	}
