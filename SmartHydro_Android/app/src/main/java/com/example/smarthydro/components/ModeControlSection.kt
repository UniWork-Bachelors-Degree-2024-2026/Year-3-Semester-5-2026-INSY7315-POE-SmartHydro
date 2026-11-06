package com.example.smarthydro.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.transport.BoxMode
import com.example.smarthydro.transport.ModeControl
import com.example.smarthydro.transport.ModeControlContent
import com.example.smarthydro.transport.ModeOption
import com.example.smarthydro.transport.ModeOptionState
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.statusAnnouncement
import com.example.smarthydro.ui.theme.*

// The mode buttons: which mode the box is in, a note when the box is switching or moved on its own, and a row for each
// mode that can be pressed to switch to it. What each row says comes from ModeControl, so this only draws it
@Composable
fun ModeControlSection(content: ModeControlContent, onChoose: (BoxMode) -> Unit) {
    val isDark = LocalThemeIsDark.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .background(if (isDark) SurfaceDarkVariant else BackgroundLight)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Text(
            text = content.headline,
            modifier = Modifier.screenHeading(),
            fontSize = 15.sp,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onSurface
        )

        // Read out when it changes, so a switch or a fallback is announced to someone who cannot see the screen
        content.note?.let {
            Text(
                text = it,
                modifier = Modifier.statusAnnouncement(),
                fontSize = 12.sp,
                color = if (isDark) StatusWarnTextDark else StatusWarnText
            )
        }

        Spacer(modifier = Modifier.height(2.dp))

        content.options.forEach { option ->
            ModeRow(option = option, onClick = { onChoose(option.mode) })
        }
    }
}

@Composable
private fun ModeRow(option: ModeOption, onClick: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    val current = option.state == ModeOptionState.CURRENT
    val shape = RoundedCornerShape(10.dp)
    val secondary = if (isDark) TextSecondaryDark else TextSecondary

    val stateWords = when (option.state) {
        ModeOptionState.CURRENT -> "in use"
        ModeOptionState.SWITCHING -> "switching"
        ModeOptionState.WANTED -> "wanted"
        ModeOptionState.AVAILABLE -> ""
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(if (current) (if (isDark) MintBannerBgDark else MintBannerBg) else MaterialTheme.colorScheme.surface)
            .border(BorderStroke(1.dp, if (current) ForestGreen else (if (isDark) CardBorderDark else CardDivider)), shape)
            .clickable(enabled = ModeControl.canChoose(option), role = Role.Button, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 12.dp)
            .semantics {
                selected = current
                contentDescription = "${option.label}. ${option.description}. $stateWords".trim().trimEnd('.')
            },
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = option.label, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
            Text(text = option.description, fontSize = 12.sp, color = secondary)
        }

        Spacer(modifier = Modifier.width(8.dp))

        when (option.state) {
            ModeOptionState.CURRENT -> Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    painter = painterResource(id = R.drawable.ic_check),
                    contentDescription = null,
                    tint = ForestGreen,
                    modifier = Modifier
                        .decorative()
                        .size(16.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(text = "In use", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = if (isDark) StatusGoodTextDark else StatusGoodText)
            }

            ModeOptionState.SWITCHING -> CircularProgressIndicator(
                modifier = Modifier
                    .decorative()
                    .size(18.dp),
                strokeWidth = 2.dp,
                color = ForestGreen
            )

            ModeOptionState.WANTED -> Text(text = "Wanted", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = if (isDark) StatusWarnTextDark else StatusWarnText)

            ModeOptionState.AVAILABLE -> Text(text = "›", modifier = Modifier.decorative(), fontSize = 20.sp, color = secondary)
        }
    }
}
