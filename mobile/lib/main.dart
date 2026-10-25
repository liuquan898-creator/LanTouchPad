import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'keyboard_input.dart';

const discoveryPort = 45831, controlPort = 45832;
const platform = MethodChannel('online.yuxiboy.lantouchpad/device');

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const TouchPadApp());
}

class TouchPadApp extends StatelessWidget {
  const TouchPadApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'LanTouchPad',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      useMaterial3: true,
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff087f6c)),
      scaffoldBackgroundColor: const Color(0xfff5f7f8),
      appBarTheme: const AppBarTheme(
        backgroundColor: Color(0xfff5f7f8),
        surfaceTintColor: Colors.transparent,
      ),
      inputDecorationTheme: const InputDecorationTheme(
        border: OutlineInputBorder(
          borderRadius: BorderRadius.all(Radius.circular(8)),
        ),
      ),
    ),
    home: const DevicePage(),
  );
}

class PcDevice {
  const PcDevice(this.name, this.ip, this.port);
  final String name, ip;
  final int port;
}

class DevicePage extends StatefulWidget {
  const DevicePage({super.key});
  @override
  State<DevicePage> createState() => _DevicePageState();
}

class _DevicePageState extends State<DevicePage> {
  final Map<String, PcDevice> devices = {};
  RawDatagramSocket? socket;
  Timer? scanTimer;
  int generation = 0;
  bool scanning = false;
  String? error;
  @override
  void initState() {
    super.initState();
    scan();
  }

  @override
  void dispose() {
    generation++;
    socket?.close();
    scanTimer?.cancel();
    super.dispose();
  }

  Future<void> scan() async {
    final attempt = ++generation;
    socket?.close();
    scanTimer?.cancel();
    setState(() {
      scanning = true;
      error = null;
      devices.clear();
    });
    try {
      final next = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
      if (!mounted || attempt != generation) {
        next.close();
        return;
      }
      socket = next;
      next.broadcastEnabled = true;
      next.listen(
        (event) {
          if (event != RawSocketEvent.read) return;
          Datagram? packet;
          while ((packet = next.receive()) != null) {
            try {
              final data = jsonDecode(utf8.decode(packet!.data));
              if (data['app'] != 'LanTouchPad' ||
                  data['protocol'] != 1 ||
                  data['port'] != controlPort) {
                continue;
              }
              final ip = packet.address.address;
              if (mounted && attempt == generation) {
                setState(
                  () => devices[ip] = PcDevice(
                    data['name'] as String,
                    ip,
                    data['port'] as int,
                  ),
                );
              }
            } catch (_) {
              /* Ignore unrelated LAN datagrams. */
            }
          }
        },
        onError: (Object e) {
          if (mounted && attempt == generation) {
            setState(() => error = '扫描失败：$e');
          }
        },
      );
      final targets = <String>{'255.255.255.255'};
      for (final interface in await NetworkInterface.list(
        type: InternetAddressType.IPv4,
      )) {
        for (final address in interface.addresses) {
          final octets = address.address.split('.');
          if (octets.length == 4 && !address.isLoopback) {
            targets.add('${octets.take(3).join('.')}.255');
          }
        }
      }
      if (!mounted || attempt != generation) return;
      void broadcast() {
        for (final target in targets) {
          try {
            next.send(
              utf8.encode('LANTOUCHPAD_DISCOVER_V1'),
              InternetAddress(target),
              discoveryPort,
            );
          } catch (_) {}
        }
      }

      broadcast();
      int count = 0;
      scanTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
        if (++count >= 4) {
          timer.cancel();
          next.close();
          if (mounted && attempt == generation) {
            setState(() => scanning = false);
          }
        } else {
          broadcast();
        }
      });
    } catch (e) {
      if (mounted && attempt == generation) {
        setState(() {
          scanning = false;
          error = '无法访问局域网：$e';
        });
      }
    }
  }

  Future<void> manual() async {
    final input = TextEditingController();
    final ip = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('连接电脑 IP'),
        content: TextField(
          controller: input,
          autofocus: true,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(
            labelText: 'IPv4 地址',
            hintText: '192.168.1.10',
          ),
          onSubmitted: (value) => Navigator.pop(context, value),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('取消'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(context, input.text),
            child: const Text('连接'),
          ),
        ],
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 300));
    input.dispose();
    if (ip == null || !mounted) return;
    final address = InternetAddress.tryParse(ip.trim());
    if (address == null || address.type != InternetAddressType.IPv4) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('请输入有效的 IPv4 地址')));
      return;
    }
    open(PcDevice(address.address, address.address, controlPort));
  }

  Future<void> open(PcDevice device) async {
    await Navigator.push(
      context,
      MaterialPageRoute<void>(builder: (_) => ControlPage(device: device)),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('LanTouchPad'),
      actions: [
        IconButton(
          tooltip: '连接 IP',
          onPressed: manual,
          icon: const Icon(Icons.add_link),
        ),
        IconButton(
          tooltip: '重新扫描',
          onPressed: scanning ? null : scan,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: SafeArea(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(24, 24, 24, 20),
            child: Row(
              children: [
                const Expanded(
                  child: Text(
                    '局域网电脑',
                    style: TextStyle(fontSize: 25, fontWeight: FontWeight.w700),
                  ),
                ),
                if (scanning)
                  const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
              ],
            ),
          ),
          if (error != null)
            Padding(
              padding: const EdgeInsets.all(24),
              child: Text(
                error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          Expanded(
            child: devices.isEmpty
                ? Center(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          scanning ? Icons.wifi_find : Icons.computer,
                          size: 64,
                          color: const Color(0xff71848a),
                        ),
                        const SizedBox(height: 20),
                        Text(
                          scanning ? '正在寻找电脑…' : '暂无可连接的电脑',
                          style: const TextStyle(fontSize: 18),
                        ),
                        if (!scanning)
                          Padding(
                            padding: const EdgeInsets.only(top: 18),
                            child: FilledButton.icon(
                              onPressed: scan,
                              icon: const Icon(Icons.refresh),
                              label: const Text('重新扫描'),
                            ),
                          ),
                      ],
                    ),
                  )
                : ListView.separated(
                    itemCount: devices.length,
                    separatorBuilder: (_, _) =>
                        const Divider(height: 1, indent: 24, endIndent: 24),
                    itemBuilder: (context, index) {
                      final device = devices.values.elementAt(index);
                      return ListTile(
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 24,
                          vertical: 10,
                        ),
                        leading: const Icon(
                          Icons.desktop_windows,
                          color: Color(0xff087f6c),
                        ),
                        title: Text(device.name),
                        subtitle: Text(device.ip),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => open(device),
                      );
                    },
                  ),
          ),
          const Padding(
            padding: EdgeInsets.all(24),
            child: Text(
              '局域网 · 免验证连接',
              style: TextStyle(color: Color(0xff71848a)),
            ),
          ),
        ],
      ),
    ),
  );
}

