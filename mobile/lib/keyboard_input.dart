import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class KeyboardInput extends StatefulWidget {
  const KeyboardInput({
    super.key,
    required this.controller,
    required this.onSend,
    required this.onKey,
    required this.canSend,
  });
  final TextEditingController controller;
  final VoidCallback onSend;
  final ValueChanged<String> onKey;
  final bool canSend;
  @override
  State<KeyboardInput> createState() => _KeyboardInputState();
}

class _KeyboardInputState extends State<KeyboardInput> {
  static const events = MethodChannel('online.yuxiboy.lantouchpad/keyboard');
  MethodChannel? view;
  String nativeText = '';

  @override
  void initState() {
    super.initState();
    nativeText = widget.controller.text;
    widget.controller.addListener(syncText);
    events.setMethodCallHandler((call) async {
      if (!mounted) return;
      switch (call.method) {
        case 'changed':
          nativeText = call.arguments as String;
          widget.controller.value = TextEditingValue(
            text: nativeText,
            selection: TextSelection.collapsed(offset: nativeText.length),
          );
        case 'submit':
          nativeText = '';
          widget.onSend();
          syncText();
        case 'key':
          widget.onKey(call.arguments as String);
      }
    });
  }

  void syncText() {
    if (nativeText == widget.controller.text) return;
    nativeText = widget.controller.text;
    view?.invokeMethod<void>('setText', nativeText);
  }

  @override
  void didUpdateWidget(KeyboardInput oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.canSend != widget.canSend) {
      view?.invokeMethod<void>('setConnected', widget.canSend);
    }
  }

  @override
  void dispose() {
    events.setMethodCallHandler(null);
    widget.controller.removeListener(syncText);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!Platform.isAndroid) {
      return TextField(
        controller: widget.controller,
        autofocus: true,
        decoration: const InputDecoration(hintText: '输入文字'),
        textInputAction: TextInputAction.send,
        onEditingComplete: () {},
        onSubmitted: (_) => widget.onSend(),
      );
    }
    return SizedBox(
      height: 64,
      child: AndroidView(
        viewType: 'online.yuxiboy.lantouchpad/keyboard-input',
        creationParams: {
          'text': widget.controller.text,
          'connected': widget.canSend,
        },
        creationParamsCodec: const StandardMessageCodec(),
        onPlatformViewCreated: (id) {
          view = MethodChannel('online.yuxiboy.lantouchpad/keyboard-input/$id');
          view!.invokeMethod<void>('setText', widget.controller.text);
          view!.invokeMethod<void>('setConnected', widget.canSend);
          view!.invokeMethod<void>('showKeyboard');
        },
      ),
    );
  }
}
