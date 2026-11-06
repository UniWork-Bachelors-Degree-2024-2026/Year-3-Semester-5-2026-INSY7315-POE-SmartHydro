package com.example.smarthydro.api

import com.example.smarthydro.models.*
import com.example.smarthydro.models.StructureThresholdsDto
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.ResponseBody
import retrofit2.Response
import retrofit2.http.*

interface SmartHydroApi {

    // 1. AUTHENTICATION ENDPOINTS (/api/Authentication)

    // Request an OTP sent to the user's email address
    @POST("Authentication/request-otp")
    suspend fun requestOtp(
        @Body dto: RequestOtpDto
    ): Response<RequestOtpResponseDto>

    // Resend a one-time code, tied to the challenge token from request-otp
    @POST("Authentication/resend-otp")
    suspend fun resendOtp(
        @Body dto: ResendOtpDto
    ): Response<SimpleMessageResponseDto>

    // Request a password reset code
    @POST("Authentication/forgot-password")
    suspend fun forgotPassword(
        @Body dto: ForgotPasswordDto
    ): Response<SimpleMessageResponseDto>

    // Reset password with verification code
    @POST("Authentication/reset-password")
    suspend fun resetPassword(
        @Body dto: ResetPasswordDto
    ): Response<SimpleMessageResponseDto>

    // Verify the 6-digit OTP code
    @POST("Authentication/verify-otp")
    suspend fun verifyOtp(
        @Body dto: VerifyOtpDto
    ): Response<AuthResponseDto>

    // Register as a farm Operator
    @POST("Authentication/register")
    suspend fun registerOperator(
        @Body dto: RegisterDto
    ): Response<RegisterResponseDto>

    // Confirm a sign-up with the emailed code
    @POST("Authentication/verify-registration")
    suspend fun verifyRegistration(
        @Body dto: VerifyRegistrationDto
    ): Response<VerifyRegistrationResponseDto>

    // Request a new verification code for a sign-up already in progress
    @POST("Authentication/resend-registration-code")
    suspend fun resendRegistrationCode(
        @Body dto: ResendRegistrationCodeDto
    ): Response<SimpleMessageResponseDto>

    // Claim a worker invitation with its 8-character access code, creating the Worker account
    @POST("Authentication/claim-invite-code")
    suspend fun claimInviteCode(
        @Body dto: ClaimInviteCodeDto
    ): Response<AuthResponseDto>

    // 2. SITES ENDPOINTS (/api/Sites)

    // List all non-deleted physical farm installation sites.
    @GET("Sites")
    suspend fun getSites(): Response<List<SiteDto>>

    // Retrieve a single installation site by its GUID identifier.
    @GET("Sites/{id}")
    suspend fun getSiteById(
        @Path("id") id: String
    ): Response<SiteDto>

    // Create a new installation site.
    @POST("Sites")
    suspend fun createSite(
        @Body dto: CreateSiteDto
    ): Response<SiteDto>

    // Update an existing site name and location.
    @PUT("Sites/{id}")
    suspend fun updateSite(
        @Path("id") id: String,
        @Body dto: UpdateSiteDto
    ): Response<SiteDto>

    // Delete a site, with optional force deletion flag.
    @DELETE("Sites/{id}")
    suspend fun deleteSite(
        @Path("id") id: String,
        @Query("force") force: Boolean = false
    ): Response<SimpleMessageResponseDto>


    // 3. STRUCTURES ENDPOINTS (/api/Structures)

    // List all cultivation structures with optional siteId filter.
    @GET("Structures")
    suspend fun getStructures(
        @Query("siteId") siteId: String? = null
    ): Response<List<StructureDto>>

    // Retrieve a single cultivation structure by its ID.
    @GET("Structures/{id}")
    suspend fun getStructureById(
        @Path("id") id: Int
    ): Response<StructureDto>

    // Single-call aggregated dashboard summary for a structure (telemetry, actuators, alerts, devices).
    @GET("Structures/{id}/dashboard-summary")
    suspend fun getStructureDashboardSummary(
        @Path("id") id: Int
    ): Response<StructureDashboardSummaryDto>

    // Create a new structure (VERTICAL_TOWER or FODDER_TRAY).
    @POST("Structures")
    suspend fun createStructure(
        @Body dto: CreateStructureDto
    ): Response<StructureDto>

