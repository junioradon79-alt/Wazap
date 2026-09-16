using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Wazap.API;
using Wazap.API.Configuration;
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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

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
        // Les portées sont désormais incluses : elles portent l'identifiant de corrélation et
        // le chemin de la requête, sans lesquels un incident signalé par un utilisateur était
        // impossible à relier à une ligne de log.
        options.IncludeScopes = true;
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

// Options passerelle Meta WhatsApp Cloud (WABA dédié) — la bascule Meta:Enabled=true
// remplace WhatChimp pour TOUTES les notifications (envoi + médias entrants).
var metaApiOptions = builder.Configuration.GetSection(MetaApiOptions.SectionName).Get<MetaApiOptions>() ?? new MetaApiOptions();
builder.Services.AddSingleton(metaApiOptions);

// Options agrégateur de paiement GeniusPay
var geniusPayOptions = builder.Configuration.GetSection(GeniusPayOptions.SectionName).Get<GeniusPayOptions>() ?? new GeniusPayOptions();
builder.Services.AddSingleton(geniusPayOptions);

// Options numérotation ivoirienne (conversion 8 → 10 chiffres, table officielle ARTCI à venir)
var ciNumberingOptions = builder.Configuration.GetSection(IvoryCoastNumberingOptions.SectionName).Get<IvoryCoastNumberingOptions>() ?? new IvoryCoastNumberingOptions();
builder.Services.AddSingleton(ciNumberingOptions);

// Options sécurité des coursiers (certification « Garantie Colis Sûr »)
var riderSecurityOptions = builder.Configuration.GetSection(RiderSecurityOptions.SectionName).Get<RiderSecurityOptions>() ?? new RiderSecurityOptions();
builder.Services.AddSingleton(riderSecurityOptions);

// Options protection des scans d'identité (chiffrement au repos AES-GCM — RGPD).
// Sans clé exploitable, le téléversement est refusé : aucun repli silencieux en clair.
var riderScansOptions = builder.Configuration.GetSection(RiderScansOptions.SectionName).Get<RiderScansOptions>() ?? new RiderScansOptions();
builder.Services.AddSingleton(riderScansOptions);

// Options Garantie Colis Sûr (barème d'indemnisation FCFA, caution livreur)
var colisSurOptions = builder.Configuration.GetSection(ColisSurOptions.SectionName).Get<ColisSurOptions>() ?? new ColisSurOptions();
builder.Services.AddSingleton(colisSurOptions);

// Versement sortant : aucune API de disbursement chez GeniusPay → virement manuel tracé.
builder.Services.AddScoped<IPayoutService, ManualPayoutService>();

// Options paiement du panier client (Mobile Money via GeniusPay, non bloquant par défaut)
var clientPaymentOptions = builder.Configuration.GetSection(ClientPaymentOptions.SectionName).Get<ClientPaymentOptions>() ?? new ClientPaymentOptions();
builder.Services.AddSingleton(clientPaymentOptions);

// Options réputation livreur (notes clients après livraison)
var riderReputationOptions = builder.Configuration.GetSection(RiderReputationOptions.SectionName).Get<RiderReputationOptions>() ?? new RiderReputationOptions();
builder.Services.AddSingleton(riderReputationOptions);

// Options pack prioritaire LIVREUR (option payante : être proposé en premier dans son rayon)
var riderPriorityOptions = builder.Configuration.GetSection(RiderPriorityOptions.SectionName).Get<RiderPriorityOptions>() ?? new RiderPriorityOptions();
builder.Services.AddSingleton(riderPriorityOptions);

// Options programme « Ambassadeur WAZAP » (suivi des 3 conditions de récompense des livreurs)
var riderProgramOptions = builder.Configuration.GetSection(RiderProgramOptions.SectionName).Get<RiderProgramOptions>() ?? new RiderProgramOptions();
builder.Services.AddSingleton(riderProgramOptions);

// Options preuve de livraison (code client à 4 chiffres restitué par le livreur)
var deliveryProofOptions = builder.Configuration.GetSection(DeliveryProofOptions.SectionName).Get<DeliveryProofOptions>() ?? new DeliveryProofOptions();
builder.Services.AddSingleton(deliveryProofOptions);

