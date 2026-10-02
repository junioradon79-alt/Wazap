using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Commandes texte du <b>vendeur</b> reçues sur WhatsApp, extraites du contrôleur webhook (P2 / C-13) :
///
///  - <c>LIVRAISON &lt;détail&gt; à &lt;adresse&gt;</c> : demande de course à la demande — <b>consomme
///    un crédit</b> et diffuse immédiatement aux livreurs proches ;
///  - <c>SINISTRE &lt;code&gt;</c> : déclaration de colis perdu/volé (Garantie Colis Sûr) ;
///  - <c>PRODUITS</c> / <c>PRODUIT &lt;nom&gt; | &lt;prix&gt; [| &lt;emoji&gt;]</c> /
///    <c>SUPPRIMER PRODUIT &lt;n°&gt;</c> : catalogue qui alimente le menu numéroté du bot client ;
///  - <c>IMPORT &lt;lien ou texte&gt;</c> : import magique de catalogue par IA (Gemini 1.5 Flash).
/// </summary>
public sealed class VendorTextCommands
{
    private readonly ApplicationDbContext _context;
    private readonly OrderService _orderService;
    private readonly VendorProductService _products;
    private readonly ColisSurService _colisSur;
    private readonly ICatalogAiExtractorService? _catalogAi;

    public VendorTextCommands(
        ApplicationDbContext context,
        OrderService orderService,
        VendorProductService products,
        ColisSurService colisSur,
        ICatalogAiExtractorService? catalogAi = null)
    {
        _context = context;
        _orderService = orderService;
        _products = products;
        _colisSur = colisSur;
        _catalogAi = catalogAi;
    }

    /// <summary>
    /// Reconnaît les messages pris en charge ici. Aligné sur le comportement d'origine : mot seul ou
    /// suivi d'un espace (« LIVRAISONNE » ne doit pas déclencher une demande de course).
    /// </summary>
    public static bool Matches(string upperText)
        => upperText == "LIVRAISON" || upperText.StartsWith("LIVRAISON ")
           || upperText is "DASHBOARD" or "STATS" or "STATISTIQUES" or "SOLDE" or "COMPTE" or "TABLEAU DE BORD"
           || upperText == "SINISTRE" || upperText.StartsWith("SINISTRE ")
           || upperText == "PRODUITS" || upperText.StartsWith("PRODUITS ")
           || upperText == "PRODUIT" || upperText.StartsWith("PRODUIT ")
           || upperText.StartsWith("SUPPRIMER PRODUIT")
           || upperText == "IMPORT" || upperText.StartsWith("IMPORT ")
           || upperText.StartsWith("CATALOGUE ")
           || upperText.StartsWith("HTTP://") || upperText.StartsWith("HTTPS://");

    /// <summary>Traite la commande. <paramref name="reply"/> envoie la réponse WhatsApp au vendeur.</summary>
    public async Task HandleAsync(User user, string command, Func<User, string, Task> reply)
    {
        var upper = command.ToUpperInvariant();

        if (upper is "DASHBOARD" or "STATS" or "STATISTIQUES" or "SOLDE" or "COMPTE" or "TABLEAU DE BORD")
        {
            await HandleDashboardAsync(user, reply);
            return;
        }

        if (upper == "LIVRAISON" || upper.StartsWith("LIVRAISON "))
        {
            await HandleDispatchAsync(user, command, reply);
            return;
        }

        // Import magique de catalogue par IA : « IMPORT <url ou texte> », « CATALOGUE <url ou texte> », ou lien web
        if (upper == "IMPORT" || upper.StartsWith("IMPORT ")
            || upper.StartsWith("CATALOGUE ")
            || upper.StartsWith("HTTP://") || upper.StartsWith("HTTPS://"))
        {
            await HandleCatalogImportAsync(user, command, reply);
            return;
        }

        // Sinistre (Garantie Colis Sûr) : « SINISTRE <code> » → colis perdu/volé signalé par le vendeur.
        if (upper == "SINISTRE" || upper.StartsWith("SINISTRE "))
        {
            var result = await _colisSur.DeclareAsync(user.Id, command);
            await reply(user, result.Message);
            return;
        }

        // « SUPPRIMER PRODUIT » est testé AVANT « PRODUIT » : les deux commandes partagent le mot
        // « PRODUIT », et l'ordre inverse ferait passer la suppression pour un ajout de produit.
        if (upper.StartsWith("SUPPRIMER PRODUIT"))
        {
            await HandleProductDeleteAsync(user, command, reply);
            return;
        }

        if (upper == "PRODUITS" || upper.StartsWith("PRODUITS "))
        {
            await HandleProductListAsync(user, reply);
            return;
        }

        await HandleProductCreateAsync(user, command, reply);
    }

