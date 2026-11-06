package com.example.smarthydro.transport

import android.util.Log

enum class LogLevel { DEBUG, INFO, WARN, ERROR }

// Where log lines go. A phone writes them to Logcat, and the tests catch them to check what was said
fun interface BoxLogSink {
    fun write(level: LogLevel, tag: String, message: String)
}

// The log of everything the Bluetooth side of the app does, under one Logcat tag so a test on a real box can be read
// back afterwards. Each line starts with the part of the app it comes from, for example [conn] or [cmd]. Nothing secret
// is written: the code a box shows for pairing never reaches the phone's app
object BoxLog {

    // Filter Logcat on this to see only these lines
    const val TAG = "SmartHydroBle"

    // How many characters of a frame from a box are kept in a line
    private const val CLIP = 160

    @Volatile
    var sink: BoxLogSink = AndroidLogSink

    fun d(area: String, message: String) = write(LogLevel.DEBUG, area, message)
    fun i(area: String, message: String) = write(LogLevel.INFO, area, message)
    fun w(area: String, message: String) = write(LogLevel.WARN, area, message)
    fun e(area: String, message: String) = write(LogLevel.ERROR, area, message)

    // Cuts a long piece of text, such as a frame from a box, so one line stays readable. A frame that is cut short on
    // the way in shows up as one that stops part way, which is what to look for
    fun clip(text: String, max: Int = CLIP): String =
        if (text.length <= max) text else text.take(max) + "… (${text.length} characters)"

    private fun write(level: LogLevel, area: String, message: String) = sink.write(level, TAG, "[$area] $message")
}

// Writes to Logcat. Where there is no Android, as in the tests, nothing is written and nothing goes wrong
private object AndroidLogSink : BoxLogSink {

    override fun write(level: LogLevel, tag: String, message: String) {
        try {
            when (level) {
                LogLevel.DEBUG -> Log.d(tag, message)
                LogLevel.INFO -> Log.i(tag, message)
                LogLevel.WARN -> Log.w(tag, message)
                LogLevel.ERROR -> Log.e(tag, message)
            }
        } catch (e: RuntimeException) {
            // Not running on a phone
        }
    }
}
