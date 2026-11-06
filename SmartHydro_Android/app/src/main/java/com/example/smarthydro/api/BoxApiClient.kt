package com.example.smarthydro.api

import com.google.gson.GsonBuilder
import okhttp3.OkHttpClient
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

// Talks to a box directly over its own hotspot rather than through the cloud, so timeouts are
// kept short: a local ESP32 either answers almost instantly or isn't reachable at all.
object BoxApiClient {

    private val gson = GsonBuilder().create()

    private val okHttpClient: OkHttpClient by lazy {
        OkHttpClient.Builder()
            .connectTimeout(4, TimeUnit.SECONDS)
            .readTimeout(4, TimeUnit.SECONDS)
            .writeTimeout(4, TimeUnit.SECONDS)
            .build()
    }

    // Built fresh per call rather than cached, since a farmer's structures can each point at
    // a different box IP and that IP can be corrected at any time
    fun serviceFor(boxIpAddress: String): BoxApi {
        return Retrofit.Builder()
            .baseUrl("http://${boxIpAddress.trim()}/")
            .client(okHttpClient)
            .addConverterFactory(GsonConverterFactory.create(gson))
            .build()
            .create(BoxApi::class.java)
    }
}
