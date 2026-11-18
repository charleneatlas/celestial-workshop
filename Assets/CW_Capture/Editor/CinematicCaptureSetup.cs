using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Capture-branch only: puts one "Big Dipper Cinematic" Movie Recorder in the
/// Recorder window (Targeted Camera by tag, 1920x1080, ProRes 422 HQ, 60 fps
/// constant, manual start/stop, no audio). Re-run it to restore the settings.
/// </summary>
public static class CinematicCaptureSetup
{
    private const string RecorderName = "Big Dipper Cinematic";

    [MenuItem("Tools/Celestial Workshop/Cinematic Capture/Configure Recorder (Big Dipper)")]
    public static void ConfigureRecorder()
    {
        bool windowWasOpen = EditorWindow.HasOpenInstances<RecorderWindow>();
        if (windowWasOpen)
        {
            // The open window keeps its own copy and would save over ours.
            EditorWindow.GetWindow<RecorderWindow>().Close();
        }

        RecorderControllerSettings global = RecorderControllerSettings.GetGlobalSettings();
        foreach (RecorderSettings existing in new System.Collections.Generic.List<RecorderSettings>(global.RecorderSettings))
        {
            if (existing.name == RecorderName)
            {
                global.RemoveRecorder(existing);
            }
            else
            {
                existing.Enabled = false; // only this one records
            }
        }

        global.SetRecordModeToManual();
        global.FrameRatePlayback = FrameRatePlayback.Constant;
        global.FrameRate = 60f;
        global.CapFrameRate = false; // capping would fight the headset's frame pacing

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = RecorderName;
        movie.Enabled = true;
        movie.EncoderSettings = new ProResEncoderSettings { Format = ProResEncoderSettings.OutputFormat.ProRes422HQ };
        movie.CaptureAlpha = false;
        movie.CaptureAudio = false; // audio capture would mute the Editor's speakers
        movie.ImageInputSettings = new CameraInputSettings
        {
            Source = ImageSource.TaggedCamera,
            CameraTag = CinematicCaptureCamera.CaptureTag,
            OutputWidth = 1920,
            OutputHeight = 1080,
            CaptureUI = false,
            FlipFinalOutput = false
        };
        movie.OutputFile = "Recordings/BigDipper_" + DefaultWildcard.Take;
        movie.Take = 1;

        global.AddRecorderSettings(movie);
        global.Save();

        Debug.Log("Cinematic Capture: Recorder configured (Targeted Camera tag '" + CinematicCaptureCamera.CaptureTag +
                  "', 1920x1080, ProRes 422 HQ, 60 fps constant, manual). Output: <project>/Recordings/BigDipper_<Take>.mov");

        EditorWindow.GetWindow<RecorderWindow>();
    }
}
