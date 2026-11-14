# Smart node test: unified failure state (VelaRay 2.7)

- On every non-successful TCP/UDP smart node probe, display **超时** (`Timeout` for English) in red on the server card. A successful probe continues to display its measured time in green.
- While testing, the card still displays its localized progress text and is not marked as failed.
- `ServerProfile.IsProbeFailure` is transient and ignored by JSON serialization. It drives the UI-only red `DataTrigger`, avoiding color or localized string comparisons in the Core model.
- Starting a new test, or editing a node endpoint/transport, resets the red state.
- The probe implementation, concurrency, timeouts, system proxy configuration, and active connection are unchanged.
- For diagnostic purposes, the actual probe outcome remains available from `NodeProbeResult` even though the card intentionally collapses all failures to Timeout.

## Verify

```
python scripts/check_smart_node_latency.py
.\scripts\verify-windows.ps1 -SkipPublish
```

Windows/.NET MAUI visual verification is still required in a Windows build environment.
