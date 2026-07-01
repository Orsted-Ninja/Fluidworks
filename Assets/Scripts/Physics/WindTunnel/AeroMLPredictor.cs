using UnityEngine;

namespace AeroFlow.Physics
{
    /// <summary>
    /// Lightweight MLP (Multi-Layer Perceptron) neural network for aerodynamic prediction.
    /// Uses physics-informed pre-trained weights — no external dependencies.
    /// Architecture: 8 inputs → 16 hidden (ReLU) → 8 hidden (ReLU) → 4 outputs (Sigmoid).
    ///
    /// Input features (from AeroGeometryAnalyzer):
    ///   [0] aspectRatio, [1] slendernessRatio, [2] frontalBlockageRatio, [3] volumeEfficiency,
    ///   [4] surfaceRoughness, [5] symmetryScore, [6] undersideClearance, [7] rearTaperRatio
    ///
    /// Outputs:
    ///   [0] predictedCd, [1] separationRisk, [2] downforcePotential, [3] efficiencyScore
    /// </summary>
    public static class AeroMLPredictor
    {
        public struct PredictionResult
        {
            public float predictedCd;
            public float separationRisk;
            public float downforcePotential;
            public float efficiencyScore;
        }

        private const int InputSize = 8;
        private const int Hidden1Size = 16;
        private const int Hidden2Size = 8;
        private const int OutputSize = 4;

        // Physics-informed weights encode known aerodynamic relationships:
        // - Higher frontal area → higher Cd
        // - Better slenderness → lower drag
        // - Surface roughness → promotes separation
        // - Rear taper → reduces wake drag
        // - Symmetry → reduces side force / improves stability

        // Layer 1: Input(8) → Hidden1(16), weights[16][8]
        private static readonly float[,] W1 = {
            // Each row = one hidden neuron's connections to all 8 inputs
            // aspectR  slender  blockage volEff   rough    symmetry clearance rearTaper
            {  0.25f,   0.30f,  -0.45f,   0.15f,  -0.35f,   0.10f,   0.05f,   0.28f  }, // drag-low detector
            { -0.20f,  -0.25f,   0.50f,  -0.10f,   0.40f,  -0.05f,   0.02f,  -0.30f  }, // drag-high detector
            {  0.10f,   0.15f,  -0.20f,   0.25f,  -0.50f,   0.08f,   0.03f,   0.35f  }, // smooth flow
            { -0.05f,  -0.10f,   0.35f,  -0.15f,   0.55f,  -0.12f,  -0.02f,  -0.25f  }, // turbulent flow
            {  0.02f,   0.05f,  -0.10f,   0.30f,  -0.20f,   0.40f,   0.10f,   0.15f  }, // stability
            { -0.08f,  -0.03f,   0.15f,  -0.25f,   0.30f,  -0.35f,  -0.05f,  -0.10f  }, // instability
            {  0.15f,   0.20f,  -0.30f,   0.10f,  -0.15f,   0.05f,   0.45f,   0.20f  }, // ground effect
            { -0.12f,   0.08f,   0.05f,   0.20f,  -0.25f,   0.15f,  -0.40f,   0.10f  }, // clearance effect
            {  0.30f,   0.35f,  -0.15f,   0.05f,  -0.10f,   0.20f,   0.08f,   0.40f  }, // efficiency high
            { -0.28f,  -0.32f,   0.20f,  -0.08f,   0.12f,  -0.18f,  -0.06f,  -0.38f  }, // efficiency low
            {  0.05f,   0.12f,  -0.25f,   0.35f,  -0.45f,   0.10f,   0.15f,   0.22f  }, // low separation
            { -0.03f,  -0.08f,   0.30f,  -0.30f,   0.48f,  -0.08f,  -0.10f,  -0.18f  }, // high separation
            {  0.18f,   0.10f,  -0.05f,   0.08f,  -0.12f,   0.25f,   0.30f,   0.08f  }, // downforce potential
            { -0.15f,   0.05f,   0.10f,   0.12f,  -0.08f,   0.08f,  -0.25f,   0.15f  }, // lift tendency
            {  0.22f,   0.28f,  -0.35f,   0.18f,  -0.30f,   0.15f,   0.12f,   0.32f  }, // overall quality
            { -0.18f,  -0.22f,   0.28f,  -0.12f,   0.25f,  -0.10f,  -0.08f,  -0.28f  }  // overall penalty
        };
        private static readonly float[] B1 = {
             0.10f, -0.05f,  0.08f, -0.03f,  0.12f, -0.08f,  0.05f,  0.02f,
             0.15f, -0.10f,  0.06f, -0.04f,  0.08f,  0.03f,  0.12f, -0.07f
        };

