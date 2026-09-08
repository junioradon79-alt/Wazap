namespace Wazap.Application.Configuration
{
    /// <summary>
    /// Paiement du panier client par Mobile Money via l'agrégateur (section « ClientPayments »).
    /// Non bloquant par défaut : le client reste libre de payer en espèces à la livraison ;
    /// <see cref="RequirePaymentBeforeDispatch"/> gèle la diffusion des livreurs tant que
    /// aucun paiement n'est complété pour la commande.
    /// </summary>
    public sealed class ClientPaymentOptions
    {
        public const string SectionName = "ClientPayments";

        /// <summary>Active le paiement client (endpoint + lien de paiement GeniusPay).</summary>
        public bool Enabled { get; set; }

        /// <summary>Commission WAZAP en pourcentage du panier (ex. 2 = 2 %).</summary>
        public decimal CommissionPercent { get; set; } = 2m;

        /// <summary>
        /// Diffusion des livreurs bloquée tant que le paiement client n'est pas complété.
        /// Désactivé par défaut (le cash à la livraison reste accepté).
        /// </summary>
        public bool RequirePaymentBeforeDispatch { get; set; }
    }
}