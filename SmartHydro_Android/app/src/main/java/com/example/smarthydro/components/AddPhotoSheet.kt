package com.example.smarthydro.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.decorative
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*

// Bottom sheet asking how to add a photo
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddPhotoSheet(
    onDismiss: () -> Unit,
    onTakePhoto: () -> Unit,
    onChoosePhoto: () -> Unit
) {
    val isDark = LocalThemeIsDark.current

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp)
                .padding(bottom = 28.dp)
        ) {
            Text(
                text = "Add a photo",
                modifier = Modifier.screenHeading(),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Text(
                text = "How would you like to add it?",
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary
            )
            Spacer(modifier = Modifier.height(16.dp))
            AddOption(symbol = null, title = "Take a photo", text = "Use this phone's camera.", onClick = onTakePhoto)
            Spacer(modifier = Modifier.height(10.dp))
            AddOption(symbol = "⬆", title = "Upload a photo", text = "Choose one from this phone's photos.", onClick = onChoosePhoto)
        }
    }
}

// symbol null means the camera icon is shown
@Composable
private fun AddOption(symbol: String?, title: String, text: String, onClick: () -> Unit) {
    val isDark = LocalThemeIsDark.current
    val secondaryText = if (isDark) TextSecondaryDark else TextSecondary

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .border(1.dp, if (isDark) CardBorderDark else CardDivider, RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(14.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(42.dp)
                .clip(CircleShape)
                .background(ForestGreen.copy(alpha = if (isDark) 0.22f else 0.12f)),
            contentAlignment = Alignment.Center
        ) {
            if (symbol == null) {
                Icon(
                    painter = painterResource(id = R.drawable.ic_camera),
                    contentDescription = null,
                    tint = ForestGreen,
                    modifier = Modifier.size(20.dp)
                )
            } else {
                Text(text = symbol, fontSize = 18.sp, color = ForestGreen, fontWeight = FontWeight.Bold)
            }
        }
        Spacer(modifier = Modifier.width(14.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )
            Text(text = text, fontSize = 12.sp, color = secondaryText)
        }
        Text(text = "›", modifier = Modifier.decorative(), fontSize = 22.sp, color = secondaryText)
    }
}