        // Layer 2: Hidden1(16) → Hidden2(8), weights[8][16]
        private static readonly float[,] W2 = {
            {  0.25f, -0.30f,  0.20f, -0.15f,  0.10f, -0.05f,  0.08f, -0.12f,  0.22f, -0.20f,  0.18f, -0.15f,  0.05f, -0.08f,  0.28f, -0.25f },
            { -0.20f,  0.35f, -0.15f,  0.25f, -0.08f,  0.12f, -0.05f,  0.10f, -0.18f,  0.22f, -0.12f,  0.20f, -0.03f,  0.06f, -0.22f,  0.28f },
            {  0.15f, -0.10f,  0.25f, -0.20f,  0.30f, -0.25f,  0.12f, -0.08f,  0.15f, -0.12f,  0.28f, -0.22f,  0.10f, -0.05f,  0.20f, -0.18f },
            { -0.12f,  0.18f, -0.22f,  0.28f, -0.25f,  0.30f, -0.10f,  0.15f, -0.12f,  0.15f, -0.25f,  0.28f, -0.08f,  0.10f, -0.18f,  0.22f },
            {  0.10f, -0.08f,  0.12f, -0.10f,  0.15f, -0.12f,  0.35f, -0.30f,  0.08f, -0.05f,  0.10f, -0.08f,  0.28f, -0.22f,  0.12f, -0.10f },
            { -0.08f,  0.12f, -0.10f,  0.15f, -0.12f,  0.18f, -0.30f,  0.35f, -0.06f,  0.08f, -0.08f,  0.12f, -0.25f,  0.28f, -0.10f,  0.12f },
            {  0.20f, -0.15f,  0.18f, -0.12f,  0.08f, -0.05f,  0.10f, -0.08f,  0.30f, -0.28f,  0.15f, -0.10f,  0.12f, -0.05f,  0.25f, -0.22f },
            { -0.18f,  0.20f, -0.15f,  0.18f, -0.10f,  0.08f, -0.08f,  0.12f, -0.28f,  0.30f, -0.12f,  0.15f, -0.10f,  0.08f, -0.22f,  0.25f }
        };
        private static readonly float[] B2 = {
             0.08f, -0.05f,  0.06f, -0.04f,  0.10f, -0.06f,  0.07f, -0.03f
        };

        // Layer 3: Hidden2(8) → Output(4), weights[4][8]
        private static readonly float[,] W3 = {
            // Cd prediction: favors neurons that detect drag characteristics
            { -0.35f,  0.40f, -0.20f,  0.25f, -0.10f,  0.15f, -0.30f,  0.35f },
            // Separation risk: favors turbulence/roughness neurons
            { -0.15f,  0.20f, -0.30f,  0.35f, -0.10f,  0.12f, -0.18f,  0.22f },
            // Downforce potential: favors ground effect and clearance neurons
            {  0.20f, -0.15f,  0.10f, -0.08f,  0.35f, -0.30f,  0.25f, -0.20f },
            // Efficiency score: overall quality aggregation
            {  0.30f, -0.25f,  0.28f, -0.22f,  0.15f, -0.10f,  0.32f, -0.28f }
        };
        private static readonly float[] B3 = { 0.35f, 0.30f, 0.25f, 0.45f };

