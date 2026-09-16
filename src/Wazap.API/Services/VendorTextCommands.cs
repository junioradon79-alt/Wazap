using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Commandes texte du <b>vendeur</b> reçues sur WhatsApp, extraites du contrôleur webhook (P2 / C-13) :
///
///  - <c>LIVRAISON &lt;détail&gt; à &lt;adresse&gt;</c> : demande de course à la demande — <b>consomme
///    un crédit</b> et diffuse immédiatement aux livreurs proches ;
///  - <c>SINISTRE &lt;code&gt;</c> : déclaration de colis perdu/volé (Garantie Colis Sûr) ;
///  - <c>PRODUITS</c> / <c>PRODUIT &lt;nom&gt; | &lt;prix&gt; [| &lt;emoji&gt;]</c> /
///    <c>SUPPRIMER PRODUIT &lt;n°&gt;</c> : catalogue qui alimente le menu numéroté du bot client.
///
/// Aucune de ces commandes n'était couverte par un test : elles vivent pourtant sur le chemin de
/// l'argent (débit d'un crédit à la demande de course) et de la garantie commerciale. Les sortir du
/// contrôleur permet de les tester directement, sans payload ni signature.
/// </summary>
public sealed class VendorTextCommands
{
    private readonly ApplicationDbContext _context;
    private readonly OrderService _orderService;
    private readonly VendorProductService _products;
    private readonly ColisSurService _colisSur;

    public VendorTextCommands(
        ApplicationDbContext context,
        OrderService orderService,
        VendorProductService products,
        ColisSurService colisSur)
    {
        _context = context;
        _orderService = orderService;
        _products = products;
        _colisSur = colisSur;
    }

    /// <summary>
    /// Reconnaît les messages pris en charge ici. Aligné sur le comportement d'origine : mot seul ou
    /// suivi d'un espace (« LIVRAISONNE » ne doit pas déclencher une demande de course).
    /// </summary>
    public static bool Matches(string upperText)
        => upperText == "LIVRAISON" || upperText.StartsWith("LIVRAISON ")
           || upperText == "SINISTRE" || upperText.StartsWith("SINISTRE ")
           || upperText == "PRODUITS" || upperText.StartsWith("PRODUITS ")
           || upperText == "PRODUIT" || upperText.StartsWith("PRODUIT ")
           || upperText.StartsWith("SUPPRIMER PRODUIT");

    /// <summary>Traite la commande. <paramref name="reply"/> envoie la réponse WhatsApp au vendeur.</summary>
    public async Task HandleAsync(User user, string command, Func<User, string, Task> reply)
    {
        var upper = command.ToUpperInvariant();

        if (upper == "LIVRAISON" || upper.StartsWith("LIVRAISON "))
        {
            await HandleDispatchAsync(user, command, reply);
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

    /// <summary>
    /// Affichage catalogue identique au menu du bot client (<see cref="VendorProduct.DisplayText"/>).
    /// </summary>
    private static string ProductDisplayText(VendorProductDto product)
        => string.IsNullOrEmpty(product.Emoji)
            ? $"{product.Name} — {product.Price:N0} FCFA"
            : $"{product.Emoji} {product.Name} — {product.Price:N0} FCFA";
}
