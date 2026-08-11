using UnityEngine;

public class PokeRotationController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private float rotationAmount = 15f;

    [SerializeField]
    private Transform fingertip;

    private Vector3 previousFingerPosition;
    private Vector3 fingerVelocity;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        previousFingerPosition = fingertip.position;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 currentPosition = fingertip.position;

        if (Time.deltaTime > 0f)
        {
            fingerVelocity =
                (currentPosition - previousFingerPosition) / Time.deltaTime;
        }

        previousFingerPosition = currentPosition;
    }

    public void PokeStar(Transform pokedStar)
    {
        Vector3 fromCenter =
        pokedStar.position - miniConstellationPivot.position;

        Vector3 pokeDirection = fingerVelocity.normalized;

        Vector3 rotationAxis =
            Vector3.Cross(fromCenter, pokeDirection).normalized;

        miniConstellationPivot.Rotate(
            rotationAxis,
            rotationAmount,
            Space.World
        );
        Debug.Log("PokeStar: STAR POKED");
    }

    public void TestPoke()
    {
        Debug.Log("STAR POKED");

        if (miniConstellationPivot != null)
        {
            Vector3 testRot = new Vector3(0f, rotationAmount, 0f);
            miniConstellationPivot.Rotate(testRot);
        }
    }
}
