namespace Wazap.Application.Dtos;

/// <summary>Ligne d'un avis pour la page admin (numéro client toujours masqué).</summary>
public sealed class RiderRatingAdminDto
{
    public Guid RatingId { get; init; }
    public string OrderCode { get; init; } = default!;
    public Guid RiderId { get; init; }
    public string RiderName { get; init; } = default!;
    public int Score { get; init; }
    public string? Comment { get; init; }
    public string? Reply { get; init; }
    public DateTime? RepliedAt { get; init; }
    public string MaskedClientPhone { get; init; } = default!;
    public DateTime CreatedAt { get; init; }
}

/// <summary>Agrégat « moyenne + nombre d'avis » par livreur (page admin des avis).</summary>
public sealed class RiderRatingSummaryDto
{
    public Guid RiderId { get; init; }
    public string RiderName { get; init; } = default!;
    public double AverageScore { get; init; }
    public int RatingCount { get; init; }
}

/// <summary>Tableau de bord admin des avis : liste détaillée + synthèse par livreur.</summary>
public sealed class RiderRatingAdminBoardDto
{
    public IReadOnlyList<RiderRatingAdminDto> Ratings { get; init; } = [];
    public IReadOnlyList<RiderRatingSummaryDto> RiderSummaries { get; init; } = [];
}