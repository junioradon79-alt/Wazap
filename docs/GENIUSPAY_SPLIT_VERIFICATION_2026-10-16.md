# GeniusPay : vérification de la répartition vendeur / livreur

Vérification du 16 octobre 2026 : code local et documentation publique officielle. Aucun paiement réel, transfert, contact avec le support ou accès au compte marchand de production n'a été effectué.

## Conclusion

**L'intégration actuelle de WAZAP ne déclenche pas de répartition bancaire ou Mobile Money quasi instantanée vers le vendeur et le livreur.** Elle calcule et conserve les montants dus. La capacité de GeniusPay à exécuter un tel split sur le compte WAZAP n'est pas établie par la documentation publique consultée.

## Ce que fait le projet

- Le QR de paiement ouvre la page de suivi WAZAP avec `payer=1&mode=split` ; il ne contient aucune instruction de virement à deux bénéficiaires (`ClientOrdersController.GetPaymentQr`).
- L'initiation envoie à GeniusPay un montant global, une devise et des métadonnées. `vendor_id` est une métadonnée de rapprochement ; ce n'est pas une instruction de règlement. La requête ne fournit ni deux comptes bénéficiaires, ni leurs montants ou leurs identifiants marchands (`GeniusPayPaymentService`).
- À réception d'un webhook valide, WAZAP enregistre le paiement et les montants dus dans `VendorPayoutDue` et `RiderPayoutDue` (`ClientPaymentService`).
- Le prestataire de reversement enregistré est `ManualPayoutService`. Aucun connecteur effectuant les deux transferts sortants n'est branché.

La confirmation d'encaissement et les deux crédits effectifs aux bénéficiaires sont donc des étapes distinctes.

## Ce que documente GeniusPay

La [documentation API marchand](https://geniuspay.ci/docs/api) présente la création d'un paiement, un checkout, des notifications de paiement, un solde marchand disponible/en attente et des événements de retrait (`cashout.requested`, `cashout.approved`, `cashout.completed`, `cashout.failed`).

Elle ne décrit pas, dans les pages consultées, une instruction de paiement avec deux bénéficiaires vendeur/livreur, les paramètres d'un reversement automatisé multi-bénéficiaire ni un engagement de délai de quelques secondes. La présence d'événements de retrait ne suffit pas à établir une telle capacité. Une offre privée ou une activation spécifique au compte restent possibles et doivent être confirmées par GeniusPay.

Le [site officiel](https://geniuspay.ci/) se présente comme un service d'intégration technique des API de paiement et précise ne pas être lui-même un agrégateur ou un établissement de paiement. Les capacités de l'opérateur sous-jacent doivent donc également être clarifiées.

**Rectification :** l'ancien commentaire du projet affirmant qu'aucune API de reversement n'existe chez GeniusPay n'était pas suffisamment étayé. Il a été remplacé par le constat vérifiable : aucun connecteur de reversement sortant n'est intégré à WAZAP.

## Points à faire confirmer avant une intégration

1. Un encaissement unique peut-il créditer deux bénéficiaires tiers indépendants, dont les bénéficiaires sont différents à chaque course ? Split natif ou deux transferts après encaissement ?
2. Quelles API, autorisations, exigences d'identification et options sont disponibles sur le compte marchand WAZAP en Côte d'Ivoire, pour chaque opérateur utilisé ?
3. Quels délais mesurés/contractuels séparent paiement confirmé, fonds disponibles et réception effective sur chacun des deux comptes ?
4. Quels frais, limites, réserves ou besoins de préfinancement s'appliquent aux transferts ?
5. Comment sont traités l'idempotence, les notifications de versement, l'échec d'un seul des deux transferts, les remboursements et les contestations ?

La validation opérationnelle nécessitera ensuite une démonstration sandbox, puis, avec autorisation, une transaction réelle dont les heures de réception chez les deux bénéficiaires sont mesurées. Une simulation ou un statut « paiement réussi » ne prouve pas le versement effectif.
