# -*- coding: utf-8 -*-
"""
Générateur du tableau exhaustif des fonctionnalités WAZAP pour les commerçants.
Produit :
  1. FONCTIONNALITES_COMMERCANTS_WAZAP.xlsx (Excel enrichi avec styles émeraude WAZAP)
  2. FONCTIONNALITES_COMMERCANTS_WAZAP.csv (CSV UTF-8 BOM compatible Excel français)
  3. tableau_fonctionnalites_commercants.html (Tableau interactif HTML avec recherche, filtres et téléchargement)
"""

import csv
import json
import os
import sys
import openpyxl

if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

# Données exhaustives des fonctionnalités marchandes WAZAP
DATA = [
    # ── 1. SÉCURITÉ FINANCIÈRE ──
    {
        "id": 1,
        "categorie": "1. Sécurité Financière",
        "nom": "Règle « Zéro Cash Marchandise »",
        "description": "Le coursier ne transporte et n'encaisse aucun argent liquide sur les produits du commerçant. Tout règlement s'opère par mobile money.",
        "valeur_ajoutee": "Suppression définitive du cauchemar des livreurs qui disparaissent avec la recette, fin des fausses coupures et des litiges de rendu de monnaie.",
        "canal": "Règle absolue / QR Code Universel",
        "reference_code": ".ecc/memory/project/invariants.md",
        "statut": "Actif en Production"
    },
    {
        "id": 2,
        "categorie": "1. Sécurité Financière",
        "nom": "Paiement Fractionné Instantané (Split Payment)",
        "description": "Ventilation automatique et simultanée des fonds dès que le client scanne et valide son règlement.",
        "valeur_ajoutee": "100% du prix des produits est crédité directement et immédiatement sur le compte Mobile Money du commerçant. Les frais de course vont au livreur.",
        "canal": "Passerelle de paiement GeniusPay / WAZAP",
        "reference_code": "src/Wazap.Application/Services/PaymentSplitService.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 3,
        "categorie": "1. Sécurité Financière",
        "nom": "QR Code Universel Multi-Opérateurs",
        "description": "Un seul QR Code compatible équitablement avec Wave, Orange Money, MTN MoMo, Moov Money et Cartes Bancaires (Visa/Mastercard).",
        "valeur_ajoutee": "Zéro friction pour le client final, aucun refus d'opérateur, le commerçant n'a pas à multiplier les numéros ou les affichages.",
        "canal": "Scan smartphone natif ou application Mobile Money",
        "reference_code": "src/Wazap.API/Controllers/ClientOrdersController.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 4,
        "categorie": "1. Sécurité Financière",
        "nom": "Garantie Colis Sûr & Indemnisation 48h",
        "description": "Scellé de sécurité numéroté et procédure accélérée de déclaration de sinistre en 1 clic par WhatsApp (« SINISTRE <code> »).",
        "valeur_ajoutee": "En cas de perte, vol ou avarie avérée en cours de transport, le marchand est indemnisé directement par Mobile Money sous 48h (jusqu'à 50 000 FCFA).",
        "canal": "WhatsApp Bot (05 44 05 19 72) / Scellé physique",
        "reference_code": "src/Wazap.Application/Services/ColisSurService.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 5,
        "categorie": "1. Sécurité Financière",
        "nom": "Code PIN de Validation de Remise",
        "description": "Code confidentiel à 4 chiffres généré pour chaque course et transmis au client pour libérer la livraison.",
        "valeur_ajoutee": "Le livreur ne peut pas clôturer la course sans ce code. Élimination totale des contestations de clients prétendant n'avoir jamais reçu leur commande.",
        "canal": "Lien de suivi client PWA / WhatsApp",
        "reference_code": "src/Wazap.Domain/Entities/Order.cs",
        "statut": "Actif en Production"
    },

    # ── 2. VITESSE & LOGISTIQUE ──
    {
        "id": 6,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Expédition Express 1-Clic WhatsApp",
        "description": "Déclenchement d'une course via un simple message « LIVRAISON <produits> à <adresse> » ou via le lien pré-rempli wa.me.",
        "valeur_ajoutee": "Zéro formulaire lourd à remplir, zéro téléchargement d'application pour le commerçant. Envoi d'un coursier en 5 secondes.",
        "canal": "WhatsApp Business (+225 05 44 05 19 72)",
        "reference_code": "src/Wazap.API/Services/VendorTextCommands.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 7,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Commande par Note Vocale IA (🎙️)",
        "description": "Le commerçant envoie directement une note vocale sur WhatsApp décrivant ce qu'il faut livrer, où et pour qui.",
        "valeur_ajoutee": "Gain de temps massif pendant les heures de rush en boutique. L'IA transcrit, identifie le contact, l'adresse et le montant sans aucune saisie clavier.",
        "canal": "WhatsApp Business (+225 05 44 05 19 72)",
        "reference_code": "src/Wazap.API/Controllers/GatewayWhatsAppController.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 8,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Dispatch Intelligent en Moins de 3 Minutes",
        "description": "Algorithme de mise en relation géolocalisée immédiate ciblant les coursiers actifs dans la commune exacte du commerce.",
        "valeur_ajoutee": "Fin des appels téléphoniques interminables pour chercher un livreur disponible. Un motard arrive à la boutique en moyenne en 3 minutes.",
        "canal": "Moteur Backend automatique",
        "reference_code": "src/Wazap.Application/Services/DeliveryOfferService.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 9,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Déclenchement Automatique par Position GPS Client",
        "description": "Le client ouvre son lien de suivi et confirme sa géolocalisation exacte sur carte interactive.",
        "valeur_ajoutee": "Le commerçant n'a plus à expliquer les carrefours ou repères compliqués au livreur. La recherche de livreur démarre automatiquement au clic du client.",
        "canal": "Page de Suivi Web / PWA (/app/suivi/:id)",
        "reference_code": "src/Wazap.API/Controllers/ClientOrdersController.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 10,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Grille Tarifaire Transparente Grand Abidjan",
        "description": "Barème fixe et clair pré-configuré : 1 000 F (même commune), 1 500 F (commune voisine), 2 000 F (traversée de pont / longue distance).",
        "valeur_ajoutee": "Plancher garanti pour les livreurs, aucun marchandage ni mauvaise surprise tarifaire pour le commerçant ou son client.",
        "canal": "Dashboard Marchand & Réponse rapide WhatsApp (/tarifs)",
        "reference_code": "web/src/pages/VendorDashboardPage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 11,
        "categorie": "2. Vitesse & Logistique",
        "nom": "Flotte 100% Certifiée par OCR (Google Cloud Vision)",
        "description": "Chaque livreur est audité avec CNI biométrique ONECI ou Permis de conduire Quipux extrait et vérifié par OCR.",
        "valeur_ajoutee": "Sécurité absolue : le commerçant confie ses marchandises de valeur uniquement à des coursiers formellement identifiés et tracés.",
        "canal": "Certification Backend & Profil Livreur",
        "reference_code": "src/Wazap.Infrastructure/Services/GoogleVisionOcrService.cs",
        "statut": "Actif en Production"
    },

    # ── 3. CATALOGUE & VENTE IA ──
    {
        "id": 12,
        "categorie": "3. Catalogue & Vente IA",
        "nom": "Magic Importer de Catalogue par IA (Gemini Flash)",
        "description": "Commande « IMPORT <url ou texte> » : l'IA analyse un lien Facebook, Instagram, un flyer photo ou une liste brute.",
        "valeur_ajoutee": "Création instantanée du catalogue du marchand en 1 seconde. Détection automatique des articles, prix et emojis sans frappe manuelle.",
        "canal": "WhatsApp Bot (05 44 05 19 72)",
        "reference_code": "src/Wazap.API/Services/VendorTextCommands.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 13,
        "categorie": "3. Catalogue & Vente IA",
        "nom": "Menu Bot WhatsApp Numéroté Interactif",
        "description": "Commande « PRODUITS » affichant la carte de la boutique sous forme de menu avec numéros simples (1, 2, 3...).",
        "valeur_ajoutee": "Les clients du commerçant peuvent commander en répondant simplement avec un chiffre, sans écrire de phrase complexe.",
        "canal": "WhatsApp Business (+225 05 44 05 19 72)",
        "reference_code": "src/Wazap.Application/Services/VendorProductService.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 14,
        "categorie": "3. Catalogue & Vente IA",
        "nom": "Générateur de Liens d'Achat 1-Clic WhatsApp par Article",
        "description": "Bouton dans l'espace catalogue générant un lien wa.me pré-rempli pour chaque produit avec nom, emoji et prix.",
        "valeur_ajoutee": "Le commerçant colle ce lien dans sa bio TikTok, story Instagram ou statut WhatsApp : le client clique et le message d'achat est prêt.",
        "canal": "Espace Web Catalogue (/app/catalogue)",
        "reference_code": "web/src/pages/CataloguePage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 15,
        "categorie": "3. Catalogue & Vente IA",
        "nom": "Gestion des Stocks en Temps Réel (1-Tap)",
        "description": "Bascule directe « En stock ✅ » / « Épuisé ❌ » depuis le tableau de bord ou par commande de retrait WhatsApp.",
        "valeur_ajoutee": "Évite les déconvenues et les annulations de commandes en masquant immédiatement les articles en rupture.",
        "canal": "Espace Web Catalogue & WhatsApp",
        "reference_code": "web/src/pages/CataloguePage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 16,
        "categorie": "3. Catalogue & Vente IA",
        "nom": "Mini-Vitrine Web E-commerce Dédiée",
        "description": "Page web responsive clé en main (/app/vente) présentant les offres de livraison et les produits du commerce.",
        "valeur_ajoutee": "Permet au commerçant d'avoir une présence en ligne professionnelle sans payer d'hébergement ou d'abonnement e-commerce tiers.",
        "canal": "Page Web Universelle (/app/vente)",
        "reference_code": "web/src/pages/VentePage.tsx",
        "statut": "Actif en Production"
    },

    # ── 4. COCKPIT & ANALYTICS ──
    {
        "id": 17,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Bannière « Commandes Reçues en 1 Clic »",
        "description": "Espace dédié en haut du tableau de bord affichant en temps réel les commandes passées par les clients sur WhatsApp.",
        "valeur_ajoutee": "Un simple tap sur le bouton vert « 🚀 Expédier en 1 Clic » lance immédiatement la recherche d'un coursier sans ressaisie.",
        "canal": "Dashboard Marchand (/app/vendor/dashboard)",
        "reference_code": "web/src/pages/VendorDashboardPage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 18,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Tableau de Bord KPIs & Suivi du Chiffre d'Affaires",
        "description": "Indicateurs en temps réel : Chiffre d'affaires livré du mois, Panier moyen (FCFA), Taux de succès des livraisons (%), Courses en cours.",
        "valeur_ajoutee": "Visibilité totale sur la santé de son business et la performance de ses expéditions, accessible sur mobile et ordinateur.",
        "canal": "Dashboard Marchand & Commande WhatsApp « DASHBOARD »",
        "reference_code": "src/Wazap.Application/Dtos/VendorDashboardDto.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 19,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Module « Meilleurs Clients » (Top Clients & Fidélisation)",
        "description": "Classement automatique des meilleurs clients avec podium (🥇🥈🥉), volume de commandes et montants totaux cumulés.",
        "valeur_ajoutee": "Bouton 1-clic « 💬 Fidéliser » ouvrant une conversation WhatsApp personnalisée avec le client pour lui offrir une remise ou un bon de course.",
        "canal": "Dashboard Marchand (/app/vendor/dashboard)",
        "reference_code": "web/src/pages/VendorDashboardPage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 20,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Suivi GPS Live Partageable en 1 Clic",
        "description": "Bouton « 📲 Partager » générant un lien interactif de tracking (/app/suivi/:id) avec le nom du client et le code de course.",
        "valeur_ajoutee": "Le client final suit l'approche du livreur sur carte. Réduction de 90% des appels impatients adressés au commerçant.",
        "canal": "Lien Web PWA & Partage WhatsApp 1-tap",
        "reference_code": "web/src/pages/VendorDashboardPage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 21,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Module Modal « Régler Coursier » en 1 Clic",
        "description": "Fenêtre dédiée séparant nettement le prix marchandise du tarif de course dû au livreur.",
        "valeur_ajoutee": "Boutons 1-tap pour appeler le coursier ou ouvrir directement l'application Wave ou Orange Money (*144#) pour lui virer ses frais sans intermédiaire.",
        "canal": "Dashboard Marchand (/app/vendor/dashboard)",
        "reference_code": "web/src/pages/VendorDashboardPage.tsx",
        "statut": "Actif en Production"
    },
    {
        "id": 22,
        "categorie": "4. Cockpit & Analytics",
        "nom": "Authentification 1-Clic Sans Mot de Passe (Magic Link)",
        "description": "Connexion automatique via les paramètres URL reçus sur WhatsApp (?u=...&p=...).",
        "valeur_ajoutee": "Zéro identifiant à taper, aucun mot de passe oublié. Accès instantané à son espace de gestion en touchant le lien reçu.",
        "canal": "Lien URL sécurisé WhatsApp",
        "reference_code": "web/src/pages/LoginPage.tsx",
        "statut": "Actif en Production"
    },

    # ── 5. MODÈLE ÉCONOMIQUE & PACKS ──
    {
        "id": 23,
        "categorie": "5. Modèle Économique & Packs",
        "nom": "0% de Commission sur les Ventes",
        "description": "WAZAP facture uniquement la mise en relation par crédit de course et ne prend aucun pourcentage sur le prix des produits vendus.",
        "valeur_ajoutee": "Préservation intégrale des marges du commerçant, contrairement aux plateformes classiques prélevant entre 15% et 30% du CA.",
        "canal": "Politique commerciale WAZAP",
        "reference_code": "marketing/POLITIQUE_COMMERCIALE_ET_POSITIONNEMENT.md",
        "statut": "Actif en Production"
    },
    {
        "id": 24,
        "categorie": "5. Modèle Économique & Packs",
        "nom": "Pack Découverte : 15 Courses 100% Offertes",
        "description": "15 crédits offerts à l'activation du compte sans aucun paiement ni engagement de carte bancaire.",
        "valeur_ajoutee": "Permet au commerçant de tester la vitesse, la fiabilité et la sécurité du réseau WAZAP en conditions réelles sans dépenser un franc.",
        "canal": "Accueil WhatsApp Business (05 44 05 19 72)",
        "reference_code": "src/Wazap.Application/Configuration/VendorOnboardingOptions.cs",
        "statut": "Actif en Production"
    },
    {
        "id": 25,
        "categorie": "5. Modèle Économique & Packs",
        "nom": "Programme de Parrainage Marchand (+5 Crédits par Filleul)",
        "description": "Code parrain unique attribué à chaque marchand avec bouton de recommandation WhatsApp prêt à envoyer.",
        "valeur_ajoutee": "Le commerçant gagne 5 livraisons gratuites chaque fois qu'un confrère commerçant s'inscrit et utilise WAZAP.",
        "canal": "Dashboard Marchand & WhatsApp Bot",
        "reference_code": "src/Wazap.Application/Dtos/VendorDashboardDto.cs",
        "statut": "Actif en Production"
    }
]

