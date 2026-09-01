using Microsoft.Extensions.Configuration;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services
{
    /// <summary>
    /// Paiement simulé pour les tests : réussit toujours après 2 secondes et génère
    /// une référence « PAY-XXXX-YYYY ». Si « Payments:SimulateAsync » est activé,
    /// retourne un lien de paiement fictif (flux asynchrone comme GeniusPay).
    /// </summary>
    public sealed class MockPaymentService : IPaymentService
    {
        private readonly bool _simulateAsync;

        public MockPaymentService(IConfiguration? config = null)
        {
            _simulateAsync = string.Equals(config?["Payments:SimulateAsync"], "true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<PaymentResult> RequestPaymentAsync(
            Guid vendorId,
            string packName,
            decimal amount,
            string reference)
        {
            // Simule la latence d'un fournisseur Mobile Money.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var transactionReference = $"PAY-{Random.Shared.Next(1000, 9999)}-{Random.Shared.Next(1000, 9999)}";

            return new PaymentResult(
                Success: true,
                TransactionReference: transactionReference,
                PaymentLink: _simulateAsync ? $"https://checkout.geniuspay.test/{transactionReference}" : null,
                ErrorMessage: null);
        }

        public Task<PaymentStatusResult?> CheckPaymentStatusAsync(string reference)
            => Task.FromResult<PaymentStatusResult?>(null);
    }
}

