# VelaRay 2.7 — Routing Policy / Rule Editor Separation

## Page responsibility

**Proxy settings** owns selection of the core route mode (`Smart`, `ProxyAll`, `Direct`, `BypassCN`, `Custom`), Smart unmatched traffic policy, and explicit promotion of a custom rule set to the selected core configuration.

**Routing & PAC** displays selected core policy and Smart fallback **read-only**, allows viewing built-in rules, and editing, adding, renaming, deleting, and reordering custom rule sets independently of the core selection. Custom rule sets are editable even while the selected mode is not `Custom`. PAC management continues using the existing hot-update/deferred-reconnect behavior.

## Operation

1. Edit or create a rule set under **Routing & PAC**, and save it. This changes the editable rule definition only. The current V2Fly process and its *next normal connection* continue using the last explicitly selected routing snapshot.
2. Open **Proxy settings**. Choose `Custom` and select a rule set. Click **Select and apply snapshot**. The generator validates the selected rule against the currently selected V4/V5 profile, stores a copy of the rule JSON, and marks a running core pending when the effective config changes.
3. Click **Reconnect and apply** to load the new routing config into the core. The app must never reconnect just because the user edits a rule.
4. Further edits do not replace the applied snapshot until the user explicitly applies the rule again in **Proxy settings**.

`EditingRoutingRuleId` records the rule opened in the editor; `SelectedRoutingRuleId` identifies the applied rule, and `AppliedRoutingRuleJson` is a frozen copy used in V4 and V5 builders **and outbound reference planning**. This separation is persisted in `settings.v5.json`.

## Existing settings migration

On load, an existing `SelectedRoutingRuleId` without an `AppliedRoutingRuleJson` captures the corresponding rule's JSON before an editor can modify it. The rule list itself is not rewritten; previous behavior is preserved until an explicit application.

Deleting an editor rule does not delete the already-applied JSON snapshot or silently reset the core to Smart. A replacement may be explicitly chosen in Proxy settings. The editor status will say if the selected rule set has been removed from the editing list.

## Scope / compatibility

- No change to live system proxy handling, PAC templates, ports, startup, V2Fly traffic statistics, or node selection.
- Existing current-run JSON snapshot (`RunningConfigChangeDetector`) remains responsible for deciding whether core reconnect is required.
- Separate the concepts **selected for next core config** vs **actually loaded in the currently running core**. Only explicit reconnection changes the latter.
- Custom external JSON files are not rewritten or managed by this feature.

## Verification

Run on any system with Python:

```bash
python scripts/check_routing_editor_policy_separation.py
python scripts/static_check.py
python scripts/check_csharp_structure.py
```

Run on Windows with .NET 10 and MAUI workload:

```powershell
.\scripts\verify-windows.ps1 -SkipPublish
```

Manual acceptance: load a preexisting Custom rule; edit it in Routing & PAC; verify the pending banner does **not** appear and a subsequent normal connection keeps the previous route. Apply the edited version in Proxy settings; verify the pending banner appears while connected, then confirm the route takes effect after the user explicitly reconnects. Repeat in V4 and V5. Verify creating/deleting editor rule sets never changes the mode selector.

**Source-level checks do not establish Windows MAUI compilation or runtime correctness.**
