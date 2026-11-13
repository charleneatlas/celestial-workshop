using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CW.AudioFeedback.Editor
{
    /// <summary>
    /// Builds Assets/CW_AudioFeedback/AudioFeedback.prefab (the configured
    /// cue table) and the AudioFeedbackPreview.unity test scene. The scene is
    /// created additively, saved and closed, so whatever scene is open stays
    /// open and untouched. Safe to run repeatedly: both assets are overwritten
    /// in place (GUIDs kept).
    /// </summary>
    public static class AudioFeedbackPreviewBuilder
    {
        private const string Folder = "Assets/CW_AudioFeedback";
        private const string ScenePath = Folder + "/AudioFeedbackPreview.unity";
        private const string PrefabPath = Folder + "/AudioFeedback.prefab";

        private const string Cw = "Assets/Audio/CW_Audio/";
        private const string Isdk = "Assets/Audio/CW_Audio/ISDK/";

        private struct CueSpec
        {
            public FeedbackCue Cue;
            public string[] Paths;
            public float Volume;
            public float PitchJitter;
            public float Cooldown;
            public bool Spatial;
            public float MinInterval;
            public string Notes;
        }

        private static CueSpec[] Specs()
        {
            return new[]
            {
                new CueSpec
                {
                    Cue = FeedbackCue.Select,
                    Paths = new[]
                    {
                        Isdk + "Interaction_BasicGrab_Grab_01.wav",
                        Isdk + "Interaction_BasicGrab_Grab_02.wav",
                        Isdk + "Interaction_BasicGrab_Grab_03.wav",
                        Isdk + "Interaction_BasicGrab_Grab_04.wav",
                        Isdk + "Interaction_BasicGrab_Grab_05.wav",
                    },
                    Volume = 0.6f, PitchJitter = 0.04f, Cooldown = 0.12f, Spatial = true, MinInterval = 2f,
                    Notes = "ISDK BasicGrab_Grab x5. Pinch start in sphere (VolumePinchRotation.PinchStarted), title letter grabbed.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.Release,
                    Paths = new[]
                    {
                        Isdk + "Interaction_BasicGrab_Release_01.wav",
                        Isdk + "Interaction_BasicGrab_Release_02.wav",
                        Isdk + "Interaction_BasicGrab_Release_03.wav",
                        Isdk + "Interaction_BasicGrab_Release_04.wav",
                        Isdk + "Interaction_BasicGrab_Release_05.wav",
                    },
                    Volume = 0.5f, PitchJitter = 0.04f, Cooldown = 0.12f, Spatial = true, MinInterval = 2f,
                    Notes = "ISDK BasicGrab_Release x5. VolumePinchRotation.PinchEnded.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.Place,
                    Paths = new[] { Cw + "constellation_dropping_on_table.wav" },
                    Volume = 0.55f, PitchJitter = 0.05f, Cooldown = 0.2f, Spatial = true, MinInterval = 0f,
                    Notes = "Project impact clip (also used by ConstellationImpactAudio). Title letter placed in sphere. If too heavy, swap for ISDK Object_Impact_*.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.AxisLock,
                    Paths = new[] { Isdk + "Interaction_Locomotion_SnapTurn.wav" },
                    Volume = 0.45f, PitchJitter = 0.03f, Cooldown = 0.25f, Spatial = true, MinInterval = 0f,
                    Notes = "ISDK snap-turn detent (0.05 s). VolumePinchRotation.AxisLocked.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.NearSolve,
                    Paths = new[] { Isdk + "Interaction_Object_Reappear_01.wav" },
                    Volume = 0.35f, PitchJitter = 0f, Cooldown = 1f, Spatial = false, MinInterval = 0f,
                    Notes = "TEMP: ISDK Object_Reappear_01 stand-in; wants a soft rising shimmer. ConstellationSolveController onNearSolveEntered.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.Solve,
                    Paths = new[] { Cw + "dragon-studio-holy-spell-cast-450460.wav" },
                    Volume = 0.8f, PitchJitter = 0f, Cooldown = 2f, Spatial = false, MinInterval = 0f,
                    Notes = "Project clip, already played on solve by a scene UnityEvent (AudioSource.PlayOneShot): remove that listener when hooking this.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.Stamp,
                    Paths = new[] { "Assets/Audio/interaction_hammer_hit_anvil.wav" },
                    Volume = 0.6f, PitchJitter = 0.03f, Cooldown = 0.5f, Spatial = false, MinInterval = 0f,
                    Notes = "Project clip (unused so far). SkyStamp3DVFX.Play().",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.Signed,
                    Paths = new[] { Isdk + "Interaction_PoseDetection_ThumbsUp_01.wav" },
                    Volume = 0.6f, PitchJitter = 0f, Cooldown = 1f, Spatial = false, MinInterval = 0f,
                    Notes = "TEMP: ISDK ThumbsUp confirmation stand-in; wants a small flourish. ConstellationSigning.Signed.",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.ButtonPress,
                    Paths = new[] { Isdk + "Interaction_BasicPoke_ButtonPress.wav" },
                    Volume = 0.7f, PitchJitter = 0.03f, Cooldown = 0.08f, Spatial = true, MinInterval = 0f,
                    Notes = "ISDK poke button press. MomentaryToggleButton.Pressed (PlayAt the button).",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.IntroStart,
                    Paths = new[] { Cw + "universfield-magic-teleport-whoosh-352764_SHORTER.wav" },
                    Volume = 0.5f, PitchJitter = 0f, Cooldown = 1f, Spatial = false, MinInterval = 0f,
                    Notes = "Project whoosh (2.7 s, intro is ~3.2 s); also the ForgeTriggerAudio clip. ConstellationIntro.PlayIntro().",
                },
                new CueSpec
                {
                    Cue = FeedbackCue.TitleStart,
                    Paths = new[] { Isdk + "Interaction_Locomotion_Teleport.wav" },
                    Volume = 0.6f, PitchJitter = 0f, Cooldown = 1f, Spatial = false, MinInterval = 0f,
                    Notes = "TEMP: ISDK Locomotion_Teleport stand-in; wants a short 'begin' swell. Title Start pressed.",
                },
            };
        }

        [MenuItem("Tools/Celestial Workshop/Build Audio Feedback Preview")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[AudioFeedback] Exit Play mode before building the preview.");
                return;
            }

            Scene existing = SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                {
                    Debug.LogError("[AudioFeedback] The preview scene is the only open scene. Open another scene, then rebuild.");
                    return;
                }

                if (existing.isDirty)
                {
                    Debug.LogError("[AudioFeedback] The open preview scene has unsaved changes. Save or close it, then rebuild.");
                    return;
                }

                EditorSceneManager.CloseScene(existing, true);
            }

            EnsureFolder(Folder);

            var missing = new List<string>();
            FeedbackCueEntry[] entries = BuildEntries(missing);

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            try
            {
                // New objects go into the active scene; point it at the preview scene.
                SceneManager.SetActiveScene(scene);

                var feedbackObject = new GameObject("AudioFeedback");
                var feedback = feedbackObject.AddComponent<AudioFeedback>();
                feedback.ConfigureEntries(entries);
                PrefabUtility.SaveAsPrefabAssetAndConnect(feedbackObject, PrefabPath, InteractionMode.AutomatedAction, out bool prefabSaved);
                if (!prefabSaved)
                {
                    Debug.LogError("[AudioFeedback] Could not save " + PrefabPath);
                }

                // Default camera sits at (0, 1, -10) facing +Z; emitter 1 m ahead, to the right.
                var emitter = new GameObject("SpatialEmitter");
                emitter.transform.position = new Vector3(0.8f, 1f, -9.4f);

                var driverObject = new GameObject("AudioFeedbackPreview");
                var driver = driverObject.AddComponent<AudioFeedbackPreviewDriver>();
                driver.Configure(feedback, emitter.transform);

                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    Debug.LogError("[AudioFeedback] Could not save " + ScenePath);
                }
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded && SceneManager.GetActiveScene() != previousActive)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.Append("[AudioFeedback] Built ").Append(PrefabPath).Append(" and ").Append(ScenePath).Append('.');
            if (missing.Count > 0)
            {
                report.Append(" Clips not found (cue left without them): ").Append(string.Join(", ", missing));
                Debug.LogWarning(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        private static FeedbackCueEntry[] BuildEntries(List<string> missing)
        {
            CueSpec[] specs = Specs();
            var entries = new FeedbackCueEntry[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                CueSpec spec = specs[i];
                var clips = new List<AudioClip>();
                foreach (string path in spec.Paths)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                    else
                    {
                        missing.Add(path);
                    }
                }

                entries[i] = new FeedbackCueEntry
                {
                    cue = spec.Cue,
                    variants = clips.ToArray(),
                    volume = spec.Volume,
                    pitchJitter = spec.PitchJitter,
                    cooldownSeconds = spec.Cooldown,
                    spatial = spec.Spatial,
                    minInterval = spec.MinInterval,
                    notes = spec.Notes,
                };
            }

            return entries;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
