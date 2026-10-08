// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.Tests;

/// <summary>Checks LAN DNS fallback boundaries and response validation without network traffic.</summary>
[TestFixture]
public sealed class LocalLanDnsTests
	{
	/// <summary>Only local DHCP names are sent to LAN routers.</summary>
	[TestCase ("powerwall.local", "powerwall"), TestCase ("powerwall.LOCAL.", "powerwall"), TestCase ("powerwall", "powerwall")]
	[TestCase ("192.0.2.1", null), TestCase ("fe80::1", null), TestCase ("example.com", null), TestCase ("host.local.example", null)]
	public void Fallback_OnlyUsesLocalNames (string host, string? expected) => Assert.That (PowerwallLanDns.ShortName (host), Is.EqualTo (expected));

	/// <summary>Matching responses return the actual IPv4 record and reject unrelated IDs and names.</summary>
	[Test]
	public void Answers_RequireMatchingTransactionAndOwner ()
		{
		byte[] packet = Answer ();
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 18, 52, "powerwall")!.Single ().ToString (), Is.EqualTo ("192.0.2.7"));
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 19, 52, "powerwall"), Is.Null);
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 18, 52, "another-host"), Is.Empty);
		packet[3] = 3;
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 18, 52, "powerwall"), Is.Empty);
		}

	/// <summary>A router's zero-TTL DNS record is usable now; an mDNS goodbye record remains excluded.</summary>
	[Test]
	public void ZeroTtl_IsValidForDnsButNotMdns ()
		{
		byte[] packet = Answer ();
		packet[packet.Length - 7] = 0;
		Assert.That (MdnsPacket.Read (packet), Is.Empty);
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 18, 52, "powerwall")!.Single ().ToString (), Is.EqualTo ("192.0.2.7"));
		}

	/// <summary>Truncation and malformed records never become address candidates.</summary>
	[Test]
	public void Answers_RejectTruncationAndMalformedRecords ()
		{
		byte[] packet = Answer ();
		Assert.That (PowerwallLanDns.ReadAnswer (packet.Take (packet.Length - 1).ToArray (), 18, 52, "powerwall"), Is.Null);
		packet[2] |= 2;
		Assert.That (PowerwallLanDns.ReadAnswer (packet, 18, 52, "powerwall"), Is.Empty);
		}

	private static byte[] Answer ()
		{
		byte[] packet = MdnsPacket.Query ("powerwall", 1);
		packet[0] = 18; packet[1] = 52; packet[2] = 0x81; packet[7] = 1;
		byte[] answer = { 0xc0, 12, 0, 1, 0, 1, 0, 0, 0, 120, 0, 4, 192, 0, 2, 7 };
		return packet.Concat (answer).ToArray ();
		}
	}
