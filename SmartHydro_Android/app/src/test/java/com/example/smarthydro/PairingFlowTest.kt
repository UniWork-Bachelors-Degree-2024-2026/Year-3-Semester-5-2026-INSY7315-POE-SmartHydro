package com.example.smarthydro

import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.BoxAdvertisement
import com.example.smarthydro.transport.ConnectionState
import com.example.smarthydro.transport.DisconnectReason
import com.example.smarthydro.transport.NearbyBox
import com.example.smarthydro.transport.PairingAction
import com.example.smarthydro.transport.PairingEvent
import com.example.smarthydro.transport.PairingFailure
import com.example.smarthydro.transport.PairingFlow
import com.example.smarthydro.transport.PairingState
import com.example.smarthydro.transport.PairingTransition
import com.example.smarthydro.transport.ScanProblem
import com.example.smarthydro.transport.ScanUpdate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// Checks every way pairing a box over Bluetooth can go, from the first tap to a saved box
class PairingFlowTest {

    private val ownMac = "A4:CF:12:9B:3E:01"
    private val flow = PairingFlow(ownMac, StructureType.HYDROPONIC)
    private val permissions = listOf("android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT")

    private fun box(name: String, address: String, rssi: Int = -60) = BoxAdvertisement.parse(name, address, rssi, 0L)!!

    private val ownBox = box("SmartHydro-Tower-9B3E01", "own")
    private val otherTower = box("SmartHydro-Tower-111111", "other", rssi = -40)
    private val fodderBox = box("SmartHydro-Fodder-44A702", "fodder", rssi = -30)

    private fun step(state: PairingState, event: PairingEvent) = flow.reduce(state, event)

    private fun play(start: PairingState, vararg events: PairingEvent): PairingTransition =
        events.fold(PairingTransition(start)) { last, event -> flow.reduce(last.state, event) }

    private fun scan(vararg boxes: NearbyBox) = PairingEvent.Scan(ScanUpdate.Boxes(boxes.toList()))

    private val searching = PairingState.Searching(emptyList(), finished = false)

    // ---- starting ----

    @Test
    fun startingWithPermissionsAsksForThem() {
        val transition = step(PairingState.Idle, PairingEvent.Start(permissions))

        assertEquals(PairingState.NeedsPermission(permissions), transition.state)
        assertEquals(PairingAction.RequestPermissions(permissions), transition.action)
    }

    @Test
    fun startingWithNothingMissingStartsSearching() {
        val transition = step(PairingState.Idle, PairingEvent.Start(emptyList()))

        assertEquals(searching, transition.state)
        assertEquals(PairingAction.StartScan, transition.action)
    }

    @Test
    fun allowedPermissionsStartTheSearch() {
        val transition = step(PairingState.NeedsPermission(permissions), PairingEvent.PermissionsAnswered(true))

        assertEquals(searching, transition.state)
        assertEquals(PairingAction.StartScan, transition.action)
    }

    @Test
    fun refusedPermissionsEndPairing() {
        val transition = step(PairingState.NeedsPermission(permissions), PairingEvent.PermissionsAnswered(false))

        assertEquals(PairingState.Failed(PairingFailure.PermissionDenied), transition.state)
        assertNull(transition.action)
    }

    // ---- searching ----

    @Test
    fun boxesHeardAreListedStrongestFirstWithOnlyTheRightKind() {
        val transition = step(searching, scan(otherTower, fodderBox, box("SmartHydro-Tower-222222", "far", rssi = -80)))

        val listed = (transition.state as PairingState.Searching).boxes
        assertEquals(listOf("other", "far"), listed.map { it.address })
    }

    @Test
    fun fodderStructureOnlySeesFodderBoxes() {
        val fodderFlow = PairingFlow(null, StructureType.FODDER)
        val transition = fodderFlow.reduce(searching, scan(otherTower, fodderBox))

        assertEquals(listOf("fodder"), (transition.state as PairingState.Searching).boxes.map { it.address })
    }

    @Test
    fun theStructuresOwnBoxIsOfferedStraightAway() {
        val transition = step(searching, scan(otherTower, ownBox))

        assertEquals(PairingState.Confirming(ownBox, listOf(ownBox, otherTower)), transition.state)
        assertEquals(PairingAction.StopScan, transition.action)
    }

    @Test
    fun structureWithNoKnownBoxGetsAListToPickFrom() {
        val unknown = PairingFlow(null, StructureType.HYDROPONIC)
        val transition = unknown.reduce(searching, scan(ownBox, otherTower))

        assertTrue(transition.state is PairingState.Searching)
        assertNull(transition.action)
    }