    // Update structure metadata (name, labelColour, locationText, coordinates, siteId, cameraMac).
    @PUT("Structures/{id}")
    suspend fun updateStructure(
        @Path("id") id: Int,
        @Body dto: UpdateStructureDto
    ): Response<SimpleMessageResponseDto>

    // Soft-delete a cultivation structure.
    @DELETE("Structures/{id}")
    suspend fun deleteStructure(
        @Path("id") id: Int
    ): Response<Unit>

    // Restore a soft-deleted structure.
    @POST("Structures/{id}/restore")
    suspend fun restoreStructure(
        @Path("id") id: Int
    ): Response<Unit>

    // Permanently purge a deleted structure (Admin only).
    @DELETE("Structures/{id}/purge")
    suspend fun purgeStructure(
        @Path("id") id: Int
    ): Response<Unit>


    // 4. DEVICES ENDPOINTS (/api/Devices)

    // Get all registered IoT devices, optionally filtered by structureId.
    @GET("Devices")
    suspend fun getDevices(
        @Query("structureId") structureId: Int? = null
    ): Response<List<DeviceDto>>

    // Get device telemetry state and connection status by hardware MAC address.
    @GET("Devices/{mac}")
    suspend fun getDeviceByMac(
        @Path("mac") mac: String
    ): Response<DeviceDto>

    // Register a new hardware device node by MAC address.
    @POST("Devices")
    suspend fun registerDevice(
        @Body dto: CreateDeviceDto
    ): Response<DeviceDto>

    // Update device configuration and assignment.
    @PUT("Devices/{mac}")
    suspend fun updateDevice(
        @Path("mac") mac: String,
        @Body dto: UpdateDeviceDto
    ): Response<DeviceDto>

    // Delete a device from the system.
    @DELETE("Devices/{mac}")
    suspend fun deleteDevice(
        @Path("mac") mac: String
    ): Response<Unit>

    // Set device structure type profile ('VERTICAL-TOWER' or 'FODDER-SYSTEM').
    @PUT("Devices/{mac}/structure-type")
    suspend fun setDeviceStructureType(
        @Path("mac") mac: String,
        @Body dto: SetStructureTypeDto
    ): Response<SimpleMessageResponseDto>

    // Flag device to transition to offline SoftAP mode.
    @POST("Devices/{mac}/offline-mode")
    suspend fun requestOfflineMode(
        @Path("mac") mac: String
    ): Response<SimpleMessageResponseDto>

    // Restore device to standard online cloud mode.
    @POST("Devices/{mac}/online-mode")
    suspend fun requestOnlineMode(
        @Path("mac") mac: String
    ): Response<SimpleMessageResponseDto>

    // Rename a friendly display name for a device node.
    @PUT("Devices/{mac}/rename")
    suspend fun renameDevice(
        @Path("mac") mac: String,
        @Body dto: RenameDeviceDto
    ): Response<SimpleMessageResponseDto>

    // Associate or unassign a device to/from a structure.
    @PUT("Devices/{mac}/assign-structure")
    suspend fun assignStructure(
        @Path("mac") mac: String,
        @Body dto: AssignStructureDto
    ): Response<SimpleMessageResponseDto>


    // 5. ACTUATORS ENDPOINTS (/api/Actuators)

    // Get the current state of all relays/actuators for a specific device MAC.
    @GET("Actuators/{mac}")
    suspend fun getActuatorState(
        @Path("mac") mac: String
    ): Response<ActuatorStateResponseDto>

    // Apply continuous manual actuator commands (GrowLight, Fans, Pump, etc.).
    @POST("Actuators/continuous")
    suspend fun sendContinuousCommand(
        @Body dto: ActuatorCommandDto
    ): Response<ActuatorStateResponseDto>

    // Apply a one-shot timed actuator command (e.g. Fodder misting spray for 8 seconds).
    @POST("Actuators/timed")
    suspend fun sendTimedCommand(
        @Body dto: TimedActuatorCommandDto
    ): Response<ActuatorStateResponseDto>

    // Get paginated historical actuator state changes.
    @GET("Actuators/history/{mac}")
    suspend fun getActuatorHistory(
        @Path("mac") mac: String,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 20
    ): Response<PagedResultDto<ActuatorHistoryDto>>


    // 6. TELEMETRY ENDPOINTS (/api/Telemetry)

    // Ingest sensor readings and piggyback latest actuator directives.
    @POST("Telemetry")
    suspend fun ingestTelemetry(
        @Body dto: TelemetryIngestDto
    ): Response<ActuatorStateResponseDto>