// Options monitoring / alertes (webhook optionnel + service d'alerte)
var monitoringOptions = builder.Configuration.GetSection(MonitoringOptions.SectionName).Get<MonitoringOptions>() ?? new MonitoringOptions();
builder.Services.AddSingleton(monitoringOptions);
// Singleton OBLIGATOIRE : l'anti-rebond des alertes repose sur un dictionnaire d'horodatage
// qui doit vivre au niveau du processus. En Scoped, un nouveau dictionnaire était créé à
// chaque scope (le watchdog en ouvre un par cycle, toutes les 5 s) et la fenêtre de
// temporisation ne s'appliquait jamais — le webhook d'alerte était inondé.
builder.Services.AddSingleton<MonitoringAlertService>();
builder.Services.AddScoped<HealthDetailsService>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<ProspectAutoService>();
builder.Services.AddScoped<LeadConversionService>();
builder.Services.AddScoped<ColisSurService>();
builder.Services.AddScoped<RiderRatingService>();
builder.Services.AddScoped<RiderProgramService>();
// Commandes livreur « RECU » / « LIVRE » (preuve de livraison) : extraites du contrôleur
// webhook pour être testables directement (voir RiderDeliveryCommands).
builder.Services.AddScoped<RiderDeliveryCommands>();
// Commandes vendeur (LIVRAISON à la demande — 1 crédit, SINISTRE, catalogue produits) :
// extraites du contrôleur webhook pour être testables directement (voir VendorTextCommands).
builder.Services.AddScoped<VendorTextCommands>();

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

// Sécurité des webhooks entrants : exiger une signature Meta ou le jeton partagé (fail closed).
var webhookSecurityOptions = builder.Configuration.GetSection(WebhookSecurityOptions.SectionName).Get<WebhookSecurityOptions>() ?? new WebhookSecurityOptions();
builder.Services.AddSingleton(webhookSecurityOptions);

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
        // Le cookie de session administrateur ne doit jamais transiter en clair ni
        // accompagner une requête inter-site : sans ces deux réglages, un déploiement
        // servi en HTTP l'exposerait sur le réseau.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
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
            // Révocation des jetons : le jeton porte l'empreinte de sécurité du compte au moment
            // de sa délivrance ; elle est comparée à la valeur courante. Un changement de mot de
            // passe, une réinitialisation ou une modification de la 2FA la régénèrent — les
            // sessions déjà ouvertes sont donc immédiatement refusées, au lieu de rester
            // valides jusqu'à l'expiration du jeton (8 h auparavant).
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var principal = context.Principal;

                var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? principal?.FindFirstValue("sub");
                var stamp = principal?.FindFirstValue(JwtTokenGenerator.SecurityStampClaim);

                if (!Guid.TryParse(subject, out var userId) || string.IsNullOrWhiteSpace(stamp))
                {
                    context.Fail("Jeton sans identité ou sans empreinte de sécurité.");
                    return;
                }

                var currentStamp = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.SecurityStamp)
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                if (currentStamp is null || !string.Equals(currentStamp, stamp, StringComparison.Ordinal))
                    context.Fail("Session révoquée : reconnectez-vous.");
            },
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

    // Chaque politique est PARTITIONNÉE (par IP, ou par clé d'API pour l'API publique).
    // Une politique non partitionnée partage un compteur unique entre tous les appelants :
    // 10 tentatives de login suffisaient alors à bloquer la connexion de tout le monde.
    options.AddPolicy("webhook", ctx => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPartitions.ByClient(ctx),
        _ => RateLimitPartitions.FixedWindow(100, TimeSpan.FromMinutes(1))));

    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPartitions.ByClient(ctx),
        _ => RateLimitPartitions.FixedWindow(10, TimeSpan.FromMinutes(1))));

    options.AddPolicy("client", ctx => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPartitions.ByClient(ctx),
        _ => RateLimitPartitions.FixedWindow(60, TimeSpan.FromMinutes(1))));

    options.AddPolicy("publicapi", ctx => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPartitions.ByApiKey(ctx),
        _ => RateLimitPartitions.FixedWindow(publicApiOptions.RateLimitPerMinute, TimeSpan.FromMinutes(1))));

    options.AddPolicy("leads", ctx => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitPartitions.ByClient(ctx),
        _ => RateLimitPartitions.FixedWindow(10, TimeSpan.FromMinutes(1))));
});

// Passerelle d'envoi : Meta WhatsApp Cloud (WABA dédié) dès Meta:Enabled=true, sinon
// WhatChimp (mode legacy en attendant la bascule — plus aucun envoi tant que le compte
// ancien est verrouillé, ce qui est justement ce qu'on veut).
//
// P2 / C-14 (second temps) : le délai par défaut d'un HttpClient est de 100 s. Une passerelle
// qui ne répond pas immobilisait donc un worker de fond (et la requête du webhook) pendant plus
// d'une minute et demie, bien au-delà du délai d'arrêt de l'hôte — l'arrêt du service attendait
// l'expiration de ce délai. Les envois sont bornés à 30 s (Meta répond en général en moins de
// deux secondes) et les téléchargements de médias à 60 s (pièce d'identité de quelques Mo sur
// réseau mobile). Le CancellationToken reste transmis en complément pour interrompre au plus tôt.
var sendTimeout = TimeSpan.FromSeconds(30);
var mediaTimeout = TimeSpan.FromSeconds(60);

