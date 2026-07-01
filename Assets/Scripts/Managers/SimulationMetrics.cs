namespace AeroFlow.Managers
{
    [System.Serializable]
    public struct SimulationMetrics
    {
        public string simulationMode;
        public float timestamp;
        public float drag;
        public float lift;
        public float sideForceCoeff;
        public float reynolds;
        public float pressure;
        public float velocity;
        public float referenceArea;
        public float dragForce;
        public float verticalAeroForce;
        public float downforce;
        public float centerOfPressureLongitudinal;
        public float centerOfPressureLateral;
        public float centerOfPressureVertical;
        public float pitchMoment;
        public float yawMoment;
        public float rollMoment;
        public float frontAxleLoad;
        public float rearAxleLoad;
        public float estimatedTopSpeed;
        public float liquidKineticEnergy;
        public float liquidImpactPressure;
        public float liquidSplashHeight;
        public float liquidContainment;
        public float liquidVelocityRms;
        public float liquidStability;
        public bool navierValid;
        public float navierMeanVelocity;
        public float navierMaxVelocity;
        public float navierPressureDrop;
        public float navierWallShear;
        public float navierDivergenceL1;
        public float qualityScore;
        public string qualityRating;
        public string assessment;
        public string flowRegime;
        public string qualityTips;

        // Pipe flow metrics
        public float pipeFrictionFactor;
        public float pipeHeadLoss;
        public float pipeFlowRate;
        public float pipePressureGradient;
        public float pipeReynolds;

        // Rotating machinery metrics
        public float machineryTorque;
        public float machineryPower;
        public float machineryEfficiency;
        public float machineryAngularVelocity;
        public float machineryMeanSwirl;
        public float machineryTipSpeedRatio;
        public float machineryWakeDeficit;
        public string machineryEnergyDirection;
        public string machineryApplicationLabel;

        // ML-based model quality analysis
        public float modelQualityScore;
        public string modelQualityGrade;
        public string modelFeatureBreakdown;
        public string modelImprovements;
        public float modelPredictedCdLow;
        public float modelPredictedCdHigh;
        public float modelSeparationRisk;
        public float modelDownforcePotential;
        public float modelEfficiencyScore;
    }
}
