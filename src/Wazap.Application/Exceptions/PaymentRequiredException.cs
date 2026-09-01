namespace Wazap.Application.Exceptions;

/// <summary>
/// Levée lorsque l'action requiert un paiement (crédits insuffisants).
/// Mappée sur HTTP 402 Payment Required.
/// </summary>
public sealed class PaymentRequiredException : Exception
{
    public PaymentRequiredException(string message) : base(message)
    {
    }
}