def generate_csv(output_path):
    """Génère le fichier CSV UTF-8 avec BOM pour une compatibilité native parfaite avec Excel."""
    with open(output_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f, delimiter=";", quoting=csv.QUOTE_MINIMAL)
        writer.writerow([
            "N°",
            "Catégorie / Pilier",
            "Fonctionnalité WAZAP",
            "Description & Fonctionnement",
            "Valeur Ajoutée pour le Commerçant",
            "Canal d'Utilisation",
            "Référence Code / Module",
            "Statut"
        ])
        for row in DATA:
            writer.writerow([
                row["id"],
                row["categorie"],
                row["nom"],
                row["description"],
                row["valeur_ajoutee"],
                row["canal"],
                row["reference_code"],
                row["statut"]
            ])
    print(f"✅ CSV généré : {output_path}")

def generate_excel(output_path):
    """Génère un classeur Excel professionnel aux couleurs émeraude WAZAP."""
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Fonctionnalités Commerçants"
    ws.views.sheetView[0].showGridLines = True

    # Couleurs WAZAP
    EMERALD_DARK = "075E54"
    EMERALD_LIGHT = "F0FDF4"
    EMERALD_BORDER = "C6F6D5"
    WHITE = "FFFFFF"
    GRAY_TEXT = "4A5568"
    DARK_TEXT = "1A202C"

    # En-tête titre principal
    ws.merge_cells("A1:H1")
    title_cell = ws["A1"]
    title_cell.value = "WAZAP — CATALOGUE EXHAUSTIF DES FONCTIONNALITÉS MARCHANDES"
    title_cell.font = Font(name="Calibri", size=15, bold=True, color=WHITE)
    title_cell.fill = PatternFill(start_color=EMERALD_DARK, end_color=EMERALD_DARK, fill_type="solid")
    title_cell.alignment = Alignment(horizontal="center", vertical="center")
    ws.row_dimensions[1].height = 40

    # Sous-titre
    ws.merge_cells("A2:H2")
    sub_cell = ws["A2"]
    sub_cell.value = "25 fonctionnalités concrètes à forte valeur ajoutée pour les commerçants d'Abidjan · Zéro Cash · 1-Tap · 0% Commission"
    sub_cell.font = Font(name="Calibri", size=11, italic=True, color=WHITE)
    sub_cell.fill = PatternFill(start_color="0A7C6E", end_color="0A7C6E", fill_type="solid")
    sub_cell.alignment = Alignment(horizontal="center", vertical="center")
    ws.row_dimensions[2].height = 24

    # En-têtes de colonnes
    headers = [
        "N°",
        "Catégorie / Pilier",
        "Fonctionnalité WAZAP",
        "Description & Fonctionnement",
        "Valeur Ajoutée pour le Commerçant",
        "Canal d'Utilisation",
        "Référence Code / Module",
        "Statut"
    ]

    ws.row_dimensions[3].height = 28
    thin_border = Border(
        left=Side(style='thin', color="CBD5E0"),
        right=Side(style='thin', color="CBD5E0"),
        top=Side(style='thin', color="CBD5E0"),
        bottom=Side(style='thin', color="CBD5E0")
    )

    for col_idx, header in enumerate(headers, 1):
        cell = ws.cell(row=3, column=col_idx, value=header)
        cell.font = Font(name="Calibri", size=11, bold=True, color=WHITE)
        cell.fill = PatternFill(start_color=EMERALD_DARK, end_color=EMERALD_DARK, fill_type="solid")
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = thin_border

    # Remplissage des données
    current_row = 4
    for item in DATA:
        is_even = (current_row % 2 == 0)
        row_fill = PatternFill(
            start_color=EMERALD_LIGHT if is_even else WHITE,
            end_color=EMERALD_LIGHT if is_even else WHITE,
            fill_type="solid"
        )

        ws.cell(row=current_row, column=1, value=item["id"]).alignment = Alignment(horizontal="center", vertical="center")
        ws.cell(row=current_row, column=2, value=item["categorie"]).alignment = Alignment(horizontal="left", vertical="center")
        ws.cell(row=current_row, column=3, value=item["nom"]).font = Font(name="Calibri", size=11, bold=True, color=DARK_TEXT)
        ws.cell(row=current_row, column=4, value=item["description"]).alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
        ws.cell(row=current_row, column=5, value=item["valeur_ajoutee"]).alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
        ws.cell(row=current_row, column=6, value=item["canal"]).alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
        ws.cell(row=current_row, column=7, value=item["reference_code"]).font = Font(name="Consolas", size=9, color=GRAY_TEXT)
        ws.cell(row=current_row, column=8, value=item["statut"]).alignment = Alignment(horizontal="center", vertical="center")

        for col_idx in range(1, 9):
            c = ws.cell(row=current_row, column=col_idx)
            c.fill = row_fill
            c.border = thin_border
            if col_idx not in (1, 3, 7):
                c.font = Font(name="Calibri", size=10, color=DARK_TEXT)

        ws.row_dimensions[current_row].height = 45
        current_row += 1

    # Largeurs idéales des colonnes
    col_widths = {
        "A": 6,   # N°
        "B": 24,  # Catégorie
        "C": 32,  # Nom
        "D": 45,  # Description
        "E": 52,  # Valeur ajoutée
        "F": 30,  # Canal
        "G": 38,  # Réf Code
        "H": 18   # Statut
    }

    for col_letter, width in col_widths.items():
        ws.column_dimensions[col_letter].width = width

    # Filtre automatique
    ws.auto_filter.ref = f"A3:H{current_row - 1}"

    wb.save(output_path)
    print(f"✅ Excel généré : {output_path}")