if (metaApiOptions.Enabled)
{
    builder.Services.AddHttpClient<IWhatsAppSender, MetaCloudApiWhatsAppSender>()
        .ConfigureHttpClient(c => c.Timeout = sendTimeout);
    builder.Services.AddHttpClient<IWhatsAppMediaDownloader, MetaCloudApiMediaDownloader>()
        .ConfigureHttpClient(c => c.Timeout = mediaTimeout);
}
else
{
    builder.Services.AddHttpClient<IWhatsAppSender, WhatChimpService>()
        .ConfigureHttpClient(c => c.Timeout = sendTimeout);
    builder.Services.AddHttpClient<IWhatsAppMediaDownloader, WhatChimpMediaDownloader>()
        .ConfigureHttpClient(c => c.Timeout = mediaTimeout);
}

// Catalogue des packs prépayés (payé à l'usage, sans abonnement)
var packs = builder.Configuration.GetSection("Packs").Get<List<PackConfiguration>>() ?? new List<PackConfiguration>();
builder.Services.AddSingleton<IReadOnlyList<PackConfiguration>>(packs);

// Catalogue des packs prioritaires LIVREUR (option payante : priorité de proposition)
var riderPriorityPacks = builder.Configuration.GetSection("RiderPriorityPacks").Get<List<RiderPriorityPackConfiguration>>() ?? new List<RiderPriorityPackConfiguration>();
builder.Services.AddSingleton<IReadOnlyList<RiderPriorityPackConfiguration>>(riderPriorityPacks);

// Injection des services applicatifs
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<WhatsAppOrchestrationService>();
builder.Services.AddScoped<AuthService>();

// Services géolocalisation / matching / tableau de bord
builder.Services.AddScoped<RiderService>();
builder.Services.AddScoped<RiderRecruitmentService>();
builder.Services.AddScoped<VendorService>();
builder.Services.AddScoped<VendorProductService>();
builder.Services.AddScoped<ClientOrderBotService>();
builder.Services.AddScoped<DeliveryOfferService>();
builder.Services.AddScoped<DashboardService>();

// Packs prépayés : catalogue + achat
builder.Services.AddScoped<PackService>();
builder.Services.AddScoped<ClientPaymentService>();

// Pack prioritaire livreur : catalogue + achat (option payante)
builder.Services.AddScoped<RiderPriorityService>();

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

// Un worker qui rencontre une erreur de base TRANSITOIRE (pool saturé, bascule PostgreSQL)
// ne doit PAS arrêter l'API : le comportement par défaut de l'hôte (.NET 6+) est de stopper
// l'application entière, ce qui couperait les webhooks WhatsApp et les paiements à cause
// d'un simple incident passager de la base. Les workers gèrent déjà leurs erreurs ; cette
// option garantit qu'un oubli ne fait pas tomber la plateforme.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

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

// Aucun canal d'envoi utilisable = aucune notification possible (confirmation de commande,
// offre livreur, code de livraison, alertes de crédits). La construction du service échouait
// jusqu'ici à la PREMIÈRE résolution — donc au premier message entrant, par un 500/409
// incompréhensible (incident prod du 11/09). On refuse désormais de SERVIR du trafic en
// nommant la clé manquante.
//
// ⚠️ Ce contrôle est volontairement APRÈS `builder.Build()` : les outils de conception
// (`dotnet ef migrations …`) construisent l'hôte pour récupérer le DbContext et s'arrêtent à
// ce point. Placé avant, il faisait échouer les MIGRATIONS de production (run #62).
if (!metaApiOptions.Enabled && string.IsNullOrWhiteSpace(builder.Configuration["WhatChimp:ApiToken"]))
{
    throw new InvalidOperationException(
        "Aucun canal d'envoi WhatsApp configuré : Meta:Enabled est à false et "
        + "WhatChimp:ApiToken est absent. Renseignez WhatChimp__ApiToken "
        + "(ou activez Meta:Enabled avec son jeton) — sans quoi aucune notification ne peut partir.");
}

