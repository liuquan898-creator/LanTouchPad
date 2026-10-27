# LAN protocol v1

Discovery: send UTF-8 `LANTOUCHPAD_DISCOVER_V1` to UDP port 45831 (broadcast or unicast). The PC replies to the sender's source port:

```json
{"app":"LanTouchPad","protocol":1,"name":"DESKTOP-NAME","port":45832}
```

`GET http://PC:45832/info` returns the same JSON. Connect to `ws://PC:45832/ws`; the first message is the metadata above. A second controller receives HTTP 409. Browser requests containing an `Origin` header receive HTTP 403. There is no authentication or encryption.

PC 1.0.2 adds a `version` field to discovery metadata and exposes `GET /diagnostics` to loopback clients only. Diagnostics contain the receiver version, elevation, foreground process name, input desktop and cursor bounds; no input text is recorded.

Each WebSocket text message is one JSON object:

| Type | Fields | Behavior |
| --- | --- | --- |
| `move` | `dx`, `dy`: finite numbers | Relative movement, individual axes clamped to +/-2000, fractional movement retained |
| `scroll` | `dy`: finite number | Windows wheel units, 120 units per notch, positive scrolls up |
| `click` | `button`: `left` or `right` | Down followed by up |
| `button` | `button`, `down`: boolean, optional `copy`: boolean | Hold or release a button. A completed left drag copies with Ctrl+C by default; `copy:false` suppresses copying on canceled gestures |
| `release` | none | Release all held buttons and discard fractional deltas |
| `key` | `key` | `enter`, `backspace`, `tab`, `escape`, `delete`, `left`, `right`, `up`, `down`, `home`, `end` |
| `text` | `text`: string, `id`: string | At most 4000 UTF-16 code units; inject Unicode into the focused application |
| `ping` | none | Receive `{"type":"pong"}` |

Successful text injection receives `{"type":"textAck","id":"..."}`. This confirms Windows accepted the input, not that the target application stored it. Validation or input errors receive `{"type":"error","message":"..."}`. Text is never automatically retried after an ambiguous failure.

Send an application `ping` every 2 seconds. Incomplete or silent sessions time out after 10 seconds and release all held buttons. Abrupt disconnect also releases buttons. Maximum message size is 65535 bytes; messages above 500 per second close the session.
