# Server card: name/address left alignment (VelaRay 2.7)

## Symptom

In the server list, the server title began to the right of a connected-status dot while the endpoint/address began at the left edge of the content area. The two text lines were not aligned.

## UI-only change

In `src/VelaRay.Maui/MainPage.xaml`, the name is now the **first** column of the title row (`*,Auto,Auto`). The connected-status dot and protocol badge follow the title, instead of preceding it. Both the name and address occupy column `0` of the same content region, with `HorizontalTextAlignment="Start"`. This left edge is stable whether `IsConnected` is true or false.

Server list selection, protocol binding, latency, connected rail, running marker and event handlers remain untouched. 78dp minimum height, 12dp card spacing and 18dp right-side scrollbar reservation remain unchanged.

## Verification

`python scripts/check_server_card_text_alignment.py`

Windows runtime validation is still recommended at different window sizes, with connected and disconnected cards and long server names. The static checks do not prove a Windows MAUI build or rendering.
