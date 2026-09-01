using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services
{
    /// <summary>
    /// Paiement simulé pour les tests : réussit toujours après 2 secondes
    /// et génère une référence « PAY-XXXX-YYYY ».
    /// </summary>
    public sealed class MockPaymentService : IPaymentService
    {
        public async Task<PaymentResult> RequestPaymentAsync(Guid vendorId, string packName, decimal amount)
        {
            // Simule la latence d'un fournisseur Mobile Money.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var reference = $"PAY-{Random.Shared.Next(1000, 9999)}-{Random.Shared.Next(1000, 9999)}";

            return new PaymentResult(
                Success: true,
                TransactionReference: reference,
                PaymentLink: null,
                ErrorMessage: null);
        }
    }
}
