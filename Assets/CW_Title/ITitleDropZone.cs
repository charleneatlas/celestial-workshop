using UnityEngine;

/// <summary>Somewhere a title letter can be released to start practising it.</summary>
public interface ITitleDropZone
{
    bool Contains(Vector3 worldPoint);
}
