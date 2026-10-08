// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TeslaPowerwallLibrary.Local;

/// <summary>Bounded DNS-SD browsing on active IPv4 interfaces. Advertisements are unverified candidates.</summary>
internal static class PowerwallMdns
	{
	private static readonly Regex _deviceName = new (@"^(?:[0-9]{7}-[0-9]{2}-[A-Za-z]--[A-Za-z0-9]+|powerwall(?:-[A-Za-z0-9-]+)?)(?:\.local)?\.?$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds (1));

	/// <summary>Browses advertised services without scanning addresses or attempting authentication.</summary>
	/// <param name="duration">Maximum browse duration.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>Candidate gateway hosts, deduplicated by hostname and port.</returns>
	internal static async Task<IReadOnlyList<PowerwallHost>> BrowseAsync (TimeSpan duration, CancellationToken cancellationToken)
		{
		if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds (30))
			{
			throw new ArgumentOutOfRangeException (nameof (duration), "Discovery must last between zero and thirty seconds.");
			}
		cancellationToken.ThrowIfCancellationRequested ();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (duration);
		IPAddress[] addresses = NetworkInterface.GetAllNetworkInterfaces ()
			.Where (static adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany (static adapter => adapter.GetIPProperties ().UnicastAddresses)
			.Select (static address => address.Address)
			.Where (static address => address.AddressFamily == AddressFamily.InterNetwork).Distinct ().ToArray ();
		IReadOnlyList<PowerwallHost>[] results = await Task.WhenAll (addresses.Select (
			address => BrowseInterfaceAsync (address, deadline.Token))).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		return results.SelectMany (static result => result)
			.GroupBy (static result => result.Host + ":" + result.Port, StringComparer.OrdinalIgnoreCase)
			.Select (static group => new PowerwallHost (group.First ().Host, group.First ().Port,
				group.SelectMany (static result => result.Addresses).Distinct ().ToArray ())).ToArray ();
		}

	private static async Task<IReadOnlyList<PowerwallHost>> BrowseInterfaceAsync (IPAddress address, CancellationToken cancellationToken)
		{
		var records = new List<MdnsRecord> ();
		try
			{
			using var socket = new UdpClient (new IPEndPoint (address, 0));
			socket.Client.SetSocketOption (SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
			using CancellationTokenRegistration registration = cancellationToken.Register (socket.Close);
			var asked = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			var multicast = new IPEndPoint (IPAddress.Parse ("224.0.0.251"), 5353);
			async Task AskAsync (string name, ushort type)
				{
				if (asked.Count >= 64 || !asked.Add (name))
					{
					return;
					}
				byte[] query = MdnsPacket.Query (name, type);
				await socket.SendAsync (query, query.Length, multicast).ConfigureAwait (false);
				}
			await AskAsync ("_services._dns-sd._udp.local", 12).ConfigureAwait (false);
			await AskAsync ("_https._tcp.local", 12).ConfigureAwait (false);
			await AskAsync ("_http._tcp.local", 12).ConfigureAwait (false);
			while (!cancellationToken.IsCancellationRequested && records.Count < 4096)
				{
				#if NETFRAMEWORK
				UdpReceiveResult packet = await socket.ReceiveAsync ().ConfigureAwait (false);
#else
				UdpReceiveResult packet = await socket.ReceiveAsync (CancellationToken.None).ConfigureAwait (false);
#endif
				IReadOnlyList<MdnsRecord> received;
				try
					{
					received = MdnsPacket.Read (packet.Buffer);
					}
				catch (InvalidDataException)
					{
					continue;
					}
				records.AddRange (received.Take (4096 - records.Count));
				foreach (MdnsRecord record in received)
					{
					if (record.Type == 12 && record.Target is string target && target.EndsWith (".local", StringComparison.OrdinalIgnoreCase))
						{
						await AskAsync (target, record.Name == "_services._dns-sd._udp.local" ? (ushort)12 : (ushort)255).ConfigureAwait (false);
						}
					if (record.Type == 33 && record.Target is string host && _deviceName.IsMatch (host))
						{
						await AskAsync (host, 255).ConfigureAwait (false);
						}
					}
				}
			}
		catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
			{
			}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
		catch (SocketException)
			{
			// An unavailable multicast interface must not hide results from other interfaces.
			}
		var hosts = records.Where (static record => record.Type == 33 && record.Port > 0 && record.Target is not null)
			.Where (record => _deviceName.IsMatch (record.Target!))
			.Select (record => new PowerwallHost (record.Target!, record.Port,
				records.Where (value => value.Address is not null && string.Equals (value.Name, record.Target, StringComparison.OrdinalIgnoreCase))
					.Select (static value => value.Address!).Distinct ().ToArray ())).ToList ();
		hosts.AddRange (records.Where (record => record.Address is not null && _deviceName.IsMatch (record.Name))
			.Select (static record => new PowerwallHost (record.Name, 443, new[] { record.Address! })));
		return hosts;
		}
	}

/// <summary>A DNS resource record needed for DNS-SD discovery.</summary>
/// <param name="Name">Record owner.</param>
/// <param name="Type">DNS record type.</param>
/// <param name="Target">PTR or SRV target.</param>
/// <param name="Port">SRV service port.</param>
/// <param name="Address">A or AAAA address.</param>
internal sealed record MdnsRecord (string Name, ushort Type, string? Target, int Port, IPAddress? Address);

/// <summary>Minimal bounded DNS packet codec for unicast-response multicast discovery.</summary>
internal static class MdnsPacket
	{
	/// <summary>Builds one DNS question requesting a unicast answer.</summary>
	/// <param name="name">Question name.</param>
	/// <param name="type">DNS record type.</param>
	/// <returns>The encoded question.</returns>
	internal static byte[] Query (string name, ushort type)
		{
		using var stream = new MemoryStream ();
		stream.Write (new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 12);
		foreach (string label in name.TrimEnd ('.').Split ('.'))
			{
			byte[] bytes = Encoding.UTF8.GetBytes (label);
			if (bytes.Length is 0 or > 63)
				{
				throw new ArgumentException ("Invalid DNS label.", nameof (name));
				}
			stream.WriteByte ((byte)bytes.Length);
			stream.Write (bytes, 0, bytes.Length);
			}
		stream.WriteByte (0);
		stream.WriteByte ((byte)(type >> 8));
		stream.WriteByte ((byte)type);
		stream.WriteByte (0x80);
		stream.WriteByte (1);
		return stream.ToArray ();
		}

	/// <summary>Reads discovery records while rejecting truncated messages and compression loops.</summary>
	/// <param name="data">Received DNS packet.</param>
	/// <param name="includeZeroTtl">True for ordinary DNS responses, where a zero TTL means use without caching; false for mDNS goodbye records.</param>
	/// <returns>Parsed records. Query packets are ignored.</returns>
	internal static IReadOnlyList<MdnsRecord> Read (byte[] data, bool includeZeroTtl = false)
		{
		if (data.Length < 12)
			{
			throw new InvalidDataException ("Truncated DNS header.");
			}
		var offset = 4;
		var questions = Number (data, ref offset);
		var count = Number (data, ref offset) + Number (data, ref offset) + Number (data, ref offset);
		if ((data[2] & 0x80) == 0 || count > 1024 || questions > 1024)
			{
			return Array.Empty<MdnsRecord> ();
			}
		for (var i = 0; i < questions; i++)
			{
			_ = Name (data, ref offset);
			offset += 4;
			}
		var records = new List<MdnsRecord> ();
		for (var i = 0; i < count; i++)
			{
			var name = Name (data, ref offset);
			var type = Number (data, ref offset);
			var recordClass = Number (data, ref offset);
			if (offset + 4 > data.Length)
				{
				throw new InvalidDataException ("Truncated DNS record.");
				}
			var expired = data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 0 && data[offset + 3] == 0;
			offset += 4;
			var length = Number (data, ref offset);
			var end = offset + length;
			if (end > data.Length)
				{
				throw new InvalidDataException ("Truncated DNS record data.");
				}
			string? target = null;
			IPAddress? address = null;
			var port = 443;
			if (type == 12)
				{
				target = Name (data, ref offset);
				}
			else if (type == 33 && length >= 7)
				{
				offset += 4;
				port = Number (data, ref offset);
				target = Name (data, ref offset);
				}
			else if ((type == 1 && length == 4) || (type == 28 && length == 16))
				{
				var bytes = new byte[length];
				Array.Copy (data, offset, bytes, 0, length);
				address = new IPAddress (bytes);
				}
			if (offset > end)
				{
				throw new InvalidDataException ("DNS name exceeds its record.");
				}
			if ((!expired || includeZeroTtl) && (recordClass & 0x7fff) == 1)
				{
				records.Add (new MdnsRecord (name, type, target, port, address));
				}
			offset = end;
			}
		return records;
		}

	private static ushort Number (byte[] data, ref int offset)
		{
		if (offset + 2 > data.Length)
			{
			throw new InvalidDataException ("Truncated DNS number.");
			}
		var value = (ushort)((data[offset] << 8) | data[offset + 1]);
		offset += 2;
		return value;
		}

	private static string Name (byte[] data, ref int offset)
		{
		var current = offset;
		var jumped = false;
		var labels = new List<string> ();
		for (var depth = 0; depth < 128; depth++)
			{
			if (current >= data.Length)
				{
				throw new InvalidDataException ("Truncated DNS name.");
				}
			var length = data[current++];
			if ((length & 0xc0) == 0xc0)
				{
				if (current >= data.Length)
					{
					throw new InvalidDataException ("Truncated DNS pointer.");
					}
				var pointer = ((length & 0x3f) << 8) | data[current++];
				if (!jumped)
					{
					offset = current;
					}
				current = pointer;
				jumped = true;
				continue;
				}
			if (length > 63 || current + length > data.Length)
				{
				throw new InvalidDataException ("Invalid DNS name.");
				}
			if (!jumped)
				{
				offset = current + length;
				}
			if (length == 0)
				{
				var result = string.Join (".", labels);
				if (result.Length > 253)
					{
					throw new InvalidDataException ("DNS name is too long.");
					}
				return result;
				}
			labels.Add (Encoding.UTF8.GetString (data, current, length));
			current += length;
			}
		throw new InvalidDataException ("DNS compression loop.");
		}
	}
