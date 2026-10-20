# WAZAP — concrétiser le paiement unique et les deux versements rapides

Document de décision du 16 octobre 2026. Recherche sur les documentations officielles ; aucune ouverture de compte, activation payante, prise de contact ou transaction financière effectuée.

## Résultat recherché

Le client règle une seule fois depuis un QR dynamique. Le vendeur reçoit le net marchand et le livreur les frais de course sur leurs comptes vérifiés. Chacun dispose d'une confirmation réelle de réception. Le délai cible doit porter sur le crédit effectif des deux comptes.

Exemple hors frais : 25 000 FCFA de marchandise et 1 500 FCFA de livraison donnent deux versements nets de 25 000 et 1 500 FCFA. Les frais d'encaissement et de transfert doivent être chiffrés et leur payeur fixé avant la validation du client.

## Deux mécanismes possibles

**Split natif du prestataire.** Le prestataire connaît les deux bénéficiaires, reçoit les montants à répartir et gère leur règlement. C'est la voie à privilégier si son contrat couvre les bénéficiaires tiers, les opérateurs souhaités et le délai recherché. Un split de règlement bancaire peut néanmoins suivre un calendrier de plusieurs jours.

**Encaissement, puis deux transferts automatiques.** Après confirmation vérifiée, WAZAP déclenche deux opérations de payout. Cette voie est techniquement documentée chez des prestataires, mais chaque transfert possède son propre résultat. Elle exige un solde disponible chez le prestataire de versement. Si l'encaissement et les transferts passent par deux prestataires différents, il faut organiser le préfinancement et le rapprochement entre eux ; les fonds du premier ne deviennent pas automatiquement disponibles chez le second.

## Prestataires à qualifier

### GeniusPay : conserver l'existant si l'offre répond au besoin

L'encaissement est déjà intégré à WAZAP. La [documentation publique](https://geniuspay.ci/docs/api) présente paiements, solde et événements de retrait, mais les pages consultées ne décrivent pas le split dynamique vendeur/livreur recherché. Une offre spécifique au compte reste à confirmer.

Demander une documentation exploitable et une démonstration de deux bénéficiaires différents sur un même paiement, ainsi que les conditions de disponibilité des fonds. Si GeniusPay expose une fonction privée de reversement ou une passerelle compatible, vérifier ses permissions avant de développer.

### CinetPay : candidat prioritaire pour le multi-opérateur ivoirien

