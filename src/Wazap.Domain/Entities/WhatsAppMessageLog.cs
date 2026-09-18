namespace Wazap.Domain.Entities;

/// <summary>
/// Trace chaque message WhatsApp émis ou reçu (chantier T2 / C1).
/// Permet l'audit des flux, la détection des échecs hors fenêtre 24h et
/// le calcul du coût unitaire réel par course / par catégorie Meta.
/// </summary>
public class WhatsAppMessageLog
{
    public Guid Id { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public string RecipientPhone { get; private set; } = default!;
    public string? SenderPhone { get; private set; }
    public string Direction { get; private set; } = default!; // "Outbound" ou "Inbound"
    public string MessageType { get; private set; } = default!; // "Template", "Text", "Interactive", "Media", etc.
    public string? TemplateName { get; private set; }
    public string? Category { get; private set; } // "utility", "marketing", "authentication", "service"
    public string Provider { get; private set; } = default!; // "Meta", "WhatChimp"
    public string? ProviderMessageId { get; private set; } // Identifiant passerelle (wamid...)
    public string Status { get; private set; } = default!; // "Sent", "Failed", "Received", "Delivered"
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public decimal? EstimatedCostFcfa { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private WhatsAppMessageLog() { }

    public WhatsAppMessageLog(
        string recipientPhone,
        string direction,
        string messageType,
        string provider,
        string status,
        string? templateName = null,
        string? category = null,
        string? providerMessageId = null,
        int? errorCode = null,
        string? errorMessage = null,
        decimal? estimatedCostFcfa = null,
        Guid? orderId = null,
        Guid? recipientUserId = null,
        string? senderPhone = null,
        DateTime? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
            throw new ArgumentException("Le numéro du destinataire est requis.", nameof(recipientPhone));
        if (string.IsNullOrWhiteSpace(direction))
            throw new ArgumentException("La direction du message est requise.", nameof(direction));
        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("Le type de message est requis.", nameof(messageType));
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Le fournisseur de messagerie est requis.", nameof(provider));
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Le statut du message est requis.", nameof(status));

        Id = Guid.NewGuid();
        RecipientPhone = recipientPhone;
        Direction = direction;
        MessageType = messageType;
        Provider = provider;
        Status = status;
        TemplateName = templateName;
        Category = category;
        ProviderMessageId = providerMessageId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage?.Length > 500 ? errorMessage[..500] : errorMessage;
        EstimatedCostFcfa = estimatedCostFcfa;
        OrderId = orderId;
        RecipientUserId = recipientUserId;
        SenderPhone = senderPhone;
        CreatedAt = createdAt ?? DateTime.UtcNow;
    }

    public void UpdateStatus(string status, int? errorCode = null, string? errorMessage = null)
    {
        Status = status;
        if (errorCode.HasValue) ErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            ErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage;
    }
}
