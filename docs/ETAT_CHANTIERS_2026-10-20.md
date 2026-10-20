# WAZAP — État vérifié des chantiers au 20 octobre 2026

Ce document décrit le travail réellement effectué dans le dépôt et les contrôles effectués sur les comptes externes. Un correctif local n'est pas présenté comme un correctif en production. Le lot commencé le 16 octobre a été repris et complété.

## 1. Résultat technique local

| Contrôle | Résultat du 20 octobre |
|---|---|
| Compilation .NET Release, avertissements bloquants | Réussie, 0 erreur, 0 avertissement |
| Suite xUnit dans le même processus | 926 cas recensés : 916 réussis, 10 ignorés, 0 échec |
| Tests PostgreSQL réels | 10 non exécutés dans cette session ; exigés dans la CI PostgreSQL 18 |
| Tests web | 64 réussis dans 12 fichiers |
| Lint web, TypeScript, compilation Vite | Réussis |
| Modèle EF et migrations | Aucun changement de modèle non couvert ; script SQL idempotent généré localement |
| Signatures de secrets dans les sources/configurations | Aucun résultat pour les règles du scanner ajouté ; ce contrôle n'est pas une analyse exhaustive de l'historique Git |
| Audit npm en ligne | Nouvelle interrogation impossible : résolution réseau du registre indisponible ; aucun nouveau résultat « zéro vulnérabilité » revendiqué |
| APK Android release | APK v1.2.0, versionCode 3, produit et signature de production vérifiée |
| Validation Android complète | Lint vital bloqué par l'écriture de son rapport dans l'environnement restreint ; validation complète exigée en CI |

Le lanceur VSTest n'a pas pu établir son canal de communication avec testhost. `tools/InProcessTests` utilise le contrôleur xUnit officiel dans le même processus, sans supprimer de tests. Les dix tests PostgreSQL restent explicitement ignorés : ils ne sont pas remplacés par des tests InMemory. La CI continue d'utiliser VSTest et PostgreSQL réel.

Rapports locaux : `artifacts/validation-20261020/inprocess-results.json`, `inprocess-output.log`, `migrations.sql`. Le rapport TRX de la tentative VSTest est un rapport d'abandon, pas un rapport de réussite.

## 2. Correctifs livrés dans les sources

### Paiements et versements

- Confirmation publique `complete-split` désactivée, simulation retirée du parcours réel.
- Confirmation d'un paiement exact, montant obligatoire, devise XOF obligatoire et statut cohérent. Les notifications signées annonçant un succès avec une devise différente, absente ou un statut d'échec sont ignorées.
- Réconciliation serveur vérifiant montant, devise et référence. Une reprise manuelle d'une initiation incertaine exige aussi les métadonnées du paiement WAZAP exact.
- Devis conservé jusqu'au règlement : frais et montants dus ne sont pas recalculés avec un autre choix de payeur.
- Intention enregistrée avant l'appel au prestataire. Une réponse perdue ne fait plus disparaître cette intention ; une seconde demande ne crée pas automatiquement un deuxième checkout.
- Registre `PaymentPayouts`, distinct de l'encaissement : un enregistrement par paiement et rôle bénéficiaire, montant et bénéficiaire mémorisés, référence, justificatif, auteur et date du rapprochement.
- Interface administrative dans **Transactions** : sommes dues, versements rapprochés, initiations à vérifier et paiements historiques sans registre.
- Rapprochement d'un versement réservé à l'administrateur ; montant exact, référence et preuve exigés. Références locales fictives `DISB-*` refusées. Cette action ne transfère pas d'argent.
- Notifications de paiement et reprise de diffusion conservées dans l'outbox. Livraison des messages au moins une fois : une panne après envoi mais avant acquittement peut encore provoquer un doublon de notification, sans second encaissement logique.

**Limite commerciale inchangée :** aucun connecteur de reversement réel à deux bénéficiaires n'a été inventé. Les versements automatiques nécessitent une offre prestataire effectivement activée, sa documentation et des tests. Voir `GENIUSPAY_SPLIT_VERIFICATION_2026-10-16.md` et `PLAN_VERSEMENTS_AUTOMATIQUES_2026-10-16.md`.

### Identité et accès

- Cookie administrateur délivré après validation complète du second facteur ; vérification de l'empreinte des sessions HTTP et revalidation des circuits Blazor chaque minute.
- Refresh tokens liés à l'empreinte de sécurité et révoqués après les changements sensibles.
- Liens de connexion de quinze minutes, opaques et à usage unique ; hash distinct de celui des refresh tokens. Les nouveaux messages ne transmettent plus de mot de passe.
- Le secret du lien est dans le fragment, retiré après prise en charge. Aucun nouvel essai automatique en boucle après un échec.
- Un lien WhatsApp ne contourne ni la MFA ni les restrictions administrateur.
- Protection contre les essais répétés des codes de réinitialisation par compte ; un code actif n'est pas réémis à chaque demande.
- Passerelle Android authentifiée par appareil : HMAC, corps signé, timestamp borné et nonce réservé en base avant l'action.
- OCR et réception de photo distincts d'une certification humaine. La configuration et le défaut du code exigent désormais des livreurs certifiés pour le matching.
- Routes React filtrées par rôle ; parcours MFA utilisable dans le portail.

