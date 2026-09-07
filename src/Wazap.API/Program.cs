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

// Logs structurés JSON en production (une ligne JSON par événement, collectable tel quel) ;
// en développement on garde la console lisible.
if (builder.Environment.IsProduction())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        options.UseUtcTimestamp = true;
        options.IncludeScopes = false;
    });
}

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

// Options onboarding vendeur séquencé (J+1/J+3/J+7, worker + templates Meta)
var vendorOnboardingOptions = builder.Configuration.GetSection(VendorOnboardingOptions.SectionName).Get<VendorOnboardingOptions>() ?? new VendorOnboardingOptions();
builder.Services.AddSingleton(vendorOnboardingOptions);

// Options parcours acheteur (lien de suivi PWA)
var clientOptions = builder.Configuration.GetSection(ClientOptions.SectionName).Get<ClientOptions>() ?? new ClientOptions();
builder.Services.AddSingleton(clientOptions);

// Options templates WhatsApp
var whatsAppOptions = builder.Configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
builder.Services.AddSingleton(whatsAppOptions);

// Options agrégateur de paiement GeniusPay
var geniusPayOptions = builder.Configuration.GetSection(GeniusPayOptions.SectionName).Get<GeniusPayOptions>() ?? new GeniusPayOptions();
builder.Services.AddSingleton(geniusPayOptions);

// Options numérotation ivoirienne (conversion 8 → 10 chiffres, table officielle ARTCI à venir)
var ciNumberingOptions = builder.Configuration.GetSection(IvoryCoastNumberingOptions.SectionName).Get<IvoryCoastNumberingOptions>() ?? new IvoryCoastNumberingOptions();
builder.Services.AddSingleton(ciNumberingOptions);

// Options sécurité des coursiers (certification « Garantie Colis Sûr »)
var riderSecurityOptions = builder.Configuration.GetSection(RiderSecurityOptions.SectionName).Get<RiderSecurityOptions>() ?? new RiderSecurityOptions();
builder.Services.AddSingleton(riderSecurityOptions);

// Options réputation livreur (notes clients après livraison)
var riderReputationOptions = builder.Configuration.GetSection(RiderReputationOptions.SectionName).Get<RiderReputationOptions>() ?? new RiderReputationOptions();
builder.Services.AddSingleton(riderReputationOptions);

// Options preuve de livraison (code client à 4 chiffres restitué par le livreur)
var deliveryProofOptions = builder.Configuration.GetSection(DeliveryProofOptions.SectionName).Get<DeliveryProofOptions>() ?? new DeliveryProofOptions();
builder.Services.AddSingleton(deliveryProofOptions);

// Options monitoring / alertes (webhook optionnel + service d'alerte)
var monitoringOptions = builder.Configuration.GetSection(MonitoringOptions.SectionName).Get<MonitoringOptions>() ?? new MonitoringOptions();
builder.Services.AddSingleton(monitoringOptions);
builder.Services.AddScoped<MonitoringAlertService>();
builder.Services.AddScoped<HealthDetailsService>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<ProspectAutoService>();
builder.Services.AddScoped<LeadConversionService>();
builder.Services.AddScoped<ColisSurService>();
builder.Services.AddScoped<RiderRatingService>();

// Options rétention / archivage des données (purge opt-in, désactivée par défaut)
var retentionOptions = builder.Configuration.GetSection(RetentionOptions.SectionName).Get<RetentionOptions>() ?? new RetentionOptions();
builder.Services.AddSingleton(retentionOptions);

// Options API publique v1 (clés partenaires + rate limit)
var publicApiOptions = builder.Configuration.GetSection(PublicApiOptions.SectionName).Get<PublicApiOptions>() ?? new PublicApiOptions();
builder.Services.AddSingleton(publicApiOptions);
builder.Services.AddScoped<PublicApiService>();

// Options page de vente / acquisition de leads (numéro WhatsApp du CTA)
var salesPageOptions = builder.Configuration.GetSection(SalesPageOptions.SectionName).Get<SalesPageOptions>() ?? new SalesPageOptions();
builder.Services.AddSingleton(salesPageOptions);

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
    options.AddFixedWindowLimiter("client", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("publicapi", o =>
    {
        o.PermitLimit = Math.Max(1, publicApiOptions.RateLimitPerMinute);
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("leads", o =>
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
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddHostedService<VendorOnboardingWorker>();

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

// API publique v1 : protégée par clé (X-Api-Key) — montée uniquement sur /api/v1.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/v1"),
    branch => branch.UseMiddleware<PublicApiKeyMiddleware>());

app.MapControllers();
app.MapHealthChecks("/health");

// Métriques de supervision détaillées (DB, file outbox, workers) — pour uptime monitors et dashboards.
app.MapGet("/health/details", async (HealthDetailsService service, CancellationToken ct)
    => await service.BuildAsync(ct));

// Métriques Prometheus au format texte (0.0.4) — pour Prometheus/Grafana.
app.MapGet("/metrics", async (MetricsService service, CancellationToken ct)
    => Results.Text(await service.BuildTextAsync(ct), "text/plain; version=0.0.4"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SPA React (web/) servie depuis /app — fallback pour le routing client
app.MapFallbackToFile("app/{*path:nonfile}", "app/index.html");

// Liens courts marketing (évitent le long préfixe /app) :
//   /vente       → page de vente          ·   /parrainage → page parrainage
app.MapGet("/vente", () => Results.Redirect("/app/vente"));
app.MapGet("/parrainage", () => Results.Redirect("/app/parrainage"));

// Les migrations sont appliquées hors démarrage (étape de déploiement dédiée) :
//   dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
app.Run();
