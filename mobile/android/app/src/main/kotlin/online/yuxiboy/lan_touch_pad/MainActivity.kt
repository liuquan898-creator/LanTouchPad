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
                } else if (call.method == "getScrollSpeed") {
                    val preferences = getSharedPreferences("controls", MODE_PRIVATE)
                    result.success(preferences.getFloat("scrollSpeed", 2f).toDouble())
                } else if (call.method == "setScrollSpeed") {
                    val speed = (call.arguments as? Number)?.toDouble()
                    if (speed == null || !speed.isFinite() || speed < .5 || speed > 5) {
                        result.error("invalid_speed", "Scroll speed must be between 0.5 and 5", null)
                    } else {
                        getSharedPreferences("controls", MODE_PRIVATE).edit()
                            .putFloat("scrollSpeed", speed.toFloat()).apply()
                        result.success(null)
                    }
                } else result.notImplemented()
            }
    }
}
