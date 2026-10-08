// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Security.Cryptography;
using System.Text;

using Google.Protobuf;

using Signed = TeslaPowerwallLibrary.Tedapi.Protocol.Signed;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Encodes the Powerwall 3 v1r signature using a caller-owned RSA key.</summary>
internal static class TedapiSigning
	{
	/// <summary>Signs one request with a fresh identifier and a short, explicit expiry.</summary>
	/// <param name="key">Registered 4096-bit RSA private key.</param>
	/// <param name="din">Gateway device identification number.</param>
	/// <param name="envelope">Encoded TEDAPI message envelope.</param>
	/// <param name="expiresAt">Signature expiry as Unix seconds.</param>
	/// <returns>The signed transport message.</returns>
	internal static Signed.RoutableMessage Sign (RSA key, string din, byte[] envelope, uint expiresAt)
		{
		if (key.KeySize != 4096)
			{
			throw new PowerwallInvalidConfigurationException ("Powerwall 3 v1r requires a 4096-bit RSA signing key.");
			}
		var identity = Encoding.UTF8.GetBytes (din);
		if (identity.Length == 0 || identity.Length > 255)
			{
			throw new PowerwallInvalidConfigurationException ("A valid gateway DIN is required for signing.");
			}
		using var stream = new MemoryStream ();
		var prefix = new byte[] { 0, 1, 7, 1, 1, 7, 2, (byte)identity.Length };
		stream.Write (prefix, 0, prefix.Length);
		stream.Write (identity, 0, identity.Length);
		var expiry = new byte[] { 4, 4, (byte)(expiresAt >> 24), (byte)(expiresAt >> 16), (byte)(expiresAt >> 8), (byte)expiresAt, 255 };
		stream.Write (expiry, 0, expiry.Length);
		stream.Write (envelope, 0, envelope.Length);
		return new Signed.RoutableMessage
			{
			ToDestination = new Signed.Destination { Domain = Signed.Domain.EnergyDevice },
			ProtobufMessageAsBytes = ByteString.CopyFrom (envelope),
			Uuid = ByteString.CopyFromUtf8 (Guid.NewGuid ().ToString ()),
			SignatureData = new Signed.SignatureData
				{
				SignerIdentity = new Signed.KeyIdentity { PublicKey = ByteString.CopyFrom (PublicKeyDer (key)) },
				RsaData = new Signed.RsaSignatureData
					{
					ExpiresAt = expiresAt,
					Signature = ByteString.CopyFrom (key.SignData (stream.ToArray (), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1))
					}
				}
			};
		}

	/// <summary>Exports PKCS#1 RSA public-key DER on both supported runtimes.</summary>
	/// <param name="key">Key whose public parameters will be exported.</param>
	/// <returns>DER-encoded public key; no private key material is exported.</returns>
	internal static byte[] PublicKeyDer (RSA key)
		{
		RSAParameters parameters = key.ExportParameters (false);
		return Der (0x30, Integer (parameters.Modulus!), Integer (parameters.Exponent!));
		}

	private static byte[] Integer (byte[] value) =>
		value[0] >= 128 ? Der (0x02, new byte[] { 0 }, value) : Der (0x02, value);

	private static byte[] Der (byte tag, params byte[][] values)
		{
		var length = values.Sum (static value => value.Length);
		using var stream = new MemoryStream ();
		stream.WriteByte (tag);
		if (length < 128)
			{
			stream.WriteByte ((byte)length);
			}
		else
			{
			var bytes = new List<byte> ();
			for (var remaining = length; remaining > 0; remaining >>= 8)
				{
				bytes.Insert (0, (byte)remaining);
				}
			stream.WriteByte ((byte)(0x80 | bytes.Count));
			foreach (var value in bytes)
				{
				stream.WriteByte (value);
				}
			}
		foreach (byte[] value in values)
			{
			stream.Write (value, 0, value.Length);
			}
		return stream.ToArray ();
		}
	}
