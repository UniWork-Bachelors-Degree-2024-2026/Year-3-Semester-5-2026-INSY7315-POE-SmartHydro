package com.example.smarthydro.transport

import java.util.concurrent.atomic.AtomicInteger
import kotlin.random.Random

// Hands out the ids that go on box commands so each answer can be matched to the command it belongs to.
// The box accepts ids from 1 to 65535 and answers a repeated id with its earlier result instead of running
// it again, so a fresh app run starts somewhere random instead of at 1 again
class CommandIdSource(first: Int = Random.nextInt(1, MAX_ID + 1)) {

    companion object {
        const val MAX_ID = 65535
    }

    private val upcoming = AtomicInteger(first.coerceIn(1, MAX_ID))

    // The next id, going back to 1 after 65535 and never 0
    fun next(): Int = upcoming.getAndUpdate { if (it >= MAX_ID) 1 else it + 1 }
}