    // Get most recent real-time sensor telemetry packet for a specific device MAC.
    @GET("Telemetry/latest/{mac}")
    suspend fun getLatestTelemetryByMac(
        @Path("mac") mac: String
    ): Response<TelemetryRealtimeDto>

    // Get latest telemetry readings for all sensor nodes associated with a structure.
    @GET("Telemetry/structure/{structureId}/latest")
    suspend fun getLatestTelemetryByStructure(
        @Path("structureId") structureId: Int
    ): Response<List<TelemetryRealtimeDto>>

    // Get aggregated daily history metrics over a rolling window of days.
    @GET("Telemetry/history/{mac}")
    suspend fun getTelemetryHistory(
        @Path("mac") mac: String,
        @Query("days") days: Int = 7
    ): Response<List<TelemetryDailyHistoryDto>>


    // 7. ALERTS ENDPOINTS (/api/Alerts)

    // Get paginated alerts filtered by structure and resolution status.
    @GET("Alerts")
    suspend fun getAlerts(
        @Query("structureId") structureId: Int? = null,
        @Query("unresolvedOnly") unresolvedOnly: Boolean = true,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 20
    ): Response<PagedResultDto<AlertResponseDto>>

    // Get count of unresolved alerts for notification badging.
    @GET("Alerts/unresolved-count")
    suspend fun getUnresolvedAlertsCount(
        @Query("structureId") structureId: Int? = null
    ): Response<UnresolvedCountResponseDto>

    // Fetch a single alert by ID.
    @GET("Alerts/{id}")
    suspend fun getAlertById(
        @Path("id") id: Long
    ): Response<AlertResponseDto>

    // Create a new telemetry threshold violation or hardware anomaly alert.
    @POST("Alerts")
    suspend fun createAlert(
        @Body dto: CreateAlertDto
    ): Response<AlertResponseDto>

    // Mark an alert as resolved.
    @PUT("Alerts/{id}/resolve")
    suspend fun resolveAlert(
        @Path("id") id: Long,
        @Body dto: ResolveAlertDto
    ): Response<Unit>

    // Soft-delete an alert by ID.
    @DELETE("Alerts/{id}")
    suspend fun deleteAlert(
        @Path("id") id: Long
    ): Response<Unit>

    // Restore a soft-deleted alert.
    @POST("Alerts/{id}/restore")
    suspend fun restoreAlert(
        @Path("id") id: Long
    ): Response<Unit>

    // Permanently purge an alert (Admin only).
    @DELETE("Alerts/{id}/purge")
    suspend fun purgeAlert(
        @Path("id") id: Long
    ): Response<Unit>


    // 8. DOSING ENDPOINTS (/api/Dosing)

    // Enqueue a one-shot volumetric dose for nutrients or pH balancing.
    @POST("Dosing/command")
    suspend fun enqueueDosingCommand(
        @Body dto: DosingCommandRequestDto
    ): Response<DosingCommandResponseDto>

    // Microcontroller callback reporting completion of a dosing command.
    @POST("Dosing/callback")
    suspend fun dosingCallback(
        @Body dto: DosingCallbackRequestDto
    ): Response<SimpleMessageResponseDto>

    // Get pending unexecuted dosing commands for an ESP32 MAC.
    @GET("Dosing/pending/{mac}")
    suspend fun getPendingDosingCommands(
        @Path("mac") mac: String
    ): Response<List<DosingCommandResponseDto>>

    // Get recent dosing history for a device.
    @GET("Dosing/history/{mac}")
    suspend fun getDosingHistory(
        @Path("mac") mac: String,
        @Query("limit") limit: Int = 50
    ): Response<List<DosingCommandResponseDto>>


    // 9. CAMERA ENDPOINTS (/api/Camera)

    // Poll whether a camera capture is pending for this MAC address.
    @GET("Camera/poll-command/{mac}")
    suspend fun pollCameraCommand(
        @Path("mac") mac: String
    ): Response<CameraPendingResponseDto>

    // Trigger an on-demand capture command for an ESP32-CAM.
    @POST("Camera/trigger-capture")
    suspend fun triggerCameraCapture(
        @Body dto: CameraTriggerRequestDto
    ): Response<SimpleMessageResponseDto>

    // Request an on-demand ephemeral camera stream, waking the node via SignalR.
    @POST("Camera/stream/request")
    suspend fun requestCameraStream(
        @Body dto: CameraStreamRequestDto
    ): Response<SimpleMessageResponseDto>

