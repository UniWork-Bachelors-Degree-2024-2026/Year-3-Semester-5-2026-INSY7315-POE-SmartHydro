package com.example.smarthydro.api

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.Response
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.time.Instant
import java.time.format.DateTimeParseException
import java.util.concurrent.TimeUnit

// Singleton client and provider for the SmartHydroAPI networking layer.
object ApiClient {

    // The deployed SmartHydroAPI. Retrofit needs the trailing slash.
    const val BASE_URL = "https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net/api/"

    // Name of the SharedPreferences file used to persist client settings locally on device
    private const val PREFS_NAME = "smarthydro_api_prefs"
    // Key used to store and retrieve the JWT authentication token in SharedPreferences
    private const val KEY_AUTH_TOKEN = "auth_jwt_token"
    // Key used to store and retrieve the token's expiry, so a stale token isn't reused
    private const val KEY_AUTH_TOKEN_EXPIRES_AT = "auth_jwt_token_expires_at"
    // Key used to store and retrieve who is signed in, so the app can show "Signed in as ..."
    private const val KEY_AUTH_EMAIL = "auth_email"
    // Key used to store and retrieve the signed-in user's role
    private const val KEY_AUTH_ROLE = "auth_role"
    // Key for the custom full name
    private const val KEY_AUTH_FULLNAME = "auth_custom_fullname"
    // Prefix for the custom full name saved per account
    private const val KEY_FULLNAME_PREFIX = "custom_fullname_"
    // Stands in for the account when nobody is signed in, so offline mode keeps its own name
    private const val OFFLINE_PROFILE = "offline"
    // Key used to track the last logged-in user email for user-scoped cache invalidation
    private const val KEY_LAST_LOGGED_IN_EMAIL = "last_logged_in_email"
    // Key used to persist user dark mode preference
    private const val KEY_THEME_DARK_MODE = "theme_is_dark_mode"
    private const val KEY_THEME_HIGH_CONTRAST = "theme_is_high_contrast"
    // Key used to remember if the user chose offline mode so they skip login on future launches
    private const val KEY_PREFER_OFFLINE = "prefer_offline_mode"
    // Key used to store custom site name chosen in offline mode
    private const val KEY_OFFLINE_SITE_NAME = "offline_site_name"
    // Key used to remember whether offline mode should use simpler single-screen view
    private const val KEY_OFFLINE_SIMPLE_MODE = "offline_simple_mode"
    // Key used to remember the last structure selected in Simple Mode
    private const val KEY_SIMPLE_MODE_STRUCTURE_ID = "simple_mode_structure_id"
    // Key used to cache user's sites locally so the top bar and site lists load instantly without waiting for network
    private const val KEY_CACHED_SITES = "cached_sites_json"

    // Reference to Android SharedPreferences for reading and writing persisted preferences
    private var sharedPreferences: SharedPreferences? = null

    // In-memory cache of the JWT bearer token
    @Volatile
    private var authToken: String? = null

    // In-memory cache of when the token expires (ISO-8601, as the API sends it)
    @Volatile
    private var authTokenExpiresAt: String? = null

    // In-memory cache of the signed-in user's email and role, for display and role checks
    @Volatile
    private var authEmail: String? = null
    @Volatile
    private var authRole: String? = null
    @Volatile
    private var authFullName: String? = null

    // In-memory cache of the user's saved dark mode choice (null means follow system default)
    @Volatile
    private var savedDarkMode: Boolean? = null

    // In-memory cache of the user's saved high contrast choice (null means follow the system setting)
    @Volatile
    private var savedHighContrast: Boolean? = null

    // In-memory cache of whether user selected offline mode
    @Volatile
    private var preferOfflineMode: Boolean = false

    // In-memory cache of the last structure selected in Simple Mode
    @Volatile
    private var simpleModeStructureId: String? = null

    // Cached singleton instance of the Retrofit SmartHydroApi service
    @Volatile
    private var apiServiceInstance: SmartHydroApi? = null

    // Initialize ApiClient with an application context to load persisted JWT bearer token.
    fun init(context: Context) {
        val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        sharedPreferences = prefs
        authToken = prefs.getString(KEY_AUTH_TOKEN, null)
        authTokenExpiresAt = prefs.getString(KEY_AUTH_TOKEN_EXPIRES_AT, null)
        authEmail = prefs.getString(KEY_AUTH_EMAIL, null)
        authRole = prefs.getString(KEY_AUTH_ROLE, null)
        // The old shared name can't be matched to an account, so it is dropped rather than shown to the wrong person
        prefs.edit().remove(KEY_AUTH_FULLNAME).apply()
        authFullName = prefs.getString(fullNameKey(authEmail), null)
        savedDarkMode = if (prefs.contains(KEY_THEME_DARK_MODE)) {
            prefs.getBoolean(KEY_THEME_DARK_MODE, false)
        } else {
            null
        }
        savedHighContrast = if (prefs.contains(KEY_THEME_HIGH_CONTRAST)) {
            prefs.getBoolean(KEY_THEME_HIGH_CONTRAST, false)
        } else {
            null
        }
        preferOfflineMode = prefs.getBoolean(KEY_PREFER_OFFLINE, false)
        offlineSimpleMode = prefs.getBoolean(KEY_OFFLINE_SIMPLE_MODE, false)
        simpleModeStructureId = prefs.getString(KEY_SIMPLE_MODE_STRUCTURE_ID, null)
        offlineSiteName = prefs.getString(KEY_OFFLINE_SITE_NAME, "Emeris NMB") ?: "Emeris NMB"
    }

