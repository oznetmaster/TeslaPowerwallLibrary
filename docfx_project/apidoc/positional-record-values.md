---
uid: TeslaPowerwallLibrary.Local.PowerwallHost.Host
syntax:
  return:
    description: "Original hostname or IP literal, retained so DHCP changes can be resolved again."
---

---
uid: TeslaPowerwallLibrary.Local.PowerwallHost.Port
syntax:
  return:
    description: "HTTPS service port."
---

---
uid: TeslaPowerwallLibrary.Local.PowerwallHost.Addresses
syntax:
  return:
    description: "Addresses returned by the resolver; these are not proof of device identity."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupEvent.Id
syntax:
  return:
    description: "Gateway event identifier."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupEvent.Name
syntax:
  return:
    description: "Event name."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupEvent.Schedule
syntax:
  return:
    description: "Event timing, or null if omitted."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupEvents.ManualBackup
syntax:
  return:
    description: "Manual backup timing, or null when none was reported."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupEvents.Events
syntax:
  return:
    description: "Other reported backup events."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupSchedule.StartTime
syntax:
  return:
    description: "UTC start time, or null when omitted."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupSchedule.DurationSeconds
syntax:
  return:
    description: "Reported event duration in seconds."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalBackupSchedule.Priority
syntax:
  return:
    description: "Gateway scheduling priority."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalCommandResult.Acknowledged
syntax:
  return:
    description: "Whether the expected command response was received; this does not confirm physical actuation."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalGridCommandResult.ConnectToGrid
syntax:
  return:
    description: "True for a reconnection request; false for intentional islanding."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalGridCommandResult.ResultCode
syntax:
  return:
    description: "Gateway result code, or null when no matching response was returned. One is the upstream-observed success code."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalKeyRegistration.State
syntax:
  return:
    description: "Reported authorization state; only Verified establishes authorization."
---

---
uid: TeslaPowerwallLibrary.Tedapi.LocalKeyRegistration.Fingerprint
syntax:
  return:
    description: "SHA-256 fingerprint of the PKCS#1 public-key DER."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginResult.Status
syntax:
  return:
    description: "The outcome category."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginResult.Tokens
syntax:
  return:
    description: "The captured tokens when <xref href=\"TeslaPowerwallLibrary.Login.TeslaCloudLoginResult.Status\" data-throw-if-not-resolved=\"false\"></xref> is <xref href=\"TeslaPowerwallLibrary.Login.TeslaCloudLoginStatus.Success\" data-throw-if-not-resolved=\"false\"></xref>."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginResult.Message
syntax:
  return:
    description: "An explanatory message for non-success outcomes."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginTokens.RefreshToken
syntax:
  return:
    description: "The long-lived refresh token (valid ~90 days)."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginTokens.AccessToken
syntax:
  return:
    description: "The short-lived access token (valid ~8 hours)."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaCloudLoginTokens.Email
syntax:
  return:
    description: "The Tesla account email parsed from the id_token, when available."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiLoginResult.Status
syntax:
  return:
    description: "The outcome category."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiLoginResult.Tokens
syntax:
  return:
    description: "The captured tokens, when <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiLoginResult.Status\" data-throw-if-not-resolved=\"false\"></xref> is <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiLoginStatus.Success\" data-throw-if-not-resolved=\"false\"></xref>."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiLoginResult.Message
syntax:
  return:
    description: "An explanatory message for failures."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiLoginTokens.AccessToken
syntax:
  return:
    description: "The short-lived FleetAPI access token."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiLoginTokens.RefreshToken
syntax:
  return:
    description: "The long-lived FleetAPI refresh token."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerAccountResult.Status
syntax:
  return:
    description: "The outcome category."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerAccountResult.ResponseJson
syntax:
  return:
    description: "The raw JSON response from Tesla, when <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerAccountResult.Status\" data-throw-if-not-resolved=\"false\"></xref> is <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiLoginStatus.Success\" data-throw-if-not-resolved=\"false\"></xref>."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerAccountResult.Message
syntax:
  return:
    description: "An explanatory message for failures."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerTokenResult.Status
syntax:
  return:
    description: "The outcome category."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerTokenResult.PartnerToken
syntax:
  return:
    description: "The partner token, when <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerTokenResult.Status\" data-throw-if-not-resolved=\"false\"></xref> is <xref href=\"TeslaPowerwallLibrary.Login.TeslaFleetApiLoginStatus.Success\" data-throw-if-not-resolved=\"false\"></xref>."
---

---
uid: TeslaPowerwallLibrary.Login.TeslaFleetApiPartnerTokenResult.Message
syntax:
  return:
    description: "An explanatory message for failures."
---