    // Stop an on-demand ephemeral camera stream.
    @POST("Camera/stream/stop")
    suspend fun stopCameraStream(
        @Body dto: CameraStreamRequestDto
    ): Response<SimpleMessageResponseDto>

    // Get active stream connection status/parameters for target camera node.
    @GET("Camera/stream/status/{mac}")
    suspend fun getCameraStreamStatus(
        @Path("mac") mac: String
    ): Response<CameraStreamStatusDto>

    // Upload captured photo multipart form-data.
    @Multipart
    @POST("Camera/upload")
    suspend fun uploadCameraPhoto(
        @Part file: MultipartBody.Part,
        @Part("deviceMac") deviceMac: RequestBody,
        @Part("structureId") structureId: RequestBody?,
        @Part("plant") plant: RequestBody?,
        @Part("label") label: RequestBody?,
        @Part("cameraLabel") cameraLabel: RequestBody?,
        @Part("capturedAt") capturedAt: RequestBody? = null,
        @Part("isFavourite") isFavourite: RequestBody? = null
    ): Response<CameraUploadResponseDto>

    // Edit a photo's details and when a file is sent, replace its image
    @Multipart
    @PUT("Camera/photos/{id}")
    suspend fun updatePhoto(
        @Path("id") id: Long,
        @Part file: MultipartBody.Part? = null,
        @Part("structureId") structureId: RequestBody? = null,
        @Part("plant") plant: RequestBody? = null,
        @Part("cameraLabel") cameraLabel: RequestBody? = null,
        @Part("capturedAt") capturedAt: RequestBody? = null,
        @Part("isFavourite") isFavourite: RequestBody? = null
    ): Response<Unit>

    // Download latest image binary stream for a device.
    @Streaming
    @GET("Camera/latest-image/{mac}")
    suspend fun getLatestCameraImage(
        @Path("mac") mac: String
    ): Response<ResponseBody>

    // Download photo image binary stream by capture ID.
    @Streaming
    @GET("Camera/photos/{id}")
    suspend fun getPhotoById(
        @Path("id") id: Long
    ): Response<ResponseBody>

    // Get photo metadata JSON by capture ID.
    @GET("Camera/photos/{id}/metadata")
    suspend fun getPhotoMetadata(
        @Path("id") id: Long
    ): Response<CameraPhotoMetadataDto>

    // Delete a photo capture by ID.
    @DELETE("Camera/photos/{id}")
    suspend fun deletePhoto(
        @Path("id") id: Long
    ): Response<Unit>

    // Restore a soft-deleted photo.
    @POST("Camera/photos/{id}/restore")
    suspend fun restorePhoto(
        @Path("id") id: Long
    ): Response<Unit>

    // Permanently purge a deleted photo (Admin only).
    @DELETE("Camera/photos/{id}/purge")
    suspend fun purgePhoto(
        @Path("id") id: Long
    ): Response<Unit>

    // Query paginated photo gallery.
    @GET("Camera/gallery")
    suspend fun getGallery(
        @Query("structureId") structureId: Int? = null,
        @Query("deviceMac") deviceMac: String? = null,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 12
    ): Response<PagedResultDto<CameraPhotoMetadataDto>>

    // Toggle the favourite flag on a photo capture.
    @PUT("Camera/{id}/favourite")
    suspend fun togglePhotoFavourite(
        @Path("id") id: Long
    ): Response<ToggleFavouriteResponseDto>


    // 10. THRESHOLDS ENDPOINTS (/api/Thresholds)

    // Get the ranges of every structure in one site, with the healthy default for any reading not customised yet
    @GET("Thresholds/site/{siteId}")
    suspend fun getThresholdsBySite(
        @Path("siteId") siteId: String
    ): Response<List<StructureThresholdsDto>>

    // Get the ranges of one structure, with the healthy default for any reading not customised yet
    @GET("Thresholds/structure/{structureId}")
    suspend fun getThresholdsByStructure(
        @Path("structureId") structureId: Int
    ): Response<StructureThresholdsDto>

    // Upsert a sensor threshold limit boundary.
    @PUT("Thresholds")
    suspend fun upsertThreshold(
        @Body dto: UpdateThresholdDto
    ): Response<ThresholdResponseDto>

    // Delete a threshold configuration by ID.
    @DELETE("Thresholds/{id}")
    suspend fun deleteThreshold(
        @Path("id") id: Int
    ): Response<Unit>


