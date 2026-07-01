using UnityEngine;

namespace AeroFlow.Core
{
    /// <summary>
    /// Marks hidden runtime geometry used only for solver collision/proxy sampling.
    /// </summary>
    public class RuntimeSimulationProxy : MonoBehaviour
    {
        [SerializeField] private string sourcePath;

        public string SourcePath => sourcePath;

        public void Initialize(string path)
        {
            sourcePath = path;
            gameObject.name = "SimulationProxy";
        }
    }
}
