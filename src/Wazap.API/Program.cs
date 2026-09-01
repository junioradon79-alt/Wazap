using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Wazap.API.Services;
using Wazap.API.Middleware;
using Wazap.Application.Abstractions;
using Wazap.Application.Services;
using Wazap.Application.Validators;
using Wazap.API.Health;
using Wazap.API.Components;
using Wazap.Application.Configuration;
using Wazap.Domain.Configuration;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Ajout des services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Collez votre token JWT ici (sans le préfixe « Bearer »)."
    });

});

// Configuration du DbContext (PostgreSQL)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Options géolocalisation
var geoOptions = builder.Configuration.GetSection(GeoOptions.SectionName).Get<GeoOptions>() ?? new GeoOptions();
builder.Services.AddSingleton(geoOptions);

// Options templates WhatsApp
var whatsAppOptions = builder.Configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
builder.Services.AddSingleton(whatsAppOptions);

// Géocodage d'adresses (Nominatim / OpenStreetMap)
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>();

// Authentification JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key manquante.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });

builder.Services.AddAuthorization();

// Validation FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("webhook", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
});

// Injection du service WhatsApp avec HttpClient
builder.Services.AddHttpClient<IWhatsAppSender, WhatChimpService>();

// Catalogue des packs prépayés (payé à l'usage, sans abonnement)
var packs = builder.Configuration.GetSection("Packs").Get<List<PackConfiguration>>() ?? new List<PackConfiguration>();
builder.Services.AddSingleton<IReadOnlyList<PackConfiguration>>(packs);

// Injection des services applicatifs
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<WhatsAppNotificationService>();
builder.Services.AddScoped<AuthService>();

// Services géolocalisation / matching / tableau de bord
builder.Services.AddScoped<RiderService>();
builder.Services.AddScoped<VendorService>();
builder.Services.AddScoped<DeliveryOfferService>();
builder.Services.AddScoped<DashboardService>();

// Auth : hashage de mot de passe + génération de JWT
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

// Utilisateur courant (autorisation par ressource)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Outbox durable : worker de fond pour l'envoi des notifications WhatsApp
builder.Services.AddHostedService<OutboxBackgroundWorker>();

// Seed d'un administrateur initial (si SeedAdmin:Username / SeedAdmin:Password sont configurés)
builder.Services.AddHostedService<AdminSeeder>();

// Données de démonstration (vendeurs + livreurs) + workers géolocalisation
builder.Services.AddHostedService<DemoDataSeeder>();
builder.Services.AddHostedService<DeliveryOfferWorker>();
builder.Services.AddHostedService<LocationPurgeWorker>();

// Gestion globale des erreurs (ProblemDetails + handler personnalisé)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Front Blazor Server (tableau de bord administrateur)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Gestion globale des erreurs (doit être le premier middleware)
app.UseExceptionHandler();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapHealthChecks("/health");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Les migrations sont appliquées hors démarrage (étape de déploiement dédiée) :
//   dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
app.Run();