    @Volatile
    private var offlineSimpleMode: Boolean = false

    @Volatile
    private var offlineSiteName: String = "Emeris NMB"

    // Saves custom site name for offline mode
    fun setOfflineSiteName(name: String) {
        val trimmed = name.trim().ifEmpty { "Emeris NMB" }
        offlineSiteName = trimmed
        sharedPreferences?.edit()?.putString(KEY_OFFLINE_SITE_NAME, trimmed)?.apply()
    }

    // Returns custom site name for offline mode
    fun getOfflineSiteName(): String = offlineSiteName

    // Saves whether user selected offline mode
    fun setPreferOfflineMode(offline: Boolean) {
        preferOfflineMode = offline
        sharedPreferences?.edit()?.putBoolean(KEY_PREFER_OFFLINE, offline)?.apply()
    }

    // Returns whether the user previously selected offline mode
    fun isOfflineModePreferred(): Boolean = preferOfflineMode

    // Saves whether offline mode should use simpler single-screen mode
    fun setOfflineSimpleMode(enabled: Boolean) {
        offlineSimpleMode = enabled
        sharedPreferences?.edit()?.putBoolean(KEY_OFFLINE_SIMPLE_MODE, enabled)?.apply()
    }

    // Returns whether simpler single-screen mode is chosen for offline use
    fun isOfflineSimpleMode(): Boolean = offlineSimpleMode

    // Saves the last structure selected in Simple Mode
    fun setSimpleModeStructureId(id: String?) {
        simpleModeStructureId = id
        sharedPreferences?.edit()?.apply {
            if (id.isNullOrBlank()) remove(KEY_SIMPLE_MODE_STRUCTURE_ID)
            else putString(KEY_SIMPLE_MODE_STRUCTURE_ID, id)
        }?.apply()
    }

    // Returns the last structure selected in Simple Mode, or null
    fun getSimpleModeStructureId(): String? = simpleModeStructureId

    // Determines if simpler mode should be shown for the current user/context:
    // 1) Online user with role "Worker"
    // 2) Offline user who enabled simple mode
    fun isSimpleModeActive(): Boolean {
        return if (isSignedIn()) {
            getSignedInRole().equals("Worker", ignoreCase = true)
        } else {
            offlineSimpleMode
        }
    }

    // Retrieves locally cached sites JSON
    fun getCachedSitesJson(): String? = sharedPreferences?.getString(KEY_CACHED_SITES, null)

    // Persists locally cached sites JSON
    fun saveCachedSitesJson(json: String?) {
        sharedPreferences?.edit()?.apply {
            if (json == null) remove(KEY_CACHED_SITES) else putString(KEY_CACHED_SITES, json)
        }?.apply()
    }

    // Saves user dark mode choice so it persists across app restarts
    fun setDarkMode(isDark: Boolean) {
        savedDarkMode = isDark
        sharedPreferences?.edit()?.putBoolean(KEY_THEME_DARK_MODE, isDark)?.apply()
    }

    // Returns saved dark mode choice, or system default if never chosen
    fun getSavedDarkMode(systemDefault: Boolean): Boolean {
        return savedDarkMode ?: systemDefault
    }

    // Saves user high contrast choice so it persists across app restarts
    fun setHighContrast(isHigh: Boolean) {
        savedHighContrast = isHigh
        sharedPreferences?.edit()?.putBoolean(KEY_THEME_HIGH_CONTRAST, isHigh)?.apply()
    }

    // Returns saved high contrast choice, or the system setting if never chosen
    fun getSavedHighContrast(systemDefault: Boolean): Boolean {
        return savedHighContrast ?: systemDefault
    }

    // Saves the JWT bearer token, when it expires, and who signed in, so isSignedIn() can
    // tell a stale saved token from a good one, and the app can show who is signed in,
    // after it is closed and reopened.
    fun setAuthToken(token: String?, expiresAt: String? = null, email: String? = null, role: String? = null) {
        authToken = token
        authTokenExpiresAt = expiresAt
        authEmail = email
        authRole = role
        // Loads the name the account saved before
        authFullName = sharedPreferences?.getString(fullNameKey(email), null)
        // When user signs in, they are online, so clear offline mode preference
        preferOfflineMode = false
        sharedPreferences?.edit()
            ?.putString(KEY_AUTH_TOKEN, token)
            ?.putString(KEY_AUTH_TOKEN_EXPIRES_AT, expiresAt)
            ?.putString(KEY_AUTH_EMAIL, email)
            ?.putString(KEY_AUTH_ROLE, role)
            ?.putBoolean(KEY_PREFER_OFFLINE, false)
            ?.apply()
    }

