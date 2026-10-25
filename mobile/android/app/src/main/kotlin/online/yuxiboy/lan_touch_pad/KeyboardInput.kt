package online.yuxiboy.lan_touch_pad

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.text.Editable
import android.text.InputFilter
import android.text.InputType
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputConnectionWrapper
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import io.flutter.plugin.common.BinaryMessenger
import io.flutter.plugin.common.MethodChannel
import io.flutter.plugin.common.StandardMessageCodec
import io.flutter.plugin.platform.PlatformView
import io.flutter.plugin.platform.PlatformViewFactory

class KeyboardInputFactory(private val messenger: BinaryMessenger) : PlatformViewFactory(StandardMessageCodec.INSTANCE) {
    override fun create(context: Context, id: Int, args: Any?): PlatformView = KeyboardInputView(context, id, args as? Map<*, *>, messenger)
}

private class KeyboardInputView(context: Context, id: Int, args: Map<*, *>?, messenger: BinaryMessenger) : PlatformView {
    private val events = MethodChannel(messenger, "online.yuxiboy.lantouchpad/keyboard")
    private val commands = MethodChannel(messenger, "online.yuxiboy.lantouchpad/keyboard-input/$id")
    private var disposed = false
    private var connected = args?.get("connected") == true
    private var suppressChanges = false
    private val editor = RemoteEditText(context, ::submit, { key -> if (!disposed) events.invokeMethod("key", key) })

    private fun submit() {
        if (!disposed && connected) {
            events.invokeMethod("submit", null)
            suppressChanges = true
            try { editor.text.clear() } finally { suppressChanges = false }
        }
    }

    init {
        val density = context.resources.displayMetrics.density
        editor.textSize = 16f
        editor.hint = "输入文字"
        editor.setTextColor(Color.rgb(32, 59, 67))
        editor.setHintTextColor(Color.rgb(100, 119, 128))
        editor.setPadding((12 * density).toInt(), (8 * density).toInt(), (12 * density).toInt(), (8 * density).toInt())
        editor.background = GradientDrawable().apply {
            setColor(Color.TRANSPARENT)
            cornerRadius = 8 * density
            setStroke(density.toInt().coerceAtLeast(1), Color.rgb(113, 132, 138))
        }
        editor.inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
        editor.imeOptions = EditorInfo.IME_ACTION_SEND
        editor.maxLines = 3
        editor.filters = arrayOf(InputFilter.LengthFilter(4000))
        editor.setText(args?.get("text") as? String ?: "")
        editor.setSelection(editor.length())
        editor.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                if (!disposed && !suppressChanges) events.invokeMethod("changed", s.toString())
            }
            override fun afterTextChanged(s: Editable?) {}
        })
        editor.setOnEditorActionListener { _, action, event ->
            if (event == null && action in listOf(EditorInfo.IME_ACTION_SEND, EditorInfo.IME_ACTION_DONE, EditorInfo.IME_ACTION_GO)) {
                submit()
                true
            } else false
        }
        commands.setMethodCallHandler { call, result ->
            if (call.method == "setText") {
                val value = call.arguments as String
                if (editor.text.toString() != value) { editor.setText(value); editor.setSelection(editor.length()) }
                result.success(null)
            } else if (call.method == "setConnected") {
                connected = call.arguments == true
                result.success(null)
            } else if (call.method == "showKeyboard") {
                editor.showKeyboard()
                result.success(null)
            } else result.notImplemented()
        }
        editor.showKeyboard()
    }

    override fun getView(): View = editor
    override fun dispose() { disposed = true; commands.setMethodCallHandler(null); editor.hideKeyboard() }
}

private class RemoteEditText(context: Context, private val submit: () -> Unit, private val remoteKey: (String) -> Unit) : EditText(context) {
    private var deleting = false
    private var keyboardRequested = false
    private var showAttempts = 0
    private val showInput = object : Runnable {
        override fun run() {
            if (!keyboardRequested || !isAttachedToWindow || !hasWindowFocus()) return
            requestFocus()
            val input = context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            if (input.showSoftInput(this@RemoteEditText, InputMethodManager.SHOW_IMPLICIT)) {
                keyboardRequested = false
            } else if (++showAttempts < 5) {
                postDelayed(this, 100)
            }
        }
    }

    fun showKeyboard() {
        keyboardRequested = true
        showAttempts = 0
        requestFocus()
        removeCallbacks(showInput)
        post(showInput)
    }

    fun hideKeyboard() {
        keyboardRequested = false
        removeCallbacks(showInput)
        (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).hideSoftInputFromWindow(windowToken, 0)
        clearFocus()
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        if (keyboardRequested) post(showInput)
    }

    override fun onWindowFocusChanged(hasWindowFocus: Boolean) {
        super.onWindowFocusChanged(hasWindowFocus)
        if (hasWindowFocus && keyboardRequested) post(showInput)
    }

    override fun onDetachedFromWindow() {
        removeCallbacks(showInput)
        super.onDetachedFromWindow()
    }

    private fun composing(): Boolean = BaseInputConnection.getComposingSpanStart(text) >= 0

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (keyCode == KeyEvent.KEYCODE_ENTER) { submit(); return true }
        if (!deleting && !composing()) {
            if (keyCode == KeyEvent.KEYCODE_DEL) remoteKey("backspace")
            if (keyCode == KeyEvent.KEYCODE_FORWARD_DEL) remoteKey("delete")
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean = if (keyCode == KeyEvent.KEYCODE_ENTER) true else super.onKeyUp(keyCode, event)

    override fun onCreateInputConnection(info: EditorInfo): InputConnection? {
        val target = super.onCreateInputConnection(info) ?: return null
        info.imeOptions = (info.imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION.inv())
        return object : InputConnectionWrapper(target, false) {
            private fun delete(before: Int, after: Int, action: () -> Boolean): Boolean {
                if (!deleting && !composing()) {
                    if (before > 0) remoteKey("backspace") else if (after > 0) remoteKey("delete")
                }
                deleting = true
                return try { action() } finally { deleting = false }
            }
            override fun deleteSurroundingText(beforeLength: Int, afterLength: Int): Boolean = delete(beforeLength, afterLength) { super.deleteSurroundingText(beforeLength, afterLength) }
            override fun deleteSurroundingTextInCodePoints(beforeLength: Int, afterLength: Int): Boolean = delete(beforeLength, afterLength) { super.deleteSurroundingTextInCodePoints(beforeLength, afterLength) }
            override fun commitText(text: CharSequence?, newCursorPosition: Int): Boolean {
                if (text?.toString() in listOf("\n", "\r", "\r\n")) { finishComposingText(); submit(); return true }
                return super.commitText(text, newCursorPosition)
            }
        }
    }
}
