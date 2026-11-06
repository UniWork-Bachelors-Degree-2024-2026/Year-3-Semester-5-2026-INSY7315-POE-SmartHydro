package com.example.smarthydro

import com.example.smarthydro.data.HistoryLogic
import com.example.smarthydro.models.BoxTelemetryDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// Checks that the stand-in numbers a box sends for a failed sensor are never treated as real readings
class BoxReadingFaultsTest {

    // Mock data: a reading as the box's hotspot sends it, with every sensor working
    private fun reading(
        ph: Double? = 6.2,
        ec: Double? = 1.4,
        temperature: Double? = 22.5,
        humidity: Double? = 61.0,
        light: Double? = 850.0
    ) = BoxTelemetryDto(
        ph = ph, ec = ec, temperature = temperature, humidity = humidity, light = light,
        waterLevelRaw = 2100, waterLevelDetected = true,
        mlPh = 0, mlEc = 0, mlTemp = 0, mlHumidity = 0
    )

    @Test
    fun realReadingsAreKept() {
        val clean = HistoryLogic.withoutFaults(reading())

        assertEquals(6.2, clean.ph!!, 0.0)
        assertEquals(1.4, clean.ec!!, 0.0)
        assertEquals(22.5, clean.temperature!!, 0.0)
        assertEquals(61.0, clean.humidity!!, 0.0)
        assertEquals(850.0, clean.light!!, 0.0)
    }

    @Test
    fun minusOneFaultsBecomeNoReading() {
        val clean = HistoryLogic.withoutFaults(reading(ph = -1.0, ec = -1.0, humidity = -1.0, light = -1.0))

        assertNull(clean.ph)
        assertNull(clean.ec)
        assertNull(clean.humidity)
        assertNull(clean.light)
    }

    @Test
    fun temperatureProbeFaultsBecomeNoReading() {
        assertNull(HistoryLogic.withoutFaults(reading(temperature = -99.0)).temperature)
        assertNull(HistoryLogic.withoutFaults(reading(temperature = -127.0)).temperature)
        assertNull(HistoryLogic.withoutFaults(reading(temperature = 85.0)).temperature)
    }

    @Test
    fun aPhOfZeroIsAnUnpluggedProbe() {
        assertNull(HistoryLogic.withoutFaults(reading(ph = 0.0)).ph)
    }

    @Test
    fun valuesThatAreNotNumbersBecomeNoReading() {
        val clean = HistoryLogic.withoutFaults(reading(ph = Double.NaN, temperature = Double.POSITIVE_INFINITY))

        assertNull(clean.ph)
        assertNull(clean.temperature)
    }

    @Test
    fun theRestOfTheReadingIsUntouched() {
        val clean = HistoryLogic.withoutFaults(reading(ph = -1.0))

        assertEquals(2100, clean.waterLevelRaw)
        assertEquals(true, clean.waterLevelDetected)
    }
}