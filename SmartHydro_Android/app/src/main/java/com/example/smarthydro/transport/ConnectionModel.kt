package com.example.smarthydro.transport

// The three characteristics of a box and the ids they have in the firmware's Config.h
enum class BoxCharacteristic(val uuid: String) {
    // Readings, which any phone can read
    TELEMETRY("0000ff01-0000-1000-8000-00805f9b34fb"),

    // Commands, which only a paired phone can write
    COMMAND("0000ff02-0000-1000-8000-00805f9b34fb"),

    // The box's answers to commands, which only a paired phone can read
    ACK("0000ff03-0000-1000-8000-00805f9b34fb");

    companion object {

        // The characteristic with this id, or null for one that is not the box's
        fun fromUuid(uuid: String?): BoxCharacteristic? = entries.firstOrNull { it.uuid.equals(uuid, ignoreCase = true) }
    }
}

// Numbers the connection settles on
object BoxGatt {

    // The size the phone asks for. The box allows up to 256
    const val REQUESTED_MTU = 256

    // The smallest size that still carries the box's answers (the firmware asks for 185 in TowerBLE.h)
    const val MIN_MTU = 185

    // The size a box's biggest reading needs: 206 bytes of JSON and the 3 bytes the protocol adds
    const val FULL_READING_MTU = 209
}

// Status numbers Android reports when a connection changes, as the Bluetooth stack defines them
// (Android Open Source Project, 2026). 0, 5 and 15 are also public constants of BluetoothGatt
object GattStatus {
    const val SUCCESS = 0
    const val INSUFFICIENT_AUTHENTICATION = 5
    const val INSUFFICIENT_ENCRYPTION = 15
    const val CONNECTION_TIMEOUT = 8
    const val PEER_TERMINATED = 19
    const val LMP_TIMEOUT = 34
}

// Why a connection to a box ended or never got going, with the words the person is told
enum class DisconnectReason(val message: String) {
    USER("Disconnected from the box."),
    PERMISSION_MISSING(ScanProblem.PERMISSION_MISSING.message),
    NO_BLUETOOTH(ScanProblem.NO_BLUETOOTH.message),
    BLUETOOTH_OFF(ScanProblem.BLUETOOTH_OFF.message),
    CONNECT_FAILED("Couldn't connect to the box. Move closer and try again."),
    PAIRING_FAILED("Pairing didn't work. Check the 6-digit code on the box's screen and try again."),
    STALE_PAIRING("The box doesn't recognise this phone any more. Remove the box in your phone's Bluetooth settings, then connect again."),
    NOT_A_BOX("That device isn't a SmartHydro box."),
    MTU_TOO_SMALL("This phone can't receive the box's readings over Bluetooth."),
    LOST_SIGNAL("The box went out of range."),
    BOX_CLOSED("The box ended the connection."),
    LOST("The Bluetooth connection was lost.");

    companion object {

        // The reason for a connection that could not even start, going by what stopped it
        fun fromProblem(problem: ScanProblem): DisconnectReason = when (problem) {
            ScanProblem.PERMISSION_MISSING -> PERMISSION_MISSING
            ScanProblem.NO_BLUETOOTH -> NO_BLUETOOTH
            ScanProblem.BLUETOOTH_OFF -> BLUETOOTH_OFF
            ScanProblem.BUSY, ScanProblem.FAILED -> CONNECT_FAILED
        }
    }
}

// Where a connection to one box has got to
sealed interface ConnectionState {

    // Nothing started yet
    data object Idle : ConnectionState

    // Waiting for the box to answer
    data object Connecting : ConnectionState

    // Agreeing how big a message can be
    data object NegotiatingMtu : ConnectionState

    // Looking up the box's services. The agreed message size is kept for the end
    data class DiscoveringServices(val mtu: Int) : ConnectionState

    // Turning on notifications, one characteristic at a time. bonded says whether the phone was already paired
    data class Subscribing(val next: BoxCharacteristic, val mtu: Int, val bonded: Boolean) : ConnectionState

    // Waiting for the person to enter the box's code
    data class Pairing(val mtu: Int) : ConnectionState

    // Readings and commands both work. limitedMtu is true when a big reading may be cut short
    data class Ready(val mtu: Int) : ConnectionState {
        val limitedMtu: Boolean get() = mtu < BoxGatt.FULL_READING_MTU
    }

    // Over, with the reason
    data class Disconnected(val reason: DisconnectReason) : ConnectionState
}

// A short line saying how the connection to a box is doing, for the screen that shows a saved box
fun ConnectionState.statusText(): String = when (this) {
    is ConnectionState.Ready -> "Connected over Bluetooth"
    is ConnectionState.Pairing -> "Waiting for the code on the box's screen…"
    is ConnectionState.Connecting,
    is ConnectionState.NegotiatingMtu,
    is ConnectionState.DiscoveringServices,
    is ConnectionState.Subscribing -> "Connecting…"
    is ConnectionState.Disconnected -> reason.message
    is ConnectionState.Idle -> "Not connected"
}

// What Android tells the connection
sealed interface ConnectionEvent {

    // The person chose a box
    data object ConnectRequested : ConnectionEvent

    // The person closed the connection
    data object DisconnectRequested : ConnectionEvent

    // The box answered, or the attempt failed with this status
    data class GattConnected(val status: Int) : ConnectionEvent

    // The connection dropped with this status
    data class GattDisconnected(val status: Int) : ConnectionEvent

    // The agreed message size, or a failure
    data class MtuChanged(val mtu: Int, val status: Int) : ConnectionEvent

    // The services were looked up. hasBoxService says the box's service and all three characteristics are there
    data class ServicesDiscovered(val status: Int, val hasBoxService: Boolean, val isBonded: Boolean) : ConnectionEvent

    // A notification was turned on, or failed with this status
    data class NotificationsEnabled(val characteristic: BoxCharacteristic, val status: Int) : ConnectionEvent

    // The pairing state changed, as one of BluetoothDevice's BOND_NONE, BOND_BONDING and BOND_BONDED
    data class BondChanged(val bondState: Int) : ConnectionEvent
}

// What the phone has to do next
sealed interface ConnectionAction {
    data class RequestMtu(val mtu: Int) : ConnectionAction
    data object DiscoverServices : ConnectionAction
    data class EnableNotifications(val characteristic: BoxCharacteristic) : ConnectionAction
    data object CreateBond : ConnectionAction

    // Close the connection
    data object Disconnect : ConnectionAction
}

// The next state, and what to do to get there
data class Transition(val state: ConnectionState, val action: ConnectionAction? = null)

//Reference Lists:

//Android Open Source Project, 2026. gatt_api.h. [online] Available at: <https://android.googlesource.com/platform/packages/modules/Bluetooth/+/refs/heads/main/system/stack/include/gatt_api.h> [Accessed 29 October 2026].
