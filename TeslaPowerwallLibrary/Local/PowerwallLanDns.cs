// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TeslaPowerwallLibrary.Local;

/// <summary>Queries DHCP-aware LAN routers for an IPv4 address without changing system DNS or scanning the network.</summary>
internal static class PowerwallLanDns
	{
	/// <summary>Returns the single-label DHCP name eligible for LAN-only lookup.</summary>
	/// <param name="host">Configured hostname.</param>
	/// <returns>A short hostname, or null for IP literals and non-local fully qualified names.</returns>
	internal static string? ShortName (string host)
		{
		string name = host.TrimEnd ('.');
		if (name.EndsWith (".local", StringComparison.OrdinalIgnoreCase)) name = name.Substring (0, name.Length - 6);
		return name.Length is > 0 and <= 63 && !name.Any (character => character == '.') && Uri.CheckHostName (name) == UriHostNameType.Dns ? name : null;
		}

	/// <summary>Asks active Ethernet/Wi-Fi gateways for the DHCP hostname with a bounded deadline.</summary>
	/// <param name="name">Single-label hostname.</param>
	/// <param name="cancellationToken">Cancels all interface queries.</param>
	/// <returns>Unverified IPv4 address candidates.</returns>
	internal static async Task<IReadOnlyList<IPAddress>> ResolveAsync (string name, CancellationToken cancellationToken)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (TimeSpan.FromSeconds (2));
		var routes = NetworkInterface.GetAllNetworkInterfaces ()
			.Where (adapter => adapter.OperationalStatus == OperationalStatus.Up
				&& adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
			.SelectMany (adapter =>
				{
				IPInterfaceProperties properties = adapter.GetIPProperties ();
				return properties.UnicastAddresses.Where (a => a.Address.AddressFamily == AddressFamily.InterNetwork)
					.SelectMany (a => properties.GatewayAddresses.Where (g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals (IPAddress.Any))
						.Select (g => (Local: a.Address, Router: g.Address)));
				}).Distinct ().ToArray ();
		var answers = await Task.WhenAll (routes.Select (route => QueryAsync (name, route.Local, route.Router, deadline.Token))).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		return answers.SelectMany (a => a).Distinct ().ToArray ();
		}

	private static async Task<IReadOnlyList<IPAddress>> QueryAsync (string name, IPAddress local, IPAddress router, CancellationToken token)
		{
		try
			{
			using var socket = new UdpClient (new IPEndPoint (local, 0));
			socket.Connect (router, 53);
			using var registration = token.Register (socket.Close);
			byte[] query = MdnsPacket.Query (name, 1);
			byte[] id = Guid.NewGuid ().ToByteArray ();
			query[0] = id[0]; query[1] = id[1]; query[2] = 1;
			query[query.Length - 2] = 0; // Ordinary IN question, not an mDNS unicast-response flag.
			await socket.SendAsync (query, query.Length).ConfigureAwait (false);
			while (!token.IsCancellationRequested)
				{
				#if NETFRAMEWORK
				UdpReceiveResult response = await socket.ReceiveAsync ().ConfigureAwait (false);
#else
				UdpReceiveResult response = await socket.ReceiveAsync (CancellationToken.None).ConfigureAwait (false);
#endif
				var addresses = ReadAnswer (response.Buffer, id[0], id[1], name);
				if (addresses is not null) return addresses;
				}
			}
		catch (Exception exc) when (exc is SocketException or ObjectDisposedException or OperationCanceledException)
			{
			// Router DNS is optional; retain the operating system's resolution on failure.
			}
		return Array.Empty<IPAddress> ();
		}

	/// <summary>Accepts matching successful DNS answers and returns only the queried name's IPv4 records.</summary>
	/// <param name="packet">DNS response.</param>
	/// <param name="idHigh">Expected transaction identifier high byte.</param>
	/// <param name="idLow">Expected transaction identifier low byte.</param>
	/// <param name="name">Requested DHCP hostname.</param>
	/// <returns>Matching records, an empty answer, or null for an unrelated or malformed packet.</returns>
	internal static IReadOnlyList<IPAddress>? ReadAnswer (byte[] packet, byte idHigh, byte idLow, string name)
		{
		if (packet.Length < 12 || packet[0] != idHigh || packet[1] != idLow || (packet[2] & 0x80) == 0) return null;
		if ((packet[2] & 0x7a) != 0 || (packet[3] & 0x0f) != 0) return Array.Empty<IPAddress> ();
		try
			{
			return MdnsPacket.Read (packet, includeZeroTtl: true).Where (r => r.Type == 1 && r.Address is not null
				&& string.Equals (r.Name.TrimEnd ('.'), name.TrimEnd ('.'), StringComparison.OrdinalIgnoreCase))
				.Select (r => r.Address!).Distinct ().ToArray ();
			}
		catch (InvalidDataException) { return null; }
		}
	}
