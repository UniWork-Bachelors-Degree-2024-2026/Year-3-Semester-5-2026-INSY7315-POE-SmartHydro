package com.example.smarthydro.transport

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

// Keeps one open connection per key, so a connection outlives the screen that opened it. The connection type is
// left open so the bookkeeping can be tested without Bluetooth, and closeConnection is what ends one
class ConnectionRegistry<T : Any>(private val closeConnection: (T) -> Unit) {

    // A connection and the address it was opened to
    data class Entry<T>(val address: String, val connection: T)

    private val entries = MutableStateFlow<Map<String, Entry<T>>>(emptyMap())

    // Every open connection by key, which changes as connections are opened and closed
    val all: StateFlow<Map<String, Entry<T>>> = entries

    // The connection open for this key, or null
    fun get(key: String): T? = entries.value[key]?.connection

    // The connection for this key to this address. One already open to the same address is reused, and one open to
    // another address is closed and replaced
    @Synchronized
    fun open(key: String, address: String, create: () -> T): T {
        val current = entries.value[key]
        if (current != null && current.address == address) return current.connection

        current?.let { closeConnection(it.connection) }
        val connection = create()
        entries.value = entries.value + (key to Entry(address, connection))
        return connection
    }

    // Closes the connection for this key, if there is one
    @Synchronized
    fun close(key: String) {
        val current = entries.value[key] ?: return
        closeConnection(current.connection)
        entries.value = entries.value - key
    }

    // Closes every connection
    @Synchronized
    fun closeAll() {
        entries.value.values.forEach { closeConnection(it.connection) }
        entries.value = emptyMap()
    }
}