### Remise des colis

- Code de livraison et secret destinataire absents des réponses JSON de commande et du suivi public.
- Ramassage et démarrage depuis les routes de suivi réservés au livreur authentifié affecté à la commande.
- Nouveau lien personnel de réception : signature propre à la commande, expiration 48 heures ; confirmation par le destinataire en un clic.
- Un livreur ne clôture pas une commande portant ce nouveau mécanisme avec son ancien code PIN ou la simple connaissance du suivi.
- Action **Lien destinataire** dans la liste des commandes : envoi sur le numéro client enregistré et révocation des anciens liens personnels de cette commande.
- Badge « identité vérifiée » affiché seulement lorsque la certification est effectivement connue du serveur.

**Reprise des anciennes commandes nécessaire :** les commandes historiques ne reçoivent pas rétroactivement un secret envoyé au client. Réémettre le lien destinataire sur les courses encore actives avant le pilote. Le parcours historique de clôture reste à vérifier avec les paramètres de production.

### Données et exploitation

- La purge opérationnelle ne supprime plus les commandes portant des paiements et leur journal financier.
- Purge indépendante des photos de livraison expirées, hors commandes comportant un sinistre ; conservation du journal de paiement.
- Durée configurable des journaux WhatsApp ; réduction des informations de téléphone, messages et documents dans les logs ciblés.
- Headers de protection et CSP sur `/app`, politique sans Referer et absence de cache sur les accès sensibles.
- Secrets de signature Android fournis par variables d'environnement ; exclusions des keystores et fichiers locaux ; scanner sans impression des valeurs.
- CI réutilisable du même commit obligatoire avant déploiement : serveur, web, PostgreSQL 18, audits et compilation Android.
- Sauvegarde échouée ou absente bloquant les migrations, hors dérogation manuelle d'urgence déjà prévue. Outils de sauvegarde alignés sur PostgreSQL 18.
- Contrôle de restauration isolée ajouté à la CI. Il vérifie un aller-retour de données de test ; il ne prouve pas la restauration de la production.

## 3. Infrastructure effectivement observée

| Ressource | Observation directe | Action restante |
|---|---|---|
| API SmarterASP temporaire | `/health` affiche `Healthy` | Vérification après le prochain déploiement |
| PostgreSQL SmarterASP | `db_acdd27_wazap`, actif, PostgreSQL 18.x | Sauvegarde et restauration de production à exercer avant migration |
| Hébergement SmarterASP | Offre W60-EU, expiration affichée le **30 octobre 2026** | Régularisation de l'abonnement par le propriétaire |
| Render `wazap-db` | Suspendue, PostgreSQL 18, suppression annoncée **dans 13 jours** lors du contrôle du 20 octobre | Choisir conservation et offre payante avant échéance ; aucun export Free disponible dans Recovery |
| Render `wazap-api` | Service affiché déployé dans le compte | Déterminer son rôle à conserver face à SmarterASP ; ne supprimer aucune ressource avant sauvegarde |
| `wazap.ci` | Ouverture échouée, connexion réinitialisée | Reconnexion WiniHost puis contrôle DNS/Cloudflare/SSL |
| WiniHost | Session expirée, formulaire de connexion | Connexion du propriétaire nécessaire |
| GitHub `main` | Dernier commit visible `bf1db17`, 7 octobre | Publier le lot validé et contrôler les nouveaux workflows |
| Contrôles GitHub du dernier commit | Backend et ancien déploiement réussis ; frontend en échec | Nouvelle CI bloquante corrigée dans les sources locales |

Render et SmarterASP possèdent donc deux bases distinctes. La suspension Render ne suffit pas à conclure que la base SmarterASP est arrêtée.

## 4. Catalogue, acquisition et terrain

- Catalogue constaté : **11 articles, 3 collections, 11 visuels**. Les anciens rappels de neuf articles sont dépassés.
- Textes du catalogue corrigés : crédits de mise en relation offerts distincts des frais de livraison/paiement ; retrait de la promesse de virement instantané et de délai d'affectation garanti.
- Prévisualisation et page HTML du catalogue régénérées. Aucune modification du catalogue dans l'application WhatsApp Business n'est revendiquée.
- Six vidéos 30 secondes et une vidéo 20 secondes retrouvées. Les documents et vidéos historiques comportent des promesses de crédit instantané non établies ; réviser les textes, voix et incrustations avant publication. Une nouvelle légende ne corrige pas une affirmation déjà intégrée à une vidéo.
- Aucun message envoyé aux prospects, aucun commerçant/livreur réel inscrit, aucune campagne payante lancée et aucune publication TikTok effectuée dans cette session.
- Recette physique à effectuer sur le téléphone et avec les acteurs du pilote : photo réelle, revue d'identité, accès personnel, disponibilité, commande, paiement vérifié, ramassage autorisé, réception par le client et rapprochement des deux bénéficiaires.

