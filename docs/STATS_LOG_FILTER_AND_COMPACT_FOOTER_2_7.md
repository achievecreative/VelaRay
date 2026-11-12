# VelaRay 2.7 — Internal Stats Log Filter and One-line Traffic Footer

## Behavior

- Suppress only log entries containing `tcp:127.0.0.1:0 [velaray-stats-api]`, case-insensitively, before they enter the bounded UI log store and disk batching queue.
- Do not suppress other core logs or statistics API errors. Statistics collection still runs as before.
- Keep the bottom status area in three zones: port details at left, traffic in the middle, and running proxy mode at right.
- Show upload speed, download speed, and session totals in **one horizontal row**, while keeping the existing named XAML bindings and sampling logic unchanged.
- On narrower windows, single-line session totals are truncated rather than wrapped; no extra footer row is introduced.

## Validation

```sh
python scripts/check_stats_log_filter_and_one_line_footer.py
python scripts/check_server_card_spacing_and_centered_traffic.py
```

On Windows, also build and visually inspect the MAUI app at narrow and wide window sizes.
