import asyncio
import os
import subprocess
import edge_tts

AUDIO_DIR = r"c:\Dev\Wazap\WazapSln\marketing\videos\magic_importer\audio"
VOICE_NARRATOR = "fr-FR-HenriNeural"
VOICE_AWA = "fr-FR-DeniseNeural"

SEGMENTS = [
    {
        "id": "seg1_narrator",
        "voice": VOICE_NARRATOR,
        "rate": "+4%",
        "text": "Vous passez encore des heures à taper la liste de vos articles et vos prix un par un sur WhatsApp ? Stop ! Vos journées sont bien trop précieuses pour ça."
    },
    {
        "id": "seg2a_awa",
        "voice": VOICE_AWA,
        "rate": "+5%",
        "text": "Regardez bien : une seule photo de mon menu ou le lien de ma page Facebook... et c'est tout !"
    },
    {
        "id": "seg2b_narrator",
        "voice": VOICE_NARRATOR,
        "rate": "+5%",
        "text": "Grâce à l'intelligence artificielle WAZAP, envoyez une photo de votre menu, une capture d'écran de votre page, ou collez simplement votre texte. En trois secondes chrono, votre Mini-Boutique WhatsApp est prête !"
    },
    {
        "id": "seg3_narrator",
        "voice": VOICE_NARRATOR,
        "rate": "+5%",
        "text": "Vos produits sont automatiquement sublimés avec des visuels professionnels haute définition, des prix clairs en FCFA et un menu interactif. Vos clients voient vos créations en grand et commandent en répondant simplement par un chiffre !"
    },
    {
        "id": "seg4a_awa",
        "voice": VOICE_AWA,
        "rate": "+5%",
        "text": "Dès qu'un client choisit son article, un livreur certifié est assigné immédiatement. Zéro tracas, et ma marchandise est assurée jusqu'à cinquante mille francs CFA !"
    },
    {
        "id": "seg4b_narrator",
        "voice": VOICE_NARRATOR,
        "rate": "+5%",
        "text": "Aucun appel interminable, aucun intermédiaire. Livraison express et traçabilité totale en temps réel dans tout Abidjan."
    },
    {
        "id": "seg5_narrator",
        "voice": VOICE_NARRATOR,
        "rate": "+5%",
        "text": "Testez la magie WAZAP dès aujourd'hui ! Vos quinze premières livraisons et votre Mini-Boutique IA sont totalement offertes. Envoyez le mot IMPORT sur WhatsApp au 07 87 11 95 20 et vendez plus vite dès ce soir !"
    }
]

async def generate():
    for seg in SEGMENTS:
        out_path = os.path.join(AUDIO_DIR, f"{seg['id']}.mp3")
        communicate = edge_tts.Communicate(seg["text"], seg["voice"], rate=seg["rate"])
        await communicate.save(out_path)
        print(f"Generated {out_path}")
        
        # Get duration via ffprobe
        cmd = [
            "ffprobe", "-v", "error", "-show_entries",
            "format=duration", "-of", "default=noprint_wrappers=1:nokey=1",
            out_path
        ]
        duration = subprocess.check_output(cmd).decode().strip()
        print(f"  Duration: {float(duration):.2f}s")

if __name__ == "__main__":
    asyncio.run(generate())
