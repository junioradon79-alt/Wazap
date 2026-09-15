namespace Wazap.Application.Dtos
{
    /// <summary>
    /// Demande d'achat d'un pack prioritaire livreur (« pack prioritaire »).
    /// </summary>
    public class BuyRiderPriorityRequest
    {
        public Guid RiderId { get; set; }
        public string PackName { get; set; } = default!;
    }

    /// <summary>Pack prioritaire du catalogue exposé par l'API.</summary>
    public sealed record RiderPriorityPackDto(string Name, decimal Price, int Days);

    /// <summary>
    /// État de priorité d'un livreur + catalogue des packs : le livreur (ou l'admin) sait si sa
    /// priorité est active, jusqu'à quand, et ce qu'il peut acheter.
    /// </summary>
    public sealed record RiderPriorityStatusDto(
        Guid RiderId,
        bool Active,
        DateTime? UntilUtc,
        int RemainingDays,
        bool PurchaseEnabled,
        IReadOnlyList<RiderPriorityPackDto> Packs);
}