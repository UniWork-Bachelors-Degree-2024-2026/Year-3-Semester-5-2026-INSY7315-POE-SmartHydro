package com.example.smarthydro.screens

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.smarthydro.R
import com.example.smarthydro.ui.theme.ForestGreen
import kotlinx.coroutines.delay

@Composable
fun SplashScreen(
    onSplashClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    LaunchedEffect(Unit) {
        delay(3000)
        onSplashClick()
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .background(ForestGreen)
            .clickable {
                onSplashClick()
            }
    ) {
        // Decorative background circles
        Canvas(modifier = Modifier.fillMaxSize()) {
            val subtleGreen = Color(0xFF265F36).copy(alpha = 0.55f)
            // Top right subtle circle
            drawCircle(
                color = subtleGreen,
                radius = size.width * 0.55f,
                center = Offset(size.width * 0.9f, size.height * 0.05f)
            )
            // Bottom left subtle circle
            drawCircle(
                color = subtleGreen,
                radius = size.width * 0.65f,
                center = Offset(size.width * 0.1f, size.height * 0.95f)
            )
        }

        // Center Content
        Column(
            modifier = Modifier
                .align(Alignment.Center)
                .fillMaxWidth()
                .padding(horizontal = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Logo from design specs
            Box(
                modifier = Modifier
                    .size(130.dp)
                    .clip(CircleShape)
                    .background(Color.White),
                contentAlignment = Alignment.Center
            ) {
                Image(
                    painter = painterResource(id = R.drawable.ic_smarthydro_logo),
                    contentDescription = "Smart Hydro Logo",
                    modifier = Modifier.size(110.dp)
                )
            }

            Spacer(modifier = Modifier.height(28.dp))

            Text(
                text = "Smart Hydro",
                color = Color.White,
                fontSize = 32.sp,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center
            )

            Spacer(modifier = Modifier.height(10.dp))

            Text(
                text = "WE'RE TOTALLY ROOT-ING FOR YOUR\nHARVEST!",
                color = Color.White.copy(alpha = 0.92f),
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                letterSpacing = 1.2.sp,
                textAlign = TextAlign.Center,
                lineHeight = 18.sp
            )
        }

        // Bottom Footer
        Text(
            text = "EMERIS NMB · SMART HYDRO",
            color = Color.White.copy(alpha = 0.75f),
            fontSize = 11.sp,
            fontWeight = FontWeight.Medium,
            letterSpacing = 1.sp,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(bottom = 28.dp)
        )
    }
}
