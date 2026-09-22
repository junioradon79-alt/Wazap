namespace Wazap.Application.Abstractions;

/// <summary>
/// Résultat de l'analyse OCR d'une pièce d'identité (CNI / Passeport).
/// </summary>
public sealed record OcrIdentityResult(
    bool Success,
    string? FullName,
    string? IdNumber,
    string? RawText,
    string? Error = null);

/// <summary>
/// Service de reconnaissance optique de caractères (OCR) pour les pièces d'identité (CNI ivoiriennes).
/// </summary>
public interface IOcrService
{
    Task<OcrIdentityResult> ParseIdentityCardAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default);
}
