package com.example.smarthydro.transport

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import java.util.UUID

// One Bluetooth connection to one box. This only passes what Android reports to ConnectionMachine and does what
// the machine says to do next, so the decisions are the ones ConnectionMachineTest checks. The permissions have to
// be granted first, see BluetoothPermissions
@SuppressLint("MissingPermission")
class BleBox(private val context: Context, private val address: String) {

    companion object {
        // The descriptor that switches notifications on for a characteristic, Client Characteristic Configuration, 0x2902
    // in the 16-bit form (Bluetooth SIG, 2026)
        private val NOTIFICATION_SWITCH = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")

        // How long a write to the box may take before it counts as failed
        private const val WRITE_TIMEOUT_MS = 3_000L
    }

    private val lock = Any()

    @Volatile
    private var gatt: BluetoothGatt? = null

    // Android runs one operation at a time on a connection, so writes take turns and each waits for its confirmation
    private val writeTurn = Mutex()

    @Volatile
    private var pendingWrite: CompletableDeferred<Int>? = null
    private var bondReceiver: BroadcastReceiver? = null

    private val connectionState = MutableStateFlow<ConnectionState>(ConnectionState.Idle)

    // Where the connection has got to
    val state: StateFlow<ConnectionState> = connectionState

    // Every reading the box sends once connected. A slow collector loses the oldest, never blocks the connection
    private val readingFlow = MutableSharedFlow<BleTelemetry>(extraBufferCapacity = 16, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    val readings: SharedFlow<BleTelemetry> = readingFlow

    // Every answer the box gives to a command
    private val answerFlow = MutableSharedFlow<BleCommandAck>(extraBufferCapacity = 16, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    val answers: SharedFlow<BleCommandAck> = answerFlow

    private val bluetoothManager: BluetoothManager? get() = context.getSystemService(BluetoothManager::class.java)

    // Starts connecting, from not connected or after a connection ended
    fun connect() {
        synchronized(lock) {
            val before = connectionState.value
            if (before !is ConnectionState.Idle && before !is ConnectionState.Disconnected) {
                BoxLog.d("conn", "connect to $address ignored, already $before")
                return
            }
            BoxLog.i("conn", "connect to $address (was $before)")

            val adapter = bluetoothManager?.adapter
            val permissionsMissing = BluetoothPermissions.missing(Build.VERSION.SDK_INT) {
                context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED
            }.isNotEmpty()
            val problem = ScanProblem.beforeScanning(permissionsMissing, adapter != null, adapter?.isEnabled == true)
            if (problem != null || adapter == null) {
                val reason = problem?.let { DisconnectReason.fromProblem(it) } ?: DisconnectReason.NO_BLUETOOTH
                BoxLog.w("conn", "cannot connect to $address: $reason")
                connectionState.value = ConnectionState.Disconnected(reason)
                return
            }

            dispatch(ConnectionEvent.ConnectRequested)
            listenForPairing()

            // Not set to reconnect on its own, because the person chose this box (Android Developers, 2026)
            gatt = try {
                adapter.getRemoteDevice(address).connectGatt(context, false, callback)
            } catch (e: IllegalArgumentException) {
                BoxLog.e("conn", "$address is not a Bluetooth address: ${e.message}")
                null
            }
            if (gatt == null) {
                BoxLog.w("conn", "Android did not start a connection to $address")
                dispatch(ConnectionEvent.GattConnected(BluetoothGatt.GATT_FAILURE))
            }
        }
    }

    // Closes the connection. The connection is closed once it is finished with, so it does not drain the battery
    // (Android Developers, 2026)
    fun disconnect() {
        dispatch(ConnectionEvent.DisconnectRequested)
    }

    // Writes one command to the box and says whether the box took it. This is only the delivery: the box's answer
    // to what the command did comes back on answers
    @Suppress("DEPRECATION")
    suspend fun writeCommand(json: String): Boolean = writeTurn.withLock {
        val connection = gatt
        val characteristic = connection?.let { findCharacteristic(it, BoxCharacteristic.COMMAND) }
        if (connection == null || characteristic == null || connectionState.value !is ConnectionState.Ready) {
            BoxLog.w("cmd", "write refused, not ready: connected=${connection != null} characteristic=${characteristic != null} state=${connectionState.value}")
            return false
        }

        val confirmed = CompletableDeferred<Int>()
        pendingWrite = confirmed

        characteristic.writeType = BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
        characteristic.value = json.toByteArray(Charsets.UTF_8)
        val started = connection.writeCharacteristic(characteristic)

        val status = if (started) withTimeoutOrNull(WRITE_TIMEOUT_MS) { confirmed.await() } else null
        pendingWrite = null

        // A status other than 0 is Android's reason, and 5 or 15 means the box wants the phone paired first
        BoxLog.d("cmd", "write to box ${if (started) "started" else "NOT started"}, status=$status: ${BoxLog.clip(json)}")
        status == BluetoothGatt.GATT_SUCCESS
    }

    // Feeds the machine and does what it says. Android calls back on its own threads, so this takes turns
    private fun dispatch(event: ConnectionEvent) {
        synchronized(lock) {
            val before = connectionState.value
            val transition = ConnectionMachine.reduce(before, event)
            connectionState.value = transition.state

            // Every step of the connection, with the status Android gave. 133 is Android's general failure, 8 a timeout
            BoxLog.d("conn", "$event: $before -> ${transition.state}${transition.action?.let { ", then $it" }.orEmpty()}")
            transition.action?.let { perform(it) }
        }
    }

    private fun perform(action: ConnectionAction) {
        val connection = gatt
        when (action) {
            is ConnectionAction.RequestMtu ->
                if (connection?.requestMtu(action.mtu) != true) {
                    BoxLog.w("conn", "Android refused to start the message size request")
                    dispatch(ConnectionEvent.MtuChanged(0, BluetoothGatt.GATT_FAILURE))
                }

            is ConnectionAction.DiscoverServices ->
                if (connection?.discoverServices() != true) {
                    BoxLog.w("conn", "Android refused to start the service lookup")
                    dispatch(ConnectionEvent.ServicesDiscovered(BluetoothGatt.GATT_FAILURE, hasBoxService = false, isBonded = false))
                }

            is ConnectionAction.EnableNotifications ->
                if (!enableNotifications(connection, action.characteristic)) {
                    BoxLog.w("conn", "could not switch on notifications for ${action.characteristic}")
                    dispatch(ConnectionEvent.NotificationsEnabled(action.characteristic, BluetoothGatt.GATT_FAILURE))
                }

            is ConnectionAction.CreateBond ->
                if (connection?.device?.createBond() != true) {
                    BoxLog.w("conn", "Android refused to start pairing")
                    dispatch(ConnectionEvent.BondChanged(BluetoothDevice.BOND_NONE))
                }

            is ConnectionAction.Disconnect -> close()
        }
    }

    // Switches notifications on in two steps: the phone starts listening, then the box is told to send them
    @Suppress("DEPRECATION")
    private fun enableNotifications(connection: BluetoothGatt?, which: BoxCharacteristic): Boolean {
        val characteristic = connection?.let { findCharacteristic(it, which) } ?: return false
        val descriptor = characteristic.getDescriptor(NOTIFICATION_SWITCH) ?: return false

        if (!connection.setCharacteristicNotification(characteristic, true)) return false
        descriptor.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
        return connection.writeDescriptor(descriptor)
    }

    private fun findCharacteristic(connection: BluetoothGatt, which: BoxCharacteristic): BluetoothGattCharacteristic? =
        connection.getService(UUID.fromString(BoxAdvertisement.SERVICE_UUID))?.getCharacteristic(UUID.fromString(which.uuid))

    private fun hasWholeBoxService(connection: BluetoothGatt): Boolean =
        BoxCharacteristic.entries.all { findCharacteristic(connection, it) != null }

    private fun close() {
        bondReceiver?.let {
            try {
                context.unregisterReceiver(it)
            } catch (e: IllegalArgumentException) {
                // Already unregistered
            }
        }
        bondReceiver = null

        gatt?.let {
            try {
                it.disconnect()
            } catch (e: Exception) {
                // Bluetooth was switched off, so there is nothing to disconnect from
            }
            it.close()
        }
        gatt = null
    }

    // Pairing is not reported through the connection, so it is listened for separately and only for this box
    @Suppress("DEPRECATION")
    private fun listenForPairing() {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                val device = intent.getParcelableExtra<BluetoothDevice>(BluetoothDevice.EXTRA_DEVICE)
                if (device?.address != address) return

                val bondState = intent.getIntExtra(BluetoothDevice.EXTRA_BOND_STATE, BluetoothDevice.BOND_NONE)
                dispatch(ConnectionEvent.BondChanged(bondState))
            }
        }
        bondReceiver = receiver
        ContextCompat.registerReceiver(
            context, receiver, IntentFilter(BluetoothDevice.ACTION_BOND_STATE_CHANGED), ContextCompat.RECEIVER_NOT_EXPORTED
        )
    }

