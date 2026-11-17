"""Assemble Assets/StreamingAssets/Licenses/ThirdPartyNotices.txt.

Header text is hand-written; every licence/notice block after it is copied
verbatim from the package's own file in Library/PackageCache.
Usage: python -I build_notices.py <PackageCache dir> <output file>
"""
import glob
import os
import sys

pc, out = sys.argv[1], sys.argv[2]


def pkg(name):
    hits = [p for p in glob.glob(os.path.join(pc, name + "@*")) if os.path.isdir(p)]
    assert len(hits) == 1, (name, hits)
    return hits[0]


def read(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read().replace("\r\n", "\n").strip("\n")


RULE = "=" * 78

HEADER = """CELESTIAL WORKSHOP - THIRD-PARTY NOTICES
Celestial Workshop by Charlene Atlas, a Proper Motion LLC experience.

This file accompanies the application and lists the notices and licence texts
for third-party software and content distributed with it. Licence and notice
texts below are reproduced verbatim from the files supplied with each
component. Font licences (SIL Open Font License 1.1) are in the Fonts folder
next to this file.


META XR SDKs
Copyright © Meta Platform Technologies, LLC and its affiliates. All rights reserved.
Used and distributed under the Meta Platform Technologies SDK License Agreement:
https://developers.meta.com/horizon/licenses/oculussdk/
Components: Meta XR Core SDK 207.0.0, Meta XR Interaction SDK 207.0.0
(and Interaction SDK OVR integration), Meta MR Utility Kit 207.0.0, Meta XR
Platform SDK 207.0.0, Meta XR Haptics SDK 207.0.0, Meta XR Audio SDK 85.0.0,
Meta XR Voice SDK 85.0.1, including sample materials (UI prefabs, materials,
icons and sounds) from the Meta XR Interaction SDK samples.


UNITY
Celestial Workshop was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies
Copyright © 2005-2026 Unity Technologies. All rights reserved.


OPENXR
Unity OpenXR Plugin 1.17.1 and Unity OpenXR: Meta 2.4.0 include the Khronos
OpenXR loader; their third-party notices are reproduced below.


MUSIC
"The Britons" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/

"Morning" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/


SOUND EFFECTS
"Holy Spell Cast" by DRAGON-STUDIO, Pixabay
https://pixabay.com/sound-effects/holy-spell-cast-450460/
"Magic Teleport Whoosh" by Universfield, Pixabay (edited/shortened)
https://pixabay.com/sound-effects/magic-teleport-whoosh-352764/
Used under the Pixabay Content License: https://pixabay.com/service/license-summary/


OTHER CONTENT
Emoji artwork (TextMesh Pro sample sprite asset, as supplied by Unity):
"This sample of beautiful emojis are provided by EmojiOne https://www.emojione.com/"

Map texture (Meta XR Interaction SDK sample, ComprehensiveGrabMapTexture):
© OpenStreetMap contributors - https://www.openstreetmap.org/copyright

Starfield skybox: "Vibrant 4K Starfield Skybox Pack" by Parallel Cascades
(Unity Asset Store, used under the Unity Asset Store EULA).
"""

BLOCKS = [
    ("Meta XR Core SDK - LICENSE.md", "com.meta.xr.sdk.core", "LICENSE.md"),
    ("Meta XR Core SDK - NOTICE.txt (OpenVR, SimpleJSON)", "com.meta.xr.sdk.core", "NOTICE.txt"),
    ("Meta MR Utility Kit - NOTICE.txt", "com.meta.xr.mrutilitykit", "NOTICE.txt"),
    ("Meta XR Audio SDK - THIRD_PARTY_NOTICES.txt", "com.meta.xr.sdk.audio", "THIRD_PARTY_NOTICES.txt"),
    ("Meta XR Haptics SDK - THIRD_PARTY_NOTICES.txt", "com.meta.xr.sdk.haptics", "THIRD_PARTY_NOTICES.txt"),
    ("Meta XR Interaction SDK - Runtime/ThirdParty/InterfaceSupport/LICENSE.txt", "com.meta.xr.sdk.interaction",
     "Runtime/ThirdParty/InterfaceSupport/LICENSE.txt"),
    ("Meta XR Interaction SDK samples - Runtime/Sample/ThirdParty/Box2DSignedDistance.cginc (licence header)",
     "com.meta.xr.sdk.interaction", "Runtime/Sample/ThirdParty/Box2DSignedDistance.cginc"),
    ("Meta XR Interaction SDK samples - Runtime/Sample/ThirdParty/Textures/ComprehensiveGrabMapTextureCredit.txt",
     "com.meta.xr.sdk.interaction", "Runtime/Sample/ThirdParty/Textures/ComprehensiveGrabMapTextureCredit.txt"),
    ("Meta XR Interaction SDK samples - Runtime/Sample/ThirdParty/Textures/EnvironmentMaps/license.txt",
     "com.meta.xr.sdk.interaction", "Runtime/Sample/ThirdParty/Textures/EnvironmentMaps/license.txt"),
    ("Meta XR Voice SDK - Lib/Wit.ai/LICENSE", "com.meta.xr.sdk.voice", "Lib/Wit.ai/LICENSE"),
    ("Meta XR Voice SDK - third-party/NLayer/LICENSE", "com.meta.xr.sdk.voice", "Lib/Wit.ai/Lib/third-party/NLayer/LICENSE"),
    ("Meta XR Voice SDK - third-party/NativeWebSocket/LICENSE", "com.meta.xr.sdk.voice",
     "Lib/Wit.ai/Lib/third-party/NativeWebSocket/LICENSE"),
    ("Meta XR Voice SDK - third-party/UnityOpus/LICENSE", "com.meta.xr.sdk.voice", "Lib/Wit.ai/Lib/third-party/UnityOpus/LICENSE"),
    ("Unity OpenXR Plugin - Third Party Notices.md", "com.unity.xr.openxr", "Third Party Notices.md"),
    ("Unity OpenXR: Meta - Third Party Notices.md", "com.unity.xr.meta-openxr", "Third Party Notices.md"),
]

parts = [HEADER.strip("\n")]
for title, name, rel in BLOCKS:
    text = read(os.path.join(pkg(name), rel))
    if rel.endswith(".cginc"):
        # Only the licence comment header, not the shader code.
        lines = []
        for line in text.split("\n"):
            if not line.startswith("//"):
                break
            lines.append(line)
        text = "\n".join(lines)
    parts.append(f"{RULE}\n{title}\n{RULE}\n\n{text}")

os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n\n\n".join(parts) + "\n")
print("wrote", out, os.path.getsize(out), "bytes")
