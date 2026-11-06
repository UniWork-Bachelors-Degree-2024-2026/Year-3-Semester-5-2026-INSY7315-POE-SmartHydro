package com.example.smarthydro.transport

// Where one of the mode buttons is at
enum class ModeOptionState {
    // The box is in this mode now
    CURRENT,

    // The person asked for this mode and the box has not got there yet
    SWITCHING,

    // The box wants this mode but is in another one because the link failed, and will go back when it can
    WANTED,

    // The box can be switched to this mode
    AVAILABLE
}

// One mode button: its name, what it means for the farmer and where it is at
data class ModeOption(val mode: BoxMode, val label: String, val description: String, val state: ModeOptionState)

// What the mode buttons show: which mode the box is in, a note when something needs explaining, and the three buttons
data class ModeControlContent(val headline: String, val note: String?, val options: List<ModeOption>)

// What a person is asked before the box is switched to a mode
data class ModeConfirmation(val title: String, val message: String, val confirmLabel: String)

// Works out what the mode buttons say and when to ask first. The buttons only draw this, so what each one shows for
// each way the box can be is decided here and can be tested
object ModeControl {

    // How long after asking for a mode the buttons keep saying the box is switching, before showing what the box really does
    const val SWITCHING_SHOWN_MS = 20_000L

    private val descriptions = mapOf(
        BoxMode.LORA_BLE to "Long range, needs no Wi-Fi",
        BoxMode.ONLINE to "Uses the box's saved Wi-Fi",
        BoxMode.SOFTAP to "The box makes its own Wi-Fi"
    )

    // The mode buttons for a box, or null when they should not be shown: the mode can only be changed over Bluetooth,
    // and only a box that reports its mode can be shown one. requested is what the person just asked for, if the box
    // has not reached it yet
    fun content(info: BoxModeInfo?, link: BoxLink?, requested: BoxMode?): ModeControlContent? {
        if (link != BoxLink.BLUETOOTH) return null
        val current = info?.mode ?: return null

        val switching = requested != null && requested != current
        val fellBack = !switching && info.autoFallback && info.preferred != null && info.preferred != current

        val options = BoxMode.entries.map { mode ->
            val state = when {
                mode == current -> ModeOptionState.CURRENT
                switching && mode == requested -> ModeOptionState.SWITCHING
                fellBack && mode == info.preferred -> ModeOptionState.WANTED
                else -> ModeOptionState.AVAILABLE
            }
            ModeOption(mode, mode.label, descriptions.getValue(mode), state)
        }

        val note = when {
            switching -> "Switching to ${requested!!.label}…"
            fellBack -> "The box moved to ${current.label} by itself because ${info.preferred!!.label} wasn't getting through. " +
                "It switches back when it can."
            else -> null
        }

        return ModeControlContent(headline = "The box is using ${current.label}", note = note, options = options)
    }

    // What to ask before switching to a mode, or null for one that needs no asking. Online and Hotspot change how the box
    // reaches the internet, which is worth a second thought
    fun confirmation(mode: BoxMode): ModeConfirmation? = when (mode) {
        BoxMode.LORA_BLE -> null

        BoxMode.ONLINE -> ModeConfirmation(
            title = "Switch to Online?",
            message = "The box will use its saved Wi-Fi to reach the internet. If it has no saved Wi-Fi, or cannot connect, " +
                "it goes back to LoRa after about 3 minutes.",
            confirmLabel = "Switch"
        )

        BoxMode.SOFTAP -> ModeConfirmation(
            title = "Switch to Hotspot?",
            message = "The box will start its own Wi-Fi network for your phone, and stop using Wi-Fi to reach the internet.",
            confirmLabel = "Switch"
        )
    }

    // Whether pressing a button does anything: the box's own mode and one already being switched to are left alone
    fun canChoose(option: ModeOption): Boolean =
        option.state == ModeOptionState.AVAILABLE || option.state == ModeOptionState.WANTED
}
