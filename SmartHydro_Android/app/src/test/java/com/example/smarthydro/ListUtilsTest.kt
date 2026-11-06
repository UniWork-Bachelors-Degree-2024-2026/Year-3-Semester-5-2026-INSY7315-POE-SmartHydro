package com.example.smarthydro

import com.example.smarthydro.data.replaceWithUnique
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ListUtilsTest {

    private data class Item(val id: String, val name: String)

    @Test
    fun replacesTheContents() {
        val list = mutableListOf(Item("1", "old"))
        val fresh = listOf(Item("2", "a"), Item("3", "b"))

        list.replaceWithUnique(fresh) { it.id }

        assertEquals(listOf("2", "3"), list.map { it.id })
    }

    @Test
    fun dropsARepeatedKeyAndKeepsTheFirst() {
        val list = mutableListOf<Item>()
        val withRepeat = listOf(Item("12", "first"), Item("5", "x"), Item("12", "second"))

        list.replaceWithUnique(withRepeat) { it.id }

        assertEquals(listOf("12", "5"), list.map { it.id })
        assertEquals("first", list.first().name)
    }

    @Test
    fun anEmptyReplacementEmptiesTheList() {
        val list = mutableListOf(Item("1", "a"))

        list.replaceWithUnique(emptyList()) { it.id }

        assertTrue(list.isEmpty())
    }

    @Test
    fun refillingTwiceWithTheSameItemsNeverLeavesARepeat() {
        val list = mutableListOf<Item>()
        val cached = listOf(Item("12", "Big Structure"), Item("13", "Other"))

        list.replaceWithUnique(cached) { it.id }
        list.replaceWithUnique(cached) { it.id }

        assertEquals(listOf("12", "13"), list.map { it.id })
    }

    @Test
    fun theKeyCanBeMadeFromSeveralFields() {
        val list = mutableListOf<Item>()
        val items = listOf(Item("a", "x"), Item("b", "x"), Item("c", "y"))

        list.replaceWithUnique(items) { "${it.name}" }

        assertEquals(listOf("a", "c"), list.map { it.id })
    }
}