class RemoteConnection extends ChangeNotifier {
  RemoteConnection(this.device);
  final PcDevice device;
  WebSocket? socket;
  Timer? heartbeat, retry;
  DateTime lastPong = DateTime.now();
  bool connected = false, closed = false, suspended = false;
  int generation = 0, sequence = 0;
  String status = '正在连接…';
  void Function(String)? onError;
  final Map<String, Completer<bool>> pending = {};
  Future<void> connect() async {
    if (closed || suspended) return;
    final attempt = ++generation;
    status = '正在连接…';
    notifyListeners();
    try {
      final next = await WebSocket.connect(
        'ws://${device.ip}:${device.port}/ws',
      ).timeout(const Duration(seconds: 4));
      if (closed || attempt != generation) {
        next.close();
        return;
      }
      socket = next;
      next.pingInterval = const Duration(seconds: 3);
      lastPong = DateTime.now();
      next.listen(
        (event) {
          if (closed || attempt != generation) return;
          try {
            final data = jsonDecode(event as String);
            if (data['app'] == 'LanTouchPad' && data['protocol'] == 1) {
              connected = true;
              status = '已连接';
              platform.invokeMethod<void>('keepAwake', true);
              notifyListeners();
            } else if (data['type'] == 'pong') {
              lastPong = DateTime.now();
            } else if (data['type'] == 'textAck') {
              pending.remove(data['id'])?.complete(true);
            } else if (data['type'] == 'error') {
              for (final item in pending.values) {
                item.complete(false);
              }
              pending.clear();
              onError?.call(data['message'] as String);
            }
          } catch (_) {
            lost(attempt);
          }
        },
        onError: (Object _) => lost(attempt),
        onDone: () => lost(attempt),
      );
      heartbeat?.cancel();
      heartbeat = Timer.periodic(const Duration(seconds: 2), (_) {
        if (DateTime.now().difference(lastPong).inSeconds > 8) {
          lost(attempt);
        } else {
          next.add(jsonEncode({'type': 'ping'}));
        }
      });
    } catch (_) {
      if (!closed && attempt == generation) lost(attempt);
    }
  }

