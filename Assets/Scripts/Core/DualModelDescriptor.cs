using System;
using System.Collections.Generic;

namespace AeroFlow.Core
{
    [Serializable]
    public class ModelPartBinding
    {
        public string partId;
        public string visualNodePath;
        public string simulationPartId;
    }

    [Serializable]
    public class DualModelDescriptor
    {
        public string sourceObjectId;
        public string visualModelPath;
        public string simulationModelPath;
        public List<ModelPartBinding> partBindings = new List<ModelPartBinding>();

        public bool HasSimulationModel => !string.IsNullOrWhiteSpace(simulationModelPath);
    }
}
