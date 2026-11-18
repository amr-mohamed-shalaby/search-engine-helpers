
using DepartmentManagement.Api.Authentication;
using DepartmentManagement.Contracts;
using DepartmentManagement.Infrastructure.DependencyInjection;
using DepartmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

var jwtSettings = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
    string.IsNullOrWhiteSpace(jwtSettings.Audience) ||
    string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
{
    throw new InvalidOperationException("JWT configuration is incomplete.");
}

byte[] signingKey;

try
{
    signingKey = Convert.FromBase64String(jwtSettings.SigningKey);
}
catch (FormatException ex)
{
    throw new InvalidOperationException(
        "JWT SigningKey must be Base64 encoded.", ex);
}

if (signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT SigningKey must contain at least 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthenticationService>();

var app = builder.Build();

// Explicit one-time setup command. Apply the reviewed EF migration first.
if (args.Contains("--provision-identity", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await IdentityDataSeeder.ProvisionAsync(scope.ServiceProvider);
    return;
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .WithName("HealthCheck")
    .WithTags("System");

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

await app.RunAsync();

public partial class Program { }
