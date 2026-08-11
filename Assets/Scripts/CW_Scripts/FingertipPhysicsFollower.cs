using UnityEngine;

public class FingertipPhysicsFollower : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The transform of the fingertip.")]
    private Transform fingertipTarget;

    private Rigidbody rb;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void FixedUpdate()
    {
        rb.MovePosition(fingertipTarget.position);
    }
}
