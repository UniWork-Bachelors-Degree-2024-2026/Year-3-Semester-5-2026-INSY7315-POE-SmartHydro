using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SmartHydroAPI.Data;
using SmartHydroAPI.Extensions;
using SmartHydroAPI.Hubs;
using SmartHydroAPI.Services;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = RequireSecret(
    builder.Configuration,
    "ConnectionStrings:DefaultConnection",
    "ConnectionStrings__DefaultConnection");

var jwtKey = RequireSecret(builder.Configuration, "Jwt:Key", "Jwt__Key");

RequireSecret(builder.Configuration, "Security:HmacPepper", "Security__HmacPepper");

RequireSecret(builder.Configuration, "Smtp:Password", "Smtp__Password");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "The JWT signing key supplied through the Jwt__Key environment variable must be at least 32 bytes (256 bits) long.");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context => BuildValidationResponse(context.ModelState);
    });

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context => AddProblemMessage(context.ProblemDetails);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Neon Tech Serverless PostgreSQL cold-start handling (auto-retries during compute wake-up)
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(60);
    }));

builder.Services.AddSmartHydroPatterns(builder.Configuration);
builder.Services.AddHostedService<TelemetryRollupService>();
HomeGatewayConnections.Configure(builder.Configuration);
builder.Services.AddScoped<HomeGatewayPackets>();
builder.Services.AddHostedService<HomeGatewayDispatch>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    allowedOrigins = new[]
    {
        "https://localhost:7043",
        "http://localhost:5043",
        "https://localhost:7154",
        "http://localhost:5154",
        "https://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net",
        "https://smarthydro-hyhxfuhcdne7cxhx.southafricanorth-01.azurewebsites.net",
        "https://smarthydro-api-f4hsbmbqh2g2etc6.southafricanorth-01.azurewebsites.net"
    };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<EmailRateLimiter>();
builder.Services.AddSingleton<SecretHasher>();
builder.Services.AddSingleton<AuthEmailQueue>();
builder.Services.AddHostedService<AuthEmailWorker>();
builder.Services.AddScoped<SiteInvitationService>();
builder.Services.AddScoped<InviteClaimService>();
builder.Services.AddScoped<AdminDirectoryService>();
builder.Services.AddScoped<AccountStatusGate>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs") && context.Request.Query.TryGetValue("access_token", out var token))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var gate = context.HttpContext.RequestServices.GetRequiredService<AccountStatusGate>();

                if (!await gate.IsActiveAsync(context.Principal, context.HttpContext.RequestAborted))
                    context.Fail("This account has been disabled or removed.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Your session has expired or you are not signed in. Please sign in again." });
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new { message = "You don't have permission to do this." });
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();

        if (policy is not null && policy.PolicyName == SiteInvitationService.RateLimitPolicy)
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)SiteInvitationService.InviteWindow.TotalSeconds).ToString();
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                message = "Invite quota reached. Please wait before inviting more workers."
            }, cancellationToken: cancellationToken);
            return;
        }

        var retryAfter = TimeSpan.FromMinutes(15);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter))
            retryAfter = leaseRetryAfter;

        var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        if (retryAfterSeconds < 1)
            retryAfterSeconds = 1;

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = $"Too many attempts. Please try again in {EmailRateLimiter.DescribeWait(retryAfter)}.",
            retryAfterSeconds
        }, cancellationToken: cancellationToken);
    };

    options.AddPolicy("LoginRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy("RegisterRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy("RegistrationCodeRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy("CodeVerifyRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    // Only a flood guard for checking sign-in codes. Wrong codes are limited per account by EmailRateLimiter, so people sharing one connection can still sign in
    options.AddPolicy("VerifyOtpRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy("ForgotPasswordRateLimit", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy(InviteClaimService.RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveClientKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    options.AddPolicy(SiteInvitationService.RateLimitPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: ResolveOperatorKey(httpContext),
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = SiteInvitationService.MaxInvitesPerWindow,
            Window = SiteInvitationService.InviteWindow,
            SegmentsPerWindow = 10,
            QueueLimit = 0
        }));
});

var app = builder.Build();

await EnsureSecuritySchemaAsync(app.Services);

