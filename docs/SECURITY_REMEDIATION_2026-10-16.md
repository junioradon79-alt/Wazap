# Sécurisation WAZAP — premier lot du 16 octobre 2026

Ces corrections sont présentes dans le code local. Aucun push, déploiement, paiement réel, rotation de secret de production ou modification de la base de production n'a été effectué.

## Risques traités

| Audit | Correction apportée |
|---|---|
| A01 | La confirmation publique `complete-split` répond 410 sans effet. Le bouton de simulation a été retiré. |
| A02 | Suppression des fausses références de virements. Le service manuel indique qu'un transfert reste nécessaire. Les notifications et le suivi distinguent encaissement et reversement. |
| A03 | Aucun cookie administrateur sans validation du second facteur. Un parcours de saisie du code est disponible ; cookie lié à l'empreinte authentifiée. |
| A04 | Signature HMAC par appareil sur les POST de la passerelle, timestamp limité à cinq minutes, corps signé et nonce réservé en base avant tout effet. Le client Android signe les requêtes. |
| A05 | Réception d'une photo et extraction OCR sans certification automatique. Échec du stockage sécurisé : refus explicite. Contrôle de taille et extension ajouté. |
| A07 | Refresh tokens liés à l'empreinte du compte ; révocation après changement de mot de passe ou de 2FA ; rotation conditionnelle en base. Cookies vérifiés à la prochaine requête HTTP. |
| A09 | Suppression du second chemin de complétion split. Webhook et réconciliation passent par le paiement exact ; protection des confirmations simultanées et du rejeu ; un échec ne remplace pas un paiement déjà complété. |
| A10 | Devis split figé en base et repris à la confirmation : net marchand, livraison, frais et payeur. Écritures distinctes des montants dus au vendeur et au livreur. Réutilisation du checkout en attente. |
| A11 | CI complète du même commit requise avant déploiement. Échec de sauvegarde ou artefact absent : migration et déploiement bloqués, sauf dérogation manuelle explicite déjà prévue par `skip_backup`. |
| A13 | Mise à niveau Vite/Vitest et du plugin React ; contrôle bloquant des vulnérabilités élevées/critiques npm et NuGet dans la CI. |

Le lint existant a également été corrigé. La connexion MFA ne remet plus à zéro les tentatives du second facteur après la seule validation du mot de passe.

## Vérifications

- Suite serveur complète : **911 tests réussis, aucun ignoré**, avec PostgreSQL 17 local isolé. Cela inclut les migrations réelles, quatre nouveaux scénarios PostgreSQL de paiements et les tests HTTP de sécurité.
- Tests de frontières HTTP : paiement fabriqué refusé ; signature de passerelle absente/invalide/expirée et nonce rejoué refusés ; authentification cookie sans MFA refusée ; second facteur valide accepté ; cookie révoqué après changement d'empreinte.
- Webhook GeniusPay vérifié : signature incorrecte, montant manquant ou incorrect sans complétion ; paiement exact confirmé ; notification doublonnée sans double traitement.
- Suite web : **62 tests réussis**, dont le parcours de connexion MFA ; lint, TypeScript et build réussis après mise à niveau.
- Build serveur Release avec avertissements traités comme erreurs : **0 avertissement, 0 erreur**.
- Android : **assembleDebug réussi**. Le protocole de signature côté serveur a été testé via HTTP ; le fonctionnement sur téléphone réel et la distribution d'un APK de production signé restent à vérifier.
- Le bundle généré a été synchronisé avec `wwwroot/app`, et ses références ont été vérifiées, afin que le serveur local présente lui aussi la nouvelle interface.
- Audit npm après mise à niveau : **0 vulnérabilité signalée** dans l'arbre installé.
- Les workflows GitHub ont été modifiés localement ; leur exécution sur GitHub et les protections de l'environnement de production n'ont pas été observées pendant ce lot.

## Préparation de la mise en service

### 1. Migration et sessions

Migration additive : `20261016064351_HardenPaymentQuotesAndRefreshSessions`.

Elle ajoute les devis des paiements et l'empreinte des refresh tokens. Elle doit être appliquée avant le démarrage de la nouvelle version. Les migrations ont été vérifiées sur une base PostgreSQL de test neuve ; aucune migration n'a été appliquée à la production.

Les anciens refresh tokens n'ont pas d'empreinte : ils sont volontairement refusés. Les utilisateurs devront se reconnecter. Les anciens cookies administrateur sans empreinte sont également refusés. Les circuits Blazor déjà ouverts restent un point à traiter pour une révocation périodique en cours de session, au-delà des contrôles HTTP.

Les paiements historiques sans devis ne permettent pas de reconstituer rétroactivement le choix de répartition des frais : ils gardent leur comportement historique. Il faut rapprocher séparément les anciens encaissements et versements annoncés ; une ancienne référence `DISB-*` générée localement n'est pas une preuve bancaire.

### 2. Passerelle Android : enrôlement obligatoire

Configurer côté serveur une entrée distincte par téléphone, par exemple :