    @Test
    fun theOwnBoxIsListedFirstEvenWithAWeakerSignal() {
        val unknownOwn = PairingFlow(ownMac, StructureType.HYDROPONIC)

        assertEquals(listOf("own", "other"), unknownOwn.visibleBoxes(listOf(otherTower, ownBox.copy(rssi = -90))).map { it.address })
    }

    @Test
    fun searchProblemEndsPairing() {
        val transition = step(searching, PairingEvent.Scan(ScanUpdate.Problem(ScanProblem.BLUETOOTH_OFF)))

        assertEquals(PairingState.Failed(PairingFailure.Search(ScanProblem.BLUETOOTH_OFF)), transition.state)
        assertEquals(PairingAction.StopScan, transition.action)
    }

    @Test
    fun searchThatFindsNothingEndsWithNoBoxFound() {
        val transition = step(searching, PairingEvent.ScanEnded)

        assertEquals(PairingState.Failed(PairingFailure.NoBoxFound), transition.state)
    }

    @Test
    fun searchThatRanOutKeepsWhatItFound() {
        val found = play(searching, scan(otherTower), PairingEvent.ScanEnded)

        assertEquals(PairingState.Searching(listOf(otherTower), finished = true), found.state)
    }

    @Test
    fun searchingAgainFromAFinishedSearchStartsOver() {
        val finished = PairingState.Searching(listOf(otherTower), finished = true)
        val transition = step(finished, PairingEvent.Start(emptyList()))

        assertEquals(searching, transition.state)
        assertEquals(PairingAction.StartScan, transition.action)
    }

    // ---- choosing a box ----

    @Test
    fun pickingABoxFromTheListOffersItInTheSheet() {
        val listed = PairingState.Searching(listOf(otherTower), finished = true)
        val transition = step(listed, PairingEvent.BoxChosen("other"))

        assertEquals(PairingState.Confirming(otherTower, listOf(otherTower)), transition.state)
        assertEquals(PairingAction.StopScan, transition.action)
    }

    @Test
    fun pickingABoxThatIsNotListedChangesNothing() {
        val listed = PairingState.Searching(listOf(otherTower), finished = true)

        assertEquals(PairingTransition(listed), step(listed, PairingEvent.BoxChosen("missing")))
    }

    @Test
    fun closingTheSheetGoesBackToTheList() {
        val sheet = PairingState.Confirming(ownBox, listOf(ownBox, otherTower))
        val transition = step(sheet, PairingEvent.DismissSheet)

        assertEquals(PairingState.Searching(listOf(ownBox, otherTower), finished = true), transition.state)
        assertNull(transition.action)
    }

    // ---- connecting ----

    @Test
    fun sayingYesOpensTheConnection() {
        val transition = step(PairingState.Confirming(ownBox, listOf(ownBox)), PairingEvent.ConfirmConnect)

        assertEquals(PairingState.Connecting(ownBox, ConnectionState.Connecting), transition.state)
        assertEquals(PairingAction.OpenConnection("own"), transition.action)
    }

    @Test
    fun connectionProgressIsFollowed() {
        val start = PairingState.Connecting(ownBox, ConnectionState.Connecting)
        val transition = step(start, PairingEvent.Connection(ConnectionState.Pairing(247)))

        assertEquals(PairingState.Connecting(ownBox, ConnectionState.Pairing(247)), transition.state)
        assertNull(transition.action)
    }

    @Test
    fun theCodeIsAskedForWhilePairing() {
        val pairing = PairingState.Connecting(ownBox, ConnectionState.Pairing(247))
        val connecting = PairingState.Connecting(ownBox, ConnectionState.Connecting)

        assertEquals("Enter the 6-digit code shown on the box's screen.", pairing.message)
        assertEquals("Connecting to SmartHydro-Tower-9B3E01…", connecting.message)
    }

    @Test
    fun aReadyConnectionSavesTheBox() {
        val transition = step(PairingState.Connecting(ownBox, ConnectionState.Pairing(247)), PairingEvent.Connection(ConnectionState.Ready(247)))

        assertEquals(PairingState.Done(ownBox), transition.state)
        assertEquals(PairingAction.SaveAddress("own"), transition.action)
    }

    @Test
    fun aConnectionThatEndsFailsPairingWithItsReason() {
        val ended = ConnectionState.Disconnected(DisconnectReason.PAIRING_FAILED)
        val transition = step(PairingState.Connecting(ownBox, ConnectionState.Pairing(247)), PairingEvent.Connection(ended))

        assertEquals(PairingState.Failed(PairingFailure.Connect(DisconnectReason.PAIRING_FAILED)), transition.state)
        assertEquals(PairingAction.CloseConnection, transition.action)
    }

