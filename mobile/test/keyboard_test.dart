import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lan_touch_pad/main.dart';
import 'package:lan_touch_pad/keyboard_input.dart';

class KeyboardConnection extends RemoteConnection {
  KeyboardConnection()
    : super(const PcDevice('Test PC', '127.0.0.1', controlPort)) {
    connected = true;
  }
  final commands = <String>[];
  final messages = <Map<String, dynamic>>[];
  final acknowledgements = <Completer<bool>>[];
  bool hold = false;
  @override
  Future<void> connect() async {}
  @override
  bool send(Map<String, dynamic> data) {
    messages.add(data);
    commands.add('key:${data['key']}');
    return connected;
  }

  @override
  Future<bool> sendText(String text) {
    commands.add('text:$text');
    if (!hold) return Future.value(true);
    final result = Completer<bool>();
    acknowledgements.add(result);
    return result.future;
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
  Future<KeyboardConnection> mount(
    WidgetTester tester, {
    bool openKeyboard = true,
  }) async {
    final remote = KeyboardConnection();
    await tester.pumpWidget(
      MaterialApp(
        home: ControlPage(device: remote.device, connection: remote),
      ),
    );
    await tester.pump();
    if (openKeyboard) {
      await tester.tap(find.byTooltip('文字输入'));
      await tester.pump();
    }
    return remote;
  }

  Future<void> native(
    WidgetTester tester,
    String method, [
    Object? arguments,
  ]) async {
    await tester.binding.defaultBinaryMessenger.handlePlatformMessage(
      'online.yuxiboy.lantouchpad/keyboard',
      const StandardMethodCodec().encodeMethodCall(
        MethodCall(method, arguments),
      ),
      (_) {},
    );
    await tester.pump();
  }

  TextEditingController draft(WidgetTester tester) =>
      tester.widget<KeyboardInput>(find.byType(KeyboardInput)).controller;

  testWidgets('keyboard starts hidden and button opens focused input', (
    tester,
  ) async {
    final remote = await mount(tester, openKeyboard: false);
    expect(find.byType(KeyboardInput), findsNothing);
    expect(find.byType(MouseButton), findsNothing);
    final keyboardButton = tester.getRect(find.byTooltip('文字输入'));
    expect(
      keyboardButton.width,
      tester.getSize(find.byType(TouchSurface)).width,
    );
    expect(keyboardButton.height, 56);
    await tester.tapAt(
      Offset(keyboardButton.left + 12, keyboardButton.center.dy),
    );
    await tester.pump();
    expect(find.byType(KeyboardInput), findsOneWidget);
    await tester.enterText(find.byType(TextField), '你好');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pump();
    expect(remote.commands, ['text:你好', 'key:enter']);
    expect(draft(tester).text, isEmpty);
    expect(find.byType(KeyboardInput), findsOneWidget);
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
      isTrue,
    );
    await tester.pumpWidget(const SizedBox());
  });
  testWidgets('touchpad hides keyboard, preserves draft and still clicks', (
    tester,
  ) async {
    final remote = await mount(tester);
    await tester.enterText(find.byType(TextField), '未发送的草稿');
    await tester.tapAt(tester.getCenter(find.byType(TouchSurface)));
    await tester.pump();
    expect(find.byType(KeyboardInput), findsNothing);
    expect(remote.messages.single, {'type': 'click', 'button': 'left'});
    await tester.tap(find.byTooltip('文字输入'));
    await tester.pump();
    expect(draft(tester).text, '未发送的草稿');
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
      isTrue,
    );
    await tester.tap(find.byTooltip('文字输入'));
    await tester.pump();
    expect(find.byType(KeyboardInput), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });
  testWidgets('native return sends once and empty draft delete reaches PC', (
    tester,
  ) async {
    final remote = await mount(tester);
    await native(tester, 'changed', '输入法文字');
    await native(tester, 'submit');
    await native(tester, 'key', 'backspace');
    await native(tester, 'key', 'delete');
    expect(remote.commands, [
      'text:输入法文字',
      'key:enter',
      'key:backspace',
      'key:delete',
    ]);
    expect(draft(tester).text, isEmpty);
    await tester.pumpWidget(const SizedBox());
  });
  testWidgets('rapid returns and delete preserve command order', (
    tester,
  ) async {
    final remote = (await mount(tester))..hold = true;
    await native(tester, 'changed', '第一条');
    await native(tester, 'submit');
    await native(tester, 'changed', '第二条');
    await native(tester, 'submit');
    await native(tester, 'key', 'backspace');
    expect(remote.commands, ['text:第一条']);
    remote.acknowledgements[0].complete(true);
    await tester.pump();
    expect(remote.commands, ['text:第一条', 'key:enter', 'text:第二条']);
    remote.acknowledgements[1].complete(true);
    await tester.pump();
    expect(remote.commands, [
      'text:第一条',
      'key:enter',
      'text:第二条',
      'key:enter',
      'key:backspace',
    ]);
    await tester.pumpWidget(const SizedBox());
  });
  testWidgets(
    'failed send restores queued drafts and does not replay deletion',
    (tester) async {
      final remote = (await mount(tester))..hold = true;
      await native(tester, 'changed', '第一条');
      await native(tester, 'submit');
      await native(tester, 'changed', '第二条');
      await native(tester, 'submit');
      await native(tester, 'key', 'backspace');
      remote.acknowledgements[0].complete(false);
      await tester.pump();
      expect(remote.commands, ['text:第一条']);
      expect(draft(tester).text, '第一条第二条');
      await tester.pumpWidget(const SizedBox());
    },
  );
  testWidgets('IME action on empty draft sends Enter once', (tester) async {
    final remote = await mount(tester);
    await native(tester, 'submit');
    expect(remote.commands, ['key:enter']);
    await tester.pumpWidget(const SizedBox());
  });
}