def generate_html(output_path):
    """Génère une page web interactive avec tableau dynamique, recherche en direct et boutons de téléchargement."""
    json_data = json.dumps(DATA, ensure_ascii=False)
    html_content = f"""<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>WAZAP — Tableau Exhaustif des Fonctionnalités Commerçants</title>
  <link rel="icon" type="image/jpeg" href="../../web/public/logo-officiel-2026.jpg">
  <style>
    :root {{
      --emerald: #00A86B;
      --emerald-dark: #075E54;
      --emerald-light: #E8F8F2;
      --emerald-glow: rgba(0, 168, 107, 0.15);
      --bg: #0F172A;
      --surface: #1E293B;
      --surface-border: #334155;
      --text: #F8FAFC;
      --text-muted: #94A3B8;
      --amber: #F59E0B;
    }}

    * {{
      box-sizing: border-box;
      margin: 0;
      padding: 0;
    }}

    body {{
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      background-color: var(--bg);
      color: var(--text);
      line-height: 1.5;
      padding: 24px;
    }}

    .container {{
      max-width: 1440px;
      margin: 0 auto;
    }}

    /* HEADER */
    .header {{
      background: linear-gradient(135deg, rgba(7, 94, 84, 0.95), rgba(15, 23, 42, 0.95));
      border: 1px solid var(--surface-border);
      border-radius: 16px;
      padding: 28px 32px;
      margin-bottom: 24px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 20px;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
    }}

    .header-left {{
      display: flex;
      align-items: center;
      gap: 18px;
    }}

    .logo-badge {{
      width: 64px;
      height: 64px;
      border-radius: 50%;
      background: var(--emerald-dark);
      border: 3px solid var(--emerald);
      display: flex;
      align-items: center;
      justify-content: center;
      box-shadow: 0 0 20px var(--emerald-glow);
      overflow: hidden;
    }}

    .logo-badge img {{
      width: 100%;
      height: 100%;
      object-fit: cover;
    }}

    .header-title h1 {{
      font-size: 24px;
      font-weight: 800;
      letter-spacing: -0.5px;
      color: #fff;
    }}

    .header-title p {{
      color: var(--text-muted);
      font-size: 14px;
      margin-top: 4px;
    }}

    .actions-bar {{
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }}

    .btn {{
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 10px 18px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      text-decoration: none;
      cursor: pointer;
      border: none;
      transition: all 0.2s ease;
    }}

    .btn-emerald {{
      background: var(--emerald);
      color: #000;
    }}

    .btn-emerald:hover {{
      background: #00C87E;
      transform: translateY(-1px);
    }}

    .btn-outline {{
      background: rgba(255, 255, 255, 0.06);
      color: var(--text);
      border: 1px solid var(--surface-border);
    }}

    .btn-outline:hover {{
      background: rgba(255, 255, 255, 0.12);
      transform: translateY(-1px);
    }}

    /* STATS STRIP */
    .stats-strip {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: 16px;
      margin-bottom: 24px;
    }}

    .stat-card {{
      background: var(--surface);
      border: 1px solid var(--surface-border);
      border-radius: 12px;
      padding: 16px 20px;
    }}

    .stat-label {{
      font-size: 12px;
      color: var(--text-muted);
      text-transform: uppercase;
      font-weight: 700;
      letter-spacing: 0.5px;
    }}

    .stat-value {{
      font-size: 26px;
      font-weight: 800;
      color: var(--emerald);
      margin-top: 4px;
    }}

    .stat-sub {{
      font-size: 12px;
      color: var(--text-muted);
      margin-top: 2px;
    }}

    /* FILTERS & SEARCH */
    .controls {{
      background: var(--surface);
      border: 1px solid var(--surface-border);
      border-radius: 12px;
      padding: 16px 20px;
      margin-bottom: 20px;
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      justify-content: space-between;
      align-items: center;
    }}

    .search-box {{
      flex: 1;
      min-width: 280px;
      position: relative;
    }}

    .search-input {{
      width: 100%;
      background: rgba(15, 23, 42, 0.8);
      border: 1px solid var(--surface-border);
      border-radius: 8px;
      padding: 10px 14px 10px 38px;
      color: #fff;
      font-size: 14px;
      outline: none;
    }}

    .search-input:focus {{
      border-color: var(--emerald);
      box-shadow: 0 0 0 2px var(--emerald-glow);
    }}

    .search-icon {{
      position: absolute;
      left: 12px;
      top: 50%;
      transform: translateY(-50%);
      font-size: 14px;
      color: var(--text-muted);
    }}

    .category-pills {{
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }}

    .pill {{
      padding: 6px 14px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 600;
      background: rgba(255, 255, 255, 0.05);
      color: var(--text-muted);
      border: 1px solid var(--surface-border);
      cursor: pointer;
      transition: all 0.2s ease;
    }}

    .pill:hover, .pill.active {{
      background: var(--emerald);
      color: #000;
      border-color: var(--emerald);
    }}

    /* TABLE */
    .table-container {{
      background: var(--surface);
      border: 1px solid var(--surface-border);
      border-radius: 12px;
      overflow-x: auto;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.2);
    }}

    table {{
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
      text-align: left;
    }}

    th {{
      background: #0B132B;
      color: #CBD5E1;
      padding: 14px 16px;
      font-weight: 700;
      text-transform: uppercase;
      font-size: 11px;
      letter-spacing: 0.5px;
      border-bottom: 2px solid var(--emerald);
      white-space: nowrap;
    }}

    td {{
      padding: 14px 16px;
      border-bottom: 1px solid var(--surface-border);
      vertical-align: top;
    }}

    tr:hover td {{
      background: rgba(0, 168, 107, 0.04);
    }}

    .badge-cat {{
      display: inline-block;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 700;
      background: rgba(0, 168, 107, 0.15);
      color: var(--emerald);
      border: 1px solid rgba(0, 168, 107, 0.3);
      white-space: nowrap;
    }}

    .feature-name {{
      font-weight: 700;
      color: #fff;
      font-size: 14px;
      margin-bottom: 4px;
    }}

    .benefit-box {{
      color: #A7F3D0;
      background: rgba(16, 185, 129, 0.08);
      border-left: 3px solid var(--emerald);
      padding: 6px 10px;
      border-radius: 4px;
      font-size: 12px;
    }}

    .code-ref {{
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 11px;
      color: #94A3B8;
      background: rgba(0, 0, 0, 0.25);
      padding: 2px 6px;
      border-radius: 4px;
      word-break: break-all;
    }}

    .badge-active {{
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      font-weight: 700;
      color: #34D399;
      background: rgba(16, 185, 129, 0.15);
      padding: 3px 8px;
      border-radius: 12px;
      white-space: nowrap;
    }}

    .badge-active::before {{
      content: "";
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: #34D399;
    }}

    /* FOOTER */
    .footer {{
      margin-top: 32px;
      text-align: center;
      font-size: 12px;
      color: var(--text-muted);
      border-top: 1px solid var(--surface-border);
      padding-top: 20px;
    }}

    @media print {{
      body {{
        background: #fff;
        color: #000;
        padding: 0;
      }}
      .controls, .actions-bar, .footer {{
        display: none;
      }}
      .header {{
        background: none;
        color: #000;
        border: none;
        padding: 0 0 20px 0;
      }}
      .header-title h1 {{
        color: #075E54;
      }}
      th {{
        background: #075E54 !important;
        color: #fff !important;
        -webkit-print-color-adjust: exact;
      }}
      td {{
        color: #000;
        border-color: #ddd;
      }}
      .benefit-box {{
        background: #f0fdf4;
        color: #065f46;
      }}
    }}
  </style>
</head>
<body>

  <div class="container">
    <!-- HEADER -->
    <header class="header">
      <div class="header-left">
        <div class="logo-badge">
          <img src="../../marketing/visuels/logo_officiel_wazap.jpg" alt="WAZAP" onerror="this.src='../../web/public/logo-officiel-2026.jpg'">
        </div>
        <div class="header-title">
          <h1>Catalogue des Fonctionnalités Commerçants WAZAP</h1>
          <p>Le cockpit e-commerce & logistique 1-Tap d'Abidjan · Zéro Cash · Zéro Commission</p>
        </div>
      </div>
      <div class="actions-bar">
        <a href="FONCTIONNALITES_COMMERCANTS_WAZAP.xlsx" download class="btn btn-emerald" id="btn-dl-xlsx">
          📥 Télécharger Excel (.xlsx)
        </a>
        <a href="FONCTIONNALITES_COMMERCANTS_WAZAP.csv" download class="btn btn-outline" id="btn-dl-csv">
          📄 Télécharger CSV (.csv)
        </a>
        <button onclick="window.print()" class="btn btn-outline">
          🖨️ Imprimer / PDF
        </button>
      </div>
    </header>

    <!-- STATS STRIP -->
    <div class="stats-strip">
      <div class="stat-card">
        <div class="stat-label">Total Fonctionnalités</div>
        <div class="stat-value">25</div>
        <div class="stat-sub">100% dédiées aux commerçants</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Piliers Stratégiques</div>
        <div class="stat-value">5</div>
        <div class="stat-sub">Sécurité, Logistique, IA, Analytics, Économie</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Temps d'Assignation</div>
        <div class="stat-value">&lt; 3 min</div>
        <div class="stat-sub">Livreur certifié devant la boutique</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Commission Ventes</div>
        <div class="stat-value">0%</div>
        <div class="stat-sub">100% des marges au commerçant</div>
      </div>
    </div>

    <!-- CONTROLS -->
    <div class="controls">
      <div class="search-box">
        <span class="search-icon">🔍</span>
        <input type="text" id="searchInput" class="search-input" placeholder="Rechercher une fonctionnalité, mot-clé, code...">
      </div>
      <div class="category-pills" id="categoryPills">
        <button class="pill active" data-category="all">Toutes (25)</button>
        <button class="pill" data-category="1. Sécurité Financière">Sécurité (5)</button>
        <button class="pill" data-category="2. Vitesse & Logistique">Vitesse & Logistique (6)</button>
        <button class="pill" data-category="3. Catalogue & Vente IA">Catalogue & IA (5)</button>
        <button class="pill" data-category="4. Cockpit & Analytics">Cockpit & Analytics (6)</button>
        <button class="pill" data-category="5. Modèle Économique & Packs">Modèle Éco (3)</button>
      </div>
    </div>

    <!-- TABLE -->
    <div class="table-container">
      <table id="featuresTable">
        <thead>
          <tr>
            <th style="width: 40px; text-align: center;">N°</th>
            <th style="width: 170px;">Pilier</th>
            <th style="width: 240px;">Fonctionnalité WAZAP</th>
            <th style="width: 300px;">Description & Fonctionnement</th>
            <th>Valeur Ajoutée pour le Commerçant</th>
            <th style="width: 180px;">Canal</th>
            <th style="width: 200px;">Fichier / Réf. Code</th>
            <th style="width: 120px; text-align: center;">Statut</th>
          </tr>
        </thead>
        <tbody id="tableBody">
          <!-- Injecté dynamiquement par JavaScript -->
        </tbody>
      </table>
    </div>

    <!-- FOOTER -->
    <footer class="footer">
      <p>WAZAP Côte d'Ivoire · Contact Officiel WhatsApp Business : <strong>+225 05 44 05 19 72</strong> · Plateforme de Production Active</p>
      <p style="margin-top: 4px;">Fichier généré le 05 Octobre 2026 · Documentation Officielle WAZAP</p>
    </footer>
  </div>

  <script>
    const features = {json_data};
    let activeCategory = "all";
    let searchTerm = "";

    function renderTable() {{
      const tbody = document.getElementById("tableBody");
      const filtered = features.filter(item => {{
        const matchesCategory = activeCategory === "all" || item.categorie === activeCategory;
        const matchesSearch = !searchTerm || 
          item.nom.toLowerCase().includes(searchTerm.toLowerCase()) ||
          item.description.toLowerCase().includes(searchTerm.toLowerCase()) ||
          item.valeur_ajoutee.toLowerCase().includes(searchTerm.toLowerCase()) ||
          item.canal.toLowerCase().includes(searchTerm.toLowerCase()) ||
          item.reference_code.toLowerCase().includes(searchTerm.toLowerCase());
        return matchesCategory && matchesSearch;
      }});

      if (filtered.length === 0) {{
        tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; padding: 40px; color: #94A3B8;">
          Aucune fonctionnalité ne correspond à votre recherche.
        </td></tr>`;
        return;
      }}

      tbody.innerHTML = filtered.map(item => `
        <tr>
          <td style="text-align: center; font-weight: 700; color: #94A3B8;">${{item.id}}</td>
          <td><span class="badge-cat">${{item.categorie}}</span></td>
          <td>
            <div class="feature-name">${{item.nom}}</div>
          </td>
          <td style="color: #CBD5E1;">${{item.description}}</td>
          <td>
            <div class="benefit-box">
              <strong>Impact Commerçant :</strong> ${{item.valeur_ajoutee}}
            </div>
          </td>
          <td style="color: #94A3B8; font-size: 12px;">${{item.canal}}</td>
          <td><span class="code-ref">${{item.reference_code}}</span></td>
          <td style="text-align: center;"><span class="badge-active">${{item.statut}}</span></td>
        </tr>
      `).join("");
    }}

    // Recherche
    document.getElementById("searchInput").addEventListener("input", (e) => {{
      searchTerm = e.target.value.trim();
      renderTable();
    }});

    // Filtres Catégories
    document.getElementById("categoryPills").addEventListener("click", (e) => {{
      if (e.target.classList.contains("pill")) {{
        document.querySelectorAll(".pill").forEach(p => p.classList.remove("active"));
        e.target.classList.add("active");
        activeCategory = e.target.getAttribute("data-category");
        renderTable();
      }}
    }});

    // Initial render
    renderTable();
  </script>
</body>
</html>
"""
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(html_content)
    print(f"✅ HTML interactif généré : {output_path}")

if __name__ == "__main__":
    out_dir = r"c:\Dev\Wazap\marketing\commercants"
    os.makedirs(out_dir, exist_ok=True)

    csv_path = os.path.join(out_dir, "FONCTIONNALITES_COMMERCANTS_WAZAP.csv")
    xlsx_path = os.path.join(out_dir, "FONCTIONNALITES_COMMERCANTS_WAZAP.xlsx")
    html_path = os.path.join(out_dir, "tableau_fonctionnalites_commercants.html")

    generate_csv(csv_path)
    generate_excel(xlsx_path)
    generate_html(html_path)
