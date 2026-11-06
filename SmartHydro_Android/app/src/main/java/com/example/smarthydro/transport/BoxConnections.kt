package com.example.smarthydro.transport

import android.content.Context

// The Bluetooth connections to boxes while the app runs, one per structure. A screen comes and goes but a connection
// stays, so moving between screens does not close it and open it again
object BoxConnections {

    val registry = ConnectionRegistry<BleBox> { it.disconnect() }

    // Opens a connection to a box and starts connecting, or picks up the one already open to the same box and
    // connects it again if it had ended. The key is the structure's id
    fun open(context: Context, structureKey: String, address: String): BleBox {
        val box = registry.open(structureKey, address) {
            BoxLog.i("conn", "new connection for structure $structureKey to $address")
            BleBox(context.applicationContext, address)
        }
        box.connect()
        return box
    }

    // Closes the connection for a structure
    fun close(structureKey: String) {
        BoxLog.i("conn", "closing the connection for structure $structureKey")
        registry.close(structureKey)
    }
}
