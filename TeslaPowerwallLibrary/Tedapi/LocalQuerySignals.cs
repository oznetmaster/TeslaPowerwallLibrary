// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;

namespace TeslaPowerwallLibrary.Tedapi;

public sealed partial class PowerwallTedapiClient
	{
	/// <summary>Appends supported signal names to unsigned variables without modifying vendor-signed query captures.</summary>
	/// <remarks>Signal names follow pypowerwall v0.21.0 with the v0.22.0 TEPOD available-power additions.</remarks>
	/// <param name="name">The June 2024 query role.</param>
	/// <param name="query">The pristine captured query.</param>
	/// <returns>A request definition retaining the original text and signature.</returns>
	private static QueryDefinition WithAdditionalSignals (string name, QueryDefinition query)
		{
		var extras = name switch
			{
				"components" => new Dictionary<string, string[]> (StringComparer.Ordinal)
					{
					["pchSignalNames"] = ["PCH_AcRealPowerSum", "PCH_AcVoltageCN", "PCH_AcVoltageLL", "PCH_pcbaId", "PCH_usageId"],
					["bmsSignalNames"] = ["BMS_kwhDischargeTotal", "BMS_LOG_ahDischargeTotal", "BMS_LOG_cacAvg", "BMS_LOG_numFullBattCycles", "BMS_LOG_overDischargeCounter", "BMS_LOG_selfDischargeRate"],
					["hvpSignalNames"] = ["HVP_SafetyBiDisconnectState", "HVP_a061_LockoutOnIsoFaultRetry"],
					["pwsSignalNames"] = ["PWS_ExtSwitch_State", "PWS_RSDSelfTest_Result", "PWS_RSDSelfTest_State", "PWS_RSD_State", "PWS_assemblyId", "PWS_pcbaId", "PWS_reversePolarityStrings"],
					["baggrSignalNames"] = ["BAGGR_ExpectedEnergyRemaining", "BAGGR_ExpectedFullPackEnergy", "BAGGR_OperationType"]
					},
				"device_controller_full" => new Dictionary<string, string[]> (StringComparer.Ordinal)
					{
					["msaSignals"] = ["SYNC_Uptime", "PINV_EnergyCharged", "PINV_EnergyDischarged", "PINV_LifetimeEnergyCharged", "PINV_LifetimeEnergyDischarged",
						"PINV_HeatsinkTemp", "PINV_PllFrequency", "PINV_PllLocked", "PINV_Qout", "PINV_ReadyForGridForming", "PINV_HardwareEnableLine", "PINV_PowerLimiter",
						"THC_State", "SYNC_ExternallyPowered", "SYNC_SiteSwitchEnabled", "POD_available_charge_power", "POD_available_dischg_power"]
					},
				_ => null
				};
		if (extras is null)
			return query;
		var variables = JsonNode.Parse (query.Variables)!.AsObject ();
		if (name == "device_controller_full")
			{
			// TEPOD is appended only to unsigned June 2024 variables. Captured
			// query text, signatures and the existing type order remain intact.
			var types = variables["msaComp"]!["types"]!.AsArray ();
			if (!types.Any (value => value?.GetValue<string> () == "TEPOD")) types.Add ("TEPOD");
			}
		foreach (var group in extras)
			{
			if (variables[group.Key] is not JsonArray signals)
				variables[group.Key] = signals = new JsonArray ();
			foreach (string signal in group.Value)
				if (!signals.Any (value => string.Equals (value?.GetValue<string> (), signal, StringComparison.Ordinal)))
					signals.Add (signal);
			}
		return query with
			{
			Variables = variables.ToJsonString ()
			};
		}
	}
