package com.example.smarthydro.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.screenHeading
import com.example.smarthydro.ui.theme.*

@Composable
fun RegisterPendingScreen(
    email: String,
    onNavigateToLogin: () -> Unit,
    modifier: Modifier = Modifier
) {
    val isDark = LocalThemeIsDark.current

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 28.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Box(
                modifier = Modifier
                    .size(84.dp)
                    .clip(CircleShape)
                    .background(ForestGreen),
                contentAlignment = Alignment.Center
            ) {
                androidx.compose.foundation.Image(
                    painter = painterResource(id = R.drawable.ic_smarthydro_logo),
                    contentDescription = "Smart Hydro Logo",
                    modifier = Modifier.size(64.dp)
                )
            }

            Spacer(modifier = Modifier.height(20.dp))

            Text(
                text = "Registration received",
                modifier = Modifier.screenHeading(),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = if (isDark) TextPrimaryDark else ForestGreenDark
            )

            Spacer(modifier = Modifier.height(6.dp))

            Text(
                text = "Pending administrator review.",
                fontSize = 14.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(16.dp))

            Text(
                text = if (email.isNotBlank()) {
                    "We've recorded your Operator sign-up for $email. You'll receive an email once " +
                            "an administrator has approved your account and then you can log in."
                } else {
                    "You'll receive an email once an administrator has approved your Operator " +
                            "account and then you can log in."
                },
                fontSize = 13.sp,
                color = if (isDark) TextSecondaryDark else TextSecondary,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(28.dp))

            HorizontalDivider(color = if (isDark) CardDividerDark else CardDivider)

            Spacer(modifier = Modifier.height(20.dp))

            TextButton(onClick = onNavigateToLogin) {
                Text(
                    text = "Back to sign in",
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = if (isDark) TextPrimaryDark else ForestGreenDark
                )
            }
        }
    }
}