    /// <summary>
    /// « LIVRAISON &lt;ce qu'il faut livrer&gt; à &lt;adresse client&gt; » — un crédit est consommé et la
    /// course est diffusée immédiatement. Les deux échecs métier attendus (crédits insuffisants,
    /// commande refusée par le domaine) sont rendus au vendeur en clair, jamais en erreur technique.
    /// </summary>
    private async Task HandleDispatchAsync(User user, string command, Func<User, string, Task> reply)
    {
        var instruction = command.Length > "LIVRAISON ".Length
            ? command["LIVRAISON ".Length..].Trim()
            : string.Empty;

        if (instruction.Length < 3)
        {
            await reply(user,
                "📦 Format : LIVRAISON <ce qu'il faut livrer> à <quartier/adresse du client>\n" +
                "Exemple : LIVRAISON 2 poulets à Marcory, rue Princesse");
            return;
        }

        try
        {
            // Extraction optionnelle du téléphone du client (ex : « … tél 0708091011 »)
            // → notifications automatiques possibles (livreur assigné, livraison effectuée).
            var clientPhone = VendorCommandParser.TryExtractClientPhone(instruction);

            var order = await _orderService.CreateDispatchRequestAsync(user.Id, instruction, clientPhone);
            var creditsLeft = await _context.Users.AsNoTracking()
                .Where(u => u.Id == user.Id)
                .Select(u => u.Credits)
                .FirstOrDefaultAsync();

            var code = order.Id.ToString("N")[..8].ToUpperInvariant();
            await reply(user,
                $"✅ Course #{code} enregistrée ! Un livreur proche est contacté. Crédits restants : {creditsLeft}.");
        }
        catch (PaymentRequiredException ex)
        {
            await reply(user, "❌ " + ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await reply(user, "❌ " + ex.Message);
        }
    }

    private async Task HandleProductDeleteAsync(User user, string command, Func<User, string, Task> reply)
    {
        var raw = command.Length > "SUPPRIMER PRODUIT".Length
            ? command["SUPPRIMER PRODUIT".Length..].Trim()
            : string.Empty;

        var catalog = await _products.GetProductsAsync(user.Id);
        if (!int.TryParse(raw, out var index) || index < 1 || index > catalog.Count)
        {
            await reply(user, "❓ Format : SUPPRIMER PRODUIT <n° du catalogue> (voir PRODUITS).");
            return;
        }

        var target = catalog[index - 1];
        await reply(user, await _products.DeleteAsync(user.Id, target.Id) switch
        {
            VendorProductDeleteResult.Deleted => $"🗑️ Produit retiré : {target.Name}.",
            VendorProductDeleteResult.InUse =>
                "⚠️ Ce produit figure dans des commandes passées : il ne peut plus être supprimé (modifiez son prix).",
            _ => "⚠️ Produit introuvable."
        });
    }

    private async Task HandleProductListAsync(User user, Func<User, string, Task> reply)
    {
        var catalog = await _products.GetProductsAsync(user.Id);
        await reply(user, catalog.Count == 0
            ? "🛒 Votre catalogue est vide.\n"
              + "Ajoutez un produit : PRODUIT <nom> | <prix> [| <emoji>]\n"
              + "Exemple : PRODUIT Poulet braisé | 2500 | 🍗"
            : "🛒 Votre catalogue :\n"
              + string.Join("\n", catalog.Select((p, i) => $"{i + 1}. {ProductDisplayText(p)}"))
              + "\n\n➕ PRODUIT <nom> | <prix> [| <emoji>]\n🗑️ SUPPRIMER PRODUIT <n°>");
    }

    private async Task HandleProductCreateAsync(User user, string command, Func<User, string, Task> reply)
    {
        var payload = command.Length > "PRODUIT".Length ? command["PRODUIT".Length..].Trim() : string.Empty;
        if (!VendorCommandParser.TryParseProductCommand(payload, out var name, out var price, out var emoji))
        {
            await reply(user,
                "📦 Format : PRODUIT <nom> | <prix> [| <emoji>]\n" +
                "Exemple : PRODUIT Poulet braisé | 2500 | 🍗");
            return;
        }

        var created = await _products.CreateAsync(user.Id,
            new VendorProductRequest { Name = name, Price = price, Emoji = emoji });
        await reply(user,
            $"✅ Produit ajouté : {ProductDisplayText(created)}\n" +
            "Il apparaît maintenant dans le menu des clients qui commandent chez vous.");
    }

    private async Task HandleCatalogImportAsync(User user, string command, Func<User, string, Task> reply)
    {
        if (_catalogAi is null)
        {
            await reply(user, "⚙️ L'import intelligent de catalogue est temporairement indisponible.");
            return;
        }

        var content = command.Trim();
        if (content.StartsWith("IMPORT ", StringComparison.OrdinalIgnoreCase))
            content = content["IMPORT ".Length..].Trim();
        else if (content.StartsWith("IMPORT", StringComparison.OrdinalIgnoreCase))
            content = content["IMPORT".Length..].Trim();
        else if (content.StartsWith("CATALOGUE ", StringComparison.OrdinalIgnoreCase))
            content = content["CATALOGUE ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            await reply(user,
                "✨ *WAZAP Magic Importer (IA)* 🪄\n\n" +
                "Importez tout votre catalogue en 1 seul clic sans rien saisir !\n" +
                "• 🔗 Envoyez le lien de votre page/boutique (ex: `IMPORT https://...`)\n" +
                "• 📝 Envoyez le copier-coller de votre publication Facebook/WhatsApp avec vos prix\n" +
                "• 📸 Ou envoyez simplement une photo/capture d'écran de votre menu ou flyer !");
            return;
        }

        await reply(user, "⏳ Analyse de votre catalogue en cours par l'IA WAZAP... 🪄");

        CatalogExtractionResult extraction;
        if (Uri.TryCreate(content, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            extraction = await _catalogAi.ExtractFromUrlAsync(content);
        }
        else
        {
            extraction = await _catalogAi.ExtractFromTextAsync(content);
        }

        if (!extraction.Success || extraction.Products.Count == 0)
        {
            await reply(user,
                "⚠️ Aucun article ou prix n'a pu être identifié avec certitude.\n\n" +
                "👉 Astuce : envoyez une capture d'écran nette de votre menu/catalogue, ou collez votre liste au format :\n" +
                "_Robe soirée 15000, Sac noir 25000..._");
            return;
        }

        var imported = await _products.CreateBatchAsync(user.Id, extraction.Products);
        if (imported.Count == 0)
        {
            await reply(user,
                $"ℹ️ {extraction.Products.Count} article(s) détecté(s), mais ils existent déjà dans votre catalogue WAZAP.\n" +
                "👉 Voir mon catalogue (1 clic) :\nhttps://wa.me/2250544051972?text=PRODUITS");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"✨ *Magie WAZAP ! {imported.Count} article(s) importé(s) dans votre Mini-Boutique :*\n");
        for (var i = 0; i < imported.Count; i++)
        {
            var p = imported[i];
            var emoji = string.IsNullOrWhiteSpace(p.Emoji) ? "📦" : p.Emoji;
            var priceFormatted = p.Price.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', ' ');
            sb.AppendLine($"{i + 1}. {emoji} *{p.Name}* — {priceFormatted} FCFA");
        }
        sb.AppendLine("\n🟢 Vos clients peuvent commander dès maintenant en tapant simplement le numéro d'un article sur votre WhatsApp !");
        sb.AppendLine("👉 Voir mon catalogue (1 clic) :\nhttps://wa.me/2250544051972?text=PRODUITS");

        await reply(user, sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Affichage catalogue identique au menu du bot client (<see cref="VendorProduct.DisplayText"/>).
    /// </summary>
    private static string ProductDisplayText(VendorProductDto product)
        => string.IsNullOrEmpty(product.Emoji)
            ? $"{product.Name} — {product.Price:N0} FCFA"
            : $"{product.Emoji} {product.Name} — {product.Price:N0} FCFA";

    /// <summary>
    /// Tableau de bord conversationnel direct dans WhatsApp : solde crédits, livraisons du mois, CA et lien 1-tap.
    /// </summary>
    private async Task HandleDashboardAsync(User user, Func<User, string, Task> reply)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var orders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == user.Id)
            .ToListAsync();

        var inProgress = orders.Count(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled);
        var deliveredThisMonth = orders.Count(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= monthStart);
        var revenueThisMonth = orders.Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= monthStart).Sum(o => o.Amount);

        var webDashboardUrl = $"https://junioradon79gm-001-site1.jtempurl.com/app/login?u={Uri.EscapeDataString(user.Username)}";

        var sb = new StringBuilder();
        sb.AppendLine("📊 *TABLEAU DE BORD COMMERÇANT* — WAZAP");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🏪 *{user.Username}* ({(user.PhoneNumber ?? "WhatsApp")})");
        sb.AppendLine($"📍 Commune : {user.Zone ?? "Grand Abidjan"}");
        sb.AppendLine($"💳 Crédits livraisons disponibles : *{user.Credits}*");
        sb.AppendLine();
        sb.AppendLine("📦 *ACTIVITÉ DU MOIS*");
        sb.AppendLine($"• Courses en cours : {inProgress}");
        sb.AppendLine($"• Livraisons réussies : {deliveredThisMonth}");
        if (revenueThisMonth > 0)
            sb.AppendLine($"• CA livré : {revenueThisMonth:N0} FCFA");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(user.ReferralCode))
        {
            sb.AppendLine($"🎁 Code parrainage : *{user.ReferralCode}* (+5 crédits offerts par filleul)");
            sb.AppendLine();
        }
        sb.AppendLine("👉 *TOUCHER POUR AGIR (1 Clic) :*");
        sb.AppendLine("🚀 Expédier un colis :");
        sb.AppendLine("https://wa.me/2250544051972?text=LIVRAISON");
        sb.AppendLine();
        sb.AppendLine("📦 Mon Catalogue d'articles :");
        sb.AppendLine("https://wa.me/2250544051972?text=PRODUITS");
        sb.AppendLine();
        sb.AppendLine("📋 Grille Tarifaire Abidjan :");
        sb.AppendLine("https://wa.me/2250544051972?text=TARIFS");

        await reply(user, sb.ToString().TrimEnd());
    }
}
