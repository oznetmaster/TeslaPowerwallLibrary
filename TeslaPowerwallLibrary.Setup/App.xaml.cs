// Copyright © 2026 Neil Colvin.
// Adapted from the Python pypowerwall project Copyright © 2022 Jason A. Cox.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows;

namespace TeslaPowerwallLibrary.Setup;

/// <summary>
/// Interaction logic for the Tesla Powerwall setup application. This Windows tool is a thin wrapper
/// around the shared <c>TeslaPowerwallLibrary.Login</c> library: it performs the Tesla OAuth 2.0 PKCE
/// login through an embedded browser and presents the resulting refresh and access tokens for manual
/// copy/paste into an application's configuration.
/// </summary>
public partial class App : Application
	{
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
			if (string.Equals (arg, "--region", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
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
