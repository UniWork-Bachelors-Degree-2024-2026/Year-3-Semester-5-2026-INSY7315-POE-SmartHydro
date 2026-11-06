package com.example.smarthydro.ui

// When Simple Mode offers a farmer a way to connect their box, and what it says. Simple Mode keeps everything on one
// screen, so this is one large button that is only there while it is needed
object SimpleModeConnect {

    const val MESSAGE = "Your box isn't connected."
    const val BUTTON = "Connect your box"

    // The button is offered to a farmer using a paired box, once the box has had its chance to answer and has not.
    // It is not offered while the box is still being checked, to someone whose box is answering, or to anyone using the cloud
    fun shouldOffer(isFarmerBox: Boolean, boxChecked: Boolean, hasReading: Boolean): Boolean =
        isFarmerBox && boxChecked && !hasReading
}
