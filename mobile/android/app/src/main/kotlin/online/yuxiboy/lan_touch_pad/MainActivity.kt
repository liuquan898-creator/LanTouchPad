package online.yuxiboy.lan_touch_pad

import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import android.view.WindowManager

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        flutterEngine.platformViewsController.registry.registerViewFactory("online.yuxiboy.lantouchpad/keyboard-input", KeyboardInputFactory(flutterEngine.dartExecutor.binaryMessenger))
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "online.yuxiboy.lantouchpad/device")
            .setMethodCallHandler { call, result ->
                if (call.method == "keepAwake") {
                    if (call.arguments == true) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    result.success(null)
                } else result.notImplemented()
            }
    }
}