        // Feature normalization parameters (mean, scale)
        private static readonly float[] FeatureMean  = { 2.5f, 2.0f, 0.15f, 0.50f, 0.30f, 0.80f, 0.10f, 0.40f };
        private static readonly float[] FeatureScale = { 2.0f, 1.5f, 0.20f, 0.30f, 0.25f, 0.15f, 0.10f, 0.30f };

        public static PredictionResult Predict(float[] features)
        {
            if (features == null || features.Length != InputSize)
            {
                return new PredictionResult
                {
                    predictedCd = 0.4f,
                    separationRisk = 0.5f,
                    downforcePotential = 0.3f,
                    efficiencyScore = 0.5f
                };
            }

            // Normalize inputs
            float[] normalized = new float[InputSize];
            for (int i = 0; i < InputSize; i++)
            {
                normalized[i] = (features[i] - FeatureMean[i]) / Mathf.Max(FeatureScale[i], 1e-5f);
            }

            // Forward pass: Layer 1
            float[] h1 = new float[Hidden1Size];
            for (int j = 0; j < Hidden1Size; j++)
            {
                float sum = B1[j];
                for (int i = 0; i < InputSize; i++)
                    sum += W1[j, i] * normalized[i];
                h1[j] = ReLU(sum);
            }

            // Forward pass: Layer 2
            float[] h2 = new float[Hidden2Size];
            for (int j = 0; j < Hidden2Size; j++)
            {
                float sum = B2[j];
                for (int i = 0; i < Hidden1Size; i++)
                    sum += W2[j, i] * h1[i];
                h2[j] = ReLU(sum);
            }

            // Forward pass: Output layer
            float[] output = new float[OutputSize];
            for (int j = 0; j < OutputSize; j++)
            {
                float sum = B3[j];
                for (int i = 0; i < Hidden2Size; i++)
                    sum += W3[j, i] * h2[i];
                output[j] = Sigmoid(sum);
            }

            return new PredictionResult
            {
                predictedCd = Mathf.Lerp(0.15f, 0.90f, output[0]),
                separationRisk = output[1],
                downforcePotential = output[2],
                efficiencyScore = output[3]
            };
        }

        /// <summary>
        /// Compute feature importance using central-difference gradient approximation.
        /// Aggregates sensitivity across all 4 outputs for a holistic importance measure.
        /// Returns normalized importance values (sum to 1.0) for each input feature.
        /// </summary>
        public static float[] ComputeFeatureImportance(float[] features)
        {
            if (features == null || features.Length != InputSize)
                return new float[InputSize];

            float[] importance = new float[InputSize];
            float epsilon = 0.05f;

            for (int i = 0; i < InputSize; i++)
            {
                float[] perturbedPlus = (float[])features.Clone();
                float[] perturbedMinus = (float[])features.Clone();
                perturbedPlus[i] += epsilon;
                perturbedMinus[i] -= epsilon;
                PredictionResult pPlus = Predict(perturbedPlus);
                PredictionResult pMinus = Predict(perturbedMinus);

                // Aggregate sensitivity across all outputs using central difference
                float sensitivity =
                    Mathf.Abs(pPlus.predictedCd - pMinus.predictedCd) +
                    Mathf.Abs(pPlus.separationRisk - pMinus.separationRisk) +
                    Mathf.Abs(pPlus.downforcePotential - pMinus.downforcePotential) +
                    Mathf.Abs(pPlus.efficiencyScore - pMinus.efficiencyScore);

                importance[i] = sensitivity / (2f * epsilon);
            }

            // Normalize to sum to 1
            float total = 0f;
            for (int i = 0; i < InputSize; i++)
                total += importance[i];

            if (total > 1e-6f)
            {
                for (int i = 0; i < InputSize; i++)
                    importance[i] /= total;
            }

            return importance;
        }

        private static float ReLU(float x) => x > 0f ? x : 0f;

        private static float Sigmoid(float x)
        {
            if (x > 15f) return 1f;
            if (x < -15f) return 0f;
            return 1f / (1f + Mathf.Exp(-x));
        }
    }
}
