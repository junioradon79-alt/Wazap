namespace Wazap.Application.Abstractions;

/// <summary>
/// Récupération d'un média reçu sur WhatsApp — aujourd'hui la photo de la pièce
/// d'identité d'un livreur (« Garantie Colis Sûr »), envoyée directement depuis la
/// messagerie. Une seule méthode : l'implémentation sait résoudre aussi bien une URL
/// directe fournie par la passerelle qu'un identifiant de média.
/// </summary>
public interface IWhatsAppMediaDownloader
{
    /// <summary>
    /// Télécharge le média entrant. Retourne <c>null</c> s'il est irrécupérable (URL
    /// morte, identifiant inconnu, taille excessive) : l'appelant répond alors une
    /// erreur explicite au livreur au lieu de laisser le message se perdre en silence.
    /// </summary>
    Task<(byte[] Content, string FileName)?> TryDownloadAsync(
        string? url,
        string? mediaId,
        string? mimeType,
        CancellationToken ct = default);
}
