using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UINavigator : MonoBehaviour
{
    [Header("Optional default selection")]
    [SerializeField] private Selectable defaultSelectable;

    void Start()
    {
        // Ensure something is selected at start if you want keyboard/gamepad style focus
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
            Select(defaultSelectable ? defaultSelectable.gameObject : FindFirstSelectable());
    }

    // Call these from your custom events or input
    public void MoveUp() => SendMove(MoveDirection.Up, Vector2.up);
    public void MoveDown() => SendMove(MoveDirection.Down, Vector2.down);
    public void MoveLeft() => SendMove(MoveDirection.Left, Vector2.left);
    public void MoveRight() => SendMove(MoveDirection.Right, Vector2.right);

    public void ClickSelected() => SendSubmit();

    // Optionally expose a vector based move if you prefer
    public void Move(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.001f) return;
        dir.Normalize();
        MoveDirection md =
            Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
            ? (dir.x > 0 ? MoveDirection.Right : MoveDirection.Left)
            : (dir.y > 0 ? MoveDirection.Up : MoveDirection.Down);
        SendMove(md, dir);
    }

    // ---------- Internals ----------

    void SendMove(MoveDirection md, Vector2 moveVector)
    {
        EnsureSelection();
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null) return;

        // Let uGUI handle navigation according to the Selectable's Navigation settings
        var axis = new AxisEventData(EventSystem.current)
        {
            moveDir = md,
            moveVector = moveVector
        };
        ExecuteEvents.Execute(selected, axis, ExecuteEvents.moveHandler);

        // If nothing changed, try a manual fallback using Selectable neighbors
        if (selected == EventSystem.current.currentSelectedGameObject)
            ManualNeighborFallback(md, selected);
    }

    void ManualNeighborFallback(MoveDirection md, GameObject fromGO)
    {
        var fromSel = fromGO.GetComponent<Selectable>();
        if (fromSel == null) return;

        Selectable target = null;
        var nav = fromSel.navigation;

        // Prefer explicit neighbors if set, else geometry based
        switch (md)
        {
            case MoveDirection.Up:
                target = nav.selectOnUp ? nav.selectOnUp : fromSel.FindSelectableOnUp();
                break;
            case MoveDirection.Down:
                target = nav.selectOnDown ? nav.selectOnDown : fromSel.FindSelectableOnDown();
                break;
            case MoveDirection.Left:
                target = nav.selectOnLeft ? nav.selectOnLeft : fromSel.FindSelectableOnLeft();
                break;
            case MoveDirection.Right:
                target = nav.selectOnRight ? nav.selectOnRight : fromSel.FindSelectableOnRight();
                break;
        }

        if (target && target.IsInteractable() && target.gameObject.activeInHierarchy)
            Select(target.gameObject);
    }

    void SendSubmit()
    {
        EnsureSelection();
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null) return;

        var data = new BaseEventData(EventSystem.current);

        // Works for Button, Toggle, etc.
        if(!ExecuteEvents.Execute(selected, data, ExecuteEvents.submitHandler))
        {
            // Some controls only react to click
            ExecuteEvents.Execute(selected, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        }
    }

    void EnsureSelection()
    {
        if (EventSystem.current == null) return;
        if (EventSystem.current.currentSelectedGameObject != null) return;

        var first = defaultSelectable ? defaultSelectable.gameObject : FindFirstSelectable();
        if (first != null) Select(first);
    }

    GameObject FindFirstSelectable()
    {
        var any = FindFirstObjectByType<Selectable>();
        return any ? any.gameObject : null;
    }

    void Select(GameObject go)
    {
        if (go == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(go);
        var sel = go.GetComponent<Selectable>();
        if (sel) sel.Select();
    }
}
