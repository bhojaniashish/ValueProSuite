using System.Text;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Auth;
using ValuPro.Application.Assignments;
using ValuPro.Application.Banks;
using ValuPro.Application.Properties;
using ValuPro.Infrastructure.Assignments;
using ValuPro.Infrastructure.Properties;
using ValuPro.Application.Clients;
using ValuPro.Infrastructure.Banks;
using ValuPro.Application.Settings;
using ValuPro.Infrastructure.Clients;
using ValuPro.Application.Users;
using ValuPro.Infrastructure.Settings;
using Microsoft.AspNetCore.DataProtection;
using ValuPro.Infrastructure.Users;
using ValuPro.Infrastructure.Auth;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Storage;
using ValuPro.Infrastructure.Tenancy;

namespace ValuPro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);

        services.Configure<TenancyOptions>(config.GetSection(TenancyOptions.Section));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // Database
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(config.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure())
            .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));

        // Identity (no cookies: the API issues JWTs)
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // JWT
        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.Section))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();
        services.AddSingleton<JwtTokenService>();
        services.AddMemoryCache();
        services.AddSingleton<UserAccessCache>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRolePermissionService, RolePermissionService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.Events = new JwtBearerEvents { OnTokenValidated = TokenValidation.OnTokenValidated };
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    NameClaimType = "name",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        // File storage: a local folder in development (Storage:LocalPath), Azure Blob Storage otherwise.
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        if (!string.IsNullOrWhiteSpace(config[$"{StorageOptions.Section}:{nameof(StorageOptions.LocalPath)}"]))
        {
            services.AddScoped<IFileStorage, LocalFileStorage>();
        }
        else
        {
            services.AddSingleton(sp =>
            {
                var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(o.ConnectionString)) return new BlobServiceClient(o.ConnectionString);
                if (!string.IsNullOrWhiteSpace(o.ServiceUri)) return new BlobServiceClient(new Uri(o.ServiceUri), new DefaultAzureCredential());
                throw new InvalidOperationException("Set Storage:LocalPath, Storage:ConnectionString or Storage:ServiceUri.");
            });
            services.AddScoped<IFileStorage, BlobFileStorage>();
        }

        // Settings and secrets
        services.AddDataProtection().SetApplicationName("ValuPro");
        services.AddSingleton<SecretProtector>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ISettingsService, SettingsService>();

        // Masters
        services.AddScoped<INumberSequence, NumberSequenceService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IBankService, BankService>();
        services.AddScoped<IAssignmentUsage, AssignmentUsage>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<Application.Rates.IRateService, Rates.RateService>();
        services.AddScoped<Application.Inspections.IInspectionService, Inspections.InspectionService>();
        services.AddScoped<Application.Photos.IPhotoService, Photos.PhotoService>();
        services.AddScoped<Application.Documents.IDocumentService, Documents.DocumentService>();
        services.AddScoped<Application.Valuations.IValuationService, Valuations.ValuationService>();
        services.AddScoped<Application.Signatures.ISignatureService, Signatures.SignatureService>();
        services.AddSingleton<Application.Reports.IReportRenderer, Reports.DefaultReportTemplate>();
        services.AddScoped<Application.Reports.IReportService, Reports.ReportService>();
        services.AddScoped<Application.Reports.IReportFormatService, Reports.ReportFormatService>();

        return services;
    }
}
