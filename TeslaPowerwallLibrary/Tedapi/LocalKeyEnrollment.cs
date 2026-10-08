// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Tedapi;

/// <summary>Gateway authorization state for a particular local signing key.</summary>
public enum LocalKeyState
	{
	/// <summary>No recognized state was returned; authorization has not been established.</summary>
	Unknown = 0,
	/// <summary>The gateway is waiting for physical verification.</summary>
	PendingVerification = 1,
	/// <summary>The verification window expired. The same key may be enrolled again explicitly.</summary>
	VerificationTimedOut = 2,
	/// <summary>The gateway reports this key as verified.</summary>
	Verified = 3,
	/// <summary>The key has been removed.</summary>
	Removed = 4
	}

/// <summary>Cloud enrollment result for a specific local public key.</summary>
/// <param name="State">Reported authorization state; only Verified establishes authorization.</param>
/// <param name="Fingerprint">SHA-256 fingerprint of the PKCS#1 public-key DER.</param>
public sealed record LocalKeyRegistration (
	[property: JsonPropertyName ("state")] LocalKeyState State,
	[property: JsonPropertyName ("fingerprint")] string Fingerprint);

/// <summary>Explicit enrollment operations on an already authenticated cloud connection.</summary>
internal interface ILocalKeyEnrollmentClient
	{
	/// <summary>Registers a public key or reads its current state for the selected site.</summary>
	/// <param name="publicKey">PKCS#1 public-key DER; never private key material.</param>
	/// <param name="description">Description for registration; null requests a read of existing authorization state.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The state of this specific key.</returns>
	Task<LocalKeyRegistration> LocalKeyAsync (byte[] publicKey, string? description, CancellationToken cancellationToken);
	}

/// <summary>Attribute-controlled enrollment messages shared by Owner and Fleet connections.</summary>
internal static class LocalKeyEnrollmentProtocol
	{
	/// <summary>Builds a registration or authorization-list request.</summary>
	/// <param name="publicKey">Public-key DER.</param>
	/// <param name="description">Registration label, or null for a status read.</param>
	/// <returns>The cloud command envelope.</returns>
	internal static EnrollmentRequest Request (byte[] publicKey, string? description) => new ()
		{
		Properties = new EnrollmentProperties
			{
			Message = new EnrollmentMessage
				{
				Authorization = new EnrollmentAuthorization
					{
					Add = description is null ? null : new EnrollmentAdd
						{ PublicKey = Convert.ToBase64String (publicKey), Description = description },
					List = description is null ? new EnrollmentEmpty () : null
					}
				}
			}
		};

	/// <summary>Finds only the requested key; another verified client never verifies this one.</summary>
	/// <param name="json">Cloud command response.</param>
	/// <param name="publicKey">Expected public key.</param>
	/// <param name="registration">Whether this is the direct response to adding the key.</param>
	/// <returns>Reported state and local public-key fingerprint.</returns>
	internal static LocalKeyRegistration Parse (string? json, byte[] publicKey, bool registration)
		{
		EnrollmentResponse? response = JsonHelper.DeserializeOrNull<EnrollmentResponse> (json);
		EnrollmentReplies? message = response?.Response?.Message?.Payload?.Authorization?.Message;
		EnrollmentClient? client = null;
		if (registration)
			{
			client = (message?.Add ?? message?.AddSnake)?.Client;
			if (client is not null && (client.PublicKey ?? client.PublicKeySnake) is string returned && !Matches (returned, publicKey))
				{
				client = null;
				}
			}
		else
			{
			client = (message?.List ?? message?.ListSnake)?.Clients?.FirstOrDefault (
				value => Matches (value.PublicKey ?? value.PublicKeySnake, publicKey));
			}
		LocalKeyState state = client?.State is >= 1 and <= 4 ? (LocalKeyState)client.State.Value : LocalKeyState.Unknown;
#if NETFRAMEWORK
		using SHA256 hash = SHA256.Create ();
		var fingerprint = BitConverter.ToString (hash.ComputeHash (publicKey)).Replace ("-", string.Empty).ToLowerInvariant ();
#else
		var fingerprint = Convert.ToHexStringLower (SHA256.HashData (publicKey));
#endif
		return new LocalKeyRegistration (state, fingerprint);
		}

	private static bool Matches (string? text, byte[] expected)
		{
		if (string.IsNullOrEmpty (text))
			{
			return false;
			}
		try
			{
			return Convert.FromBase64String (text).SequenceEqual (expected);
			}
		catch (FormatException)
			{
			return false;
			}
		}
	}

/// <summary>Cloud energy-device command envelope.</summary>
internal sealed record EnrollmentRequest
	{
	/// <summary>Command routing and content.</summary>
	[JsonPropertyName ("command_properties")]
	public EnrollmentProperties Properties { get; init; } = new ();
	/// <summary>Energy-device command transport.</summary>
	[JsonPropertyName ("command_type")]
	public string Type { get; init; } = "grpc_command";
	}