    @Test
    fun theWholeWayThroughForTheStructuresOwnBox() {
        val done = play(
            PairingState.Idle,
            PairingEvent.Start(permissions),
            PairingEvent.PermissionsAnswered(true),
            scan(ownBox),
            PairingEvent.ConfirmConnect,
            PairingEvent.Connection(ConnectionState.Pairing(247)),
            PairingEvent.Connection(ConnectionState.Ready(247))
        )

        assertEquals(PairingState.Done(ownBox), done.state)
        assertEquals(PairingAction.SaveAddress("own"), done.action)
    }

    // ---- trying again after a failure ----

    @Test
    fun aFailureCanBeRetried() {
        val failed = PairingState.Failed(PairingFailure.NoBoxFound)

        assertEquals(PairingTransition(searching, PairingAction.StartScan), step(failed, PairingEvent.Start(emptyList())))
    }

    // ---- giving up ----

    @Test
    fun givingUpWhileSearchingStopsTheSearch() {
        val transition = step(PairingState.Searching(listOf(otherTower), finished = false), PairingEvent.Cancel)

        assertEquals(PairingState.Idle, transition.state)
        assertEquals(PairingAction.StopScan, transition.action)
    }

    @Test
    fun givingUpWhileConnectingClosesTheConnection() {
        val transition = step(PairingState.Connecting(ownBox, ConnectionState.Pairing(247)), PairingEvent.Cancel)

        assertEquals(PairingState.Idle, transition.state)
        assertEquals(PairingAction.CloseConnection, transition.action)
    }

    @Test
    fun givingUpFromTheSheetOrAFailureJustGoesBack() {
        assertEquals(PairingTransition(PairingState.Idle), step(PairingState.Confirming(ownBox, listOf(ownBox)), PairingEvent.Cancel))
        assertEquals(PairingTransition(PairingState.Idle), step(PairingState.Failed(PairingFailure.NoBoxFound), PairingEvent.Cancel))
    }

    @Test
    fun aFinishedPairingStaysFinished() {
        val done = PairingState.Done(ownBox)

        assertEquals(PairingTransition(done), step(done, PairingEvent.Cancel))
        assertEquals(PairingTransition(done), step(done, PairingEvent.Start(emptyList())))
        assertEquals(PairingTransition(done), step(done, scan(otherTower)))
    }

    @Test
    fun closingTheConnectedSheetGoesBackToIdle() {
        assertEquals(PairingTransition(PairingState.Idle), step(PairingState.Done(ownBox), PairingEvent.Finish))
    }

    @Test
    fun closingTheConnectedSheetWhenNotConnectedChangesNothing() {
        val sheet = PairingState.Confirming(ownBox, listOf(ownBox))

        assertEquals(PairingTransition(sheet), step(sheet, PairingEvent.Finish))
        assertEquals(PairingTransition(PairingState.Idle), step(PairingState.Idle, PairingEvent.Finish))
    }

    // ---- events that arrive at the wrong time ----

    @Test
    fun lateSearchResultsAfterTheSheetIsUpAreIgnored() {
        val sheet = PairingState.Confirming(ownBox, listOf(ownBox))

        assertEquals(PairingTransition(sheet), step(sheet, scan(otherTower)))
        assertEquals(PairingTransition(sheet), step(sheet, PairingEvent.ScanEnded))
    }

    @Test
    fun eventsBeforeAnythingStartedAreIgnored() {
        listOf(scan(ownBox), PairingEvent.ScanEnded, PairingEvent.ConfirmConnect, PairingEvent.Connection(ConnectionState.Ready(247)))
            .forEach { assertEquals("$it", PairingTransition(PairingState.Idle), step(PairingState.Idle, it)) }
    }

    @Test
    fun startingWhileConnectingChangesNothing() {
        val connecting = PairingState.Connecting(ownBox, ConnectionState.Connecting)

        assertEquals(PairingTransition(connecting), step(connecting, PairingEvent.Start(emptyList())))
    }

    @Test
    fun everyFailureHasItsOwnWords() {
        val failures = listOf(
            PairingFailure.PermissionDenied,
            PairingFailure.NoBoxFound,
            PairingFailure.Search(ScanProblem.BLUETOOTH_OFF),
            PairingFailure.Connect(DisconnectReason.PAIRING_FAILED)
        )

        assertTrue(failures.all { it.message.isNotBlank() })
        assertEquals(failures.size, failures.map { it.message }.toSet().size)
    }
}