## 5. Ordre de mise en service

1. Régulariser la conservation des infrastructures avant leurs échéances ; sauvegarder les bases utiles sans supprimer les anciennes ressources.
2. Reconnecter WiniHost ; vérifier les NS Cloudflare attendus `bonnie.ns.cloudflare.com` et `nicolas.ns.cloudflare.com`, le routage et le certificat. Ne pas annoncer le domaine opérationnel avant vérification.
3. Enrôler la passerelle : config serveur `Gateway__Devices__<identifiant>` et même secret dans l'application Android. Aucun secret n'est embarqué dans l'APK ou dans ce rapport.
4. Vérifier le certificat de l'application déjà installée, installer le candidat compatible et configurer l'appareil. Le fichier APK actuellement proposé par la version locale du site est signé **Android Debug** ; le nouveau candidat utilise la clé de production WAZAP. Une mise à jour entre certificats différents sera refusée par Android ; ne pas désinstaller sans préparer la reprise de configuration.
5. Publier les sources puis exiger la CI complète, notamment les dix tests PostgreSQL et le lint Android. Ne pas contourner un contrôle échoué pour accélérer le déploiement.
6. Sauvegarde réussie, puis migrations additives dans l'ordre : `20261016064351_HardenPaymentQuotesAndRefreshSessions`, `20261020100414_SecureRecipientReceipt`, `20261020100704_AddPaymentPayoutRegister`.
7. Vérifier réellement les champs `amount`, `currency`, `reference`, `status` et métadonnées sur l'offre GeniusPay activée. En leur absence, le nouveau code refuse de déclarer le paiement confirmé.
8. Reprendre anciennes certifications, anciens accès contenant des mots de passe, commandes actives et preuves de versement historiques. Les anciens refresh tokens sans empreinte seront refusés.
9. Pilote réel limité puis publication du catalogue et des contenus corrigés ; mesure des délais et des résultats, sans annoncer des versements automatiques non testés.

## 6. Points qui ne sont pas déclarés terminés

- Publication GitHub, nouvelle CI et déploiement effectif tant qu'ils ne sont pas observés réussis.
- Tests PostgreSQL et restauration réelle de production dans cette session.
- Lint Android complet, installation et test sur téléphone réel.
- DNS, renouvellements payants et récupération de la base Render.
- Activation contractuelle et technique des versements automatiques multi-bénéficiaires.
- Rotation des secrets historiques exposés, coffre et permissions de toutes les copies hors dépôt. Le scanner ne retire pas les secrets d'un historique public déjà publié.
- Migration des JWT du portail depuis le stockage JavaScript vers une session HttpOnly avec protection CSRF adaptée : la CSP et la révocation réduisent le risque, sans remplacer ce changement d'architecture.
- Anonymisation des données personnelles attachées aux écritures financières conservées, durées applicables à confirmer et refonte progressive des services restant dans la couche API.
- Revue/réédition des contenus vidéo et images comportant d'anciennes promesses, chargement du catalogue sur téléphone, acquisition et recette terrain.

Le chantier global reste ouvert jusqu'à validation de ces éléments. Aucun transfert réel ou engagement financier n'a été réalisé automatiquement.

## 7. Remise du lot et publication

- Sources, tests, migrations, documentation et bundle web synchronisé enregistrés dans le commit local `95bfe31` sur `main`.
- La publication d'une branche de revue `codex/security-20261020` a été tentée ; Git n'a pas pu joindre GitHub (`getaddrinfo() thread failed to start`). La branche distante et une nouvelle exécution CI ne sont donc pas confirmées.
- GitHub est accessible en lecture dans le navigateur, mais le connecteur de publication n'est pas installé. Une connexion adaptée a été proposée ; elle n'est pas présumée active.
- Un candidat APK et son SHA-256 sont conservés dans `artifacts/release-candidate-20261020`. Il ne remplace pas l'APK public avant la bascule coordonnée.
- La copie du bundle a réussi sans supprimer le dossier généré. Les anciens fichiers non référencés pourront être nettoyés par la reconstruction du pipeline ; les références de la nouvelle page ont été vérifiées.
- Les contrôles réels PostgreSQL, le lint Android et les prérequis d'enrôlement restent des conditions de mise en service, même si le commit local est enregistré.
