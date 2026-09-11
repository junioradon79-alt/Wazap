using System.Text.Json;
using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Brouillon de commande construit par le bot WhatsApp conversationnel avec un client
/// (article → commerce → adresse). Un seul brouillon actif par numéro ; expiré au-delà
/// de la fenêtre configurée ; la commande n'est créée qu'à la fin de la conversation.
/// </summary>
public class ClientOrderDraft
{
    public Guid Id { get; private set; }
    public string ClientWhatsAppNumber { get; private set; } = default!;

    /// <summary>Étape courante de la conversation.</summary>
    public ClientOrderDraftStage Stage { get; private set; }

    /// <summary>Ce que le client veut commander (texte libre).</summary>
    public string? Description { get; private set; }

    /// <summary>Commerce choisi (résolu du brouillon vers le compte vendeur).</summary>
    public Guid? VendorUserId { get; private set; }

    /// <summary>Candidats numérotés proposés au client (JSON « [guid, guid] »).</summary>
    public string? VendorCandidates { get; private set; }

        /// <summary>Produits du catalogue du commerce retenu (JSON « [guid, guid] »), pour le menu.</summary>
    public string? ProductLineIds { get; private set; }

    /// <summary>Produits choisis par le client (JSON « [guid, guid] ») : panier avant commande.</summary>
    public string? SelectedProductIds { get; private set; }

    /// <summary>Quartier/adresse de livraison donnés par le client.</summary>
    public string? Address { get; private set; }

    /// <summary>Réponses consécutives inattendues à l'étape courante (anti-boucle).</summary>
    public int StageAttempts { get; private set; }

    /// <summary>Commande créée à la fin de la conversation.</summary>
    public Guid? OrderId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    private ClientOrderDraft() { }

    public ClientOrderDraft(string clientWhatsAppNumber, DateTime utcNow, DateTime expiresAt)
    {
        Id = Guid.NewGuid();
        ClientWhatsAppNumber = clientWhatsAppNumber;
        Stage = ClientOrderDraftStage.AwaitingItems;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        ExpiresAt = expiresAt;
    }

    /// <summary>La conversation est encore active (étape en cours et non expirée) ?</summary>
    public bool IsActive(DateTime utcNow)
    {
        if (utcNow >= ExpiresAt) return false;
        return Stage is ClientOrderDraftStage.AwaitingItems
            or ClientOrderDraftStage.AwaitingVendor
            or ClientOrderDraftStage.AwaitingVendorChoice
            or ClientOrderDraftStage.AwaitingProductChoice
            or ClientOrderDraftStage.AwaitingAddress;
    }

    /// <summary>Étape 1 validée : article enregistré, passage au commerce.</summary>
    public void SubmitItems(string description)
    {
        Description = description;
        Stage = ClientOrderDraftStage.AwaitingVendor;
        ResetAttempts();
    }

    /// <summary>Dernière étape validée : le client a donné son lieu de livraison.</summary>
    public void SetAddress(string address)
    {
        Address = address.Trim();
        Touch();
    }

    /// <summary>Plusieurs commerces correspondent : liste numérotée proposée au client.</summary>
    public void SetVendorCandidates(IReadOnlyList<Guid> candidateIds)
    {
        VendorCandidates = JsonSerializer.Serialize(candidateIds);
        Stage = ClientOrderDraftStage.AwaitingVendorChoice;
        ResetAttempts();
    }

    /// <summary>Commerce retenu (choix unique ou sélection) → on vérifie le catalogue.</summary>
    public void SetVendor(Guid vendorUserId, IReadOnlyList<Guid> productIds)
    {
        VendorUserId = vendorUserId;
        VendorCandidates = null;
        SetProductCatalog(productIds);
    }

    /// <summary>Candidats proposés au client (vide si un seul commerce a été résolu).</summary>
    public List<Guid> GetVendorCandidates()
        => string.IsNullOrWhiteSpace(VendorCandidates)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(VendorCandidates) ?? [];

    /// <summary>Produits du catalogue proposés au client (menu).</summary>
    public List<Guid> GetProductCatalog()
        => string.IsNullOrWhiteSpace(ProductLineIds)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(ProductLineIds) ?? [];

    /// <summary>
    /// Le commerce retenu possède un catalogue : on propose le menu (étape produit). S'il n'a
    /// aucun produit, on passe directement à l'adresse (mode texte libre conservé).
    /// </summary>
    public void SetProductCatalog(IReadOnlyList<Guid> productIds)
    {
        ProductLineIds = productIds is { Count: > 0 } ? JsonSerializer.Serialize(productIds) : null;
        Stage = productIds is { Count: > 0 }
            ? ClientOrderDraftStage.AwaitingProductChoice
            : ClientOrderDraftStage.AwaitingAddress;
        ResetAttempts();
    }

    /// <summary>Le client a sélectionné des produits du menu : on enregistre le panier.</summary>
    public void SelectProducts(IReadOnlyList<Guid> selectedProductIds)
    {
        SelectedProductIds = JsonSerializer.Serialize(selectedProductIds);
        Stage = ClientOrderDraftStage.AwaitingAddress;
        ResetAttempts();
    }

    /// <summary>Produits choisis par le client (panier).</summary>
    public List<Guid> GetSelectedProducts()
        => string.IsNullOrWhiteSpace(SelectedProductIds)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(SelectedProductIds) ?? [];

    /// <summary>Fin de la conversation : la commande est créée.</summary>
    public void Complete(Guid orderId)
    {
        OrderId = orderId;
        Stage = ClientOrderDraftStage.Completed;
        Touch();
    }

    /// <summary>Annulation par le client ou abandon (boucle, expiration).</summary>
    public void Cancel()
    {
        Stage = ClientOrderDraftStage.Cancelled;
        Touch();
    }

    /// <summary>La réponse reçue ne correspond pas au format attendu de l'étape.</summary>
    public void RegisterInvalidAttempt() => StageAttempts++;

    /// <summary>Trop de réponses inattendues : on abandonne proprement la conversation.</summary>
    public bool ExceededAttempts(int max) => StageAttempts >= max;

    private void ResetAttempts() => StageAttempts = 0;

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
