import asyncio
import os
import sys
import subprocess
import edge_tts

# Forcer l'encodage UTF-8 pour Windows PowerShell / CMD
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

# Dossiers
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
AUDIO_DIR = os.path.join(BASE_DIR, 'audio')
MUSIC_PATH = os.path.abspath(os.path.join(BASE_DIR, '..', 'demo-video', 'musique_afrobeat_groove.wav'))

os.makedirs(AUDIO_DIR, exist_ok=True)

# 6 Scénarios calibrés pour exactement 26-27 secondes de voix dans une vidéo de 30,0 secondes
SCENARIOS = [
    {
        "id": "01",
        "name": "commercant_antivol",
        "voice": "fr-FR-HenriNeural",
        "rate": "+12%",
        "text": (
            "Commerçants d'Abidjan : combien de fois un livreur a disparu avec l'argent de vos ventes ? "
            "C'est la hantise du cash à la livraison ! "
            "Avec Wazap, ce cauchemar est terminé. "
            "À la livraison, votre client scanne simplement le QR Code Universel Wazap : "
            "votre compte Mobile Money ou Wave est crédité instantanément, et le livreur reçoit ses frais nets. "
            "Zéro argent liquide sur vos produits, zéro risque de vol ! "
            "Profitez dès maintenant de 15 courses offertes au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    },
    {
        "id": "02",
        "name": "commercant_rapidite",
        "voice": "fr-FR-DeniseNeural",
        "rate": "+14%",
        "text": (
            "Votre colis est prêt, votre client s'impatiente, et aucun livreur ne répond au téléphone ? "
            "Perdre une heure à chercher un coursier, c'est risquer d'annuler sa vente ! "
            "Passez à la vitesse Wazap. "
            "En un seul clic dans votre WhatsApp, un livreur certifié actif dans votre commune est attribué immédiatement. "
            "En trois minutes chrono, il est devant votre boutique pour récupérer le paquet. "
            "Fini le stress et les faux bonds ! "
            "Testez vos 15 courses gratuites : écrivez COLIS sur WhatsApp au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    },
    {
        "id": "03",
        "name": "commercant_colis_sur",
        "voice": "fr-FR-DeniseNeural",
        "rate": "+14%",
        "text": (
            "Marre des applications compliquées à télécharger et des colis qui arrivent ouverts ou abîmés chez vos clients ? "
            "Wazap simplifie et sécurise tout. "
            "Zéro application à installer : vous gérez vos expéditions directement dans votre WhatsApp quotidien. "
            "Et grâce à notre Scellé Colis Sûr inviolable, votre marchandise reste intacte et protégée jusqu'aux mains du client. "
            "Vos ventes sont sécurisées et vos clients rassurés à cent pour cent ! "
            "Activez vos 15 courses offertes en envoyant COLIS au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    },
    {
        "id": "04",
        "name": "livreur_zero_commission",
        "voice": "fr-FR-HenriNeural",
        "rate": "+12%",
        "text": (
            "Livreurs d'Abidjan : pourquoi donner vingt pour cent de votre travail à des applications étrangères ? "
            "Vous roulez sous le soleil, et la moitié de vos gains part en commissions et en essence ! "
            "Chez Wazap, la règle d'or est simple : zéro pour cent de commission. "
            "Cent pour cent du tarif de la course va directement et intégralement dans votre poche ! "
            "Mille francs dans la même commune, mille cinq cents en voisine, deux mille longue distance. "
            "Votre argent vous appartient ! Rejoignez le réseau en envoyant DISPO au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    },
    {
        "id": "05",
        "name": "livreur_securite_dignite",
        "voice": "fr-FR-HenriNeural",
        "rate": "+12%",
        "text": (
            "Rouler avec des centaines de milliers de francs en liquide dans les poches, c'est risquer sa vie à chaque coin de rue. "
            "Et subir la méfiance des vendeurs qui ont peur pour leur argent, ça suffit ! "
            "Wazap vous redonne votre dignité et votre sécurité. "
            "Vous ne transportez aucun cash lié à la marchandise : le client scanne le QR code, et vous touchez vos frais de course immédiatement. "
            "Travaillez l'esprit libre avec le statut officiel de Livreur Certifié Wazap ! "
            "Tapez simplement DISPO sur WhatsApp au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    },
    {
        "id": "06",
        "name": "livreur_proximite_smartphone",
        "voice": "fr-FR-HenriNeural",
        "rate": "+12%",
        "text": (
            "Assez de brûler de l'essence à tourner en rond à l'autre bout de la ville, avec un vieux téléphone cassé qui s'éteint en pleine course ! "
            "Avec Wazap, sélectionnez votre commune en un clic et enchaînez les courses de proximité près de chez vous. "
            "Et le plus beau : notre Défi Ambassadeur offre cinquante smartphones neufs Redmi 15C tous les trois mois aux livreurs les plus actifs et à leurs filleuls ! "
            "Gagnez du matériel neuf et développez votre activité. "
            "Envoyez DISPO sur WhatsApp au zéro cinq, quarante-quatre, zéro cinq, dix-neuf, soixante-douze !"
        )
    }
]

async def generate_speech(item):
    raw_mp3 = os.path.join(AUDIO_DIR, f"voice_{item['id']}_{item['name']}.mp3")
    master_aac = os.path.join(AUDIO_DIR, f"master_track_{item['id']}_30s.aac")

    print(f"🎙️ [Voice {item['id']}/06] Génération voix studio ({item['voice']})...")
    comm = edge_tts.Communicate(item['text'], item['voice'], rate=item['rate'])
    await comm.save(raw_mp3)

    print(f"🎵 [Mux {item['id']}/06] Mixage 30s avec musique Afrobeat...")
    # Mixage FFmpeg calibré sur 30,0s (la musique fait déjà 35s)
    filter_complex = (
        "[1:a]atrim=0:30,volume=0.20,afade=t=out:st=28.8:d=1.2[music];"
        "[0:a]volume=1.25[voice];"
        "[music][voice]amix=inputs=2:duration=first:dropout_transition=2,afade=t=out:st=29.2:d=0.8[aout]"
    )

    cmd = [
        "ffmpeg", "-y",
        "-i", raw_mp3,
        "-i", MUSIC_PATH,
        "-filter_complex", filter_complex,
        "-map", "[aout]",
        "-c:a", "aac",
        "-b:a", "192k",
        "-t", "30",
        master_aac
    ]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    print(f"✅ [Audio {item['id']}] Master 30s OK : {os.path.basename(master_aac)}")

async def main():
    print("===============================================================")
    print("🎙️ GÉNÉRATION DU PACK AUDIO STUDIO WAZAP (6 VOIX-OFF + AFROBEAT)")
    print("===============================================================\n")
    for s in SCENARIOS:
        await generate_speech(s)
    print("\n🎉 Les 6 pistes audio 30s ont été produites avec succès !")

if __name__ == "__main__":
    asyncio.run(main())
