using UnityEngine;

public sealed class ConstellationImpactAudio : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip impactClip;
    [SerializeField] private Collider catchCollider;

    private bool hasPlayed;

    private void OnCollisionEnter(Collision collision)
    {
        if (hasPlayed)
        {
            return;
        }

        if (collision.collider != catchCollider)
        {
            return;
        }

        hasPlayed = true;
        audioSource.PlayOneShot(impactClip);
    }

    public void ResetSound()
    {
        hasPlayed = false;
    }
}