package com.example.smarthydro.data

// One reading a threshold can be set for
data class ThresholdSensor(
    val key: String,
    val label: String,
    val unit: String,
    val scale: Double,
    val lowest: Double,
    val highest: Double,
    val sensorId: String
)

// Lists the readings each structure type has a range for
object ThresholdCatalog {
    val Ph = ThresholdSensor("PH", "pH", "pH", 1.0, 0.0, 14.0, "clean_water")
    val Ec = ThresholdSensor("EC", "Nutrient (EC)", "µS/cm", 1000.0, 0.0, 20000.0, "nutrients")
    val Temperature = ThresholdSensor("TEMPERATURE", "Temperature", "°C", 1.0, -10.0, 60.0, "temperature")
    val Humidity = ThresholdSensor("HUMIDITY", "Humidity", "%", 1.0, 0.0, 100.0, "humidity")

    private val hydroponic = listOf(Ph, Ec, Temperature, Humidity)
    private val fodder = listOf(Ec, Temperature, Humidity)

    // The readings a structure type has ranges for
    fun forApiType(apiType: String?): List<ThresholdSensor> = when (apiType?.trim()?.uppercase()) {
        "VERTICAL_TOWER" -> hydroponic
        "FODDER_TRAY" -> fodder
        else -> emptyList()
    }

    // How a structure type is named on the page
    fun titleFor(apiType: String?): String = when (apiType?.trim()?.uppercase()) {
        "FODDER_TRAY" -> "Fodder structure"
        else -> "Hydroponic structure"
    }

    // The reading a sensor detail page shows
    fun forSensorId(sensorId: String): ThresholdSensor? = hydroponic.find { it.sensorId == sensorId }
}