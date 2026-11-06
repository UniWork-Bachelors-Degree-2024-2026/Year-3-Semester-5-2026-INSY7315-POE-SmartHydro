package com.example.smarthydro.api

import com.example.smarthydro.models.BoxControlRequestDto
import com.example.smarthydro.models.BoxControlResponseDto
import com.example.smarthydro.models.BoxDoseRequestDto
import com.example.smarthydro.models.BoxDoseResponseDto
import com.example.smarthydro.models.BoxSyncBundleDto
import com.example.smarthydro.models.BoxTelemetryDto
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST

// The box's own local endpoints, served from its SoftAP when a structure is paired directly
// rather than through the cloud. No auth header, matching the firmware (see Provisioning.h).
interface BoxApi {

    @GET("api/telemetry/live")
    suspend fun getLiveTelemetry(): Response<BoxTelemetryDto>

    @GET("api/sync/bundle")
    suspend fun getSyncBundle(): Response<BoxSyncBundleDto>

    // Switches one relay on or off. Needs a box whose software has the local control route
    @POST("api/control")
    suspend fun setRelay(@Body dto: BoxControlRequestDto): Response<BoxControlResponseDto>

    // Runs one dose pump for a set volume. Needs a box whose software has the local dose route
    @POST("api/dose")
    suspend fun dose(@Body dto: BoxDoseRequestDto): Response<BoxDoseResponseDto>
}
