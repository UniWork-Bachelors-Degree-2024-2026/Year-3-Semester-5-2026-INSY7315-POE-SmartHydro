package com.example.smarthydro.data.local

// One calendar day of readings averaged together, in the phone's own time zone. An average is
// null when no reading that day included that sensor.
data class DailyAverageRow(
    val day: String,
    val avgPh: Double?,
    val avgEc: Double?,
    val avgLight: Double?,
    val avgTemperature: Double?,
    val avgHumidity: Double?,
    val samples: Int
)
