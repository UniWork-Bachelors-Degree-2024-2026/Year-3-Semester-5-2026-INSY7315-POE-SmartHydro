using Microsoft.Extensions.Logging;
using SmartHydroAPI.Patterns.Observer.Events;
using SmartHydroAPI.Patterns.Specification;
using SmartHydroAPI.Services;

namespace SmartHydroAPI.Patterns.Observer.Handlers
{
    // Observer event handler evaluating rule engine specifications when telemetry is ingested
    public class TelemetryAlertEvaluationHandler : IEventHandler<TelemetryIngestedEvent>
    {
        private readonly ITelemetryRuleEngine _ruleEngine;
        private readonly AlertService _alertService;
        private readonly ILogger<TelemetryAlertEvaluationHandler> _logger;

        // Injects rule engine, alert service, and logger
        public TelemetryAlertEvaluationHandler(
            ITelemetryRuleEngine ruleEngine,
            AlertService alertService,
            ILogger<TelemetryAlertEvaluationHandler> logger)
        {
            _ruleEngine = ruleEngine;
            _alertService = alertService;
            _logger = logger;
        }

        // Evaluates rules for the reading and persists any triggered alerts
        public async Task HandleAsync(TelemetryIngestedEvent domainEvent, CancellationToken ct = default)
        {
            try
            {
                var triggeredAlerts = await _ruleEngine.EvaluateRulesAsync(domainEvent.Reading, ct);

                foreach (var alertDto in triggeredAlerts)
                {
                    await _alertService.CreateAsync(alertDto, ct);
                    _logger.LogWarning("Auto-generated alert for device {DeviceMac}: {Component} - {Detail}",
                        alertDto.DeviceMac, alertDto.Component, alertDto.Detail);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate telemetry alerts for device {DeviceMac}", domainEvent.Reading.DeviceMac);
            }
        }
    }
}
