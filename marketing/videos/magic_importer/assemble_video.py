import os
import subprocess

BASE_DIR = r"c:\Dev\Wazap\WazapSln\marketing\videos\magic_importer"
AUDIO_DIR = os.path.join(BASE_DIR, "audio")
TOOLS_DIR = r"c:\Dev\Wazap\tools\video-recorder"

MUSIC_PATH = os.path.join(TOOLS_DIR, "music_dialogue.wav")
OUTPUT_VIDEO = os.path.join(BASE_DIR, "wazap_magic_importer_officiel.mp4")

SCENES = [
    {
        "id": "scene1",
        "image": os.path.join(BASE_DIR, "scene1_hook.jpg"),
        "duration": 10.0,
        "voices": [("seg1_narrator.mp3", 0.5)],
        # Ken burns: slow push-in zoom into Awa
        "zoompan": "zoompan=z='min(zoom+0.0015,1.2)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=300:s=1080x1920:fps=30"
    },
    {
        "id": "scene2",
        "image": os.path.join(BASE_DIR, "scene2_magic_scan.jpg"),
        "duration": 19.0,
        "voices": [("seg2a_awa.mp3", 0.4), ("seg2b_narrator.mp3", 6.2)],
        # Ken burns: slow zoom into holographic scan
        "zoompan": "zoompan=z='min(zoom+0.0008,1.18)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=570:s=1080x1920:fps=30"
    },
    {
        "id": "scene3",
        "image": os.path.join(BASE_DIR, "scene3_showcase.jpg"),
        "duration": 13.0,
        "voices": [("seg3_narrator.mp3", 0.5)],
        # Ken burns: slow zoom towards product cards
        "zoompan": "zoompan=z='min(zoom+0.0012,1.2)':x='iw/2-(iw/zoom/2)':y='ih*0.45-(ih/zoom*0.45)':d=390:s=1080x1920:fps=30"
    },
    {
        "id": "scene4",
        "image": os.path.join(BASE_DIR, "scene4_delivery.jpg"),
        "duration": 19.0,
        "voices": [("seg4a_awa.mp3", 0.4), ("seg4b_narrator.mp3", 11.0)],
        # Ken burns: gentle zoom & pan showing rider and store
        "zoompan": "zoompan=z='min(zoom+0.0008,1.18)':x='iw*0.4-(iw/zoom*0.4)':y='ih*0.5-(ih/zoom*0.5)':d=570:s=1080x1920:fps=30"
    },
    {
        "id": "scene5",
        "image": os.path.join(BASE_DIR, "scene5_cta.jpg"),
        "duration": 15.0,
        "voices": [("seg5_narrator.mp3", 0.4)],
        # Ken burns: subtle pulse into 15 courses & CTA
        "zoompan": "zoompan=z='min(zoom+0.0009,1.15)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=450:s=1080x1920:fps=30"
    }
]

def build_scene_videos():
    scene_clips = []
    for sc in SCENES:
        clip_path = os.path.join(BASE_DIR, f"{sc['id']}_clip.mp4")
        scene_clips.append(clip_path)
        print(f"Rendering {sc['id']} ({sc['duration']}s)...")
        # FFmpeg command to turn still image into smooth 1080x1920 30fps Ken Burns video
        cmd = [
            "ffmpeg", "-y",
            "-loop", "1", "-i", sc["image"],
            "-vf", f"{sc['zoompan']},format=yuv420p",
            "-t", str(sc["duration"]),
            "-r", "30",
            "-c:v", "libx264", "-preset", "fast", "-crf", "18",
            clip_path
        ]
        subprocess.check_call(cmd)
    return scene_clips

def build_voice_track(total_duration):
    voice_track = os.path.join(AUDIO_DIR, "full_voice_track.wav")
    print("Building full voice track...")
    
    # We build an ffmpeg command to position each voice segment at its absolute offset
    # Absolute offsets:
    # Scene 1 starts at 0.0s: seg1 at 0.5s
    # Scene 2 starts at 10.0s: seg2a at 10.4s, seg2b at 16.2s
    # Scene 3 starts at 29.0s: seg3 at 29.5s
    # Scene 4 starts at 42.0s: seg4a at 42.4s, seg4b at 53.0s
    # Scene 5 starts at 61.0s: seg5 at 61.4s
    
    voice_events = [
        ("seg1_narrator.mp3", 500),      # 0.5s in ms
        ("seg2a_awa.mp3", 10400),        # 10.4s
        ("seg2b_narrator.mp3", 16200),   # 16.2s
        ("seg3_narrator.mp3", 29500),    # 29.5s
        ("seg4a_awa.mp3", 42400),        # 42.4s
        ("seg4b_narrator.mp3", 53000),   # 53.0s
        ("seg5_narrator.mp3", 61400),    # 61.4s
    ]
    
    inputs = []
    filter_delays = []
    mix_inputs = []
    
    for idx, (filename, delay_ms) in enumerate(voice_events):
        file_path = os.path.join(AUDIO_DIR, filename)
        inputs.extend(["-i", file_path])
        filter_delays.append(f"[{idx}:a]adelay={delay_ms}|{delay_ms}[a{idx}]")
        mix_inputs.append(f"[a{idx}]")
    
    filter_complex = ";".join(filter_delays) + f";{''.join(mix_inputs)}amix=inputs={len(voice_events)}:dropout_transition=0,volume=2.2[aout]"
    
    cmd = ["ffmpeg", "-y"] + inputs + [
        "-filter_complex", filter_complex,
        "-map", "[aout]",
        "-t", str(total_duration),
        voice_track
    ]
    subprocess.check_call(cmd)
    return voice_track

def build_master_audio(voice_track, total_duration):
    master_audio = os.path.join(AUDIO_DIR, "master_audio.wav")
    print("Mixing speech with background music...")
    
    # Music loop + ducking: music volume at 0.12 (quiet background), speech at 1.0
    filter_str = (
        f"[1:a]aloop=loop=-1:size=2e+09,atrim=0:{total_duration},afade=t=out:st={total_duration-3}:d=3,volume=0.15[bgm];"
        f"[0:a][bgm]amix=inputs=2:duration=first:dropout_transition=0[master]"
    )
    cmd = [
        "ffmpeg", "-y",
        "-i", voice_track,
        "-i", MUSIC_PATH,
        "-filter_complex", filter_str,
        "-map", "[master]",
        master_audio
    ]
    subprocess.check_call(cmd)
    return master_audio

def assemble_final_video(scene_clips, master_audio):
    print("Concatenating video clips and muxing master audio...")
    concat_list = os.path.join(BASE_DIR, "concat_list.txt")
    with open(concat_list, "w", encoding="utf-8") as f:
        for clip in scene_clips:
            clean_path = clip.replace("\\", "/")
            f.write(f"file '{clean_path}'\n")
    
    cmd = [
        "ffmpeg", "-y",
        "-f", "concat", "-safe", "0", "-i", concat_list,
        "-i", master_audio,
        "-c:v", "copy",
        "-c:a", "aac", "-b:a", "192k",
        "-shortest",
        OUTPUT_VIDEO
    ]
    subprocess.check_call(cmd)
    print(f"SUCCESS: Master video created at {OUTPUT_VIDEO}")

if __name__ == "__main__":
    total_dur = sum(s["duration"] for s in SCENES)
    print(f"Total duration: {total_dur}s")
    clips = build_scene_videos()
    vtrack = build_voice_track(total_dur)
    maudio = build_master_audio(vtrack, total_dur)
    assemble_final_video(clips, maudio)
