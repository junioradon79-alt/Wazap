#  Publication Facebook — pipeline du kit 90 jours

> **Créé le 15/09/2026.** Rend la publication du kit Facebook **reproductible** (avant : opérations
> faites à la main, aucun script). Contexte : `MEMOIRE.md` chantier **8e** ·
> contenus : `FACEBOOK_KIT_90JOURS.md` · images : `visuels/post.html` + `visuels/batch_visuels.ps1`.
> Formulations commerciales : `marketing/POLITIQUE_COMMERCIALE_ET_POSITIONNEMENT.md` (**à appliquer
> aux posts J3/J10 avant publication**).

---

## 1. État réel (15/09/2026)

| Élément | État |
|---|---|
| Page **« WAZAP Côte d'Ivoire »** | ✅ en ligne — `page_id` **`1236914396182912`**, 0 abonné au 15/09 |
| Post « À propos » | ️ **publié 2×** (doublon 01:25 / 01:30) → à supprimer (voir §4) |
| Posts du kit J1-J90 |  **aucun en ligne** |
| Manifeste | ✅ `post_manifest_gen.json` — 90 jours : `caption`, `message` (+ hashtags), `pinned_comment`, `image_file` |
| Images | ✅ `visuels/generated/jXX_gen.png` (1080×1080) |
| **Script de publication** | ✅ **`publish_facebook.ps1`** (nouveau, 15/09) — dry-run par défaut, testé |
| Jeton de Page | ✅ `secrets/meta_page_token.txt` (hors dépôt et hors assets) |
| Planification |  à créer (Task Scheduler, §5) |

---

## 2. Utilisation du script

> PowerShell **5.1** · **dry-run par défaut** : rien n'est publié sans `-Publier`.

```powershell
cd C:\Dev\Wazap\marketing\facebook

.\publish_facebook.ps1                      # preflight : jeton + Page + prochain jour (aucune écriture)
.\publish_facebook.ps1 -Action status       # lecture seule : publications en ligne + doublons détectés
.\publish_facebook.ps1 -Action next         # simulation du prochain jour non publié
.\publish_facebook.ps1 -Action next -Publier        # PUBLIE le prochain jour (photo + commentaire épinglé)
.\publish_facebook.ps1 -Action publish -Jour 7 -Publier
.\publish_facebook.ps1 -Action remove -PostId <id> -Publier   # supprime une publication (doublon)
```

**Ce que fait une publication** : ① `POST /{page_id}/photos` en **multipart** avec le fichier local
(`message` = `message` du manifeste) → ② `POST /{post_id}/comments` avec le `pinned_comment` →
③ tentative d'épinglage automatique (si l'API le refuse : épingler à la main, non bloquant) →
④ écriture de `published_state.json` (jour → `post_id`, horodatage, image).

**Code de sortie** : `0` OK · `1` erreur (jeton/Page/manifeste/API) · `2` refus par garde-fou
(jour déjà publié — `-Republier` pour forcer).

**Idempotence** : `published_state.json` = jours déjà publiés ; `next` reprend automatiquement le
premier jour absent. Le script **ne republie jamais** un jour sans `-Republier`.

---

## 3. Deux décisions tranchées par ce script

1. **Aucun hébergement public d'images n'est nécessaire** : l'upload **multipart** envoie le fichier
   local → le dossier `gh_images_staging/` (90 JPG préparés pour un hébergement public) **n'est plus
   utilisé** (conservé comme archive ; le script ne s'en sert qu'en repli si un PNG manque).
2. **Jeton hors des assets** : lu en priorité dans `secrets/meta_page_token.txt` (1ʳᵉ ligne non vide,
   `-TokenFile` pour un autre chemin). Le repli sur `.graph_api_config.json` fonctionne encore mais
   affiche un avertissement (règle projet : **aucun secret hors `secrets/`**). Le jeton vient du
   *système utilisateur* **WAZAP Automation** (Business Manager) — portées `pages_manage_posts`,
   `pages_read_engagement`, `pages_show_list`, **sans expiration**.

---

## 4. Corriger le doublon du post « À propos »

```powershell
.\publish_facebook.ps1 -Action status
# → x2  garder …_122106539463469505  |  supprimer : …_122106537489469505
.\publish_facebook.ps1 -Action remove -PostId 1236914396182912_122106537489469505 -Publier
```

---

## 5. Planification quotidienne (1 post/jour)

Tâche quotidienne (exemple 09:00, une seule fois) :

```powershell
$action  = New-ScheduledTaskAction -Execute 'powershell.exe' `
  -Argument '-NoProfile -ExecutionPolicy Bypass -File "C:\Dev\Wazap\marketing\facebook\publish_facebook.ps1" -Action next -Publier'
$trigger = New-ScheduledTaskTrigger -Daily -At 09:00
Register-ScheduledTask -TaskName 'WAZAP-Facebook-Jour' -Action $action -Trigger $trigger `
  -Description 'Publie le post du jour du kit Facebook 90 jours WAZAP' -Force
```

> ⚠️ **Avant d'activer la planification** : ① valider le post du **jour 1** en réel (1 publication,
> contrôle visuel) ; ② appliquer les formulations officielles aux posts **J3/J10**
> (`POLITIQUE_COMMERCIALE_ET_POSITIONNEMENT.md` §7).

---

## 6. Journal

| Date | Contenu |
|---|---|
| 15/09/2026 | Création du script `publish_facebook.ps1` (5 actions, multipart, dry-run, idempotent) · jeton déplacé dans `secrets/meta_page_token.txt` · décisions §3 · **testé** en réel par API : preflight OK (Page joignable, jours 1 → 2), `status` détecte le doublon, garde-fou exit 2 validé, **aucune publication effectuée** |