    // 11. TEAM & SITE INVITATIONS ENDPOINTS (/api/team, /api/sites/{siteId}/invitations)

    // List the workers and pending invitations across every site the signed-in Operator manages.
    @GET("team")
    suspend fun getTeam(): Response<List<TeamMemberDto>>

    // Take a worker off one of the Operator's sites; their account and any other site access stay intact.
    @DELETE("team/sites/{siteId}/members/{userId}")
    suspend fun removeTeamMember(
        @Path("siteId") siteId: String,
        @Path("userId") userId: Int
    ): Response<SimpleMessageResponseDto>

    // Cancel an unused invitation so its link and access code stop working.
    @DELETE("team/sites/{siteId}/invitations")
    suspend fun cancelSiteInvite(
        @Path("siteId") siteId: String,
        @Query("email") email: String
    ): Response<SimpleMessageResponseDto>

    // Invite a worker to a site the signed-in Operator manages.
    @POST("sites/{siteId}/invitations")
    suspend fun createSiteInvite(
        @Path("siteId") siteId: String,
        @Body dto: CreateSiteInviteDto
    ): Response<SiteInviteResponseDto>


    // 12. DATA-MULE OFFLINE SYNC ENDPOINTS (/api/Sync)

    // Returns newest stored timestamp for a device to prevent redundant mobile re-uploads.
    @GET("Sync/devices/{mac}/latest-timestamp")
    suspend fun getSyncLatestTimestamp(
        @Path("mac") mac: String
    ): Response<HighWaterMarkResponseDto>

    // Ingest batch of cached sensor readings uploaded by mobile data-mule.
    @POST("Sync/telemetry-batch")
    suspend fun syncTelemetryBatch(
        @Body dto: TelemetryBatchSyncRequestDto
    ): Response<TelemetryBatchSyncResponseDto>

    // Ingest batch of offline alerts raised by the hardware node.
    @POST("Sync/alerts-batch")
    suspend fun syncAlertsBatch(
        @Body dto: AlertsBatchSyncRequestDto
    ): Response<AlertsBatchSyncResponseDto>

    // Ingest batch of manual actuator overrides executed on-site while offline.
    @POST("Sync/overrides-batch")
    suspend fun syncOverridesBatch(
        @Body dto: OverridesBatchSyncRequestDto
    ): Response<OverridesBatchSyncResponseDto>


    // 13. ADMIN MANAGEMENT ENDPOINTS (/api/admin)

    // List all users in system for the Admin Dashboard
    @GET("admin/users")
    suspend fun getAdminUsers(): Response<List<AdminUserDto>>

    // List pending farm operators awaiting admin review
    @GET("admin/pending-operators")
    suspend fun getAdminPendingOperators(): Response<List<PendingOperatorDto>>

    // List all active sites with owner info for admin
    @GET("admin/sites")
    suspend fun getAdminSites(): Response<List<AdminSiteDto>>

    // Approve a pending operator
    @POST("admin/users/{id}/approve")
    suspend fun approveOperator(
        @Path("id") id: Int
    ): Response<OperatorReviewResponseDto>

    // Deny a pending operator
    @POST("admin/users/{id}/deny")
    suspend fun denyOperator(
        @Path("id") id: Int
    ): Response<OperatorReviewResponseDto>

    // Deactivate a user
    @POST("admin/users/{id}/deactivate")
    suspend fun deactivateUser(
        @Path("id") id: Int
    ): Response<AccountActionResponseDto>

    // Reactivate a user
    @POST("admin/users/{id}/reactivate")
    suspend fun reactivateUser(
        @Path("id") id: Int
    ): Response<AccountActionResponseDto>

    // Reopen a denied operator back to pending
    @POST("admin/users/{id}/reopen")
    suspend fun reopenUser(
        @Path("id") id: Int
    ): Response<AccountActionResponseDto>

    // Update user details (name, farm, role)
    @PUT("admin/users/{id}")
    suspend fun updateAdminUser(
        @Path("id") id: Int,
        @Body dto: UpdateUserDto
    ): Response<AccountActionResponseDto>

    // Delete a user
    @DELETE("admin/users/{id}")
    suspend fun deleteAdminUser(
        @Path("id") id: Int
    ): Response<AccountActionResponseDto>

    // Reassign site owner
    @PUT("admin/sites/{id}/owner")
    suspend fun assignSiteOwner(
        @Path("id") id: String,
        @Body dto: AssignSiteOwnerDto
    ): Response<AccountActionResponseDto>
}
