package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.models.StructureType
import com.example.smarthydro.transport.PairSheetAction
import com.example.smarthydro.transport.PairSheetContent
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.theme.*

// The sheet that offers a box and connects to it, in the style of the phone's own pairing sheet: the box's name, a
// picture, a line saying what to do and a button. What it says at each step comes from PairSheetContent, and
// onAction is told which button was pressed
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BoxPairSheet(
    content: PairSheetContent,
    structureType: StructureType,
    onAction: (PairSheetAction) -> Unit
) {
    ModalBottomSheet(
        onDismissRequest = { onAction(PairSheetAction.CLOSE) },
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        BoxPairSheetBody(content = content, structureType = structureType, onAction = onAction)
    }
}

// What is inside the sheet. It is kept apart from the sheet so Android Studio can show it in a preview
@Composable
internal fun BoxPairSheetBody(
    content: PairSheetContent,
    structureType: StructureType,
    onAction: (PairSheetAction) -> Unit
) {
    val isDark = LocalThemeIsDark.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 24.dp)
            .padding(bottom = 28.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(
            text = content.title,
            modifier = Modifier.screenHeading(),
            fontSize = 20.sp,
            fontWeight = FontWeight.SemiBold,
            textAlign = TextAlign.Center,
            color = if (isDark) TextPrimaryDark else ForestGreenDark
        )

        Spacer(modifier = Modifier.height(10.dp))

        // Read out when it changes, so the person who cannot see the screen is told to enter the code
        Text(
            text = content.subtitle,
            modifier = Modifier.statusAnnouncement(),
            fontSize = 14.sp,
            textAlign = TextAlign.Center,
            color = if (isDark) TextSecondaryDark else TextSecondary
        )

        Spacer(modifier = Modifier.height(20.dp))

        BoxIllustration(structureType = structureType, showCode = content.showCode, connected = content.connected)

        Spacer(modifier = Modifier.height(24.dp))

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            content.secondaryLabel?.let { label ->
                TextButton(onClick = { onAction(PairSheetAction.CLOSE) }) {
                    Text(text = label, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = if (isDark) TextSecondaryDark else TextSecondary)
                }
            }

            Button(
                onClick = { onAction(content.primaryAction) },
                enabled = content.primaryAction != PairSheetAction.NONE,
                modifier = Modifier
                    .weight(1f)
                    .height(48.dp),
                shape = RoundedCornerShape(24.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isDark) PrimaryButtonBgDark else ForestGreen,
                    contentColor = Color.White,
                    disabledContainerColor = if (isDark) PrimaryButtonDisabledBgDark else ForestGreen.copy(alpha = 0.55f),
                    disabledContentColor = if (isDark) PrimaryButtonDisabledTextDark else Color.White.copy(alpha = 0.85f)
                )
            ) {
                if (content.working) {
                    CircularProgressIndicator(
                        modifier = Modifier
                            .size(18.dp)
                            .semantics { contentDescription = "Connecting" },
                        strokeWidth = 2.dp,
                        color = Color.White
                    )
                    Spacer(modifier = Modifier.width(10.dp))
                }
                Text(text = content.primaryLabel, fontWeight = FontWeight.Bold)
            }
        }
    }
}

// A simple picture of a box: its case, with its small screen showing the structure while it is found, dots where the
// code will appear while pairing, and a tick once connected. It is only a picture, so a screen reader skips it
@Composable
private fun BoxIllustration(structureType: StructureType, showCode: Boolean, connected: Boolean) {
    val isDark = LocalThemeIsDark.current

    Box(
        modifier = Modifier
            .decorative()
            .size(width = 150.dp, height = 104.dp)
            .background(if (isDark) MintBannerBgDark else MintBannerBg, RoundedCornerShape(22.dp))
            .border(BorderStroke(1.dp, if (isDark) CardBorderDark else CardDivider), RoundedCornerShape(22.dp)),
        contentAlignment = Alignment.Center
    ) {
        // The box's own screen
        Box(
            modifier = Modifier
                .size(width = 108.dp, height = 56.dp)
                .background(HeaderDarkBg, RoundedCornerShape(10.dp)),
            contentAlignment = Alignment.Center
        ) {
            when {
                connected -> Icon(
                    painter = painterResource(id = R.drawable.ic_check),
                    contentDescription = null,
                    tint = ForestGreenLight,
                    modifier = Modifier.size(28.dp)
                )

                showCode -> Text(
                    text = "● ● ● ● ● ●",
                    fontSize = 13.sp,
                    color = Color.White,
                    letterSpacing = 1.sp
                )

                else -> Icon(
                    painter = painterResource(
                        id = if (structureType == StructureType.FODDER) R.drawable.ic_carrot else R.drawable.ic_flower
                    ),
                    contentDescription = null,
                    tint = ForestGreenLight,
                    modifier = Modifier.size(28.dp)
                )
            }
        }
    }
}
