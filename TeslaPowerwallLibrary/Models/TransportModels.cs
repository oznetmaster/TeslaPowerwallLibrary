using System.Text.Json.Serialization;

namespace TeslaPowerwallLibrary.Models;

internal sealed record ApiResponse<T>
	{
	[JsonPropertyName ("response")]
	public T? Response
		{
		get; init;
		}
	}

internal sealed record ApiError
	{
	[JsonPropertyName ("error")]
	public string? Error
		{
		get; init;
		}
	}

internal sealed record OperationRequest
	{
	[JsonPropertyName ("backup_reserve_percent"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? BackupReservePercent
		{
		get; init;
		}
	[JsonPropertyName ("real_mode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? RealMode
		{
		get; init;
		}
	}

internal sealed record BackupReserveRequest
	{
	[JsonPropertyName ("backup_reserve_percent")]
	public int BackupReservePercent
		{
		get; init;
		}
	}

internal sealed record OperationModeRequest
	{
	[JsonPropertyName ("default_real_mode")]
	public string? DefaultRealMode
		{
		get; init;
		}
	}

internal sealed record GridImportExportRequest
	{
	[JsonPropertyName ("disallow_charge_from_grid_with_solar_installed"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? DisallowChargeFromGridWithSolarInstalled
		{
		get; init;
		}
	[JsonPropertyName ("customer_preferred_export_rule"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? CustomerPreferredExportRule
		{
		get; init;
		}
	}

internal sealed record StormModeRequest
	{
	[JsonPropertyName ("enabled")]
	public bool Enabled
		{
		get; init;
		}
	}

internal sealed record OperationWriteResult
	{
	[JsonPropertyName ("set_backup_reserve_percent"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ReserveWriteResult? Reserve
		{
		get; init;
		}
	[JsonPropertyName ("set_operation"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ModeWriteResult? Mode
		{
		get; init;
		}
	}

internal sealed record ReserveWriteResult
	{
	[JsonPropertyName ("backup_reserve_percent")]
	public int BackupReservePercent
		{
		get; init;
		}
	[JsonPropertyName ("din"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Din
		{
		get; init;
		}
	[JsonPropertyName ("result")]
	public object? Result
		{
		get; init;
		}
	}

internal sealed record ModeWriteResult
	{
	[JsonPropertyName ("real_mode")]
	public string? RealMode
		{
		get; init;
		}
	[JsonPropertyName ("din"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Din
		{
		get; init;
		}
	[JsonPropertyName ("result")]
	public object? Result
		{
		get; init;
		}
	}

internal sealed record CloudRefreshRequest
	{
	[JsonPropertyName ("grant_type")]
	public string? GrantType
		{
		get; init;
		}
	[JsonPropertyName ("client_id")]
	public string? ClientId
		{
		get; init;
		}
	[JsonPropertyName ("refresh_token")]
	public string? RefreshToken
		{
		get; init;
		}
	[JsonPropertyName ("scope")]
	public string? Scope
		{
		get; init;
		}
	}