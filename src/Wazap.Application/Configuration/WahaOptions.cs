namespace Wazap.Application.Configuration;

/// <summary>
/// Connexion à la passerelle WhatsApp HTTP API (WAHA - devlikeapro/waha).
/// Permet de piloter WhatsApp Web/Multi-Device de façon découplée, sans dépendance
/// aux approbations de templates Meta ni restriction de la fenêtre de 24h.
/// Section de configuration « Waha ».
/// </summary>
public sealed class WahaOptions
{
    public const string SectionName = "Waha";

    /// <summary>
    /// Bascule d'infrastructure : à <c>true</c>, la passerelle WAHA est utilisée pour
    /// l'envoi et la réception de tous les messages WhatsApp (prioritaire sur Meta et WhatChimp).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// URL de base de l'instance WAHA (ex: http://localhost:3000 ou http://waha:3000 sur Docker).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Clé API configurée dans WAHA (en-tête X-Api-Key). Optionnel si WAHA tourne sans authentification locale.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Nom de la session WhatsApp configurée dans WAHA (par défaut « default »).
    /// </summary>
    public string SessionName { get; set; } = "default";

    /// <summary>
    /// Jeton secret attendu lors des appels de webhook entrant depuis WAHA (en-tête X-Api-Key ou secret).
    /// </summary>
    public string? WebhookSecret { get; set; }
}
