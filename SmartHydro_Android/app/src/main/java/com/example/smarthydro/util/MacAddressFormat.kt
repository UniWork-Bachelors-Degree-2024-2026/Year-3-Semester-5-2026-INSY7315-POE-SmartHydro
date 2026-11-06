package com.example.smarthydro.util

// MAC address helpers. They accept a MAC typed with colons, dashes or no separator and convert it
// to one standard format, for example "A4:CF:12:9B:3E:01".
object MacAddressFormat {

    // Six pairs of hex digits, separated by colons, dashes or nothing at all (Kotlin, 2026).
    // ^\s* and \s*$ allow spaces around the value, [0-9A-Fa-f]{2} is one hex pair, and
    // ([:-]?[0-9A-Fa-f]{2}){5} is five more pairs, each with an optional ":" or "-" in front (Oracle, 2026).
    private val pattern = Regex("^\\s*[0-9A-Fa-f]{2}([:-]?[0-9A-Fa-f]{2}){5}\\s*$")

    // Turns whatever was typed into upper case hex pairs split by colons, e.g. "a4cf12" becomes "A4:CF:12"
    fun formatWhileTyping(input: String): String {
        val hex = input
            .filter { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }
            .uppercase()
            .take(12)
        return hex.chunked(2).joinToString(":")
    }

    // Where the cursor should sit after formatWhileTyping, so it stays after the same hex digit.
    // The formatted text has a colon after every second digit, so n digits end at n + (n - 1) / 2.
    fun cursorAfterFormat(input: String, cursor: Int): Int {
        val digitsBeforeCursor = formatWhileTyping(input.take(cursor.coerceIn(0, input.length))).count { it != ':' }
        return if (digitsBeforeCursor == 0) 0 else digitsBeforeCursor + (digitsBeforeCursor - 1) / 2
    }

    // True when the value is a complete MAC address in any of the accepted separator styles
    fun isValid(input: String): Boolean {
        val clean = input.trim()
        if (pattern.matches(clean)) return true
        val hexOnly = clean.filter { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }
        return hexOnly.length == 12
    }

    // Returns the standard format, or null when the value is not a valid MAC address
    fun normalise(input: String): String? {
        val clean = input.trim()
        val hexOnly = clean.filter { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }
        return if (hexOnly.length == 12) {
            hexOnly.uppercase().chunked(2).joinToString(":")
        } else if (isValid(clean)) {
            formatWhileTyping(clean)
        } else {
            null
        }
    }
}

//Reference Lists:

//Kotlin, 2026. Regex. [online] Available at: <https://kotlinlang.org/api/core/kotlin-stdlib/kotlin.text/-regex/> [Accessed 26 September 2026].

//Oracle, 2026. Pattern (Java SE 17 & JDK 17). [online] Available at: <https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html> [Accessed 26 September 2026].