/// <summary>Energy-device command routing.</summary>
internal sealed record EnrollmentProperties
	{
	/// <summary>Authorization operation.</summary>
	[JsonPropertyName ("message")]
	public EnrollmentMessage Message { get; init; } = new ();
	/// <summary>Selected-site identifier type.</summary>
	[JsonPropertyName ("identifier_type")]
	public int IdentifierType { get; init; } = 1;
	}

/// <summary>Authorization request container.</summary>
internal sealed record EnrollmentMessage
	{
	/// <summary>Key operation.</summary>
	[JsonPropertyName ("authorization")]
	public EnrollmentAuthorization Authorization { get; init; } = new ();
	}

/// <summary>Exactly one key-enrollment or key-list operation.</summary>
internal sealed record EnrollmentAuthorization
	{
	/// <summary>Public key to enroll.</summary>
	[JsonPropertyName ("add_authorized_client_request"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EnrollmentAdd? Add { get; init; }
	/// <summary>Read-only request for existing keys.</summary>
	[JsonPropertyName ("list_authorized_clients_request"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EnrollmentEmpty? List { get; init; }
	}

/// <summary>Empty authorization-list request.</summary>
internal sealed record EnrollmentEmpty;

/// <summary>Public-key registration data; never contains private RSA parameters.</summary>
internal sealed record EnrollmentAdd
	{
	/// <summary>RSA key type identifier.</summary>
	[JsonPropertyName ("key_type")]
	public int KeyType { get; init; } = 1;
	/// <summary>Base64 PKCS#1 public-key DER.</summary>
	[JsonPropertyName ("public_key")]
	public string PublicKey { get; init; } = string.Empty;
	/// <summary>Customer application authorization type.</summary>
	[JsonPropertyName ("authorized_client_type")]
	public int ClientType { get; init; } = 1;
	/// <summary>User-visible key label.</summary>
	[JsonPropertyName ("description")]
	public string Description { get; init; } = string.Empty;
	}

/// <summary>Outer cloud response.</summary>
internal sealed record EnrollmentResponse
	{
	/// <summary>Command result.</summary>
	[JsonPropertyName ("response")]
	public EnrollmentResponseMessage? Response { get; init; }
	}

/// <summary>Cloud command message.</summary>
internal sealed record EnrollmentResponseMessage
	{
	/// <summary>Message payload.</summary>
	[JsonPropertyName ("message")]
	public EnrollmentPayloadContainer? Message { get; init; }
	}

/// <summary>Energy-device payload container.</summary>
internal sealed record EnrollmentPayloadContainer
	{
	/// <summary>Authorization payload.</summary>
	[JsonPropertyName ("payload")]
	public EnrollmentAuthorizationContainer? Payload { get; init; }
	}

/// <summary>Authorization response container.</summary>
internal sealed record EnrollmentAuthorizationContainer
	{
	/// <summary>Authorization result.</summary>
	[JsonPropertyName ("authorization")]
	public EnrollmentReplyContainer? Authorization { get; init; }
	}

/// <summary>Authorization reply message.</summary>
internal sealed record EnrollmentReplyContainer
	{
	/// <summary>Concrete add or list response.</summary>
	[JsonPropertyName ("message")]
	public EnrollmentReplies? Message { get; init; }
	}

/// <summary>Known wire aliases for authorization replies.</summary>
internal sealed record EnrollmentReplies
	{
	/// <summary>Pascal-case registration reply.</summary>
	[JsonPropertyName ("AddAuthorizedClientResponse")]
	public EnrollmentAddReply? Add { get; init; }
	/// <summary>Snake-case registration reply.</summary>
	[JsonPropertyName ("add_authorized_client_response")]
	public EnrollmentAddReply? AddSnake { get; init; }
	/// <summary>Pascal-case list reply.</summary>
	[JsonPropertyName ("ListAuthorizedClientsResponse")]
	public EnrollmentListReply? List { get; init; }
	/// <summary>Snake-case list reply.</summary>
	[JsonPropertyName ("list_authorized_clients_response")]
	public EnrollmentListReply? ListSnake { get; init; }
	}

/// <summary>Newly enrolled client's state.</summary>
internal sealed record EnrollmentAddReply
	{
	/// <summary>Registered client.</summary>
	[JsonPropertyName ("client")]
	public EnrollmentClient? Client { get; init; }
	}

/// <summary>Existing authorized clients.</summary>
internal sealed record EnrollmentListReply
	{
	/// <summary>Clients returned by the selected site.</summary>
	[JsonPropertyName ("clients")]
	public IReadOnlyList<EnrollmentClient>? Clients { get; init; }
	}

/// <summary>A public authorization record.</summary>
internal sealed record EnrollmentClient
	{
	/// <summary>Public key using the Pascal-case wire name.</summary>
	[JsonPropertyName ("PublicKey")]
	public string? PublicKey { get; init; }
	/// <summary>Public key using the snake-case wire name.</summary>
	[JsonPropertyName ("public_key")]
	public string? PublicKeySnake { get; init; }
	/// <summary>Gateway authorization state.</summary>
	[JsonPropertyName ("state")]
	public int? State { get; init; }
	}
