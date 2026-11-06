package com.example.smarthydro.ui

import androidx.compose.foundation.selection.toggleable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics

// Marks a title so TalkBack can jump between headings and announces it as a heading
fun Modifier.screenHeading(): Modifier = semantics { heading() }

// Makes TalkBack read the content out the moment it appears, used for form errors
fun Modifier.errorAnnouncement(): Modifier = semantics { liveRegion = LiveRegionMode.Assertive }

// Makes TalkBack read the content out when it changes without interrupting what is being said, used for status messages
fun Modifier.statusAnnouncement(): Modifier = semantics { liveRegion = LiveRegionMode.Polite }

// Turns a whole row into one switch so TalkBack reads its label and state together and the row toggles from anywhere on it
fun Modifier.switchRow(checked: Boolean, onCheckedChange: (Boolean) -> Unit): Modifier =
    toggleable(value = checked, role = Role.Switch, onValueChange = onCheckedChange)

// Makes TalkBack read everything inside as one item instead of one swipe per piece of text
fun Modifier.readAsOne(): Modifier = semantics(mergeDescendants = true) {}

// Hides a purely visual character such as a chevron from TalkBack so it is not read out as a symbol name
fun Modifier.decorative(): Modifier = clearAndSetSemantics {}

// Gives a standalone switch a spoken name, used where only the switch itself should be tappable
fun Modifier.switchLabel(label: String): Modifier = semantics { contentDescription = label }
