$ErrorActionPreference = 'Stop'
$info = Invoke-RestMethod 'http://127.0.0.1:45832/info'
if ($info.app -ne 'LanTouchPad' -or $info.protocol -ne 1) { throw 'Unexpected discovery response' }
$socket = [Net.WebSockets.ClientWebSocket]::new()
$timeout = [Threading.CancellationTokenSource]::new(10000)
try {
    $socket.ConnectAsync([Uri]'ws://127.0.0.1:45832/ws', $timeout.Token).GetAwaiter().GetResult()
    $buffer = [byte[]]::new(65536)
    $segment = [ArraySegment[byte]]::new($buffer)
    $hello = $socket.ReceiveAsync($segment, $timeout.Token).GetAwaiter().GetResult()
    $metadata = [Text.Encoding]::UTF8.GetString($buffer,0,$hello.Count) | ConvertFrom-Json
    if ($metadata.protocol -ne 1) { throw 'Unexpected protocol handshake' }
    $payload = [ArraySegment[byte]]::new([Text.Encoding]::UTF8.GetBytes('{"type":"ping"}'))
    $socket.SendAsync($payload,[Net.WebSockets.WebSocketMessageType]::Text,$true,$timeout.Token).GetAwaiter().GetResult()
    $reply = $socket.ReceiveAsync($segment, $timeout.Token).GetAwaiter().GetResult()
    $pong = [Text.Encoding]::UTF8.GetString($buffer,0,$reply.Count) | ConvertFrom-Json
    if ($pong.type -ne 'pong') { throw 'Heartbeat failed' }
    $socket.CloseAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure,'done',$timeout.Token).GetAwaiter().GetResult()
    if ($socket.State -ne [Net.WebSockets.WebSocketState]::Closed) { throw 'Close handshake failed' }
    'PASS: discovery, WebSocket handshake, heartbeat and graceful close'
} finally { $socket.Dispose(); $timeout.Dispose() }
