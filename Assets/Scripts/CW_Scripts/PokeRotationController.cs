using UnityEngine;

public class PokeRotationController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private float rotationAmount = 15f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void PokeStar(Transform pokedStar)
    {
        Vector3 fromCenter =
            pokedStar.position - transform.position;

        Vector3 pokeDirection = Camera.main.transform.forward;

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
