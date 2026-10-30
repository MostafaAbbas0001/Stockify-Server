using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration));

// ---------- Database ----------
var connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is missing. Set ConnectionStrings__DefaultConnection in .env or the environment.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// ---------- Services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<DocumentNumberService>();
builder.Services.AddScoped<ReportService>();

// Identity & platform
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlatformAuthService>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<TenantStatusService>();

// Catalog
builder.Services.AddScoped<BrandService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ProductAttributeService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<ProductVariantService>();
builder.Services.AddScoped<ProductVariantSkuService>();

// Sales
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<PosService>();
builder.Services.AddScoped<SaleService>();

// Inventory
builder.Services.AddScoped<InventoryLotService>();
builder.Services.AddScoped<InventoryAdjustmentService>();
builder.Services.AddScoped<StockTransferService>();
builder.Services.AddScoped<StockMovementService>();

// People
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<UserService>();

// Finance
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<DashboardService>();

// Settings
builder.Services.AddScoped<BranchService>();
builder.Services.AddScoped<DeliveryLocationService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<CurrencyService>();

// ---------- Authentication (JWT) ----------
builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JwtSettings is missing. Set JwtSettings__Key in .env or the environment.");
if (string.IsNullOrEmpty(jwt.Key) || Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException("JwtSettings:Key must be at least 32 bytes long.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

// Every endpoint requires a signed-in company user unless it opts out with [AllowAnonymous]
// or names another policy (platform endpoints use AuthPolicies.Platform).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(StockifyClaimTypes.TenantId)
        .Build();

    options.AddPolicy(AuthPolicies.Platform, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(StockifyClaimTypes.Scope, StockifyClaimTypes.PlatformScope));
});

// Brute-force protection for the sign-in endpoints (attempts per IP per minute).
var signInPerMinute = config.GetValue("RateLimiting:SignInPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthPolicies.SignInRateLimit, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = signInPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- Controllers & Swagger ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Stockify API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT bearer token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ---------- Pipeline ----------
// Client IP and scheme from the reverse proxy.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Swagger is served before authentication: the fallback policy would otherwise require a
// signed-in user just to open the docs. Development only.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Stockify API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseRouting();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantStatusMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

await PlatformAdminSeeder.EnsureBootstrapAdminAsync(app.Services, app.Configuration);

app.Lifetime.ApplicationStarted.Register(() =>
    Log.ForContext("SourceContext", "ServerLifecycle").Information("Stockify server started"));

app.Run();