// A client that closes the page, reloads, or times out cancels its own request; that is not a server fault, so end it quietly instead of raising an error
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        if (!context.Response.HasStarted)
            context.Response.StatusCode = 499;
    }
});

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("SmartHydro IoT API Documentation")
           .WithTheme(ScalarTheme.Moon)
           .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// Global Security Headers Middleware (sets nosniff, frame deny, XSS protection, and referrer policy)
app.Use(async (context, next) =>
{
    if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

    if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
        context.Response.Headers.Append("X-Frame-Options", "DENY");

    if (!context.Response.Headers.ContainsKey("X-XSS-Protection"))
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

    if (!context.Response.Headers.ContainsKey("Referrer-Policy"))
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    await next();
});

app.UseCors("AllowAll");

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.Use((context, next) => DeviceKeySecurity.InvokeAsync(context, app.Configuration, next));
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.MapHub<ActuatorHub>("/hubs/actuator", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<ActuatorHub>("/hubs/actuators", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<HomeGatewayHub>("/hubs/home-gateway", options => options.CloseOnAuthenticationExpiration = true);

app.MapGet("/health", async (ApplicationDbContext db) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var canConnect = await db.Database.CanConnectAsync();
    stopwatch.Stop();

    if (!canConnect)
    {
        return Results.Problem(
            title: "Database Unreachable",
            detail: "Connection to Neon Tech PostgreSQL could not be established.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "Healthy",
        database = "Connected",
        latencyMs = stopwatch.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow
    });
});

app.MapGet("/", () => Results.Redirect("/scalar/v1"));

app.Run();

// Reads a secret that must come from an environment variable (or user secrets in development) and stops startup with a clear message when it is missing
static string RequireSecret(IConfiguration configuration, string key, string environmentVariable)
{
    var value = configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"'{key}' is not configured. Set the {environmentVariable} environment variable before starting the API.");
    }

    return value;
}

// Returns the standard validation problem details with an added "message" field so web and Android clients can show one readable error
static IActionResult BuildValidationResponse(ModelStateDictionary modelState)
{
    var problem = new ValidationProblemDetails(modelState)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "One or more validation errors occurred."
    };

    string? firstError = null;

    if (modelState.Keys.Any(key => key.StartsWith('$')))
    {
        firstError = "The request body is not valid JSON.";
    }
    else
    {
        firstError = modelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
    }

    if (string.IsNullOrWhiteSpace(firstError))
        firstError = "The request is invalid.";

    problem.Extensions["message"] = firstError;
    return new BadRequestObjectResult(problem);
}

// Copies the problem's detail (or title) into a "message" field so every ProblemDetails error has the same readable field clients use
static void AddProblemMessage(ProblemDetails problem)
{
    if (problem.Extensions.ContainsKey("message"))
        return;

    var message = problem.Detail;

    if (string.IsNullOrWhiteSpace(message))
        message = problem.Title;

    if (string.IsNullOrWhiteSpace(message))
        message = "Something went wrong. Please try again.";

    problem.Extensions["message"] = message;
}

// Resolves the caller's IP for the IP-partitioned rate limit policies
string ResolveClientKey(HttpContext httpContext)
{
    return httpContext.ResolveClientIp(builder.Configuration);
}

// Partitions the invitation limit per authenticated Operator so each Operator gets their own quota regardless of device or IP
string ResolveOperatorKey(HttpContext httpContext)
{
    var operatorEmail = httpContext.User.GetEmail();

    if (!string.IsNullOrWhiteSpace(operatorEmail))
        return "operator:" + operatorEmail.Trim().ToLowerInvariant();

    return "ip:" + ResolveClientKey(httpContext);
}

