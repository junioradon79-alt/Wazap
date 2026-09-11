using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Bot WhatsApp conversationnel de COMMANDE CLIENT : un numéro non enregistré qui exprime
/// l'intention de commander est guidé en 3 étapes (article → commerce → adresse), puis une
/// commande réelle est créée au compte du vendeur (confirmation via les boutons existants,
/// diffusion aux livreurs après confirmation). S'insère AVANT le bot prospects : un client
/// qui veut commander ne doit pas être pris pour un prospect commerçant.
///
/// Si le commerce retenu possède un catalogue produits, une étape supplémentaire propose le
/// menu numéroté (panier) ; sinon la conversation passe directement à l'adresse (le mode
/// texte libre d'origine est conservé).
/// </summary>
public sealed class ClientOrderBotService
{
    /// <summary>Réponses inattendues consécutives tolérées à une étape (anti-boucle).</summary>
    private const int MaxAttemptsPerStage = 3;

    /// <summary>Commerces maximum listés quand plusieurs correspondent au nom donné.</summary>
    private const int MaxVendorChoices = 5;

    /// <summary>Articles maximum affichés dans le menu catalogue (borne de message WhatsApp).</summary>
    private const int MaxProductsInMenu = 20;

    /// <summary>Longueur maximale du libellé d'article conservé (colonne brouillon).</summary>
    private const int MaxItemsLength = 500;

    /// <summary>Longueur maximale de l'adresse conservée (colonne brouillon).</summary>
    private const int MaxAddressLength = 300;

    /// <summary>Invitation commune de la dernière étape (lieu de livraison).</summary>
    private const string AddressPrompt =
        "📍 Dernière étape — où livrer ? (quartier + repère, ex. « Marcory, rue Princesse »)";

    /// <summary>Intention client explicite (« commande » couvre « commander », « je commande »…).</summary>
    private static readonly string[] OrderIntentKeywords = ["commande", "acheter", "achat", "panier"];

    /// <summary>
    /// Intention de PARTENARIAT — le bot prospects doit garder la main : un prospect
    /// commerçant parle d'activer des commandes offertes, un livreur de courses.
    /// <para>
    /// Les phrases de <b>volume / possession</b> (jamais prononcées par un client qui
    /// commande) couvrent le cas où un commerçant écrit un mot contenant « commande »
    /// (« je veux plus de commandes pour ma boutique ») : sans elles, le bot de commande
    /// volerait le prospect et aucun Lead ne serait créé (perte d'acquisition).
    /// </para>
    /// </summary>
    private static readonly string[] PartnerKeywords =
    [
        "je veux livrer", "veux livrer", "je vends", "vendre", "activer", "inscri",
        "partenaire", "parrain", "offerte", "mon commerce", "livreur", "coursier", "recrute",
        // Volume de commandes / possession : signaux de commerçant, jamais d'un client.
        "mes commandes", "plus de commandes", "recevoir des commandes",
        "recevoir plus de commandes", "des clients qui commandent",
        "ma boutique", "mon magasin", "mon restaurant", "mon snack", "ma pâtisserie",
        "mon business", "plus de clients", "attirer des clients"
    ];

    /// <summary>Numéro ivoirien cité dans un message libre (« Chez Thalia 07 08 09 10 11 »).</summary>
    private static readonly Regex PhonePattern =
        new(@"(?:\+?\s?225[\s.-]?)?(?:0[157]\d{8}|0\d{7})", RegexOptions.Compiled);

    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<ClientOrderBotService> _logger;
    private readonly bool _enabled;
    private readonly int _expirationHours;

    public ClientOrderBotService(ApplicationDbContext context, IWhatsAppSender whatsApp,
        IConfiguration config, ILogger<ClientOrderBotService> logger)
    {
        _context = context;
        _whatsApp = whatsApp;
        _enabled = config["ClientOrderBot:Enabled"] is not ("false" or "False" or "0");
        _expirationHours = int.TryParse(config["ClientOrderBot:ExpirationHours"], out var h) && h > 0
            ? h
            : 24;
        _logger = logger;
    }

