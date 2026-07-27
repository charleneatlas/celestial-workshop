using UnityEngine;

public class SimpleMove : MonoBehaviour
{
    public Transform target;
    public Vector3 speed;
    public bool isLocal = true;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (!target)
            target = transform;
    }

    // Update is called once per frame
    void Update()
    {
        if(isLocal)
        {
            target.localPosition += speed * Time.deltaTime;
        }
        else
        {
            target.position += speed * Time.deltaTime;
        }
    }
}
