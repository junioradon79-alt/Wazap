namespace Wazap.Domain.Enums;

/// <summary>
/// Statut d'une transaction d'encaissement (achat de pack de crédits ou panier client).
/// </summary>
public enum TransactionStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