// Contrôle de cohérence de la configuration AVANT de servir du trafic. Auparavant, une valeur
// aberrante (seuil à 0, commission > 100 %, section de paiement absente) produisait un
// comportement silencieusement faux — jusqu'à créditer des packs sans aucun paiement.
var configProblems = StartupConfigurationValidator.FindProblems(
    builder.Configuration,
    app.Environment,
    geoOptions,
    clientPaymentOptions,
    riderReputationOptions,
    retentionOptions,
    geniusPayOptions,
    packs,
    riderPriorityPacks);

if (configProblems.Count > 0)
{
    foreach (var problem in configProblems)
        app.Logger.LogCritical("CONFIGURATION INVALIDE : {Problem}", problem);

    throw new InvalidOperationException(
        "Configuration invalide — démarrage refusé :\n - " + string.Join("\n - ", configProblems));
}

// Témoin de conformité au démarrage : une protection RGPD inactive doit être bruyante
// immédiatement, et non découverte au premier téléversement de pièce d'identité.
switch (riderScansOptions.GetStatus())
{
    case ScanProtectionStatus.Encrypted:
        break;
    case ScanProtectionStatus.UnencryptedAllowed:
        app.Logger.LogWarning(
            "ALERTE [config] Scans d'identité stockés EN CLAIR "
            + "(RiderScans:AllowUnencryptedStorage=true) — développement uniquement.");
        break;
    default:
        app.Logger.LogWarning(
            "ALERTE [config] RiderScans:EncryptionKey absente ou invalide : les téléversements "
            + "de scans d'identité seront REFUSÉS. Détail sur /health/details (compliance).");
        break;
}

if (!retentionOptions.Enabled)
    app.Logger.LogWarning(
        "ALERTE [config] Retention:Enabled=false — aucune purge n'est exécutée : les scans "
        + "d'identité des livreurs sont conservés sans limite de durée (RGPD).");

// Le webhook WhatsApp pilote tout le produit (créer une course, accepter une offre, clôturer
// une livraison) : sans preuve d'authenticité, n'importe qui peut usurper un vendeur ou un
// livreur. L'absence de protection doit être visible au démarrage, pas découverte en production.
if (!webhookSecurityOptions.RequireAuthentication)
    app.Logger.LogWarning(
        "ALERTE [config] WebhookSecurity:RequireAuthentication=false — les POST non signés sur "
        + "/api/webhook/whatsapp sont ACCEPTÉS : n'importe qui peut usurper un vendeur ou un livreur. "
        + "À réserver au développement local.");

if (geniusPayOptions.Enabled && GeniusPaySignatureVerifier.IsPlaceholderSecret(geniusPayOptions.WebhookSecret))
    app.Logger.LogError(
        "ALERTE [config] GeniusPay:WebhookSecret absent ou resté sur la valeur d'exemple : les "
        + "notifications de paiement seront REFUSÉES (fail closed). Renseignez le secret réel du "
        + "webhook GeniusPay, sinon les achats de packs ne seront jamais crédités.");

// /metrics est ouvert par défaut (comportement historique) : il expose la profondeur de la file
// d'échecs et l'uptime. Le jeton optionnel Monitoring:MetricsToken le referme.
if (string.IsNullOrWhiteSpace(monitoringOptions.MetricsToken))
    app.Logger.LogWarning(
        "ALERTE [config] /metrics est accessible SANS authentification "
        + "(Monitoring:MetricsToken vide) : renseignez un jeton et configurez-le côté scraper.");

// Gestion globale des erreurs (doit être le premier middleware)
app.UseExceptionHandler();

// Identifiant de corrélation : permet de relier une erreur signalée par un utilisateur à sa
// ligne de log (l'identifiant est renvoyé dans l'en-tête X-Correlation-Id).
app.UseMiddleware<CorrelationIdMiddleware>();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// B-16 — adresse réelle du client quand un proxy termine la connexion (CDN, load-balancer,
// reverse-proxy devant IIS). En hébergement IIS *in-process* (notre cas : le web.config publié
// déclare hostingModel="inprocess"), `Connection.RemoteIpAddress` porte déjà l'adresse du client
// et activer cet en-tête ne changerait rien. Il devient nécessaire dès qu'une couche
// intermédiaire s'ajoute, sinon TOUS les utilisateurs partagent le compartiment du proxy : dix
// tentatives de connexion suffiraient alors à répondre 429 à toute la plateforme.
//
// L'option reste donc FERMÉE par défaut, et n'est ouverte qu'avec la liste des proxies de
// confiance (`Networking:KnownProxies`) : `X-Forwarded-For` est écrit par le client, le croire
// sans liste reviendrait à le laisser choisir son compartiment de débit (voir le contrôle de
// démarrage correspondant).
if (builder.Configuration.GetValue("Networking:TrustForwardedHeaders", false))
{
    var knownProxies = builder.Configuration.GetSection("Networking:KnownProxies").Get<string[]>() ?? [];
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    // Les réseaux de confiance par défaut (loopback) sont conservés : ils couvrent le cas d'un
    // proxy local, sans rien accorder aux adresses publiques non déclarées.
    foreach (var proxy in knownProxies)
        forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));

    app.UseForwardedHeaders(forwardedOptions);
    app.Logger.LogInformation(
        "En-têtes de proxy pris en compte (X-Forwarded-For) pour {Count} proxy(ies) déclaré(s).",
        knownProxies.Length);
}

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