La [table des opérateurs de transfert](https://docs.cinetpay.com/api/1.0-fr/transfert/annexe) liste Orange, MTN, Moov et Wave en Côte d'Ivoire. Elle indique une plage de transfert de 200 à 1 500 000 FCFA.

L'[API de transfert](https://docs.cinetpay.com/api/1.0-fr/transfert/utilisation) documente les envois individuels/groupés, le solde disponible et la consultation du traitement. L'automatisation sans confirmation manuelle nécessite un accord avec CinetPay et une IP publique fixe autorisée. Les montants doivent être multiples de 5 FCFA ; le devis WAZAP actuel produisant parfois un net comme 24 818 FCFA doit donc être adapté explicitement, sans arrondi silencieux.

La [présentation Mass Payout](https://cinetpay.com/products/mass-payout) annonce des transferts en moins de cinq minutes et un traitement moyen de deux secondes. Ce sont des indications commerciales, qui ne prouvent pas le délai complet client → crédit des deux bénéficiaires. Le compte transfert doit être alimenté ; le transfert de fonds collectés vers ce compte est à préciser dans la qualification.

Deux variantes à comparer : CinetPay pour encaisser et verser, ou GeniusPay pour encaisser et CinetPay pour verser. La seconde conserve l'intégration existante mais exige une organisation explicite de trésorerie.

### Wave : pilote ciblé possible

L'[API Payout Wave](https://docs.wave.com/payout) permet les versements par numéro de téléphone et tente une exécution immédiate de l'opération. Elle prévoit idempotence, consultation du résultat et états `processing`, `succeeded`, `failed`. Le solde doit couvrir montants et frais ; le portefeuille professionnel et le bénéficiaire doivent être dans le même pays.

Qualifier l'activation de Checkout/Payout sur un compte professionnel ivoirien et l'autorisation de ce modèle de plateforme. Un pilote limité aux versements Wave est plus restreint que la promesse de paiement vers tout opérateur. Pour un commerçant professionnel, vérifier aussi la pertinence de l'[API B2B](https://docs.wave.com/b2b/payout/index.html).

### Paystack : split documenté, délai standard moins adapté

Le [multi-split](https://paystack.com/docs/payments/multi-split-payments/) permet une répartition dynamique entre sous-comptes. Le [calendrier standard ivoirien](https://support.paystack.com/fr/articles/3049346) indique toutefois deux à trois jours ouvrables pour les règlements automatiques. De plus, la [page Transfers](https://paystack.com/docs/transfers/) ne liste pas la Côte d'Ivoire parmi les pays disponibles pour cette fonctionnalité. Une offre particulière devrait être confirmée pour répondre au versement immédiat recherché.

## Recommandation

1. Qualifier le split natif ou le reversement réellement disponible sur le compte GeniusPay.
2. En parallèle, qualifier CinetPay pour couvrir les quatre opérateurs ivoiriens avec une API de versement effectivement activée.
3. Retenir le circuit où le montant encaissé est disponible le plus vite pour les deux versements, au coût total acceptable. Si les délais imposent une avance, chiffrer et approuver ce besoin de trésorerie avant le pilote.
4. Utiliser Wave comme pilote ciblé si les accès ivoiriens et les conditions commerciales permettent de tester plus vite ; garder les limites du pilote explicites.

Aucune API ne doit être inventée à partir d'un événement de retrait ou d'une page marketing. La documentation de l'offre activée et les résultats des tests priment pour le choix.

## Préparer l'intégration WAZAP

- Conserver le paiement confirmé comme état d'encaissement distinct de l'état des versements.
- Créer deux enregistrements durables de versement par paiement éligible : bénéficiaire, compte vérifié, montant net, devise, prestataire, référence et état.
- Réserver une clé d'idempotence stable par versement, réutilisée en cas de délai réseau ou reprise ; contrainte d'unicité par paiement et rôle bénéficiaire.
- Écrire les demandes de versement dans l'outbox avec la confirmation du paiement, puis les envoyer après validation de la transaction de base de données.
- Suivre séparément les deux réponses. Un succès vendeur et un échec livreur constituent un règlement partiel ; seule l'opération non aboutie est reprise.
- Conserver un état « résultat inconnu » pour un timeout. Interroger le prestataire avant toute réémission susceptible de doubler un paiement.
- Ne montrer « argent reçu » qu'avec la preuve de crédit final du prestataire. Garder références et heures de confirmation.
- Geler les deux bénéficiaires et les frais avant de déclencher le versement. En cas de changement de livreur, prévoir une règle explicite de réaffectation avant tout paiement.
- Prévoir alertes de solde bas, suivi des échecs, rapprochement et traitement des remboursements.

Les champs `VendorPayoutDue` et `RiderPayoutDue` constituent déjà une base de calcul ; ils ne remplacent pas les deux enregistrements de versement et leurs preuves.

## Moment du paiement

Pour le pilote, recommander un QR à la remise du colis : le vendeur, le livreur et le tarif sont alors connus. Si le client paie avant l'affectation, la part livreur doit rester en attente d'un bénéficiaire défini. La décision de payer le livreur avant ou après preuve de livraison reste une règle produit à arrêter explicitement.

## Coût et trésorerie

Coût total par commande = frais d'encaissement + frais du versement vendeur + frais du versement livreur + éventuel coût de financement.

La formule actuellement codée « 100 FCFA + 1 % » ne doit pas être supposée couvrir tous les circuits et les deux transferts. Remplacer cette hypothèse par les conditions réellement obtenues et les intégrer au devis figé. La promesse « 0 % de commission WAZAP » peut coexister avec des frais de prestataire clairement annoncés.

Besoin indicatif de trésorerie = sommes à verser pendant le délai de disponibilité des encaissements + frais + réserve opérationnelle. Les limites, la responsabilité en cas de remboursement et l'autorisation de payer des tiers doivent être confirmées contractuellement avec le prestataire.

## Validation avant promesse commerciale de délai

Proposer un objectif de pilote, par exemple 95 % des commandes éligibles avec les deux comptes crédités en moins de 60 secondes. Il s'agit d'un critère de test envisagé, pas d'une capacité déjà mesurée ni d'un engagement du prestataire.

Mesurer l'heure du paiement confirmé chez le prestataire, de réception du webhook, d'envoi de chaque transfert et de crédit effectif de chaque bénéficiaire. Tester chaque opérateur retenu, les parcours croisés entre opérateurs, les doublons, le solde insuffisant, le timeout et l'échec d'un seul transfert.

Faire les tests techniques en sandbox, puis prévoir un pilote réel limité et autorisé. Le délai commercial annoncé devra refléter les résultats et les conditions contractuelles, avec un traitement explicite des indisponibilités opérateur.

## Pièces à obtenir pour choisir et démarrer

Documentation des versements et environnement sandbox ; activation des quatre opérateurs visés ; tarification complète ; règles de disponibilité/alimentation du solde ; autorisation d'automatisation et IP fixe si nécessaire ; conditions d'identification des bénéficiaires ; limites, délais et webhooks de réception ; règles de remboursement et d'opération partielle.
