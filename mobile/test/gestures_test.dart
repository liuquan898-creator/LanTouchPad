import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lan_touch_pad/main.dart';

class RecordingConnection extends RemoteConnection {
  RecordingConnection()
    : super(const PcDevice('Test PC', '127.0.0.1', controlPort)) {
    connected = true;
  }
  final messages = <Map<String, dynamic>>[];
  @override
  bool send(Map<String, dynamic> data) {
    messages.add(data);
    return true;
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(platform, (_) async => null);
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (_) async => null);
  });
  Future<RecordingConnection> mount(WidgetTester tester) async {
    final remote = RecordingConnection();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: TouchSurface(remote: remote, sensitivity: 2)),
      ),
    );
    return remote;
  }

  testWidgets('tap, double tap and movement produce expected actions', (
    tester,
  ) async {
    final remote = await mount(tester);
    await tester.tapAt(const Offset(100, 100));
    expect(remote.messages.single['button'], 'left');
    remote.messages.clear();
    await tester.tapAt(const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 80));
    await tester.tapAt(const Offset(100, 100));
    expect(remote.messages.where((m) => m['type'] == 'click').length, 2);
    remote.messages.clear();
    await tester.dragFrom(const Offset(100, 100), const Offset(60, 30));
    await tester.pump(const Duration(milliseconds: 20));
    expect(
      remote.messages.any((m) => m['type'] == 'move' && (m['dx'] as num) > 0),
      true,
    );
    expect(remote.messages.any((m) => m['type'] == 'click'), false);
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });
  testWidgets('two fingers tap right click and scroll without moving cursor', (
    tester,
  ) async {
    final remote = await mount(tester);
    final a = await tester.startGesture(const Offset(100, 100), pointer: 1);
    final b = await tester.startGesture(const Offset(150, 100), pointer: 2);
    await a.up();
    await b.up();
    expect(remote.messages.single, {'type': 'click', 'button': 'right'});
    remote.messages.clear();
    final c = await tester.startGesture(const Offset(100, 100), pointer: 3);
    final d = await tester.startGesture(const Offset(150, 100), pointer: 4);
    await c.moveBy(const Offset(0, 40));
    await d.moveBy(const Offset(0, 40));
    await tester.pump(const Duration(milliseconds: 20));
    await c.up();
    await d.up();
    expect(remote.messages.any((m) => m['type'] == 'scroll'), true);
    expect(
      remote.messages.any((m) => m['type'] == 'move' || m['type'] == 'click'),
      false,
    );
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });
  testWidgets('long press drags and cancellation releases the button', (
    tester,
  ) async {
    final remote = await mount(tester);
    final finger = await tester.startGesture(const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 500));
    expect(remote.messages.single, {
      'type': 'button',
      'button': 'left',
      'down': true,
    });
    await finger.moveBy(const Offset(50, 20));
    await tester.pump(const Duration(milliseconds: 20));
    await finger.cancel();
    expect(remote.messages.last, {
      'type': 'button',
      'button': 'left',
      'down': false,
    });
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });
  testWidgets('disconnected surface emits no control input', (tester) async {
    final remote = await mount(tester);
    remote.connected = false;
    await tester.tapAt(const Offset(100, 100));
    await tester.dragFrom(const Offset(100, 100), const Offset(50, 20));
    expect(remote.messages, isEmpty);
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });
}
