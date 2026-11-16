using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Field Note > Run Selection Table Self-Test.
/// Enters Play, walks the box selection through its states with the real
/// buttons (Press() = the same path as a poke), checks the Field Note reader,
/// locked boxes, a launch and the return, and Raise / Lower placement, then
/// leaves Play. Results go to the Console / Editor.log as "FNTEST" lines.
/// Short on purpose (~12 s): Play without a worn headset is unstable over a minute.
/// </summary>
[InitializeOnLoad]
public static class SelectionTableSelfTest
{
    const string Flag = "FNTEST.run";
    static double start;
    static int step;
    static readonly StringBuilder log = new StringBuilder();
    static int fails;
    static Vector3 tableToRig;

    static SelectionTableSelfTest()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                start = EditorApplication.timeSinceStartup;
                step = 0;
                fails = 0;
                log.Clear();
                EditorApplication.update += Tick;
            }
        };
    }

    [MenuItem("Tools/Celestial Workshop/Field Note/Run Selection Table Self-Test")]
    static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        string line = $"FNTEST {(ok ? "PASS" : "FAIL")} {what}";
        log.AppendLine(line);
        if (ok) Debug.Log(line); else Debug.LogError(line);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= Tick;
            return;
        }

        double t = EditorApplication.timeSinceStartup - start;
        // Steps are 0.8 s apart after a 3 s settle.
        if (t < 3.0 + step * 0.8) return;

        var gf = Object.FindFirstObjectByType<GameFlow>();
        var sel = Object.FindFirstObjectByType<BoxSelection>(FindObjectsInactive.Include);
        var reader = Object.FindFirstObjectByType<FieldNoteReader>(FindObjectsInactive.Include);
        var wp = Object.FindFirstObjectByType<WorkshopPlacement>();
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        var so = new SerializedObject(sel);
        var row = (GameObject)so.FindProperty("backToTitleButtonRoot").objectReferenceValue;
        var allBoxesRow = (GameObject)so.FindProperty("allBoxesButtonRoot").objectReferenceValue;
        var boxes = new List<ConstellationBox>();
        var bp = so.FindProperty("boxes");
        for (int i = 0; i < bp.arraySize; i++) boxes.Add((ConstellationBox)bp.GetArrayElementAtIndex(i).objectReferenceValue);
        var rso = new SerializedObject(reader);
        var openBtn = (MomentaryToggleButton)rso.FindProperty("openButton").objectReferenceValue;
        var backBtn = (MomentaryToggleButton)rso.FindProperty("backButton").objectReferenceValue;
        var board = (GameObject)rso.FindProperty("board").objectReferenceValue;
        Transform table = sel.transform.Find("Selection Table");
        Transform note = sel.transform.Find("Field Note (Folded)");

        try
        {
            switch (step)
            {
                case 0:
                    Check(gf != null && sel != null && reader != null, "found GameFlow, BoxSelection, FieldNoteReader");
                    gf.OpenBoxes();
                    break;
                case 1:
                    Check(gf.Current == GameFlow.Stage.Boxes, $"stage Boxes (is {gf.Current})");
                    Check(sel.Current == BoxSelection.State.Closed, $"selection Closed (is {sel.Current})");
                    Check(boxes.TrueForAll(b => b.gameObject.activeInHierarchy), "all four boxes shown");
                    Check(table.gameObject.activeInHierarchy && note.gameObject.activeInHierarchy, "table and folded note shown");
                    Check(row.activeInHierarchy && openBtn.gameObject.activeInHierarchy, "box row with Field Note button shown");
                    Check(!board.activeSelf && !reader.IsOpen, "reader closed at start");
                    foreach (var b in boxes)
                    {
                        if (!b.IsAvailable)
                        {
                            b.Open(false);
                            Check(!b.IsOpen, $"locked '{b.DisplayName}' refuses to open");
                        }
                    }

                    Check(sel.Current == BoxSelection.State.Closed, "still Closed after trying locked boxes");
                    openBtn.Press();
                    break;
                case 2:
                    Check(reader.IsOpen && board.activeInHierarchy, "Field Note button opens the reader");
                    Check(FieldNoteReader.IsAnyOpen, "IsAnyOpen (hints suppressed)");
                    Check(!row.activeSelf, "box row steps aside while reading");
                    Check(sel.Current == BoxSelection.State.Closed && boxes.TrueForAll(b => b.gameObject.activeInHierarchy), "boxes untouched while reading");
                    Check(board.transform.localScale.x > 0.99f, $"sheet settled to full size ({board.transform.localScale.x:F3})");
                    backBtn.Press();
                    break;
                case 3:
                    Check(!reader.IsOpen && !board.activeSelf, "Back closes the reader");
                    Check(row.activeSelf, "box row restored");
                    Check(!FieldNoteReader.IsAnyOpen, "IsAnyOpen cleared");
                    openBtn.Press();
                    break;
                case 4:
                    Check(reader.IsOpen, "reader reopens");
                    // Opening the Starter Pack while reading: the reader puts itself away.
                    sel.StarterPack.Open(false);
                    Check(sel.Current == BoxSelection.State.PackOpen, $"Starter Pack opens (is {sel.Current})");
                    Check(!reader.IsOpen && !board.activeSelf, "reader closes when a box opens");
                    Check(!row.activeSelf && allBoxesRow.activeSelf, "All boxes button replaces the box row");
                    Check(boxes.FindAll(b => b.gameObject.activeSelf).Count == 1, "other boxes step aside");
                    break;
                case 5:
                    sel.ShowAllBoxes();
                    Check(sel.Current == BoxSelection.State.Closed && boxes.TrueForAll(b => b.gameObject.activeInHierarchy), "All boxes: back to four shut boxes");
                    Check(row.activeSelf && !reader.IsOpen, "box row back, reader shut");
                    tableToRig = rig.transform.InverseTransformPoint(table.position);
                    wp.Raise();
                    break;
                case 6:
                {
                    Vector3 now = rig.transform.InverseTransformPoint(table.position);
                    Check(Mathf.Abs((now.y - tableToRig.y) - 0.05f) < 0.006f, $"Raise lifts the table relative to the player ({(now.y - tableToRig.y) * 100f:F1} cm)");
                    wp.Lower();
                    break;
                }
                case 7:
                {
                    Vector3 now = rig.transform.InverseTransformPoint(table.position);
                    Check((now - tableToRig).magnitude < 0.006f, "Lower puts it back");
                    sel.StarterPack.Open(true);
                    sel.DebugPlaceSculpture(1);
                    break;
                }
                case 8:
                    Check(gf.Current == GameFlow.Stage.Puzzle, $"placing a sculpture launches the puzzle (stage {gf.Current})");
                    Check(!sel.gameObject.activeInHierarchy && !reader.IsOpen, "table hidden in the puzzle");
                    break;
                case 10:
                    gf.ReturnToBoxes(false);
                    break;
                case 11:
                    Check(sel.Current == BoxSelection.State.Closed && note.gameObject.activeInHierarchy && row.activeSelf, "return to boxes: table, note and row back");
                    Check(!boxes.Exists(b => b.IsOpen), "all lids shut again");
                    break;
                case 12:
                    Debug.Log($"FNTEST DONE: {fails} failure(s)\n{log}");
                    EditorApplication.update -= Tick;
                    EditorApplication.ExitPlaymode();
                    break;
            }
        }
        catch (System.Exception e)
        {
            Check(false, $"step {step} threw {e.GetType().Name}: {e.Message}");
            Debug.LogException(e);
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }

        step++;
    }
}
