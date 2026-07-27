using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

namespace Oculus.Interaction.Samples
{
    public class CustomPoseUseSample : MonoBehaviour
    {
        [SerializeField, Interface(typeof(IHmd))]
        private UnityEngine.Object _hmd;
        private IHmd Hmd { get; set; }

        [SerializeField]
        private ActiveStateSelector[] _poses;

        [SerializeField]
        private Material[] _onSelectIcons;

        [SerializeField]
        private GameObject _poseActiveVisualPrefab;

        private GameObject[] _poseActiveVisuals;

        public Transform spawnPoint;

        protected virtual void Awake()
        {
            Hmd = _hmd as IHmd;
        }

        protected virtual void Start()
        {
            this.AssertField(Hmd, nameof(Hmd));
            this.AssertField(_poseActiveVisualPrefab, nameof(_poseActiveVisualPrefab));

            _poseActiveVisuals = new GameObject[_poses.Length];
            for (int i = 0; i < _poses.Length; i++)
            {
                _poseActiveVisuals[i] = Instantiate(_poseActiveVisualPrefab);
                _poseActiveVisuals[i].GetComponentInChildren<TextMeshPro>().text = _poses[i].name;
                _poseActiveVisuals[i].GetComponentInChildren<ParticleSystemRenderer>().material = _onSelectIcons[i];
                _poseActiveVisuals[i].SetActive(false);

                int poseNumber = i;
                _poses[i].WhenSelected += () => ShowVisuals(poseNumber);
                _poses[i].WhenUnselected += () => HideVisuals(poseNumber);
            }
        }
        private void ShowVisuals(int poseNumber)
        {
            if (!Hmd.TryGetRootPose(out Pose hmdPose))
            {
                return;
            }

            Vector3 spawnSpot = spawnPoint.position;
            _poseActiveVisuals[poseNumber].transform.position = spawnSpot;
            _poseActiveVisuals[poseNumber].transform.rotation = spawnPoint.rotation;

            _poseActiveVisuals[poseNumber].gameObject.SetActive(true);
        }

        private void HideVisuals(int poseNumber)
        {
            _poseActiveVisuals[poseNumber].gameObject.SetActive(false);
        }
    }
}