    // The key the custom full name is saved under
    private fun fullNameKey(email: String?): String {
        val owner = email?.trim()?.lowercase()?.takeIf { it.isNotEmpty() } ?: OFFLINE_PROFILE
        return KEY_FULLNAME_PREFIX + owner
    }

    // Sets and persists an optional custom Full Name for the account that is signed in
    fun setCustomFullName(fullName: String?) {
        val trimmed = fullName?.trim()?.takeIf { it.isNotBlank() }
        val key = fullNameKey(authEmail)
        authFullName = trimmed
        sharedPreferences?.edit()?.apply {
            if (trimmed == null) {
                remove(key)
            } else {
                putString(key, trimmed)
            }
        }?.apply()
    }

    // Returns custom Full Name if set, or null
    fun getCustomFullName(): String? = authFullName

    // Backward compatibility aliases
    fun setCustomUsername(username: String?) = setCustomFullName(username)
    fun getCustomUsername(): String? = getCustomFullName()

    // Returns custom full name if set, otherwise signed-in email or fallback
    fun getEffectiveDisplayName(fallback: String = "Hydro Farmer"): String {
        return authFullName?.takeIf { it.isNotBlank() }
            ?: authEmail?.takeIf { it.isNotBlank() }
            ?: fallback
    }

    // Returns the stored JWT token if present.
    fun getAuthToken(): String? = authToken

    // Tracks the last authenticated account email for user-scoped cache management
    fun getLastLoggedInEmail(): String? = sharedPreferences?.getString(KEY_LAST_LOGGED_IN_EMAIL, null)

    fun setLastLoggedInEmail(email: String?) {
        sharedPreferences?.edit()?.apply {
            if (email.isNullOrBlank()) remove(KEY_LAST_LOGGED_IN_EMAIL) else putString(KEY_LAST_LOGGED_IN_EMAIL, email)
        }?.apply()
    }

    // Returns the signed-in user's email, or null when nobody is signed in.
    fun getSignedInEmail(): String? = authEmail

    // Returns the signed-in user's role (e.g. "Admin", "Operator"), or null when signed out.
    fun getSignedInRole(): String? = authRole

    // True when a token is saved and its expiry, if known, hasn't passed yet.
    // Called on launch to decide whether the login screen can be skipped.
    fun isSignedIn(): Boolean {
        val token = authToken
        if (token.isNullOrBlank()) return false

        val expiresAt = authTokenExpiresAt ?: return true
        return try {
            Instant.parse(expiresAt).isAfter(Instant.now())
        } catch (e: DateTimeParseException) {
            // Can't tell, so trust the saved token rather than force a sign-in
            true
        }
    }

    // Clears the saved session on logout, or when a stale token is found on launch.
    fun clearAuthToken() {
        authToken = null
        authTokenExpiresAt = null
        authEmail = null
        authRole = null
        simpleModeStructureId = null
        // Swaps to the offline name so the account that signed out no longer shows
        authFullName = sharedPreferences?.getString(fullNameKey(null), null)
        sharedPreferences?.edit()
            ?.remove(KEY_AUTH_TOKEN)
            ?.remove(KEY_AUTH_TOKEN_EXPIRES_AT)
            ?.remove(KEY_AUTH_EMAIL)
            ?.remove(KEY_AUTH_ROLE)
            ?.remove(KEY_CACHED_SITES)
            ?.remove(KEY_SIMPLE_MODE_STRUCTURE_ID)
            ?.apply()
    }

    val gson: Gson = GsonBuilder()
        .setDateFormat("yyyy-MM-dd'T'HH:mm:ss")
        .create()

    private val authInterceptor = Interceptor { chain ->
        val original = chain.request()
        val requestBuilder = original.newBuilder()

        // Attach Authorization Bearer header if token is present
        authToken?.takeIf { it.isNotBlank() }?.let { token ->
            requestBuilder.header("Authorization", "Bearer $token")
        }

        requestBuilder.header("Accept", "application/json")
        chain.proceed(requestBuilder.build())
    }

    private val loggingInterceptor = HttpLoggingInterceptor().apply {
        level = HttpLoggingInterceptor.Level.BODY
    }

    val okHttpClient: OkHttpClient by lazy {
        OkHttpClient.Builder()
            .addInterceptor(authInterceptor)
            .addInterceptor(loggingInterceptor)
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()
    }

    // Retrieves the singleton SmartHydroApi instance.
    val service: SmartHydroApi
        get() {
            return apiServiceInstance ?: synchronized(this) {
                apiServiceInstance ?: buildRetrofit().create(SmartHydroApi::class.java).also {
                    apiServiceInstance = it
                }
            }
        }

    private fun buildRetrofit(): Retrofit {
        return Retrofit.Builder()
            .baseUrl(BASE_URL)
            .client(okHttpClient)
            .addConverterFactory(GsonConverterFactory.create(gson))
            .build()
    }
}