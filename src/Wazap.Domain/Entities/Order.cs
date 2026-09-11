using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

public class Order
{
    private OrderStatus _status;

    public Guid Id { get; private set; }
    public string ClientName { get; private set; } = default!;
    public string ClientWhatsAppNumber { get; private set; } = default!;
    public string VendorWhatsAppNumber { get; private set; } = default!;
    public string? RiderWhatsAppNumber { get; private set; }
    public Guid? VendorUserId { get; private set; }
    public Guid? RiderUserId { get; private set; }

    /// <summary>Lot de livraison groupée auquel appartient la commande (null = commande seule).</summary>
    public Guid? BatchId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? VendorConfirmedAt { get; private set; }
    public DateTime? RiderAssignedAt { get; private set; }
    public DateTime? PickedUpAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    // Suivi acheteur (PWA) : la commande attend les coordonnées du client avant la
    // recherche des livreurs. Coordonnées de livraison (client) une fois validées.
    public bool RequiresClientCoordinates { get; private set; }
    public double? ClientLatitude { get; private set; }
    public double? ClientLongitude { get; private set; }
    public string? ClientAddress { get; private set; }

    // Preuve de livraison : code à 4 chiffres remis au client à l'assignation du livreur,
    // que le livreur doit restituer pour clôturer la course (« LIVRE <code> CODE <4 chiffres> »).
    public string? DeliveryCode { get; private set; }
    public DateTime? DeliveryCodeVerifiedAt { get; private set; }
    public int DeliveryCodeAttempts { get; private set; }

    // Preuve photo de livraison : photo du colis envoyée par le livreur via WhatsApp
    // (au retrait ou à la remise), stockée chiffrée au repos. La provenance est gardée.
    public string? DeliveryProofPhotoFileName { get; private set; }
    public string? DeliveryProofPhotoSourceUrl { get; private set; }
    public DateTime? DeliveryProofPhotoReceivedAt { get; private set; }

    /// <summary>Tentatives erronées au-delà desquelles le code est bloqué (anti-force brute).</summary>
    public const int MaxDeliveryCodeAttempts = 5;

    public OrderStatus Status
    {
        get => _status;
        private set => _status = value;
    }

        // Constructeur privé pour EF Core
    private Order() { }

    public List<OrderLine> OrderLines { get; private set; } = new();

    // Constructeur public pour la création (mode texte libre)
    public Order(string clientName, string clientWhatsAppNumber, string vendorWhatsAppNumber, string description, decimal amount)
    {
        Id = Guid.NewGuid();
        ClientName = clientName;
        ClientWhatsAppNumber = clientWhatsAppNumber;
        VendorWhatsAppNumber = vendorWhatsAppNumber;
        Description = description;
        Amount = amount;
        CreatedAt = DateTime.UtcNow;
        _status = OrderStatus.PendingVendorConfirmation;
    }

    // Constructeur public pour la création (mode catalogue produit)
    public Order(string clientName, string clientWhatsAppNumber, string vendorWhatsAppNumber, Guid vendorUserId, List<OrderLine> lines, string description)
    {
        Id = Guid.NewGuid();
        ClientName = clientName;
        ClientWhatsAppNumber = clientWhatsAppNumber;
        VendorWhatsAppNumber = vendorWhatsAppNumber;
        VendorUserId = vendorUserId;
        Description = description;
        foreach (var line in lines)
            AddLine(line);
        CreatedAt = DateTime.UtcNow;
        _status = OrderStatus.PendingVendorConfirmation;
    }

    public void AddLine(OrderLine line)
    {
        line.AttachToOrder(Id);
        OrderLines.Add(line);
        CalculateTotal();
    }

    private void CalculateTotal() => Amount = OrderLines.Sum(l => l.TotalPrice);

    public void ConfirmByVendor()
    {
        if (_status != OrderStatus.PendingVendorConfirmation)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de confirmer.");
        _status = OrderStatus.VendorConfirmed;
        VendorConfirmedAt = DateTime.UtcNow;
    }

    public void AwaitRiderAcceptance()
    {
        if (_status != OrderStatus.VendorConfirmed)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en attente livreur.");
        _status = OrderStatus.AwaitingRiderAcceptance;
    }

    public void AssignRider(string riderWhatsAppNumber)
    {
        if (_status != OrderStatus.AwaitingRiderAcceptance)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible d'assigner un livreur.");
        RiderWhatsAppNumber = riderWhatsAppNumber;
        _status = OrderStatus.RiderAssigned;
        RiderAssignedAt = DateTime.UtcNow;
    }