  void lost(int attempt) {
    if (closed || attempt != generation) return;
    generation++;
    connected = false;
    heartbeat?.cancel();
    socket?.close();
    socket = null;
    for (final item in pending.values) {
      item.complete(false);
    }
    pending.clear();
    platform.invokeMethod<void>('keepAwake', false);
    status = suspended ? '已暂停连接' : '连接中断，正在重连…';
    notifyListeners();
    retry?.cancel();
    if (!suspended) retry = Timer(const Duration(seconds: 2), connect);
  }

  bool send(Map<String, dynamic> data) {
    if (!connected || socket == null) return false;
    try {
      socket!.add(jsonEncode(data));
      return true;
    } catch (_) {
      lost(generation);
      return false;
    }
  }

  Future<bool> sendText(String text) async {
    final id = '${++sequence}';
    final result = Completer<bool>();
    pending[id] = result;
    if (!send({'type': 'text', 'text': text, 'id': id})) {
      pending.remove(id);
      return false;
    }
    try {
      return await result.future.timeout(const Duration(seconds: 5));
    } on TimeoutException {
      return false;
    } finally {
      pending.remove(id);
    }
  }

  @override
  void dispose() {
    closed = true;
    generation++;
    retry?.cancel();
    heartbeat?.cancel();
    if (connected) socket?.add(jsonEncode({'type': 'release'}));
    socket?.close();
    for (final item in pending.values) {
      item.complete(false);
    }
    pending.clear();
    platform.invokeMethod<void>('keepAwake', false);
    super.dispose();
  }
}

class ControlPage extends StatefulWidget {
  const ControlPage({super.key, required this.device, this.connection});
  final PcDevice device;
  final RemoteConnection? connection;
  @override
  State<ControlPage> createState() => _ControlPageState();
}

