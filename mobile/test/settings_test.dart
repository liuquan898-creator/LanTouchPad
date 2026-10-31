import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lan_touch_pad/main.dart';

class SettingsConnection extends RemoteConnection {
  SettingsConnection()
    : super(const PcDevice('Test PC', '127.0.0.1', controlPort)) {
    connected = true;
  }
  final messages = <Map<String, dynamic>>[];
  @override
  Future<void> connect() async {}
  @override
  bool send(Map<String, dynamic> data) {
    messages.add(data);
    return true;
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  double? saved;
  setUp(() {
    saved = null;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(platform, (call) async {
          if (call.method == 'getScrollSpeed') return saved;
          if (call.method == 'setScrollSpeed') saved = call.arguments as double;
          return null;
        });
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (_) async => null);
  });

  Future<SettingsConnection> mount(WidgetTester tester) async {
    final remote = SettingsConnection();
    await tester.pumpWidget(
      MaterialApp(
        home: ControlPage(device: remote.device, connection: remote),
      ),
    );
    await tester.pump();
    return remote;
  }

  testWidgets(
    'scroll setting changes both scroll gestures and survives reopening',
    (tester) async {
      final remote = await mount(tester);
      final original = tester.widget<TouchSurface>(find.byType(TouchSurface));
      expect(original.scrollSpeed, 2);
      await tester.tap(find.byTooltip('设置'));
      await tester.pumpAndSettle();
      final slider = find.byKey(const ValueKey('scroll-speed'));
      await tester.tapAt(tester.getCenter(slider) + const Offset(80, 0));
      await tester.pumpAndSettle();
      final speed = tester.widget<Slider>(slider).value;
      expect(speed, greaterThan(2));
      expect(saved, speed);
      Navigator.of(tester.element(slider)).pop();
      await tester.pumpAndSettle();
      final surface = tester.widget<TouchSurface>(find.byType(TouchSurface));
      expect(surface.scrollSpeed, speed);
      expect(surface.sensitivity, original.sensitivity);
      final rect = tester.getRect(find.byType(TouchSurface));
      final edge = await tester.startGesture(
        Offset(rect.left + 10, rect.center.dy),
      );
      await edge.moveBy(const Offset(0, 40));
      await edge.up();
      expect(remote.messages.single, {'type': 'scroll', 'dy': 48 * speed});
      remote.messages.clear();
      final a = await tester.startGesture(rect.center, pointer: 2);
      final b = await tester.startGesture(
        rect.center + const Offset(40, 0),
        pointer: 3,
      );
      await a.moveBy(const Offset(0, 40));
      await b.moveBy(const Offset(0, 40));
      await a.up();
      await b.up();
      expect(remote.messages.single, {'type': 'scroll', 'dy': 96 * speed});
      await tester.pumpWidget(const SizedBox());
      await mount(tester);
      expect(
        tester.widget<TouchSurface>(find.byType(TouchSurface)).scrollSpeed,
        speed,
      );
      await tester.pumpWidget(const SizedBox());
    },
  );

  for (final speed in [.5, 5.0, -1.0, double.nan]) {
    testWidgets('stored scroll speed $speed is validated on load', (
      tester,
    ) async {
      saved = speed;
      final remote = await mount(tester);
      final valid = speed.isFinite && speed >= .5 && speed <= 5;
      expect(
        tester.widget<TouchSurface>(find.byType(TouchSurface)).scrollSpeed,
        valid ? speed : defaultScrollSpeed,
      );
      final rect = tester.getRect(find.byType(TouchSurface));
      final edge = await tester.startGesture(
        Offset(rect.right - 10, rect.center.dy),
      );
      await edge.moveBy(const Offset(0, 40));
      await edge.up();
      expect(
        remote.messages.single['dy'],
        48 * (valid ? speed : defaultScrollSpeed),
      );
      await tester.pumpWidget(const SizedBox());
    });
  }

  testWidgets('speed settings fit a short landscape display', (tester) async {
    tester.view.physicalSize = const Size(640, 360);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await mount(tester);
    await tester.tap(find.byTooltip('设置'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const ValueKey('scroll-speed')));
    expect(tester.takeException(), isNull);
    expect(find.text('滚轮速度'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });
}
