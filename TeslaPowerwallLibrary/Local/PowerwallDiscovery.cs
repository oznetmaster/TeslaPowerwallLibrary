// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Net.Sockets;

namespace TeslaPowerwallLibrary.Local;

/// <summary>A local Powerwall address resolved through the operating system's DNS resolver.</summary>
/// <param name="Host">Original hostname or IP literal, retained so DHCP changes can be resolved again.</param>
/// <param name="Port">HTTPS service port.</param>
/// <param name="Addresses">Addresses returned by the resolver; these are not proof of device identity.</param>
public sealed record PowerwallHost (string Host, int Port, IReadOnlyList<IPAddress> Addresses);

/// <summary>Resolves local Powerwall hostnames and IP literals without logging in or changing gateway settings.</summary>
public static class PowerwallDiscovery
	{
	/// <summary>Browses local IPv4 mDNS/DNS-SD advertisements for candidate Powerwall hostnames.</summary>
	/// <param name="duration">Browse duration; defaults to three seconds and must not exceed thirty seconds.</param>
	/// <param name="cancellationToken">Cancels browsing.</param>
	/// <returns>Unverified candidates. Firmware without DNS-SD advertisements may require an explicit hostname.
	/// Discovery never scans a subnet, authenticates a device or selects a device automatically.</returns>
	public static Task<IReadOnlyList<PowerwallHost>> DiscoverAsync (TimeSpan? duration = null, CancellationToken cancellationToken = default) =>
		PowerwallMdns.BrowseAsync (duration ?? TimeSpan.FromSeconds (3), cancellationToken);

	/// <summary>Resolves a local address, asking active Ethernet/Wi-Fi routers for a DHCP IPv4 name when normal DNS has no IPv4 result.</summary>
	/// <param name="host">Configured hostname or address, optionally including the HTTPS port.</param>
	/// <param name="cancellationToken">Cancels DNS and the bounded router fallback.</param>
	/// <returns>Unverified candidates with IPv4 first. The original hostname and port are retained; no connection is made.</returns>
	/// <remarks>Router lookup is limited to single-label names and their .local equivalents. This does not change system DNS or authenticate a device.</remarks>
	public static async Task<PowerwallHost> ResolveLanAsync (string host, CancellationToken cancellationToken = default)
		{
		Uri endpoint = LocalEndpoint.Create (host);
		string name = endpoint.DnsSafeHost.Trim ('[', ']');
		string? shortName = PowerwallLanDns.ShortName (name);
		PowerwallHost result;
		try { result = await ResolveAsync (host, cancellationToken).ConfigureAwait (false); }
		catch (SocketException) when (shortName is not null) { result = new PowerwallHost (name, endpoint.Port, Array.Empty<IPAddress> ()); }
		if (shortName is not null && !result.Addresses.Any (a => a.AddressFamily == AddressFamily.InterNetwork))
			{
			try
				{
				var ipv4 = await PowerwallLanDns.ResolveAsync (shortName, cancellationToken).ConfigureAwait (false);
				result = result with { Addresses = ipv4.Concat (result.Addresses).Distinct ().ToArray () };
				}
			catch (System.Net.NetworkInformation.NetworkInformationException) { }
			}
		return result with { Addresses = result.Addresses.OrderBy (a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray () };
		}

	/// <summary>Resolves a hostname, IPv4 address or bracketed IPv6 address, optionally followed by an HTTPS port.</summary>
	/// <param name="host">Hostname or IP literal. A bare IPv6 literal is also accepted with the default port.</param>
	/// <param name="cancellationToken">Cancels waiting for name resolution.</param>
	/// <returns>The configured host and its current addresses. Resolution does not authenticate the device.</returns>
	/// <exception cref="ArgumentException">The host contains a URL, path, credentials or an invalid port.</exception>
	/// <exception cref="SocketException">The hostname could not be resolved.</exception>
	public static async Task<PowerwallHost> ResolveAsync (string host, CancellationToken cancellationToken = default)
		{
		Uri endpoint = LocalEndpoint.Create (host);
		cancellationToken.ThrowIfCancellationRequested ();
		var name = endpoint.DnsSafeHost.Trim ('[', ']');
		if (IPAddress.TryParse (name, out IPAddress? literal))
			{
			return new PowerwallHost (name, endpoint.Port, new[] { literal });
			}

		#if NETFRAMEWORK
		Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync (name);
#else
		Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync (name, cancellationToken);
#endif
		var canceled = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		using (cancellationToken.Register (() => canceled.TrySetResult (true)))
			{
			if (await Task.WhenAny (lookup, canceled.Task).ConfigureAwait (false) != lookup)
				{
				// Observe a late resolver failure without delaying cancellation.
				_ = lookup.ContinueWith (static task => _ = task.Exception,
					CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
				cancellationToken.ThrowIfCancellationRequested ();
				}
			}
		return new PowerwallHost (name, endpoint.Port, await lookup.ConfigureAwait (false));
		}
	}

/// <summary>Validates local HTTPS authorities without allowing paths, credentials or another scheme.</summary>
internal static class LocalEndpoint
	{
	/// <summary>Creates a gateway base URI from a hostname or IP address with an optional port.</summary>
	/// <param name="host">The configured authority.</param>
	/// <returns>A validated HTTPS base URI.</returns>
	internal static Uri Create (string host)
		{
		if (string.IsNullOrWhiteSpace (host) || host != host.Trim () ||
			host.Any (static character => character is '/' or '\\' or '@' or '?' or '#'))
			{
			throw new ArgumentException ("Supply a hostname or IP address, optionally with an HTTPS port.", nameof (host));
			}
		var authority = host;
		if (host[0] != '[' && IPAddress.TryParse (host, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetworkV6)
			{
			authority = "[" + host + "]";
			}
		if (!Uri.TryCreate ("https://" + authority + "/", UriKind.Absolute, out Uri? endpoint) ||
			endpoint.Port < 1 || Uri.CheckHostName (endpoint.Host.Trim ('[', ']')) == UriHostNameType.Unknown)
			{
			throw new ArgumentException ("Invalid gateway hostname, IP address or port.", nameof (host));
			}
		return endpoint;
		}
	}