class _ControlPageState extends State<ControlPage> with WidgetsBindingObserver {
  late final RemoteConnection remote;
  final draft = TextEditingController();
  final surface = GlobalKey<_TouchSurfaceState>();
  bool keyboard = false, sending = false;
  bool leftHeld = false;
  final List<({String? text, String? key})> outgoing = [];
  double sensitivity = 1.6;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    remote = (widget.connection ?? RemoteConnection(widget.device))
      ..onError = showError;
    remote.addListener(update);
    remote.connect();
  }

  void update() {
    if (mounted) {
      setState(() {
        if (!remote.connected) leftHeld = false;
      });
    }
  }

  void showError(String text) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      remote.suspended = true;
      remote.send({'type': 'release'});
      remote.lost(remote.generation);
    } else if (state == AppLifecycleState.resumed && remote.suspended) {
      remote.suspended = false;
      remote.connect();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    remote.removeListener(update);
    remote.dispose();
    draft.dispose();
    super.dispose();
  }

  Future<void> sendText() async {
    if (draft.text.isEmpty || !remote.connected) return;
    outgoing.add((text: draft.text, key: null));
    draft.clear();
    if (sending) return;
    setState(() => sending = true);
    while (outgoing.isNotEmpty) {
      final command = outgoing.first;
      if (command.key != null) {
        remote.send({'type': 'key', 'key': command.key});
        outgoing.removeAt(0);
        continue;
      }
      final success = await remote.sendText(command.text!);
      if (!mounted) return;
      if (!success) {
        draft.text =
            outgoing.map((command) => command.text ?? '').join() + draft.text;
        outgoing.clear();
        setState(() => sending = false);
        showError('未收到输入确认，草稿已保留；请检查电脑内容后再发送');
        return;
      }
      outgoing.removeAt(0);
    }
    setState(() => sending = false);
  }

  Future<void> settings() async {
    await showModalBottomSheet<void>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, updateSheet) => SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  '指针速度',
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
                ),
                Slider(
                  value: sensitivity,
                  min: .5,
                  max: 3.5,
                  divisions: 12,
                  label: sensitivity.toStringAsFixed(1),
                  onChanged: (value) {
                    setState(() => sensitivity = value);
                    updateSheet(() {});
                  },
                ),
                Text(
                  '${sensitivity.toStringAsFixed(1)}×',
                  style: const TextStyle(color: Color(0xff71848a)),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  void key(String name) {
    if (sending) {
      outgoing.add((text: null, key: name));
    } else {
      remote.send({'type': 'key', 'key': name});
    }
  }

  void hideKeyboard() {
    if (!keyboard) return;
    FocusScope.of(context).unfocus();
    SystemChannels.textInput.invokeMethod<void>('TextInput.hide');
    setState(() => keyboard = false);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            widget.device.name,
            style: const TextStyle(fontSize: 17),
            overflow: TextOverflow.ellipsis,
          ),
          Text(
            remote.status,
            style: TextStyle(
              fontSize: 12,
              color: remote.connected
                  ? const Color(0xff087f6c)
                  : Colors.deepOrange,
            ),
          ),
        ],
      ),
      actions: [
        IconButton(
          tooltip: '指针速度',
          onPressed: settings,
          icon: const Icon(Icons.tune),
        ),
      ],
    ),
    body: SafeArea(
      child: LayoutBuilder(
        builder: (context, constraints) => SingleChildScrollView(
          physics: constraints.maxHeight < (keyboard ? 360 : 180)
              ? const ClampingScrollPhysics()
              : const NeverScrollableScrollPhysics(),
          child: SizedBox(
            height: math.max(constraints.maxHeight, keyboard ? 360.0 : 180.0),
            child: Column(
              children: [
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                    child: TouchSurface(
                      key: surface,
                      remote: remote,
                      sensitivity: sensitivity,
                      leftHeld: leftHeld,
                      onTouch: hideKeyboard,
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
                  child: Row(
                    children: [
                      Expanded(
                        child: MouseButton(
                          remote: remote,
                          button: 'left',
                          icon: Icons.mouse_outlined,
                          label: '左键',
                          onHold: (held) => setState(() => leftHeld = held),
                          beforeRelease: () =>
                              surface.currentState?.flushInput(),
                          onMove: (delta) =>
                              surface.currentState?.moveBy(delta * sensitivity),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: MouseButton(
                          remote: remote,
                          button: 'right',
                          icon: Icons.more_horiz,
                          label: '右键',
                        ),
                      ),
                      const SizedBox(width: 8),
                      IconButton.filledTonal(
                        tooltip: '文字输入',
                        onPressed: () {
                          if (keyboard) {
                            hideKeyboard();
                          } else {
                            setState(() => keyboard = true);
                          }
                        },
                        icon: Icon(
                          keyboard ? Icons.keyboard_hide : Icons.keyboard,
                        ),
                      ),
                    ],
                  ),
                ),
                if (keyboard)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
                    child: Column(
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: KeyboardInput(
                                controller: draft,
                                canSend: remote.connected,
                                onSend: sendText,
                                onKey: key,
                              ),
                            ),
                            const SizedBox(width: 8),
                            IconButton.filled(
                              tooltip: '发送到电脑',
                              onPressed: remote.connected && !sending
                                  ? sendText
                                  : null,
                              icon: sending
                                  ? const SizedBox(
                                      width: 20,
                                      height: 20,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                      ),
                                    )
                                  : const Icon(Icons.send),
                            ),
                          ],
                        ),
                        SizedBox(
                          height: 48,
                          child: ListView(
                            scrollDirection: Axis.horizontal,
                            children: [
                              IconButton(
                                tooltip: '退格',
                                onPressed: remote.connected
                                    ? () => key('backspace')
                                    : null,
                                icon: const Icon(Icons.backspace_outlined),
                              ),
                              IconButton(
                                tooltip: '回车',
                                onPressed: remote.connected
                                    ? () => key('enter')
                                    : null,
                                icon: const Icon(Icons.keyboard_return),
                              ),
                              IconButton(
                                tooltip: 'Tab',
                                onPressed: remote.connected
                                    ? () => key('tab')
                                    : null,
                                icon: const Icon(Icons.keyboard_tab),
                              ),
                              IconButton(
                                tooltip: '左',
                                onPressed: remote.connected
                                    ? () => key('left')
                                    : null,
                                icon: const Icon(Icons.arrow_back),
                              ),
                              IconButton(
                                tooltip: '右',
                                onPressed: remote.connected
                                    ? () => key('right')
                                    : null,
                                icon: const Icon(Icons.arrow_forward),
                              ),
                              IconButton(
                                tooltip: '上',
                                onPressed: remote.connected
                                    ? () => key('up')
                                    : null,
                                icon: const Icon(Icons.arrow_upward),
                              ),
                              IconButton(
                                tooltip: '下',
                                onPressed: remote.connected
                                    ? () => key('down')
                                    : null,
                                icon: const Icon(Icons.arrow_downward),
                              ),
                              TextButton(
                                onPressed: remote.connected
                                    ? () => key('escape')
                                    : null,
                                child: const Text('Esc'),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class MouseButton extends StatefulWidget {
  const MouseButton({
    super.key,
    required this.remote,
    required this.button,
    required this.icon,
    required this.label,
    this.onHold,
    this.beforeRelease,
    this.onMove,
  });
  final RemoteConnection remote;
  final String button, label;
  final IconData icon;
  final ValueChanged<bool>? onHold;
  final VoidCallback? beforeRelease;
  final ValueChanged<Offset>? onMove;
  @override
  State<MouseButton> createState() => _MouseButtonState();
}

class _MouseButtonState extends State<MouseButton> {
  final pointers = <int>{};
  void finish(PointerEvent event, bool canceled) {
    if (!pointers.remove(event.pointer) || pointers.isNotEmpty) return;
    widget.beforeRelease?.call();
    widget.remote.send({
      'type': 'button',
      'button': widget.button,
      'down': false,
      'copy': !canceled,
    });
    widget.onHold?.call(false);
    setState(() {});
  }

  @override
  void didUpdateWidget(MouseButton oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.remote.connected) pointers.clear();
  }

  @override
  Widget build(BuildContext context) => Listener(
    onPointerDown: (event) {
      if (!widget.remote.connected) return;
      pointers.add(event.pointer);
      if (pointers.length == 1) {
        widget.remote.send({
          'type': 'button',
          'button': widget.button,
          'down': true,
        });
        widget.onHold?.call(true);
      }
      setState(() {});
    },
    onPointerMove: (event) {
      if (pointers.contains(event.pointer)) widget.onMove?.call(event.delta);
    },
    onPointerUp: (event) => finish(event, false),
    onPointerCancel: (event) => finish(event, true),
    child: Semantics(
      button: true,
      label: widget.label,
      child: Container(
        height: 52,
        decoration: BoxDecoration(
          color: pointers.isEmpty
              ? const Color(0xffe2e9ec)
              : const Color(0xffe5c388),
          borderRadius: BorderRadius.circular(8),
        ),
        alignment: Alignment.center,
        child: Icon(
          widget.icon,
          color: widget.remote.connected
              ? const Color(0xff29464c)
              : Colors.grey,
        ),
      ),
    ),
  );
}

class TouchSurface extends StatefulWidget {
  const TouchSurface({
    super.key,
    required this.remote,
    required this.sensitivity,
    this.leftHeld = false,
    this.onTouch,
  });
  final RemoteConnection remote;
  final double sensitivity;
  final bool leftHeld;
  final VoidCallback? onTouch;
  @override
  State<TouchSurface> createState() => _TouchSurfaceState();
}

class _TouchSurfaceState extends State<TouchSurface> {
  final Map<int, Offset> points = {};
  Timer? hold, flush;
  Offset movement = Offset.zero;
  double scroll = 0, distance = 0;
  bool dragging = false;
  bool externalDrag = false;
  int maxFingers = 0;
  Duration started = Duration.zero;
  Duration? lastTapUp;
  Offset? lastTapPosition;
  @override
  void initState() {
    super.initState();
    flush = Timer.periodic(
      const Duration(milliseconds: 16),
      (_) => flushInput(),
    );
  }

  @override
  void didUpdateWidget(TouchSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.remote.connected) reset();
    if (widget.leftHeld && points.isNotEmpty) {
      externalDrag = true;
      hold?.cancel();
    }
  }

  void flushInput() {
    if (movement != Offset.zero) {
      widget.remote.send({
        'type': 'move',
        'dx': movement.dx,
        'dy': movement.dy,
      });
    }
    if (scroll != 0) widget.remote.send({'type': 'scroll', 'dy': scroll});
    movement = Offset.zero;
    scroll = 0;
  }

  void moveBy(Offset delta) {
    movement += delta;
  }

  void reset() {
    hold?.cancel();
    points.clear();
    movement = Offset.zero;
    scroll = 0;
    dragging = false;
    externalDrag = false;
    maxFingers = 0;
    lastTapUp = null;
    lastTapPosition = null;
  }

  void startDrag() {
    flushInput();
    dragging = true;
    widget.remote.send({'type': 'button', 'button': 'left', 'down': true});
    HapticFeedback.selectionClick();
  }

  @override
  void dispose() {
    hold?.cancel();
    flush?.cancel();
    super.dispose();
  }

  void down(PointerDownEvent event) {
    widget.onTouch?.call();
    if (!widget.remote.connected) return;
    if (points.isEmpty) {
      started = event.timeStamp;
      distance = 0;
      maxFingers = 0;
      externalDrag = widget.leftHeld;
      final gap = lastTapUp == null ? null : started - lastTapUp!;
      final doubleTap =
          !externalDrag &&
          gap != null &&
          gap >= Duration.zero &&
          gap <= kDoubleTapTimeout &&
          lastTapPosition != null &&
          (event.localPosition - lastTapPosition!).distance <= kDoubleTapSlop;
      lastTapUp = null;
      lastTapPosition = null;
      if (doubleTap) {
        startDrag();
      } else {
        hold = Timer(const Duration(milliseconds: 450), () {
          if (!externalDrag &&
              points.length == 1 &&
              distance < 8 &&
              maxFingers == 1) {
            startDrag();
            if (mounted) setState(() {});
          }
        });
      }
    }
    points[event.pointer] = event.localPosition;
    if (points.length > maxFingers) maxFingers = points.length;
    if (points.length > 1) {
      hold?.cancel();
      if (dragging) {
        widget.remote.send({
          'type': 'button',
          'button': 'left',
          'down': false,
          'copy': false,
        });
        dragging = false;
      }
    }
    setState(() {});
  }

  void move(PointerMoveEvent event) {
    final previous = points[event.pointer];
    if (previous == null) return;
    final delta = event.localPosition - previous;
    points[event.pointer] = event.localPosition;
    distance += delta.distance;
    if (distance >= 8) hold?.cancel();
    if (points.length == 1 && maxFingers == 1) {
      movement += delta * widget.sensitivity;
    } else if (points.length == 2) {
      scroll += delta.dy * 1.2;
    }
    setState(() {});
  }

  void up(PointerEvent event, {bool cancel = false}) {
    if (!points.containsKey(event.pointer)) return;
    hold?.cancel();
    points.remove(event.pointer);
    if (points.isEmpty) {
      flushInput();
      if (dragging) {
        widget.remote.send({
          'type': 'button',
          'button': 'left',
          'down': false,
          'copy': !cancel,
        });
      } else if (!externalDrag &&
          !cancel &&
          maxFingers <= 2 &&
          distance < 10 &&
          event.timeStamp - started < const Duration(milliseconds: 400)) {
        widget.remote.send({
          'type': 'click',
          'button': maxFingers == 2 ? 'right' : 'left',
        });
        if (maxFingers == 1) {
          lastTapUp = event.timeStamp;
          lastTapPosition = event.localPosition;
        }
      }
      dragging = false;
      externalDrag = false;
      maxFingers = 0;
    }
    setState(() {});
  }

  @override
  Widget build(BuildContext context) => Listener(
    behavior: HitTestBehavior.opaque,
    onPointerDown: down,
    onPointerMove: move,
    onPointerUp: up,
    onPointerCancel: (event) => up(event, cancel: true),
    child: ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: ColoredBox(
        color: const Color(0xffe8eff1),
        child: Stack(
          fit: StackFit.expand,
          children: [
            CustomPaint(
              painter: SurfacePainter(
                points.values.toList(),
                dragging || widget.leftHeld,
              ),
            ),
            if (!widget.remote.connected)
              const Center(
                child: Icon(Icons.wifi_off, color: Color(0xff71848a), size: 40),
              ),
            Positioned(
              left: 18,
              bottom: 16,
              child: Icon(
                dragging ? Icons.pan_tool_alt : Icons.touch_app_outlined,
                color: const Color(0xff71848a),
                size: 24,
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

class SurfacePainter extends CustomPainter {
  SurfacePainter(this.points, this.dragging);
  final List<Offset> points;
  final bool dragging;
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()..color = const Color(0xffc4d4d9);
    for (double x = 20; x < size.width; x += 24) {
      for (double y = 20; y < size.height; y += 24) {
        canvas.drawCircle(Offset(x, y), 1, paint);
      }
    }
    paint.color = (dragging ? const Color(0xffd89136) : const Color(0xff087f6c))
        .withValues(alpha: .25);
    for (final point in points) {
      canvas.drawCircle(point, 25, paint);
    }
  }

  @override
  bool shouldRepaint(SurfacePainter oldDelegate) => true;
}
