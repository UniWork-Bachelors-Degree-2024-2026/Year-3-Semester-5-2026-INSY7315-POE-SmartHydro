package com.example.smarthydro

import com.example.smarthydro.transport.ConnectionRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotSame
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks how open connections to boxes are kept, reused, replaced and closed
class ConnectionRegistryTest {

    // A stand-in for a connection that remembers whether it was closed
    private class FakeConnection(val address: String) {
        var closed = false
    }

    private val registry = ConnectionRegistry<FakeConnection> { it.closed = true }

    private fun open(key: String, address: String) = registry.open(key, address) { FakeConnection(address) }

    @Test
    fun nothingIsOpenAtFirst() {
        assertNull(registry.get("1"))
        assertTrue(registry.all.value.isEmpty())
    }

    @Test
    fun openingMakesTheConnectionAvailable() {
        val connection = open("1", "AA:AA")

        assertSame(connection, registry.get("1"))
        assertEquals("AA:AA", registry.all.value["1"]!!.address)
    }

    @Test
    fun openingTheSameBoxAgainReusesTheConnection() {
        var made = 0
        val first = registry.open("1", "AA:AA") { made++; FakeConnection("AA:AA") }
        val second = registry.open("1", "AA:AA") { made++; FakeConnection("AA:AA") }

        assertSame(first, second)
        assertEquals(1, made)
        assertEquals(false, first.closed)
    }

    @Test
    fun openingAnotherBoxForTheSameStructureReplacesAndClosesTheOldOne() {
        val old = open("1", "AA:AA")
        val replacement = open("1", "BB:BB")

        assertNotSame(old, replacement)
        assertTrue(old.closed)
        assertEquals(false, replacement.closed)
        assertSame(replacement, registry.get("1"))
    }

    @Test
    fun structuresKeepTheirOwnConnections() {
        val first = open("1", "AA:AA")
        val second = open("2", "BB:BB")

        assertSame(first, registry.get("1"))
        assertSame(second, registry.get("2"))
        assertEquals(setOf("1", "2"), registry.all.value.keys)
    }

    @Test
    fun closingEndsAndRemovesTheConnection() {
        val connection = open("1", "AA:AA")
        val other = open("2", "BB:BB")

        registry.close("1")

        assertTrue(connection.closed)
        assertNull(registry.get("1"))
        assertEquals(false, other.closed)
        assertSame(other, registry.get("2"))
    }

    @Test
    fun closingWhatIsNotOpenDoesNothing() {
        val connection = open("1", "AA:AA")

        registry.close("9")

        assertEquals(false, connection.closed)
        assertEquals(setOf("1"), registry.all.value.keys)
    }

    @Test
    fun aClosedConnectionIsReplacedByAFreshOneWhenOpenedAgain() {
        val first = open("1", "AA:AA")
        registry.close("1")

        val second = open("1", "AA:AA")

        assertNotSame(first, second)
        assertEquals(false, second.closed)
    }

    @Test
    fun closingEverythingClosesEveryConnection() {
        val first = open("1", "AA:AA")
        val second = open("2", "BB:BB")

        registry.closeAll()

        assertTrue(first.closed)
        assertTrue(second.closed)
        assertTrue(registry.all.value.isEmpty())
    }
}
