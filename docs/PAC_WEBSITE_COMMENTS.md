# Managed PAC website-list comments

Both **Direct websites** and **Proxied websites** accept comment lines beginning with `//` (leading spaces are allowed). Inline comments following whitespace and `//` are also accepted. Existing `#` full-line comments continue to work.

```text
// Internal destinations — do not proxy
intranet.example.org
example.cn // Direct exception

// External sites — use the proxy
https://github.com/project // URL is reduced to its host
google.com
```

The raw text, including comments, is preserved in `settings/settings.v5.json`. When the managed PAC file (`pacs/managed.pac`) and Smart V4/V5 router rules are generated, comment lines and inline notes are excluded; only normalized host/IP entries are used. The parser removes comments **before** splitting on commas and semicolons so annotations containing punctuation cannot accidentally become invalid rules. The `//` in an `https://` or `http://` URL is not treated as a comment.

This applies to the two managed website lists only. Manually edited custom PAC JavaScript files already use regular JavaScript comments and are not parsed by this feature.

**Windows validation:** `dotnet run --project tests/VelaRay.SmokeTests -c Release`, then test PAC preview and Smart V4/V5 config validation with the official V2Fly core. The source-level checks cannot replace a Windows MAUI build.
