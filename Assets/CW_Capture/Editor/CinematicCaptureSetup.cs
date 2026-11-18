using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Capture-branch only: puts one Movie Recorder per cinematic shot in the
/// Recorder window (Targeted Camera by tag, 1920x1080, ProRes 422 HQ, 60 fps
/// constant, manual start/stop, no audio). Re-run it to restore the settings.
/// Shots already listed keep their on/off tick; new ones start with only the
/// Big Dipper ticked. Tick a recorder only when its camera object is active.
/// </summary>
public static class CinematicCaptureSetup
{
    private struct Shot
    {
        public string Name;
        public string Tag;
        public string File;
        public bool OnByDefault;
    }

    private static readonly Shot[] Shots =
    {
        new Shot { Name = "Big Dipper Cinematic", Tag = CinematicCaptureCamera.CaptureTag, File = "BigDipper_", OnByDefault = true },
        new Shot { Name = "Diorama Celebration", Tag = "CinematicDiorama", File = "Diorama_", OnByDefault = false },
        new Shot { Name = "Star Chart Close-up", Tag = "CinematicChart", File = "StarChart_", OnByDefault = false },
    };

    [MenuItem("Tools/Celestial Workshop/Cinematic Capture/Configure Recorders")]
    public static void ConfigureRecorder()
    {
        if (EditorWindow.HasOpenInstances<RecorderWindow>())
        {
            // The open window keeps its own copy and would save over ours.
            EditorWindow.GetWindow<RecorderWindow>().Close();
        }

        RecorderControllerSettings global = RecorderControllerSettings.GetGlobalSettings();
        var wasOn = new Dictionary<string, bool>();
        foreach (RecorderSettings existing in new List<RecorderSettings>(global.RecorderSettings))
        {
            bool ours = System.Array.Exists(Shots, s => s.Name == existing.name);
            if (ours)
            {
                wasOn[existing.name] = existing.Enabled;
                global.RemoveRecorder(existing);
            }
            else
            {
                existing.Enabled = false; // only the cinematic shots record
            }
        }

        global.SetRecordModeToManual();
        global.FrameRatePlayback = FrameRatePlayback.Constant;
        global.FrameRate = 60f;
        global.CapFrameRate = false; // capping would fight the headset's frame pacing

        foreach (Shot shot in Shots)
        {
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = shot.Name;
            movie.Enabled = wasOn.TryGetValue(shot.Name, out bool on) ? on : shot.OnByDefault;
            movie.EncoderSettings = new ProResEncoderSettings { Format = ProResEncoderSettings.OutputFormat.ProRes422HQ };
            movie.CaptureAlpha = false;
            movie.CaptureAudio = false; // audio capture would mute the Editor's speakers
            movie.ImageInputSettings = new CameraInputSettings
            {
                Source = ImageSource.TaggedCamera,
                CameraTag = shot.Tag,
                OutputWidth = 1920,
                OutputHeight = 1080,
                CaptureUI = false,
                FlipFinalOutput = false
            };
            movie.OutputFile = "Recordings/" + shot.File + DefaultWildcard.Take;
            movie.Take = 1;
            global.AddRecorderSettings(movie);
        }

        global.Save();
        Debug.Log("Cinematic Capture: recorders configured (Big Dipper, Diorama Celebration, Star Chart Close-up; 1920x1080 ProRes 422 HQ, 60 fps constant, manual). Output: <project>/Recordings/<Shot>_<Take>.mov");
        EditorWindow.GetWindow<RecorderWindow>();
    }
}
