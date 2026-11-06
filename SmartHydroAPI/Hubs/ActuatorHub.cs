using Microsoft.AspNetCore.SignalR;
using SmartHydroAPI.Models.Dtos;
using SmartHydroAPI.Patterns.Decorator;
using SmartHydroAPI.Services;
using SmartHydroAPI.Data;
using Microsoft.AspNetCore.Authorization;

namespace SmartHydroAPI.Hubs
{
    // Carries controller reports and device-scoped dashboard updates.
    [Authorize]
    public class ActuatorHub : Hub
    {
        private readonly IActuatorService _actuatorService;
        private readonly ILogger<ActuatorHub> _logger;
        private readonly ApplicationDbContext _database;
        private readonly IHubContext<ActuatorHub> _hubContext;

        public ActuatorHub(IActuatorService actuatorService, ILogger<ActuatorHub> logger,
            ApplicationDbContext database, IHubContext<ActuatorHub> hubContext)
        {
            _actuatorService = actuatorService;
            _logger = logger;
            _database = database;
            _hubContext = hubContext;
        }

        public Task<bool> ReportCommandAck(LoraCompactTelemetryDto ack)
        {
            RequireDevice();
            return CommandAcknowledgements.AcceptAsync(_database, _actuatorService, _hubContext, ack, Context.ConnectionAborted);
        }

        private void RequireDevice()
        {
            if (Context.User?.HasClaim("device", "true") != true) throw new HubException("Device authentication required.");
        }

        // Only authenticated controllers may report relay state changes.
        public async Task ReportActuatorState(string macAddress, ActuatorCommandDto report)
        {
            RequireDevice();
            if (string.IsNullOrWhiteSpace(macAddress) || report == null) return;

            report.DeviceMac = macAddress.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(report.ChangedBy))
            {
                report.ChangedBy = "EdgeFirmware";
            }

            try
            {
                var updatedState = await _actuatorService.ApplyCommandAsync(report);

                // Only listeners authorised for this device receive its state.
                await Clients.Group(LoraDownlinkQueue.NormalizeMac(report.DeviceMac).Replace(":", "")).SendAsync("OnActuatorStateChanged", updatedState);


                _logger.LogInformation("[ActuatorHub] Edge State Reported from {Mac} by {Caller}", report.DeviceMac, report.ChangedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ActuatorHub] Error applying edge state report from {Mac}", report.DeviceMac);
            }
        }

        // Registers an edge device node or UI listener to a device-specific MAC address group
        public async Task RegisterDevice(string macAddress)
        {
            if (Context.User?.HasClaim("device", "true") != true &&
                !await DeviceAccess.CanReadDeviceAsync(_database, Context.User!, macAddress, Context.ConnectionAborted))
                throw new HubException("Device access denied.");
            if (!string.IsNullOrWhiteSpace(macAddress))
            {
                var groupName = macAddress.Trim().ToUpperInvariant();
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                var noColons = groupName.Replace(":", "");
                if (noColons != groupName)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, noColons);
                }
            }
        }

        // Subscribes a client to a structure-level group for multi-node monitoring
        public async Task JoinStructureGroup(int structureId)
        {
            if (Context.User?.HasClaim("device", "true") != true &&
                !await DeviceAccess.CanReadStructureAsync(_database, Context.User!, structureId, Context.ConnectionAborted))
                throw new HubException("Structure access denied.");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Structure_{structureId}");
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}