    /// <summary>
    /// Traite un message d'un numéro inconnu. Retourne true si le bot a consommé le
    /// message (intention de commande OU conversation déjà en cours).
    /// </summary>
    public async Task<bool> TryHandleAsync(string? phone, string? text)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        var normalized = "+" + PhoneNumberNormalizer.DigitsOnly(phone);

        try
        {
            var draft = await FindActiveDraftAsync(normalized);
            if (draft is null)
            {
                if (!IsOrderIntent(trimmed))
                    return false;

                draft = new ClientOrderDraft(normalized, DateTime.UtcNow,
                    DateTime.UtcNow.AddHours(_expirationHours));
                _context.ClientOrderDrafts.Add(draft);
                await _context.SaveChangesAsync();

                await SendAsync(normalized,
                    "🛒 C'est parti ! Étape 1/3 — que voulez-vous commander ?\n" +
                    "Ex. « 1 poulet braisé + alloco ».\n" +
                    "Écrivez ANNULER pour arrêter.");
                _logger.LogInformation("Bot commande : conversation démarrée avec {Phone}.", normalized);
                return true;
            }

            if (IsCancel(trimmed))
            {
                draft.Cancel();
                await _context.SaveChangesAsync();
                await SendAsync(normalized, "❌ Commande annulée. À bientôt sur WAZAP ⚡");
                return true;
            }

            await HandleStepAsync(draft, trimmed);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot commande : traitement du message de {Phone} en échec.", phone);
            return false;
        }
    }

    /// <summary>
    /// Intention de COMMANDE d'un inconnu : il parle de commander et ne parle pas de
    /// partenariat (les prospects disent « activer », « je vends », « offertes »…).
    /// </summary>
    internal static bool IsOrderIntent(string text)
    {
        var lower = text.ToLowerInvariant();
        return OrderIntentKeywords.Any(lower.Contains)
            && !PartnerKeywords.Any(lower.Contains);
    }

    /// <summary>Le client renonce à sa commande en cours.</summary>
    private static bool IsCancel(string text)
        => text.Equals("annuler", StringComparison.OrdinalIgnoreCase)
           || text.Equals("annule", StringComparison.OrdinalIgnoreCase)
           || text.Equals("stop", StringComparison.OrdinalIgnoreCase);

    private async Task<ClientOrderDraft?> FindActiveDraftAsync(string normalizedPhone)
    {
        var now = DateTime.UtcNow;
        var drafts = await _context.ClientOrderDrafts
            .Where(d => d.ClientWhatsAppNumber == normalizedPhone)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return drafts.FirstOrDefault(d => d.IsActive(now));
    }

    private async Task HandleStepAsync(ClientOrderDraft draft, string text)
    {
        switch (draft.Stage)
        {
            case ClientOrderDraftStage.AwaitingItems:
                if (text.Length < 3)
                {
                    await RejectStepAsync(draft,
                        "❓ Donnez-nous l'article à commander (ex. « 1 poulet braisé + alloco »).");
                    return;
                }

                var items = Truncate(text, MaxItemsLength);
                draft.SubmitItems(items);
                await _context.SaveChangesAsync();
                await SendAsync(draft.ClientWhatsAppNumber,
                    $"✅ Noté : « {items} ».\n" +
                    "🏪 Étape 2/3 — chez quel commerce ?\n" +
                    "Donnez son NOM (ex. « Chez Thalia ») ou son numéro WhatsApp.");
                return;

            case ClientOrderDraftStage.AwaitingVendor:
                await HandleVendorSearchAsync(draft, text);
                return;

            case ClientOrderDraftStage.AwaitingVendorChoice:
                await HandleVendorChoiceAsync(draft, text);
                return;

            case ClientOrderDraftStage.AwaitingProductChoice:
                await HandleProductChoiceAsync(draft, text);
                return;

            case ClientOrderDraftStage.AwaitingAddress:
                await CompleteOrderAsync(draft, text);
                return;
        }
    }

    /// <summary>Réponse hors format : on réinvite (3 fois maximum, puis abandon).</summary>
    private async Task RejectStepAsync(ClientOrderDraft draft, string message)
    {
        draft.RegisterInvalidAttempt();
        await _context.SaveChangesAsync();

        if (draft.ExceededAttempts(MaxAttemptsPerStage))
        {
            draft.Cancel();
            await _context.SaveChangesAsync();
            await SendAsync(draft.ClientWhatsAppNumber,
                "😕 Trop d'essais sans réponse attendue — commande abandonnée.\n" +
                "Écrivez COMMANDE pour recommencer.");
            return;
        }

        await SendAsync(draft.ClientWhatsAppNumber, message);
    }

    private async Task HandleVendorSearchAsync(ClientOrderDraft draft, string text)
    {
        var matches = await FindVendorsAsync(text);
        if (matches.Count == 0)
        {
            await RejectStepAsync(draft,
                "🤔 Aucun commerce enregistré ne correspond. Donnez son NOM exact\n" +
                "(ex. « Chez Thalia ») ou son numéro WhatsApp (+225…).");
            return;
        }

        if (matches.Count == 1)
        {
            await SelectVendorAsync(draft, matches[0]);
            return;
        }

        draft.SetVendorCandidates(matches.Select(m => m.Id).ToList());
        await _context.SaveChangesAsync();
        var list = string.Join("\n", matches.Select((m, i) =>
            $"{i + 1}. {m.Username}" + (string.IsNullOrWhiteSpace(m.Zone) ? string.Empty : $" ({m.Zone})")));
        await SendAsync(draft.ClientWhatsAppNumber,
            "🏪 Plusieurs commerces correspondent — répondez avec le NUMÉRO :\n" + list);
    }

    private async Task HandleVendorChoiceAsync(ClientOrderDraft draft, string text)
    {
        var candidates = draft.GetVendorCandidates();
        if (!int.TryParse(text.Trim(), out var choice) || choice < 1 || choice > candidates.Count)
        {
            await RejectStepAsync(draft, "❓ Répondez avec le NUMÉRO du commerce (ex. « 1 »).");
            return;
        }

        var chosen = candidates[choice - 1];
        var vendor = await _context.Users.AsNoTracking()
            .Where(u => u.Id == chosen && u.Role == UserRole.Vendor)
            .Select(u => new { u.Id, u.Username, u.Zone })
            .FirstOrDefaultAsync();
        if (vendor is null)
        {
            await RejectStepAsync(draft, "🤔 Ce commerce n'est plus disponible — choisissez-en un autre.");
            return;
        }

        await SelectVendorAsync(draft, new VendorMatch(vendor.Id, vendor.Username, vendor.Zone));
    }

    /// <summary>Commerce retenu : on charge son catalogue puis on oriente la conversation.</summary>
    private async Task SelectVendorAsync(ClientOrderDraft draft, VendorMatch vendor)
    {
        var products = await LoadCatalogAsync(vendor.Id);
        draft.SetVendor(vendor.Id, products.Select(p => p.Id).ToList());
        await _context.SaveChangesAsync();

        if (products.Count > 0)
        {
            await SendAsync(draft.ClientWhatsAppNumber,
                $"🏪 Commerce retenu : {vendor.Username}.\n" +
                "🛒 Étape 3/3 — composez votre commande :\n" +
                BuildProductMenuText(products) +
                "\nRépondez avec le(s) NUMÉRO(S) des articles (ex. « 1 » ou « 1 2 »).");
            return;
        }

        await SendAsync(draft.ClientWhatsAppNumber,
            $"🏪 Commerce retenu : {vendor.Username}.\n" + AddressPrompt);
    }

    private async Task HandleProductChoiceAsync(ClientOrderDraft draft, string text)
    {
        var catalog = draft.GetProductCatalog();
        var indexes = ParseMenuIndexes(text, catalog.Count);
        if (indexes.Count == 0)
        {
            await RejectStepAsync(draft,
                "❓ Répondez avec le(s) NUMÉRO(S) des articles du menu (ex. « 1 » ou « 1 2 »).");
            return;
        }

        var selected = indexes.Select(i => catalog[i - 1]).Distinct().ToList();
        draft.SelectProducts(selected);
        await _context.SaveChangesAsync();
        await SendAsync(draft.ClientWhatsAppNumber, "✅ Articles notés.\n" + AddressPrompt);
    }

    /// <summary>Catalogue courant du commerce (menu borné, ordre alphabétique stable).</summary>
    private Task<List<VendorProduct>> LoadCatalogAsync(Guid vendorId) =>
        _context.VendorProducts.AsNoTracking()
            .Where(p => p.VendorId == vendorId)
            .OrderBy(p => p.Name)
            .Take(MaxProductsInMenu)
            .ToListAsync();

    private static string BuildProductMenuText(IReadOnlyList<VendorProduct> products)
        => string.Join("\n", products.Select((p, i) => $"{i + 1}. {p.DisplayText}"));

    /// <summary>
    /// Commerces correspondant au nom ou au numéro cité par le client. La correspondance par
    /// nom ignore la casse, les accents et les espaces ; la correspondance par numéro compare
    /// les 8 derniers chiffres (même logique « même abonné » que le reste du produit).
    /// </summary>
    private async Task<List<VendorMatch>> FindVendorsAsync(string text)
    {
        // SameSubscriber n'est pas traduisible en SQL : on rapproche en mémoire (catalogue
        // de vendeurs volontairement petit et déjà chargé par le reste du produit).
        var vendors = await _context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Vendor)
            .Select(u => new { u.Id, u.Username, u.Zone, u.PhoneNumber })
            .ToListAsync();

        var phoneCandidate = PhonePattern.Match(StripSeparators(text)).Value;
        if (phoneCandidate.Length > 0)
        {
            var byPhone = vendors
                .Where(v => LastEightDigitsMatch(v.PhoneNumber, phoneCandidate))
                .Select(v => new VendorMatch(v.Id, v.Username, v.Zone))
                .ToList();
            if (byPhone.Count > 0)
                return byPhone.Take(MaxVendorChoices).ToList();
        }

        var needle = Compact(text);
        if (needle.Length < 2)
            return [];

        return vendors
            .Where(v => Compact(v.Username).Contains(needle, StringComparison.Ordinal))
            .OrderBy(v => v.Username)
            .Select(v => new VendorMatch(v.Id, v.Username, v.Zone))
            .Take(MaxVendorChoices)
            .ToList();
    }

    /// <summary>Repère un numéro saisi avec séparateurs (« 07 08 09 10 11 » → « 0708091011 »).</summary>
    private static string StripSeparators(string text)
        => new(text.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray());

    /// <summary>Deux numéros partagent-ils leurs 8 derniers chiffres (même abonné) ?</summary>
    private static bool LastEightDigitsMatch(string? a, string b)
    {
        var da = PhoneNumberNormalizer.DigitsOnly(a);
        var db = PhoneNumberNormalizer.DigitsOnly(b);
        return da.Length >= 8 && db.Length >= 8 && da[^8..] == db[^8..];
    }

    /// <summary>Minuscules sans accents ni séparateurs (« Chez Thalia » ↔ « ChezThalia »).</summary>
    private static string Compact(string input)
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>Numéros du menu cités par le client (« 1 », « 1 2 », « 1,3 ») → indices 1..max.</summary>
    private static List<int> ParseMenuIndexes(string text, int max)
    {
        var result = new List<int>();
        foreach (Match match in Regex.Matches(text, @"\d+"))
        {
            if (int.TryParse(match.Value, out var number)
                && number >= 1 && number <= max
                && !result.Contains(number))
            {
                result.Add(number);
            }
        }

        return result;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    /// <summary>Commerce retenu par le bot (identifiant + libellé + zone pour l'affichage).</summary>
    private sealed record VendorMatch(Guid Id, string Username, string? Zone);

    /// <summary>
    /// Étape finale : le client a donné son lieu de livraison → la commande réelle est créée au
    /// compte du vendeur (statut « en attente de confirmation »). Le vendeur confirme ensuite
    /// depuis son espace / WhatsApp : la diffusion aux livreurs suit la confirmation.
    /// </summary>
    private async Task CompleteOrderAsync(ClientOrderDraft draft, string text)
    {
        var address = Truncate(text.Trim(), MaxAddressLength);
        if (address.Length < 3)
        {
            await RejectStepAsync(draft,
                "❓ Précisez le lieu de livraison (quartier + repère, ex. « Marcory, rue Princesse »).");
            return;
        }

        var vendor = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == draft.VendorUserId && u.Role == UserRole.Vendor);
        if (vendor is null)
        {
            draft.Cancel();
            await _context.SaveChangesAsync();
            await SendAsync(draft.ClientWhatsAppNumber,
                "😕 Ce commerce n'est plus disponible — réessayez avec COMMANDE.");
            return;
        }

        draft.SetAddress(address);
        var lines = await BuildLinesAsync(vendor.Id, draft.GetSelectedProducts());
        var description = BuildOrderDescription(draft.Description, lines, address);
        var vendorPhone = vendor.PhoneNumber ?? string.Empty;

        // Catalogue → commande enrichie de lignes (montant calculé) ; sinon mode texte libre.
        var order = lines.Count > 0
            ? new Order("Client", draft.ClientWhatsAppNumber, vendorPhone, vendor.Id, lines, description)
            : new Order("Client", draft.ClientWhatsAppNumber, vendorPhone, description, 0m);

        order.LinkVendor(vendor.Id);
        _context.Orders.Add(order);

        foreach (var line in lines)
        {
            line.AttachToOrder(order.Id);
            _context.OrderLines.Add(line);
        }

        draft.Complete(order.Id);
        await _context.SaveChangesAsync();

        var code = order.Id.ToString("N")[..8].ToUpperInvariant();
        await SendAsync(draft.ClientWhatsAppNumber,
            $"✅ Commande #{code} transmise à {vendor.Username} !\n" +
            $"🛒 {draft.Description}\n📍 {address}\n" +
            "Le commerce confirme, puis un livreur est recherché. ⚡");
        await SendAsync(vendorPhone,
            $"🛎️ Nouvelle commande client #{code} :\n{description}\n" +
            "Ouvrez l'application pour la confirmer.");

        _logger.LogInformation(
            "Bot commande : commande {OrderId} créée pour {Vendor} via WhatsApp ({Lines} ligne(s)).",
            order.Id, vendor.Username, lines.Count);
    }

    /// <summary>
    /// Reconstruit les lignes de commande depuis le catalogue courant (le panier du brouillon
    /// ne stocke que les identifiants). Un produit retiré entre-temps est simplement ignoré :
    /// la commande reste valide avec les autres articles.
    /// </summary>
    private async Task<List<OrderLine>> BuildLinesAsync(Guid vendorId, IReadOnlyList<Guid> selectedProductIds)
    {
        var lines = new List<OrderLine>();
        if (selectedProductIds.Count == 0)
            return lines;

        var products = await _context.VendorProducts.AsNoTracking()
            .Where(p => p.VendorId == vendorId && selectedProductIds.Contains(p.Id))
            .ToListAsync();

        foreach (var productId in selectedProductIds)
        {
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product is null)
                continue;

            lines.Add(new OrderLine(product.Id, product.Name, product.Emoji, product.Description,
                quantity: 1, unitPrice: product.Price));
        }

        return lines;
    }

    /// <summary>Description de commande : article(s) libre(s), lignes catalogue puis adresse.</summary>
    private static string BuildOrderDescription(string? items, IReadOnlyList<OrderLine> lines, string address)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(items))
            builder.Append(items.Trim());

        foreach (var line in lines)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(line.DisplayText);
        }

        if (builder.Length > 0)
            builder.Append('\n');
        builder.Append("📍 Livraison : ").Append(address);
        return builder.ToString();
    }

    /// <summary>Réponse WhatsApp best-effort : un incident d'envoi ne casse jamais la conversation.</summary>
    private async Task SendAsync(string phone, string message)
    {
        try
        {
            await _whatsApp.SendTextMessageAsync(phone, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot commande : réponse impossible vers {Phone}.", phone);
        }
    }
}