// Webhooks entrants WhatsApp : authentification OBLIGATOIRE avant routage — signature
// HMAC Meta (X-Hub-Signature-256) ou, pour la passerelle historique qui ne signe pas,
// jeton partagé (?token= / X-Webhook-Token). Sans l'un des deux : 403.
// Ce corps de requête pilote tout le produit (créer une course, accepter une offre,
// clôturer une livraison) : un POST anonyme y est une usurpation d'identité.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/webhook/whatsapp"),
    branch => branch.UseMiddleware<MetaWebhookSignatureMiddleware>());

app.MapControllers();
app.MapHealthChecks("/health");

// Métriques de supervision détaillées (DB, file outbox, workers) — pour uptime monitors et dashboards.
// Endpoint ouvert : le détail de conformité (nature exacte de la faiblesse) n'est servi
// qu'aux administrateurs authentifiés ; les anonymes reçoivent le seul statut de synthèse.
// Le schéma par défaut étant le cookie (tableau de bord Blazor), le schéma JWT doit être
// essayé explicitement : sans cela, un administrateur muni d'un jeton Bearer — supervision,
// script, curl — serait traité comme anonyme et privé du détail auquel il a droit.
app.MapGet("/health/details", async (HealthDetailsService service, HttpContext http, CancellationToken ct) =>
{
    var isAdmin = http.User.IsInRole("Admin");
    if (!isAdmin)
    {
        // AuthenticateAsync n'émet pas de challenge : sans jeton, le résultat est simplement négatif.
        var bearer = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        isAdmin = bearer.Succeeded && bearer.Principal?.IsInRole("Admin") == true;
    }

    return await service.BuildAsync(isAdmin, ct);
});

// Métriques Prometheus au format texte (0.0.4) — pour Prometheus/Grafana.
// Protection OPTIONNELLE : si Monitoring:MetricsToken est renseigné, le jeton est exigé
// (query ?token= ou en-tête X-Metrics-Token, comparaison à temps constant) ; sinon l'endpoint
// reste ouvert comme auparavant et le démarrage le signale.
app.MapGet("/metrics", async (MetricsService service, HttpContext http, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(monitoringOptions.MetricsToken))
    {
        var provided = http.Request.Query["token"].FirstOrDefault()
                       ?? http.Request.Headers["X-Metrics-Token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(provided)
            || !Wazap.Application.Helpers.SecurityHelper.FixedTimeEquals(provided, monitoringOptions.MetricsToken))
            return Results.Unauthorized();
    }

    return Results.Text(await service.BuildTextAsync(ct), "text/plain; version=0.0.4");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SPA React (web/) servie depuis /app — fallback pour le routing client
app.MapFallbackToFile("app/{*path:nonfile}", "app/index.html");

// Liens courts marketing (évitent le long préfixe /app) :
//   /vente       → page de vente          ·   /parrainage → page parrainage
app.MapGet("/vente", () => Results.Redirect("/app/vente"));
app.MapGet("/parrainage", () => Results.Redirect("/app/parrainage"));

// Page publique dédiée au recrutement des livreurs (offre « Ambassadeur WAZAP »).
// Servie depuis wwwroot (même mécanisme que suivi.html / demo.html), URL courte pour le QR.
app.MapGet("/devenir-livreur", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "devenir-livreur.html"), "text/html; charset=utf-8"));

// Les migrations sont appliquées hors démarrage (étape de déploiement dédiée) :
//   dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
app.Run();

/// <summary>
/// Point d'entrée exposé aux tests d'intégration (<c>WebApplicationFactory&lt;Program&gt;</c>).
/// Permet de DÉMARRER l'application réelle — donc de vérifier le graphe DI de production —
/// au lieu de reconstruire les contrôleurs à la main. C'est précisément l'angle mort qui a
/// laissé passer une panne en production : <c>ClientOrderBotService</c> était injecté dans
/// <c>WebhookWhatsAppController</c> mais jamais enregistré, si bien que TOUT message WhatsApp
/// entrant répondait 409 (DI incapable de construire le contrôleur).
/// </summary>
public partial class Program;
