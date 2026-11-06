package com.example.smarthydro

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Place
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.example.smarthydro.api.ApiClient
import com.example.smarthydro.components.BottomNavBar
import com.example.smarthydro.components.ConfirmationDialog
import com.example.smarthydro.components.NavTab
import com.example.smarthydro.data.StructureStore
import com.example.smarthydro.screens.*
import com.example.smarthydro.ui.SimpleModeRoutes
import com.example.smarthydro.ui.SimpleModeState
import com.example.smarthydro.ui.theme.LocalNavigateToSettings
import kotlinx.coroutines.launch

@Composable
fun AppNavigation(
    navController: NavHostController = rememberNavController()
) {
    var activeStructureId by remember { mutableStateOf("fodder") }

    // Readings stays locked until a structure has been opened
    var hasChosenStructure by remember { mutableStateOf(false) }
    var showSelectStructurePrompt by remember { mutableStateOf(false) }

    // Holds the email and verification token for a registration in progress
    var registrationEmail by remember { mutableStateOf("") }
    var registrationToken by remember { mutableStateOf("") }

    val navBackStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = navBackStackEntry?.destination?.route ?: ""
    val isAdmin = ApiClient.getSignedInRole() == "Admin"
    val isSimpleMode = SimpleModeState.isActive

    // Only show bottom navigation on screens that belong to the active bottom tabs, never on splash, auth, or in simple mode
    val showBottomBar = if (currentRoute.isEmpty() || currentRoute == "splash" || isSimpleMode) {
        false
    } else if (isAdmin) {
        currentRoute in listOf("admin_dashboard", "admin_approvals", "admin_sites")
    } else {
        currentRoute in listOf("home", "gallery", "alerts") ||
                currentRoute.startsWith("readings/") ||
                currentRoute.startsWith("sensor_detail/")
    }
    val readingsLocked = !hasChosenStructure || StructureStore.find(activeStructureId) == null

    // Returns to a single structure list, clearing any screens opened from it
    val goToStructureList = {
        if (currentRoute != "home") {
            navController.navigate("home") {
                popUpTo("home") { inclusive = true }
                launchSingleTop = true
            }
        }
    }

    // Simple Mode has its own single screen on the home route, so a screen of the normal app that was open when it
    // was switched on is left at once, instead of staying on screen with no way to move around
    LaunchedEffect(isSimpleMode, currentRoute) {
        if (isSimpleMode && SimpleModeRoutes.isNormalScreen(currentRoute)) {
            navController.navigate("home") {
                popUpTo(0) { inclusive = true }
                launchSingleTop = true
            }
        }
    }

    val openSettings = {
        if (currentRoute != "settings") {
            navController.navigate("settings") {
                launchSingleTop = true
            }
        }
    }

    // Shown when Readings is tapped before a structure has been chosen
    if (showSelectStructurePrompt) {
        ConfirmationDialog(
            title = "Select a Structure",
            message = "Please select a structure to view its real-time sensor readings.",
            confirmButtonText = "Choose Structure",
            dismissButtonText = "Cancel",
            icon = Icons.Filled.Place,
            onDismiss = { showSelectStructurePrompt = false },
            onConfirm = {
                showSelectStructurePrompt = false
                goToStructureList()
            }
        )
    }

    // Determine start and target destination based on authentication and role
    val getStartDestination = {
        if (ApiClient.isSignedIn()) {
            if (ApiClient.getSignedInRole() == "Admin") "admin_dashboard" else "home"
        } else if (ApiClient.isOfflineModePreferred()) {
            "home"
        } else {
            "login"
        }
    }

    CompositionLocalProvider(
        LocalNavigateToSettings provides openSettings
    ) {
        Scaffold(
            bottomBar = {
                androidx.compose.animation.AnimatedVisibility(
                    visible = showBottomBar,
                    enter = androidx.compose.animation.fadeIn(
                        animationSpec = androidx.compose.animation.core.tween(250)
                    ) + androidx.compose.animation.slideInVertically(
                        initialOffsetY = { it },
                        animationSpec = androidx.compose.animation.core.tween(250)
                    ),
                    exit = androidx.compose.animation.fadeOut(
                        animationSpec = androidx.compose.animation.core.tween(150)
                    ) + androidx.compose.animation.slideOutVertically(
                        targetOffsetY = { it },
                        animationSpec = androidx.compose.animation.core.tween(150)
                    )
                ) {
                    val visibleTabs = if (isAdmin) {
                        listOf(NavTab.ADMIN_DASHBOARD, NavTab.ADMIN_APPROVALS, NavTab.ADMIN_SITES)
                    } else if (ApiClient.isSignedIn()) {
                        listOf(NavTab.STRUCTURE, NavTab.READINGS, NavTab.CAMERA, NavTab.ALERTS)
                    } else {
                        listOf(NavTab.STRUCTURE, NavTab.READINGS, NavTab.CAMERA)
                    }

                    BottomNavBar(
                        currentRoute = currentRoute,
                        readingsLocked = readingsLocked,
                        tabs = visibleTabs,
                        onTabSelected = { tab ->
                            when (tab) {
                                NavTab.ADMIN_DASHBOARD -> {
                                    if (currentRoute != "admin_dashboard") {
                                        navController.navigate("admin_dashboard") {
                                            popUpTo("admin_dashboard") { inclusive = true }
                                            launchSingleTop = true
                                        }
                                    }
                                }
                                NavTab.ADMIN_APPROVALS -> {
                                    if (currentRoute != "admin_approvals") {
                                        navController.navigate("admin_approvals") {
                                            popUpTo("admin_dashboard") { inclusive = false }
                                            launchSingleTop = true
                                        }
                                    }
                                }
                                NavTab.ADMIN_SITES -> {
                                    if (currentRoute != "admin_sites") {
                                        navController.navigate("admin_sites") {
                                            popUpTo("admin_dashboard") { inclusive = false }
                                            launchSingleTop = true
                                        }
                                    }
                                }
                                NavTab.STRUCTURE -> goToStructureList()
                                NavTab.READINGS -> when {
                                    readingsLocked -> showSelectStructurePrompt = true
                                    currentRoute.startsWith("readings/") -> Unit
                                    // Sensor detail is opened from Readings, so going back returns to it
                                    currentRoute.startsWith("sensor_detail/") -> navController.popBackStack()
                                    else -> navController.navigate("readings/$activeStructureId") {
                                        popUpTo("home") { saveState = true }
                                        launchSingleTop = true
                                    }
                                }
                                else -> navController.navigate(tab.route) {
                                    popUpTo("home") { saveState = true }
                                    launchSingleTop = true
                                    restoreState = true
                                }
                            }
                        }
                    )
                }
            },
            modifier = Modifier.fillMaxSize()
        ) { innerPadding ->
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(bottom = if (showBottomBar) innerPadding.calculateBottomPadding() else androidx.compose.ui.unit.Dp.Unspecified)
            ) {
                NavHost(
                    navController = navController,
                    startDestination = "splash"
                ) {
                    composable("splash") {
                        SplashScreen(
                            onSplashClick = {
                                val destination = getStartDestination()
                                if (destination == "login") {
                                    ApiClient.clearAuthToken()
                                }
                                navController.navigate(destination) {
                                    popUpTo("splash") { inclusive = true }
                                }
                            }
                        )
                    }

                    composable("login") {
                        val navigateToMain = {
                            val destination = getStartDestination()
                            navController.navigate(destination) {
                                popUpTo("login") { inclusive = true }
                                launchSingleTop = true
                            }
                        }
                        LoginScreen(
                            onSignedIn = {
                                ApiClient.setPreferOfflineMode(false)
                                navigateToMain()
                            },
                            onContinueWithoutSigningIn = {
                                ApiClient.setPreferOfflineMode(true)
                                goToStructureList()
                            },
                            onNavigateToRegister = {
                                navController.navigate("register")
                            },
                            onNavigateToForgotPassword = {
                                navController.navigate("forgot_password")
                            },
                            onNavigateToClaimInvite = {
                                navController.navigate("claim_invite")
                            }
                        )
                    }

                    // Worker invite claim: email + access code + new password, signs them
                    // straight in on success (same as verifying a sign-in code)
                    composable("claim_invite") {
                        ClaimInviteScreen(
                            onClaimed = {
                                ApiClient.setPreferOfflineMode(false)
                                navController.navigate(getStartDestination()) {
                                    popUpTo("login") { inclusive = true }
                                    launchSingleTop = true
                                }
                            },
                            onNavigateToLogin = {
                                navController.popBackStack()
                            }
                        )
                    }

                    // Operator sign-up. Submits to the API which emails a verification
                    // code and holds the sign-up until it's confirmed on the next screen
                    composable("register") {
                        RegisterScreen(
                            onCodeSent = { email, token ->
                                registrationEmail = email
                                registrationToken = token
                                navController.navigate("register_verify")
                            },
                            onNavigateToLogin = {
                                navController.popBackStack()
                            }
                        )
                    }

                    // Operator sign-up. Confirms the emailed code which is what
                    // actually creates the account as PendingApproval
                    composable("register_verify") {
                        RegisterVerifyScreen(
                            email = registrationEmail,
                            verificationToken = registrationToken,
                            onVerified = {
                                navController.navigate("register_pending/$registrationEmail") {
                                    popUpTo("register") { inclusive = true }
                                }
                            },
                            onCancel = {
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            }
                        )
                    }

                    composable(
                        route = "register_pending/{email}",
                        arguments = listOf(navArgument("email") { type = NavType.StringType })
                    ) { backStackEntry ->
                        RegisterPendingScreen(
                            email = backStackEntry.arguments?.getString("email") ?: "",
                            onNavigateToLogin = {
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            }
                        )
                    }

                    // Forgot-password flow, email to otp to new password
                    composable("forgot_password") {
                        ForgotPasswordScreen(
                            onCodeSent = { email ->
                                navController.navigate("reset_password/$email")
                            },
                            onNavigateToLogin = {
                                navController.popBackStack()
                            }
                        )
                    }

                    composable(
                        route = "reset_password/{email}",
                        arguments = listOf(navArgument("email") { type = NavType.StringType })
                    ) { backStackEntry ->
                        ResetPasswordScreen(
                            email = backStackEntry.arguments?.getString("email") ?: "",
                            onResetComplete = {
                                // Password was reset, sends them back to sign in
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            },
                            onNavigateToLogin = {
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            }
                        )
                    }

                    composable("home") {
                        if (isSimpleMode) {
                            SimpleModeScreen(
                                onOpenSettings = openSettings,
                                onPairWithBox = { id -> navController.navigate("box_pairing/$id") }
                            )
                        } else {
                            StructureScreen(
                                onSelectStructure = { id ->
                                    activeStructureId = id
                                    hasChosenStructure = true
                                    navController.navigate("readings/$id")
                                },
                                onEditStructure = { id ->
                                    navController.navigate("edit_structure/$id")
                                },
                                onNavigateToAddStructure = {
                                    navController.navigate("add_structure")
                                },
                                onManageTeam = {
                                    navController.navigate("team")
                                },
                                onOpenThresholds = {
                                    navController.navigate("thresholds")
                                },
                                onSignIn = {
                                    ApiClient.setPreferOfflineMode(false)
                                    navController.navigate("login")
                                },
                                onLogout = {
                                    ApiClient.clearAuthToken()
                                    ApiClient.setPreferOfflineMode(false)
                                    hasChosenStructure = false
                                    activeStructureId = ""
                                    // Drops the whole back stack, so back from login doesn't return
                                    // to a structure list the app no longer has permission to show
                                    navController.navigate("login") {
                                        popUpTo(0) { inclusive = true }
                                    }
                                }
                            )
                        }
                    }

                    composable(
                        route = "readings/{structureId}",
                        arguments = listOf(navArgument("structureId") { type = NavType.StringType })
                    ) { backStackEntry ->
                        val structureId = backStackEntry.arguments?.getString("structureId") ?: activeStructureId
                        HomeScreen(
                            activeStructureId = structureId,
                            onNavigateToSensorDetail = { sensorStructureId, sensorId ->
                                navController.navigate("sensor_detail/$sensorStructureId/$sensorId")
                            },
                            onSwitchStructure = { id ->
                                activeStructureId = id
                                // Replaces the current readings screen rather than stacking on top of it,
                                // so switching structures repeatedly doesn't grow the back stack
                                navController.navigate("readings/$id") {
                                    popUpTo("home") { saveState = true }
                                    launchSingleTop = true
                                }
                            },
                            onPairWithBox = { id ->
                                navController.navigate("box_pairing/$id")
                            },
                            onOpenGallery = {
                                navController.navigate("gallery") {
                                    popUpTo("home") { saveState = true }
                                    launchSingleTop = true
                                    restoreState = true
                                }
                            },
                            onNavigateBack = {
                                navController.popBackStack()
                            }
                        )
                    }

                    composable(
                        route = "box_pairing/{structureId}",
                        arguments = listOf(navArgument("structureId") { type = NavType.StringType })
                    ) { backStackEntry ->
                        BoxPairingScreen(
                            structureId = backStackEntry.arguments?.getString("structureId") ?: "",
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }

                    composable("team") {
                        TeamScreen(
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }

                    composable("thresholds") {
                        ThresholdsScreen(
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }

                    composable("settings") {
                        SettingsScreen(
                            onNavigateBack = { navController.popBackStack() },
                            onNavigateToLogin = {
                                ApiClient.setPreferOfflineMode(false)
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            }
                        )
                    }

                    composable("add_structure") {
                        AddStructureScreen(
                            onNavigateBack = { navController.popBackStack() },
                            onStructureAdded = { navController.popBackStack() }
                        )
                    }

                    // The same form as adding, filled in with the chosen structure
                    composable(
                        route = "edit_structure/{structureId}",
                        arguments = listOf(navArgument("structureId") { type = NavType.StringType })
                    ) { backStackEntry ->
                        AddStructureScreen(
                            onNavigateBack = { navController.popBackStack() },
                            onStructureAdded = { navController.popBackStack() },
                            structureId = backStackEntry.arguments?.getString("structureId")
                        )
                    }

                    composable("gallery") {
                        GalleryScreen()
                    }

                    composable("alerts") {
                        AlertsScreen(
                            onNavigateToSensorDetail = { structureId, sensorId ->
                                navController.navigate("sensor_detail/$structureId/$sensorId")
                            }
                        )
                    }

                    composable(
                        route = "sensor_detail/{structureId}/{sensorId}",
                        arguments = listOf(
                            navArgument("structureId") { type = NavType.StringType },
                            navArgument("sensorId") { type = NavType.StringType }
                        )
                    ) { backStackEntry ->
                        val structureId = backStackEntry.arguments?.getString("structureId") ?: ""
                        val sensorId = backStackEntry.arguments?.getString("sensorId") ?: "clean_water"
                        SensorDetailScreen(
                            structureId = structureId,
                            sensorId = sensorId,
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }

                    // --- ADMIN ROUTES ---
                    composable("admin_dashboard") {
                        AdminDashboardScreen(
                            onNavigateToApprovals = {
                                navController.navigate("admin_approvals") {
                                    popUpTo("admin_dashboard") { inclusive = false }
                                    launchSingleTop = true
                                }
                            },
                            onNavigateToSites = {
                                navController.navigate("admin_sites") {
                                    popUpTo("admin_dashboard") { inclusive = false }
                                    launchSingleTop = true
                                }
                            },
                            onLogout = {
                                ApiClient.clearAuthToken()
                                ApiClient.setPreferOfflineMode(false)
                                navController.navigate("login") {
                                    popUpTo(0) { inclusive = true }
                                }
                            }
                        )
                    }

                    composable("admin_approvals") {
                        AdminPendingApprovalsScreen(
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }

                    composable("admin_sites") {
                        AdminSitesScreen(
                            onNavigateBack = { navController.popBackStack() }
                        )
                    }
                }
            }
        }
    }
}