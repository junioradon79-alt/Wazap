namespace Wazap.Domain.Entities;

/// <summary>Dette de règlement et preuve de versement, distinctes de l'encaissement.</summary>
public class PaymentPayout
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public string BeneficiaryRole { get; private set; } = default!;
    public Guid? BeneficiaryUserId { get; private set; }
    public string? BeneficiaryPhone { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "XOF";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? Provider { get; private set; }
    public string? Evidence { get; private set; }
    public string IdempotencyKey => $"wazap-payout-{PaymentId:N}-{BeneficiaryRole}";
    private PaymentPayout() { }

    public PaymentPayout(Guid paymentId, string role, Guid? userId, string? phone, decimal amount)
    {
        if (paymentId == Guid.Empty || role is not ("Vendor" or "Rider") || amount <= 0 || decimal.Truncate(amount) != amount)
            throw new ArgumentException("Versement invalide.");
        Id = Guid.NewGuid(); PaymentId = paymentId; BeneficiaryRole = role;
        BeneficiaryUserId = userId; BeneficiaryPhone = phone; Amount = amount;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Confirm(Guid reviewer, string provider, string reference, string evidence, decimal amount)
    {
        if (ConfirmedAtUtc is not null) throw new InvalidOperationException("Versement déjà rapproché.");
        if (reviewer == Guid.Empty || amount != Amount || string.IsNullOrWhiteSpace(BeneficiaryPhone)
            || string.IsNullOrWhiteSpace(provider) || provider.Length > 80
            || string.IsNullOrWhiteSpace(reference) || reference.Length > 200
            || reference.StartsWith("DISB-", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1000)
            throw new ArgumentException("Montant exact, bénéficiaire, prestataire et preuve réelle obligatoires.");
        Provider = provider.Trim(); ProviderReference = reference.Trim(); Evidence = evidence.Trim();
        ConfirmedByUserId = reviewer; ConfirmedAtUtc = DateTime.UtcNow;
    }
}