    public void MarkReadyForPickup()
    {
        if (_status != OrderStatus.RiderAssigned)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en prêt.");
        _status = OrderStatus.ReadyForPickup;
    }

    public void MarkPickedUp()
    {
        if (_status != OrderStatus.ReadyForPickup)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de marquer récupérée.");
        _status = OrderStatus.PickedUp;
        PickedUpAt = DateTime.UtcNow;
    }

    public void MarkInTransit()
    {
        if (_status != OrderStatus.PickedUp)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en transit.");
        _status = OrderStatus.InTransit;
    }

    public void MarkDelivered()
    {
        if (_status != OrderStatus.InTransit)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de marquer livrée.");
        _status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (_status == OrderStatus.Delivered || _status == OrderStatus.InTransit)
            throw new InvalidOperationException($"Impossible d'annuler une commande en statut {_status}.");
        _status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }
    public void LinkVendor(Guid vendorUserId) => VendorUserId = vendorUserId;

    public void LinkRider(Guid riderUserId) => RiderUserId = riderUserId;

    /// <summary>
    /// Active le parcours acheteur : la course attendra les coordonnées du client
    /// (validées sur la page de suivi) avant de déclencher la recherche des livreurs.
    /// </summary>
    public void EnableBuyerTracking() => RequiresClientCoordinates = true;

    /// <summary>
    /// Enregistre les coordonnées de livraison fournies par le client (page de suivi).
    /// </summary>
    public void SetClientCoordinates(double latitude, double longitude, string? address)
    {
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude invalide.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude invalide.");

        ClientLatitude = latitude;
        ClientLongitude = longitude;
        ClientAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    /// <summary>
    /// Rattache la commande à un lot de livraison groupée (groupage par vendeur + fenêtre).
    /// </summary>
    public void JoinBatch(Guid batchId) => BatchId = batchId;

    /// <summary>
    /// Génère (une seule fois) le code de livraison à 4 chiffres remis au client.
    /// Idempotent : un code déjà attribué est conservé, pour qu'une re-notification
    /// n'invalide jamais le code que le client a sous les yeux.
    /// </summary>
    public string EnsureDeliveryCode()
    {
        DeliveryCode ??= RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture);
        return DeliveryCode;
    }

    /// <summary>
    /// Vérifie le code annoncé par le livreur. Une erreur incrémente le compteur de
    /// tentatives ; au-delà de <see cref="MaxDeliveryCodeAttempts"/> le code est bloqué
    /// (même correct) et seule une clôture par le vendeur ou l'admin reste possible.
    /// </summary>
    public DeliveryCodeResult VerifyDeliveryCode(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(DeliveryCode))
            return DeliveryCodeResult.NotSet;

        if (DeliveryCodeAttempts >= MaxDeliveryCodeAttempts)
            return DeliveryCodeResult.Locked;

        var submitted = Encoding.ASCII.GetBytes((candidate ?? string.Empty).Trim());
        var expected = Encoding.ASCII.GetBytes(DeliveryCode);

        if (!CryptographicOperations.FixedTimeEquals(submitted, expected))
        {
            DeliveryCodeAttempts++;
            return DeliveryCodeResult.Mismatch;
        }

        DeliveryCodeVerifiedAt = DateTime.UtcNow;
        return DeliveryCodeResult.Ok;
    }

    /// <summary>
    /// Enregistre la photo du colis prise par le livreur (preuve de livraison). Acceptée
    /// uniquement pendant la course (assignée ou en transit) : une course clôturée
    /// n'accepte plus de preuve après coup.
    /// </summary>
    public void SubmitDeliveryProofPhoto(string fileName, string? sourceUrl)
    {
        if (Status is not (OrderStatus.RiderAssigned or OrderStatus.InTransit))
            throw new InvalidOperationException(
                "Photo de livraison refusée : aucune course en cours (assignée ou en transit).");

        DeliveryProofPhotoFileName = fileName;
        DeliveryProofPhotoSourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim();
        DeliveryProofPhotoReceivedAt = DateTime.UtcNow;
    }

    /// <summary>Efface la photo de preuve (rétention) ; la date de réception reste tracée.</summary>
    public void PurgeDeliveryProofPhoto()
    {
        DeliveryProofPhotoFileName = null;
        DeliveryProofPhotoSourceUrl = null;
    }
}
