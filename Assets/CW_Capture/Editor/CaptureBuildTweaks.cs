using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Capture-branch only: player builds from this branch are footage builds.
/// While the scene is processed for a build (never in Play mode, never saved
/// to the scene file) it turns the background music off and drops the
/// Editor-only cinematic cameras, so the headset doesn't render them.
/// </summary>
public sealed class CaptureBuildTweaks : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!BuildPipeline.isBuildingPlayer)
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (BackgroundMusic music in root.GetComponentsInChildren<BackgroundMusic>(true))
            {
                music.MusicEnabled = false;
                Debug.Log("Capture build: background music off in " + scene.name, music);
            }

            foreach (CinematicCaptureCamera capture in root.GetComponentsInChildren<CinematicCaptureCamera>(true))
            {
                capture.gameObject.SetActive(false);
                Debug.Log("Capture build: cinematic capture camera disabled in " + scene.name, capture);
            }

            foreach (CinematicShotCamera shot in root.GetComponentsInChildren<CinematicShotCamera>(true))
            {
                shot.gameObject.SetActive(false);
            }
        }
    }
}
