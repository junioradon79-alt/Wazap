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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
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

// Les services d'application dépendent du port IApplicationDbContext (implémentation réelle : ApplicationDbContext).
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

// Options géolocalisation
var geoOptions = builder.Configuration.GetSection(GeoOptions.SectionName).Get<GeoOptions>() ?? new GeoOptions();
builder.Services.AddSingleton(geoOptions);

// Options groupage des livraisons
var groupingOptions = builder.Configuration.GetSection(GroupingOptions.SectionName).Get<GroupingOptions>() ?? new GroupingOptions();
builder.Services.AddSingleton(groupingOptions);

// Options sécurité des comptes (verrouillage anti force-brute)
var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.AddSingleton(securityOptions);

// Options offre de découverte (crédits offerts aux nouveaux vendeurs)
var trialOptions = builder.Configuration.GetSection(TrialOptions.SectionName).Get<TrialOptions>() ?? new TrialOptions();
builder.Services.AddSingleton(trialOptions);

// Options parcours acheteur (lien de suivi PWA)
var clientOptions = builder.Configuration.GetSection(ClientOptions.SectionName).Get<ClientOptions>() ?? new ClientOptions();
builder.Services.AddSingleton(clientOptions);

// Options templates WhatsApp
var whatsAppOptions = builder.Configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
builder.Services.AddSingleton(whatsAppOptions);

// Options agrégateur de paiement GeniusPay
var geniusPayOptions = builder.Configuration.GetSection(GeniusPayOptions.SectionName).Get<GeniusPayOptions>() ?? new GeniusPayOptions();
builder.Services.AddSingleton(geniusPayOptions);

// Géocodage d'adresses (Nominatim / OpenStreetMap)
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>();

// HttpClient par défaut (pages Blazor qui appellent l'API)
builder.Services.AddHttpClient();

// Authentification : cookie pour le Blazor UI (admin), JWT pour l'API
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "wazap.admin";
        options.Cookie.HttpOnly = true;
    })
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

        // 401/403 de l'API en ProblemDetails JSON (et non un corps vide).
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                return context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Non autorisé",
                    Detail = "Authentification requise."
                });
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                return context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Accès refusé",
                    Detail = "Vous n'avez pas les droits nécessaires pour cette ressource."
                });
            }
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
builder.Services.AddScoped<WhatsAppOrchestrationService>();
builder.Services.AddScoped<AuthService>();

// Services géolocalisation / matching / tableau de bord
builder.Services.AddScoped<RiderService>();
builder.Services.AddScoped<VendorService>();
builder.Services.AddScoped<DeliveryOfferService>();
builder.Services.AddScoped<DashboardService>();

// Packs prépayés : catalogue + achat
builder.Services.AddScoped<PackService>();

// Paiement des packs : GeniusPay si activé, sinon mock (dev/test)
if (geniusPayOptions.Enabled)
    builder.Services.AddHttpClient<IPaymentService, GeniusPayPaymentService>();
else
    builder.Services.AddScoped<IPaymentService, MockPaymentService>();

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
builder.Services.AddHostedService<PaymentReconciliationWorker>();

// Gestion globale des erreurs (ProblemDetails + handler personnalisé)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// CORS pour le frontend React/Vite (dev : localhost:5173 ; prod : même domaine → sans objet, mais toléré)
var corsOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
    options.AddPolicy("WebFrontend", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

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
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseCors("WebFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapHealthChecks("/health");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SPA React (web/) servie depuis /app — fallback pour le routing client
app.MapFallbackToFile("app/{*path:nonfile}", "app/index.html");

// Les migrations sont appliquées hors démarrage (étape de déploiement dédiée) :
//   dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
app.Run();