    // Whether the first reading of this connection has been written to the log, since one every few seconds is too many
    @Volatile
    private var firstReadingLogged = false

    // Reads what the box sends, by which characteristic it came on
    private fun handleNotification(characteristicUuid: String?, value: ByteArray) {
        val text = String(value, Charsets.UTF_8)
        when (BoxCharacteristic.fromUuid(characteristicUuid)) {
            BoxCharacteristic.TELEMETRY -> {
                val reading = BleTelemetryMapper.parse(text)
                when {
                    // A frame that does not parse is usually one cut short because the message size was not raised
                    reading == null -> BoxLog.w("notify", "reading not understood (${value.size} bytes): ${BoxLog.clip(text)}")
                    !firstReadingLogged -> {
                        firstReadingLogged = true
                        BoxLog.i("notify", "first reading (${value.size} bytes): ${BoxLog.clip(text)}")
                    }
                }
                reading?.let { readingFlow.tryEmit(it) }
            }

            BoxCharacteristic.ACK -> {
                val ack = BleCommandAckParser.parse(text)
                BoxLog.d("notify", "answer (${value.size} bytes): ${BoxLog.clip(text)}${if (ack == null) "  <- not understood" else ""}")
                ack?.let { answerFlow.tryEmit(it) }
            }

            else -> BoxLog.d("notify", "data on an unknown characteristic $characteristicUuid")
        }
    }