// Brings an existing database up to the schema the authentication, approval and invitation flows rely on
async Task EnsureSecuritySchemaAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS sites (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            name varchar(60) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            is_deleted boolean NOT NULL DEFAULT false
        );

        CREATE INDEX IF NOT EXISTS idx_sites_name_deleted ON sites (name, is_deleted);

        ALTER TABLE structures
            ADD COLUMN IF NOT EXISTS site_id uuid REFERENCES sites(id) ON DELETE SET NULL;

        CREATE INDEX IF NOT EXISTS idx_structures_site_id ON structures (site_id);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS users (
            id serial PRIMARY KEY,
            email varchar(255) NOT NULL UNIQUE,
            full_name varchar(100),
            role varchar(20) NOT NULL DEFAULT 'Operator',
            password_hash varchar(255),
            farm_name varchar(60),
            status varchar(20) NOT NULL DEFAULT 'Approved',
            is_verified boolean NOT NULL DEFAULT false,
            created_at timestamptz NOT NULL DEFAULT now(),
            last_login_at timestamptz
        );

        ALTER TABLE users ADD COLUMN IF NOT EXISTS role varchar(20) NOT NULL DEFAULT 'Operator';
        ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash varchar(255);
        ALTER TABLE users ADD COLUMN IF NOT EXISTS status varchar(20) NOT NULL DEFAULT 'Approved';
        ALTER TABLE users ADD COLUMN IF NOT EXISTS farm_name varchar(60);

        ALTER TABLE users ADD COLUMN IF NOT EXISTS full_name varchar(100);
        ALTER TABLE users ADD COLUMN IF NOT EXISTS is_verified boolean NOT NULL DEFAULT false;

        UPDATE users SET status = 'PendingApproval' WHERE status = 'Pending';
        UPDATE users SET is_verified = true WHERE status = 'Approved' AND is_verified = false;

        ALTER TABLE sites ADD COLUMN IF NOT EXISTS owner_email varchar(255);
        ALTER TABLE sites ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
        ALTER TABLE sites ADD COLUMN IF NOT EXISTS deleted_by_email varchar(255);
        CREATE INDEX IF NOT EXISTS idx_sites_is_deleted_deleted_at ON sites (is_deleted, deleted_at);
        CREATE INDEX IF NOT EXISTS idx_sites_owner_email ON sites (owner_email);

        ALTER TABLE structures ADD COLUMN IF NOT EXISTS owner_email varchar(255);

        UPDATE sites s
        SET owner_email = owners.owner_email
        FROM (
            SELECT site_id, MIN(owner_email) AS owner_email
            FROM structures
            WHERE site_id IS NOT NULL AND owner_email IS NOT NULL AND owner_email <> ''
            GROUP BY site_id
            HAVING COUNT(DISTINCT owner_email) = 1
        ) owners
        WHERE s.id = owners.site_id
          AND (s.owner_email IS NULL OR s.owner_email = '');
        ALTER TABLE structures ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false;
        ALTER TABLE structures ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
        ALTER TABLE structures ADD COLUMN IF NOT EXISTS deleted_by_email varchar(255);

        ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false;
        ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
        ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS deleted_by_email varchar(255);

        ALTER TABLE alerts ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false;
        ALTER TABLE alerts ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
        ALTER TABLE alerts ADD COLUMN IF NOT EXISTS deleted_by_email varchar(255);

        CREATE TABLE IF NOT EXISTS site_invites (
            id bigserial PRIMARY KEY,
            site_id uuid NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
            email varchar(255) NOT NULL,
            token_hash varchar(512) NOT NULL,
            code_hash varchar(512) NOT NULL,
            expires_at timestamptz NOT NULL,
            attempts integer NOT NULL DEFAULT 0,
            created_by_email varchar(255) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS uq_site_invites_site_email ON site_invites (site_id, email);
        CREATE INDEX IF NOT EXISTS idx_site_invites_expires_at ON site_invites (expires_at);
        CREATE INDEX IF NOT EXISTS idx_site_invites_email ON site_invites (email);

        CREATE TABLE IF NOT EXISTS site_users (
            site_id uuid NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
            user_id integer NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            created_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (site_id, user_id)
        );
        CREATE INDEX IF NOT EXISTS idx_site_users_user_id ON site_users (user_id);

        CREATE TABLE IF NOT EXISTS password_reset_codes (
            id bigserial PRIMARY KEY,
            email varchar(255) NOT NULL,
            code_hash varchar(255) NOT NULL,
            expires_at timestamptz NOT NULL,
            is_used boolean NOT NULL DEFAULT false,
            attempts integer NOT NULL DEFAULT 0,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_password_reset_codes_email ON password_reset_codes (email);
        ALTER TABLE password_reset_codes ADD COLUMN IF NOT EXISTS is_verified boolean NOT NULL DEFAULT false;
        ALTER TABLE password_reset_codes ADD COLUMN IF NOT EXISTS reset_token_hash varchar(255);
        ALTER TABLE password_reset_codes ADD COLUMN IF NOT EXISTS reset_token_expires_at timestamptz;

        CREATE TABLE IF NOT EXISTS pending_registrations (
            id bigserial PRIMARY KEY,
            email varchar(255) NOT NULL,
            password_hash varchar(255) NOT NULL,
            full_name varchar(100) NOT NULL DEFAULT '',
            farm_name varchar(60) NOT NULL,
            verification_token_hash varchar(255) NOT NULL,
            code_hash varchar(255) NOT NULL,
            expires_at timestamptz NOT NULL,
            attempts integer NOT NULL DEFAULT 0,
            send_count integer NOT NULL DEFAULT 0,
            last_sent_at timestamptz,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        ALTER TABLE pending_registrations ADD COLUMN IF NOT EXISTS full_name varchar(100) NOT NULL DEFAULT '';
        CREATE INDEX IF NOT EXISTS idx_pending_registrations_email ON pending_registrations (email);
        CREATE INDEX IF NOT EXISTS idx_pending_registrations_expires_at ON pending_registrations (expires_at);

        CREATE TABLE IF NOT EXISTS email_rate_limits (
            limit_key varchar(512) PRIMARY KEY,
            hits integer NOT NULL DEFAULT 0,
            window_start timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_email_rate_limits_window_start ON email_rate_limits (window_start);

        CREATE TABLE IF NOT EXISTS password_history (
            id bigserial PRIMARY KEY,
            email varchar(255) NOT NULL,
            password_hash varchar(255) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_password_history_email_created ON password_history (email, created_at);

        CREATE TABLE IF NOT EXISTS login_attempts (
            email varchar(255) NOT NULL,
            client_ip varchar(64) NOT NULL DEFAULT '',
            failed_attempts integer NOT NULL DEFAULT 0,
            last_failed_at timestamptz,
            locked_until timestamptz,
            CONSTRAINT login_attempts_pkey PRIMARY KEY (email, client_ip)
        );

        CREATE TABLE IF NOT EXISTS otp_codes (
            id bigserial PRIMARY KEY,
            email varchar(255) NOT NULL,
            code_hash varchar(255) NOT NULL,
            challenge_hash varchar(255) NOT NULL DEFAULT '',
            expires_at timestamptz NOT NULL,
            is_used boolean NOT NULL DEFAULT false,
            attempts integer NOT NULL DEFAULT 0,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        ALTER TABLE otp_codes ADD COLUMN IF NOT EXISTS challenge_hash varchar(255) NOT NULL DEFAULT '';
        CREATE INDEX IF NOT EXISTS idx_otp_codes_email_created ON otp_codes (email, created_at);

        ALTER TABLE login_attempts ADD COLUMN IF NOT EXISTS client_ip varchar(64) NOT NULL DEFAULT '';

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM information_schema.key_column_usage
                WHERE table_schema = current_schema()
                  AND table_name = 'login_attempts'
                  AND constraint_name = 'login_attempts_pkey'
                  AND column_name = 'client_ip') THEN
                ALTER TABLE login_attempts DROP CONSTRAINT IF EXISTS login_attempts_pkey;
                ALTER TABLE login_attempts ADD CONSTRAINT login_attempts_pkey PRIMARY KEY (email, client_ip);
            END IF;

            IF EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'email_rate_limits'
                  AND column_name = 'limit_key'
                  AND character_maximum_length < 512) THEN
                ALTER TABLE email_rate_limits ALTER COLUMN limit_key TYPE varchar(512);
            END IF;
        END $$;
        """);

    // Older databases stored the EC reading under a different column name; rename it (keeping the data) or add the column so telemetry queries work
    await db.Database.ExecuteSqlRawAsync("""
        DO $$
        DECLARE
            old_name text;
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'telemetry_realtime')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'telemetry_realtime' AND column_name = 'ec_ms') THEN

                SELECT column_name INTO old_name
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'telemetry_realtime'
                  AND column_name IN ('ec_level', 'ec_value', 'ec', 'ec_ms_cm', 'ec_reading', 'conductivity')
                ORDER BY array_position(ARRAY['ec_level', 'ec_value', 'ec', 'ec_ms_cm', 'ec_reading', 'conductivity'], column_name::text)
                LIMIT 1;

                IF old_name IS NOT NULL THEN
                    EXECUTE format('ALTER TABLE telemetry_realtime RENAME COLUMN %I TO ec_ms', old_name);
                ELSE
                    ALTER TABLE telemetry_realtime ADD COLUMN ec_ms numeric(4,2) NOT NULL DEFAULT 0;
                END IF;
            END IF;
        END $$;
        """);

    // Same repair for the daily history table: rename an older average-EC column, then add any other mapped column that is missing so queries stop failing on absent columns
    await db.Database.ExecuteSqlRawAsync("""
        DO $$
        DECLARE
            old_name text;
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'telemetry_daily_history') THEN

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'telemetry_daily_history' AND column_name = 'avg_ec') THEN
                    SELECT column_name INTO old_name
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'telemetry_daily_history'
                      AND column_name IN ('avg_ec_ms', 'avg_ec_level', 'avg_ec_value', 'ec_avg', 'average_ec', 'avg_conductivity')
                    ORDER BY array_position(ARRAY['avg_ec_ms', 'avg_ec_level', 'avg_ec_value', 'ec_avg', 'average_ec', 'avg_conductivity'], column_name::text)
                    LIMIT 1;

                    IF old_name IS NOT NULL THEN
                        EXECUTE format('ALTER TABLE telemetry_daily_history RENAME COLUMN %I TO avg_ec', old_name);
                    END IF;
                END IF;

                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS avg_ph numeric(4,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS avg_ec numeric(4,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS avg_light numeric(7,1) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS avg_temperature_c numeric(5,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS avg_humidity_percent numeric(5,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS total_samples integer NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS calculated_at timestamptz NOT NULL DEFAULT now();
            END IF;

            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'telemetry_realtime') THEN
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS ph_level numeric(4,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS light_lux numeric(7,1) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS water_level_raw integer NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS water_level_detected boolean NOT NULL DEFAULT false;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS temperature_c numeric(5,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS humidity_percent numeric(5,2) NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS ml_ph_prediction integer NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS ml_ec_prediction integer NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS ml_temp_prediction integer NOT NULL DEFAULT 0;
                ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS ml_humidity_prediction integer NOT NULL DEFAULT 0;
            END IF;
        END $$;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS sample_sequence bigint;
        ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS received_at timestamptz;
        ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS capture_time_uncertain boolean NOT NULL DEFAULT false;
        ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS relay_bitmask integer;
        ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS operating_mode integer;
        CREATE UNIQUE INDEX IF NOT EXISTS idx_telemetry_device_sequence ON telemetry_realtime(device_mac, sample_sequence);
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS mode_request_confirmed_at timestamptz;
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_mode integer;
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_preferred_mode integer;
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_mode_reason integer;
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS mode_reported_at timestamptz;
        ALTER TABLE devices ADD COLUMN IF NOT EXISTS last_telemetry_transport text;
        CREATE TABLE IF NOT EXISTS activity_logs (
            id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            device_mac text NOT NULL, event_key text NOT NULL, source text NOT NULL,
            action text NOT NULL, status text NOT NULL, details text NOT NULL,
            trigger_value numeric, minimum numeric, maximum numeric, estimated_ml numeric,
            runtime_ms integer, command_id integer, recorded_at timestamptz,
            received_at timestamptz NOT NULL DEFAULT now(), capture_time_uncertain boolean NOT NULL DEFAULT false
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_activity_event ON activity_logs(device_mac, event_key);
        CREATE TABLE IF NOT EXISTS telemetry_receipts (
            device_mac varchar(17) NOT NULL,
            sample_sequence bigint NOT NULL,
            received_at timestamptz NOT NULL,
            PRIMARY KEY(device_mac, sample_sequence)
        );
        CREATE INDEX IF NOT EXISTS idx_telemetry_receipt_time ON telemetry_receipts(received_at);
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_ph numeric;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_ph integer;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_ec numeric;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_ec integer;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_light numeric;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_light integer;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_temperature numeric;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_temperature integer;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_humidity numeric;
        ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_humidity integer;

        """);

    // Tables the dosing queue and the safe-range thresholds rely on; created here when an older database does not have them yet
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS device_commands (
            id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            command_id integer NOT NULL DEFAULT 0,
            request_id uuid NOT NULL UNIQUE,
            device_mac varchar(17) NOT NULL,
            system_type varchar(20) NOT NULL,
            target varchar(100) NOT NULL,
            command_text text NOT NULL,
            payload text NOT NULL,
            status varchar(20) NOT NULL DEFAULT 'QUEUED',
            source varchar(100) NOT NULL,
            retry_count integer NOT NULL DEFAULT 0,
            created_at timestamptz NOT NULL,
            sent_at timestamptz,
            acked_at timestamptz,
            expires_at timestamptz NOT NULL,
            relay_bitmask integer,
            succeeded boolean,
            reason varchar(100),
            dosing_command_id uuid
        );
        CREATE INDEX IF NOT EXISTS idx_device_commands_delivery ON device_commands (device_mac, status, id);
        ALTER TABLE device_commands ADD COLUMN IF NOT EXISTS request_id uuid NOT NULL DEFAULT gen_random_uuid();
        CREATE UNIQUE INDEX IF NOT EXISTS idx_device_commands_request ON device_commands (request_id);

        CREATE TABLE IF NOT EXISTS dosing_commands (
            id uuid PRIMARY KEY,
            device_mac varchar(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
            dosing_type varchar(30) NOT NULL,
            target_volume_ml numeric(6,2) NOT NULL DEFAULT 0,
            calculated_duration_ms integer NOT NULL DEFAULT 0,
            status varchar(20) NOT NULL DEFAULT 'PENDING',
            created_at timestamptz NOT NULL DEFAULT now(),
            executed_at timestamptz,
            issued_by varchar(100)
        );
        CREATE INDEX IF NOT EXISTS idx_dosing_commands_mac_status ON dosing_commands (device_mac, status);

        CREATE TABLE IF NOT EXISTS thresholds (
            id serial PRIMARY KEY,
            structure_type varchar(20) NOT NULL,
            sensor_type varchar(30) NOT NULL,
            min_value numeric(7,2) NOT NULL DEFAULT 0,
            max_value numeric(7,2) NOT NULL DEFAULT 0
        );
        """);

    // Safe ranges now belong to one structure (and so to one site) instead of a structure type or an owner; a reading without a row uses the healthy default in code.
    // Ranges an Operator saved under the old per-owner rule are copied onto the structures they own, then the old rows are removed. Once they are gone this does nothing
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE thresholds ADD COLUMN IF NOT EXISTS owner_email varchar(255);
        ALTER TABLE thresholds ADD COLUMN IF NOT EXISTS structure_id integer REFERENCES structures(id) ON DELETE CASCADE;
        ALTER TABLE thresholds DROP CONSTRAINT IF EXISTS uq_threshold_type_sensor;
        DROP INDEX IF EXISTS uq_threshold_type_sensor;
        DROP INDEX IF EXISTS uq_threshold_type_sensor_owner;
        DROP INDEX IF EXISTS idx_thresholds_type_sensor_owner;
        CREATE UNIQUE INDEX IF NOT EXISTS uq_threshold_structure_sensor
            ON thresholds (structure_id, sensor_type);

        INSERT INTO thresholds (structure_type, sensor_type, min_value, max_value, structure_id)
        SELECT t.structure_type, t.sensor_type, t.min_value, t.max_value, s.id
        FROM thresholds t
        JOIN structures s ON s.structure_type = t.structure_type
        LEFT JOIN sites si ON si.id = s.site_id
        WHERE t.structure_id IS NULL
          AND t.owner_email IS NOT NULL
          AND lower(COALESCE(NULLIF(s.owner_email, ''), si.owner_email)) = lower(t.owner_email)
        ON CONFLICT DO NOTHING;

        DELETE FROM thresholds WHERE structure_id IS NULL;
        ALTER TABLE thresholds ALTER COLUMN structure_id SET NOT NULL;

        -- The person who cleared an alert is shown by email, which can be longer than the old 50 characters
        ALTER TABLE alerts ALTER COLUMN cleared_by TYPE varchar(255);

        -- Ensure telemetry_realtime does not reject ingests with missing optional tds_ppm
        -- ALTER TABLE telemetry_realtime ALTER COLUMN tds_ppm DROP NOT NULL;
        -- ALTER TABLE telemetry_realtime ALTER COLUMN tds_ppm SET DEFAULT 0;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        DELETE FROM thresholds
        WHERE structure_id IN (
            SELECT id FROM structures
            WHERE site_id IN ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222')
              AND is_deleted = false);

        UPDATE structures
        SET is_deleted = true, deleted_at = now(), deleted_by_email = 'system'
        WHERE site_id IN ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222')
          AND is_deleted = false;

        UPDATE sites
        SET is_deleted = true
        WHERE id IN ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222')
          AND is_deleted = false;
        """);
}