```text
Gateway__Devices__telephone-01 = <secret aléatoire propre à cet appareil, au moins 32 octets UTF-8>
```

Ne jamais intégrer le secret au code, à l'APK ou à un rapport. Saisir le même identifiant et le même secret dans le bouton **Configurer les accès de cet appareil** de la nouvelle application. Le secret Android est chiffré au repos avec Android Keystore ; sauvegarde de l'application et HTTP en clair sont désactivés.

Une configuration absente refuse tous les POST : il n'existe pas de repli anonyme. Les anciens APK ne signent pas les requêtes et ne sont donc plus compatibles. Préparer et installer un APK de production avec la clé de signature officielle, puis mettre à jour l'APK proposé au téléchargement lors de la mise en service. Le build debug produit localement n'est pas une version de distribution.

La source modifiée est `WazapSln/android/WazapGateway`. La copie extérieure `C:/Dev/Wazap/android/WazapGateway` n'a pas été écrasée et contient encore l'ancien code : elle ne doit pas servir à reconstruire ce lot. Les mots de passe de signature présents dans cette copie restent à sécuriser.

Pour révoquer un appareil, retirer son entrée de configuration ; redémarrer le service si la configuration est fournie par variables d'environnement.

Signature envoyée : HMAC-SHA256 du texte UTF-8 suivant, séparé par des sauts de ligne LF : méthode HTTP, chemin, timestamp Unix en secondes, nonce GUID sans tirets, SHA-256 hexadécimal minuscule du corps exact. Les en-têtes sont `X-Gateway-Device`, `X-Gateway-Timestamp`, `X-Gateway-Nonce`, `X-Gateway-Signature` (hexadécimal). Le même nonce est conservé lors du repli entre domaines pour empêcher une seconde exécution.

Un nonce est réservé avant l'action : si le traitement échoue après réservation, son rejeu est refusé. L'appareil doit présenter une erreur à l'opérateur ; cette protection ne constitue pas une file de reprise garantie. La conservation/purge des marqueurs doit être supervisée.

### 3. Paiements et reversements

Le split respecte désormais `ClientPayments:Enabled`. En production, activer ce réglage seulement avec le vrai prestataire configuré. L'initiation ne retourne plus de faux succès avec un lien circulaire si le paiement n'est pas disponible. Aucune nouvelle API de versement bancaire n'a été inventée ou branchée.

`VendorPayoutDue` et `RiderPayoutDue` sont des dettes à régler, pas des virements réalisés. Le suivi administratif des règlements effectifs, de leurs preuves et des éventuels remboursements reste à compléter. Les notifications sont encore envoyées en best effort après la transition ; une panne entre cette transition et la notification nécessite une reprise opérationnelle. La reprise d'une initiation dont le résultat réseau est inconnu doit aussi être renforcée avant de revendiquer une garantie d'idempotence de bout en bout.

Les webhooks de succès sans montant sont désormais ignorés. Vérifier en staging que le prestataire fournit bien le montant et l'identifiant interne attendus ; les tests couvrent la forme traitée par le parseur actuel.

### 4. Déploiement et dépendances

La publication dépend maintenant de la CI réutilisable complète : tests serveur, PostgreSQL réel, frontend, contrôles qualité et audits. Une sauvegarde échouée bloque la migration par défaut. La restauration réelle d'une sauvegarde de production reste à exercer séparément.

L'outillage web utilise Vite 8 et Vitest 5. Node 24 est utilisé par la CI ; le champ `engines` précise les versions compatibles. Les cibles de navigateurs du build ont été conservées explicitement pour limiter les régressions sur les téléphones anciens.

Références de migration consultées : [Vite](https://vite.dev/guide/) et [Vitest](https://vitest.dev/guide/migration/). Les versions et les alertes ont également été vérifiées dans le registre npm au moment du changement.

## Risques élevés restant à traiter dans le lot suivant

1. **A06 — Suivi et remise :** dissocier les capacités du destinataire de l'identifiant de commande ; empêcher un livreur de retrouver le PIN via les API de lecture ; authentifier les opérations de collecte et de transport. Le suivi public actuel n'a pas encore été refondu.
2. **A08 — Liens de connexion :** remplacer les mots de passe dans les URL par des liens temporaires à usage unique, sans perdre le parcours simple. Les anciens liens et l'auto-connexion par paramètres n'ont pas été supprimés dans ce lot.
3. **A12 — Secrets :** coffre, permissions, contrôle des copies et rotation selon l'exposition des secrets réellement actifs. Aucun secret de production n'a été déplacé ou révoqué automatiquement.
4. Vérifier l'activation réelle des exigences de certification et de code de remise en production ; les valeurs d'environnement n'ont pas été modifiées. Identifier les certifications historiques accordées sans examen humain et les reprendre.
5. Achever le registre administratif des versements, la reprise durable des notifications/paiements et la révocation des circuits Blazor déjà ouverts ; reprendre ensuite la conservation des données et les autres constats moyens de l'audit.

Ce premier lot ferme les contournements critiques identifiés dans le code. Il ne constitue pas une déclaration de résolution de tous les risques de l'audit ni une validation de l'état de production.