    private val callback = object : BluetoothGattCallback() {

        override fun onConnectionStateChange(connection: BluetoothGatt, status: Int, newState: Int) {
            BoxLog.d("conn", "Android: connection changed, status=$status newState=$newState (2 connected, 0 disconnected)")
            when (newState) {
                BluetoothProfile.STATE_CONNECTED -> dispatch(ConnectionEvent.GattConnected(status))
                BluetoothProfile.STATE_DISCONNECTED -> dispatch(ConnectionEvent.GattDisconnected(status))
            }
        }

        override fun onMtuChanged(connection: BluetoothGatt, mtu: Int, status: Int) {
            dispatch(ConnectionEvent.MtuChanged(mtu, status))
        }

        override fun onServicesDiscovered(connection: BluetoothGatt, status: Int) {
            val bonded = connection.device.bondState == BluetoothDevice.BOND_BONDED
            dispatch(ConnectionEvent.ServicesDiscovered(status, hasWholeBoxService(connection), bonded))
        }

        override fun onDescriptorWrite(connection: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            val which = BoxCharacteristic.fromUuid(descriptor.characteristic?.uuid?.toString()) ?: return
            dispatch(ConnectionEvent.NotificationsEnabled(which, status))
        }

        override fun onCharacteristicWrite(connection: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            BoxLog.d("cmd", "Android: write confirmed, status=$status")
            pendingWrite?.complete(status)
        }

        // Android 13 and newer call this one with the value, and older versions call the one below
        override fun onCharacteristicChanged(connection: BluetoothGatt, characteristic: BluetoothGattCharacteristic, value: ByteArray) {
            handleNotification(characteristic.uuid.toString(), value)
        }

        @Deprecated("Used by Android 12 and older")
        @Suppress("DEPRECATION")
        override fun onCharacteristicChanged(connection: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            characteristic.value?.let { handleNotification(characteristic.uuid.toString(), it) }
        }
    }
}

//Reference Lists:

//Android Developers, 2026. Connect to a GATT server. [online] Available at: <https://developer.android.com/develop/connectivity/bluetooth/ble/connect-gatt-server> [Accessed 29 October 2026].

//Bluetooth SIG, 2026. Assigned Numbers: descriptors. [online] Available at: <https://bitbucket.org/bluetooth-SIG/public/raw/main/assigned_numbers/uuids/descriptors.yaml> [Accessed 29 October 2026].
