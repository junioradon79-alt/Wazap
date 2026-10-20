# Sprint actif WAZAP — 20 octobre 2026

Objectif global : fermer les chantiers techniques, infrastructure et exploitation. Le lot local est vérifié ; la mise en service et les dépendances externes restent ouvertes.

## Livré dans les sources
- Paiements vérifiés (montant, XOF, statut, référence), devis immuable, intention durable avant appel prestataire, absence de second checkout automatique après timeout.
- Versements dus distincts des encaissements et preuves réelles ; registre et rapprochement administrateur dans Transactions.
- Liens personnels de connexion à usage unique (15 min), réception destinataire (48 h), routes de transport limitées au livreur affecté.
- MFA administrateur, sessions révoquées, revalidation Blazor, passerelle HMAC et anti-rejeu, certification humaine et matching certifié par défaut.
- Conservation financière, purge séparée des preuves, CSP, scanner de secrets et signature Android par environnement.
- CI bloquante du même commit : PostgreSQL 18, web, Android, audits, sauvegarde avant migration et exercice de restauration isolé.

## Validation
916 tests .NET réussis, 10 PostgreSQL ignorés, aucun échec ; 64 tests web réussis ; build .NET 0 warning ; lint/TypeScript/build web OK. APK release v1.2.0 signé, contrôle lint complet à confirmer en CI.

## Prochaines actions bloquantes
1. Conserver les infrastructures : SmarterASP expire le 30/10/2026 ; Render wazap-db suspendue, suppression annoncée dans 13 jours au contrôle du 20/10.
2. Reconnexion WiniHost, DNS Cloudflare et SSL. Production temporaire Healthy ; base SmarterASP active et distincte de Render.
3. Enrôlement du téléphone (secret serveur/appareil), compatibilité de certificat, puis publication avec CI complète et migration sauvegardée.
4. Reprise des anciennes commandes, certifications et preuves de paiements/versements. Anciennes URL à mot de passe à révoquer.
5. Contrat/API réelle de reversement ; revue des 11 articles et 7 vidéos avant publication ; recette et acquisition terrain.
6. Durcissement complémentaire : sessions HttpOnly du portail, coffre/rotation historiques, anonymisation et architecture des services.

Source détaillée : docs/ETAT_CHANTIERS_2026-10-20.md.