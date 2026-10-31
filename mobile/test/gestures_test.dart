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

  Duration timestamp(WidgetTester tester) =>
      Duration(microseconds: tester.binding.clock.now().microsecondsSinceEpoch);

  Future<TestGesture> downAt(WidgetTester tester, Offset position) async {
    final finger = await tester.createGesture();
    await finger.down(position, timeStamp: timestamp(tester));
    return finger;
  }

  Future<void> tapAt(WidgetTester tester, Offset position) async {
    final finger = await downAt(tester, position);
    await finger.up(timeStamp: timestamp(tester));
  }

  for (final right in [false, true]) {
    testWidgets(
      '${right ? 'right' : 'left'} edge scrolls and stays scrolling inward',
      (tester) async {
        final remote = await mount(tester);
        final width = tester.getSize(find.byType(TouchSurface)).width;
        final finger = await downAt(
          tester,
          Offset(right ? width - 10 : 10, 100),
        );
        await finger.moveBy(Offset(right ? -100 : 100, 40));
        await tester.pump(const Duration(milliseconds: 20));
        expect(remote.messages.single, {'type': 'scroll', 'dy': 48.0});
        await finger.moveBy(const Offset(0, -20));
        await finger.up(timeStamp: timestamp(tester));
        expect(remote.messages.last, {'type': 'scroll', 'dy': -24.0});
        expect(remote.messages.length, 2);
        await tester.pumpWidget(const SizedBox());
        remote.dispose();
      },
    );
  }

  testWidgets('edge taps, double taps and holds never click or drag', (
    tester,
  ) async {
    final remote = await mount(tester);
    for (final x in [10.0, 790.0]) {
      await tapAt(tester, Offset(x, 100));
      await tester.pump(const Duration(milliseconds: 80));
      final finger = await downAt(tester, Offset(x, 100));
      await tester.pump(const Duration(milliseconds: 600));
      await finger.up(timeStamp: timestamp(tester));
    }
    expect(remote.messages, isEmpty);
    // An edge touch also clears a pending central double tap.
    await tapAt(tester, const Offset(80, 100));
    await tapAt(tester, const Offset(10, 100));
    await tapAt(tester, const Offset(80, 100));
    expect(remote.messages, [
      {'type': 'click', 'button': 'left'},
      {'type': 'click', 'button': 'left'},
    ]);
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });

  testWidgets('central movement and drag stay pointer gestures across edges', (
    tester,
  ) async {
    final remote = await mount(tester);
    for (final right in [false, true]) {
      final start = Offset(right ? 700 : 100, 100);
      final end = Offset(right ? 790 : 10, 140);
      final finger = await downAt(tester, start);
      await finger.moveTo(end);
      await finger.up(timeStamp: timestamp(tester));
      expect(remote.messages.single, {
        'type': 'move',
        'dx': (end.dx - start.dx) * 2,
        'dy': 80.0,
      });
      remote.messages.clear();
      await tapAt(tester, start);
      await tester.pump(const Duration(milliseconds: 80));
      final drag = await downAt(tester, start);
      await drag.moveTo(end);
      await drag.up(timeStamp: timestamp(tester));
      expect(remote.messages.map((m) => m['type']), [
        'click',
        'button',
        'move',
        'button',
      ]);
      expect(remote.messages.last['copy'], isTrue);
      remote.messages.clear();
    }
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });

  testWidgets('edge cancellation and disconnect reset the gesture mode', (
    tester,
  ) async {
    final remote = await mount(tester);
    final canceled = await downAt(tester, const Offset(10, 100));
    await canceled.moveBy(const Offset(0, 40));
    await canceled.cancel();
    expect(remote.messages.single['type'], 'scroll');
    remote.messages.clear();
    final interrupted = await downAt(tester, const Offset(790, 100));
    await interrupted.moveBy(const Offset(0, 20));
    remote.connected = false;
    Future<void> rebuild() => tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: TouchSurface(remote: remote, sensitivity: 2)),
      ),
    );
    await rebuild();
    await interrupted.moveBy(const Offset(0, 20));
    await interrupted.up();
    await tester.pump(const Duration(milliseconds: 20));
    expect(remote.messages, isEmpty);
    remote.connected = true;
    await rebuild();
    await tapAt(tester, const Offset(100, 100));
    expect(remote.messages.single, {'type': 'click', 'button': 'left'});
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });

  testWidgets('tap, double tap and movement produce expected actions', (
    tester,
  ) async {
    final remote = await mount(tester);
    await tapAt(tester, const Offset(100, 100));
    expect(remote.messages.single['button'], 'left');
    remote.messages.clear();
    await tester.pump(const Duration(milliseconds: 400));
    await tapAt(tester, const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 80));
    await tapAt(tester, const Offset(100, 100));
    expect(remote.messages, [
      {'type': 'click', 'button': 'left'},
      {'type': 'button', 'button': 'left', 'down': true},
      {'type': 'button', 'button': 'left', 'down': false, 'copy': true},
    ]);
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
  testWidgets(
    'second tap immediately holds left while dragging until release',
    (tester) async {
      final remote = await mount(tester);
      await tapAt(tester, const Offset(100, 100));
      await tester.pump(const Duration(milliseconds: 80));
      final finger = await downAt(tester, const Offset(105, 102));
      expect(remote.messages, [
        {'type': 'click', 'button': 'left'},
        {'type': 'button', 'button': 'left', 'down': true},
      ]);
      await finger.moveBy(const Offset(80, 20));
      await tester.pump(const Duration(milliseconds: 500));
      expect(remote.messages.where((m) => m['down'] == true).length, 1);
      expect(remote.messages.any((m) => m['down'] == false), isFalse);
      expect(remote.messages.where((m) => m['type'] == 'move').single, {
        'type': 'move',
        'dx': 160.0,
        'dy': 40.0,
      });
      await finger.moveBy(const Offset(10, 0));
      await finger.up(timeStamp: timestamp(tester));
      expect(remote.messages[remote.messages.length - 2]['type'], 'move');
      expect(remote.messages.last, {
        'type': 'button',
        'button': 'left',
        'down': false,
        'copy': true,
      });
      expect(remote.messages.where((m) => m['type'] == 'click').length, 1);
      await tester.pumpWidget(const SizedBox());
      remote.dispose();
    },
  );
  testWidgets('slow or distant second touch stays ordinary pointer movement', (
    tester,
  ) async {
    final remote = await mount(tester);
    await tapAt(tester, const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 350));
    remote.messages.clear();
    final slow = await downAt(tester, const Offset(100, 100));
    await slow.moveBy(const Offset(30, 0));
    await slow.up(timeStamp: timestamp(tester));
    expect(remote.messages.every((m) => m['type'] == 'move'), isTrue);
    await tapAt(tester, const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 80));
    remote.messages.clear();
    final distant = await downAt(tester, const Offset(300, 300));
    await distant.moveBy(const Offset(30, 0));
    await distant.up(timeStamp: timestamp(tester));
    expect(remote.messages.every((m) => m['type'] == 'move'), isTrue);
    await tester.pumpWidget(const SizedBox());
    remote.dispose();
  });
  testWidgets('canceling double-tap drag releases without copying', (
    tester,
  ) async {
    final remote = await mount(tester);
    await tapAt(tester, const Offset(100, 100));
    await tester.pump(const Duration(milliseconds: 80));
    final finger = await downAt(tester, const Offset(100, 100));
    await finger.moveBy(const Offset(40, 0));
    await finger.cancel();
    expect(remote.messages.last, {
      'type': 'button',
      'button': 'left',
      'down': false,
      'copy': false,
    });
    remote.messages.clear();
    await tapAt(tester, const Offset(100, 100));
    expect(remote.messages.single, {'type': 'click', 'button': 'left'});
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
      'copy': false,
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

  testWidgets(
    'holding left button allows one-finger drag and flushes before release',
    (tester) async {
      final remote = RecordingConnection();
      final surface = GlobalKey<State<TouchSurface>>();
      var held = false;
      await tester.pumpWidget(
        MaterialApp(
          home: StatefulBuilder(
            builder: (context, update) => Scaffold(
              body: Column(
                children: [
                  Expanded(
                    child: TouchSurface(
                      key: surface,
                      remote: remote,
                      sensitivity: 1,
                      leftHeld: held,
                    ),
                  ),
                  MouseButton(
                    remote: remote,
                    button: 'left',
                    icon: Icons.mouse,
                    label: '左键',
                    onHold: (value) => update(() => held = value),
                    onMove: (delta) => remote.send({
                      'type': 'move',
                      'dx': delta.dx,
                      'dy': delta.dy,
                    }),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
      final finger = await tester.startGesture(
        tester.getCenter(find.byType(MouseButton)),
      );
      await tester.pump();
      await finger.moveBy(const Offset(50, -25));
      await finger.up();
      expect(remote.messages.first, {
        'type': 'button',
        'button': 'left',
        'down': true,
      });
      expect(remote.messages.any((m) => m['type'] == 'move'), isTrue);
      expect(remote.messages.last, {
        'type': 'button',
        'button': 'left',
        'down': false,
        'copy': true,
      });
      expect(remote.messages.any((m) => m['type'] == 'click'), isFalse);
      remote.messages.clear();
      final buttonFinger = await tester.startGesture(
        tester.getCenter(find.byType(MouseButton)),
        pointer: 2,
      );
      await tester.pump();
      final padFinger = await tester.startGesture(
        const Offset(10, 100),
        pointer: 3,
      );
      await padFinger.moveBy(const Offset(60, 20));
      await tester.pump(const Duration(milliseconds: 20));
      await padFinger.up();
      expect(remote.messages.where((m) => m['type'] == 'button').length, 1);
      expect(remote.messages.any((m) => m['type'] == 'click'), isFalse);
      expect(remote.messages.where((m) => m['type'] == 'move').single, {
        'type': 'move',
        'dx': 60.0,
        'dy': 20.0,
      });
      expect(remote.messages.any((m) => m['type'] == 'scroll'), isFalse);
      await buttonFinger.up();
      expect(remote.messages.last['down'], false);
      await tester.pumpWidget(const SizedBox());
      remote.dispose();
    },
  );
}
