using UnityEngine;
using AeroFlow.Physics;
using AeroFlow.Visualization;
using AeroFlow.Core;
using AeroFlow.UI;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Sim3D.RotatingMachinery;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AeroFlow.Managers
{
    public class SimulationManager : MonoBehaviour
    {
        private enum SimulationMode
        {
            None,
            WindTunnel,
            DamBreak,
            PipeFlow,
            RotatingMachinery
        }

        public static SimulationManager Instance { get; private set; }

        [Header("Scene References")]
        public FlowParticleSystem flowVisualizer;
        public RuntimeModelLoader modelLoader;
        public MainScreenController uiController;
        public Material defaultModelMaterial;

        [Header("Simulation State")]
        [Range(0, 150)] public float airSpeed = 50.0f;
        [Range(0.5f, 1500)] public float fluidDensity = 1.225f;
        [Range(0.00001f, 0.1f)] public float viscosity = 1.81e-5f;
        [Range(0f, 1f)] public float turbulence = 0.1f;
        [Range(-45, 45)] public float angleOfAttack = 0.0f;

        [Header("Live Metrics")]
        public float currentDragCoeff;
        public float currentLiftCoeff;
        public float currentSideForceCoeff;
        public float currentReynolds;
        public float dynamicPressure;
        public float currentReferenceArea;
        public float currentDragForce;
        public float currentVerticalAeroForce;
        public float currentDownforce;
        public float currentCoPLongitudinal;
        public float currentCoPLateral;
        public float currentCoPVertical;
        public float currentPitchMoment;
        public float currentYawMoment;
        public float currentRollMoment;
        public float currentFrontAxleLoad;
        public float currentRearAxleLoad;
        public float estimatedTopSpeed;
        public float qualityScore;
        public string qualityRating = "-";
        public string assessment = "-";
        public string flowRegime = "-";
        public string qualityTips = "-";
        public float navierMeanVelocity;
        public float navierMaxVelocity;
        public float navierPressureDrop;
        public float navierWallShear;
        public float navierDivergenceL1;
        public bool navierDiagnosticsValid;

        // Pipe flow metrics
        public float pipeFrictionFactor;
        public float pipeHeadLoss;
        public float pipeFlowRate;
        public float pipePressureGradient;
        public float pipeReynoldsNumber;

        // Rotating machinery metrics
        public float machineryTorque;
        public float machineryPower;
        public float machineryEfficiency;
        public float machineryAngularVelocity;
        public float machineryMeanSwirl;
        public float machineryTipSpeedRatio;
        public float machineryWakeDeficit;
        public string machineryEnergyDirection = "-";
        public string machineryApplicationLabel = "-";

        private bool isModelLoaded = false;
        private AeroFlow.Physics.FluidLoadIntegrator fluidLoadIntegrator;
        private ExternalAeroLoadEstimator externalAeroEstimator;
        private AeroFlow.Physics.AeroGeometryAnalyzer geoAnalyzer;
        private StreamlineFieldRenderer streamlineRenderer;
        private readonly List<SimulationMetrics> metricsHistory = new List<SimulationMetrics>(4096);
        private SimulationMetrics lastMetrics;
        private Bounds lastKnownModelBounds;
        private bool hasModelBounds;
        private SimulationMode currentMode = SimulationMode.None;
        private float[] liquidSpeedSqCache;
        private Vector3[] liquidPositionCache;
        private Vector3[] liquidVelocityCache;
        private float nextLiquidDiagnosticsTime;
        private float nextMetricsPublishTime;
        private float nextModelAnalysisRetryTime;
        private bool liveResultsComputationEnabled;
        private float liquidKineticEnergy;
        private float liquidImpactPressure;
        private float liquidSplashHeight;
        private float liquidContainment = 1f;
        private float liquidVelocityRms;
        private float liquidStability = 1f;
        private const string WindSmokeName = "WindTunnelSmokeOverlay";
        private const float MetricsPublishInterval = 0.10f;
        private const float ModelAnalysisRetryInterval = 0.75f;
        private WindTunnelSimulation3D cachedWind;
        private Simulation3D cachedDam;
        private PipeFlowSimulation3D cachedPipe;
        private RotatingMachinerySimulation3D cachedMachinery;

        [Header("Layman Modes")]
        public bool laymanLiquidVisualizationMode = true;
        [Header("Wind Tunnel Visuals")]
        public bool enableWindParticleOverlay = false;
        private LiquidInteractionLaymanMode laymanLiquidViz;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            if (GetComponent<VideoCaptureManager>() == null)
                gameObject.AddComponent<VideoCaptureManager>();

            if (GetComponent<AeroFlow.Display.VisualsBootstrapper>() == null)
                gameObject.AddComponent<AeroFlow.Display.VisualsBootstrapper>();
        }

        private void Start()
        {
            if (flowVisualizer == null) flowVisualizer = FindAnyObjectByType<FlowParticleSystem>();
            if (modelLoader == null)   modelLoader   = FindAnyObjectByType<RuntimeModelLoader>();
            if (uiController == null)  uiController  = FindAnyObjectByType<MainScreenController>();

            if (modelLoader != null)
            {
                modelLoader.defaultMaterial = defaultModelMaterial;
                modelLoader.simManager = this;
            }

            fluidLoadIntegrator = FindAnyObjectByType<AeroFlow.Physics.FluidLoadIntegrator>();
            if (fluidLoadIntegrator == null)
                fluidLoadIntegrator = gameObject.AddComponent<AeroFlow.Physics.FluidLoadIntegrator>();

            externalAeroEstimator = FindAnyObjectByType<ExternalAeroLoadEstimator>();
            if (externalAeroEstimator == null)
                externalAeroEstimator = gameObject.AddComponent<ExternalAeroLoadEstimator>();

            geoAnalyzer = FindAnyObjectByType<AeroFlow.Physics.AeroGeometryAnalyzer>();
            if (geoAnalyzer == null)
                geoAnalyzer = gameObject.AddComponent<AeroFlow.Physics.AeroGeometryAnalyzer>();

            streamlineRenderer = FindAnyObjectByType<StreamlineFieldRenderer>();

            if (flowVisualizer != null)
                flowVisualizer.gameObject.SetActive(false);
        }

        public void OnModelLoaded()
        {
            isModelLoaded = true;
            if (uiController != null) uiController.HideLoadPrompt();
            if (liveResultsComputationEnabled)
            {
                RunModelQualityAnalysis();
            }
        }

        private void RunModelQualityAnalysis()
        {
            if (geoAnalyzer == null) return;
            GameObject model = AeroFlow.Core.RuntimeModelLookup.GetLoadedModel();
            if (model == null && modelLoader != null && modelLoader.HasLoadedModel())
                model = modelLoader.GetLoadedModelInstance();
            if (model == null) return;

            Bounds tunnelBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 5f));
            var wind = cachedWind != null ? cachedWind : FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind != null)
                tunnelBounds = wind.GetTunnelBounds();

            Vector3 flowDir = ResolveCurrentFlowDirection();
            geoAnalyzer.AnalyzeModel(model, tunnelBounds, flowDir);
        }

        private void Update()
        {
            if (cachedWind == null) cachedWind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (cachedDam == null)  cachedDam  = FindAnyObjectByType<Simulation3D>();
            if (cachedPipe == null) cachedPipe = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (cachedMachinery == null) cachedMachinery = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            var wind = cachedWind;
            var dam  = cachedDam;
            currentMode = ResolveMode(wind, dam);

            if (!isModelLoaded)
            {
                if ((modelLoader != null && modelLoader.HasLoadedModel()) || RuntimeModelLookup.GetLoadedModel() != null || currentMode != SimulationMode.None)
                    isModelLoaded = true;
            }
            if (!isModelLoaded || currentMode == SimulationMode.None) return;

            bool allowResultsComputation = liveResultsComputationEnabled;

            // Retry model quality analysis if it hasn't produced valid results yet
            if (currentMode == SimulationMode.WindTunnel
                && allowResultsComputation
                && geoAnalyzer != null
                && !geoAnalyzer.TryGetResult(out _)
                && Time.unscaledTime >= nextModelAnalysisRetryTime)
            {
                nextModelAnalysisRetryTime = Time.unscaledTime + ModelAnalysisRetryInterval;
                RunModelQualityAnalysis();
            }

            bool wantWindOverlay = currentMode == SimulationMode.WindTunnel
                                && wind != null
                                && (enableWindParticleOverlay || wind.showInstancedParticles);
            if (flowVisualizer != null)
                flowVisualizer.gameObject.SetActive(wantWindOverlay);

            if (currentMode == SimulationMode.WindTunnel)
            {
                SyncFromWindTunnel(wind);
                SyncVehicleEstimateInputs(wind);
                if (wantWindOverlay) EnsureWindFlowOverlay(wind);
                EnsureStreamlineRenderer(wind);
                if (laymanLiquidViz != null && laymanLiquidViz.gameObject.activeSelf) 
                    laymanLiquidViz.gameObject.SetActive(false);
            }
            else if (currentMode == SimulationMode.DamBreak)
            {
                SyncFromDamBreak(dam);
                EnsureLaymanLiquidVisualizer(dam);
                if (streamlineRenderer != null && streamlineRenderer.enabled)
                    streamlineRenderer.enabled = false;
            }
            else
            {
                if (currentMode == SimulationMode.PipeFlow)
                {
                    SyncFromPipeFlow(cachedPipe);
                }
                else if (currentMode == SimulationMode.RotatingMachinery)
                {
                    SyncFromRotatingMachinery(cachedMachinery);
                }

                if (streamlineRenderer != null && streamlineRenderer.enabled)
                    streamlineRenderer.enabled = false;
                if (laymanLiquidViz != null && laymanLiquidViz.gameObject.activeSelf)
                    laymanLiquidViz.gameObject.SetActive(false);
            }

            EnsureFluidCouplingBindings();
            if (allowResultsComputation)
            {
                RecalculateMetrics(wind, dam, cachedPipe, cachedMachinery);
            }

            if (flowVisualizer != null && wantWindOverlay)
            {
                flowVisualizer.SetFlowParameters(airSpeed, turbulence);
                flowVisualizer.gameObject.SetActive(true);
            }

            if (currentMode == SimulationMode.DamBreak && laymanLiquidViz != null)
                laymanLiquidViz.modeEnabled = laymanLiquidVisualizationMode;

            if (allowResultsComputation)
            {
                lastMetrics = new SimulationMetrics
                {
                    simulationMode     = currentMode.ToString(),
                    timestamp          = Time.time,
                    drag               = currentDragCoeff,
                    lift               = currentLiftCoeff,
                    sideForceCoeff     = currentSideForceCoeff,
                    reynolds           = currentReynolds,
                    pressure           = dynamicPressure,
                    velocity           = airSpeed,
                    referenceArea      = currentReferenceArea,
                    dragForce          = currentDragForce,
                    verticalAeroForce  = currentVerticalAeroForce,
                    downforce          = currentDownforce,
                    centerOfPressureLongitudinal = currentCoPLongitudinal,
                    centerOfPressureLateral      = currentCoPLateral,
                    centerOfPressureVertical     = currentCoPVertical,
                    pitchMoment       = currentPitchMoment,
                    yawMoment         = currentYawMoment,
                    rollMoment        = currentRollMoment,
                    frontAxleLoad      = currentFrontAxleLoad,
                    rearAxleLoad       = currentRearAxleLoad,
                    estimatedTopSpeed  = estimatedTopSpeed,
                    liquidKineticEnergy  = liquidKineticEnergy,
                    liquidImpactPressure = liquidImpactPressure,
                    liquidSplashHeight   = liquidSplashHeight,
                    liquidContainment    = liquidContainment,
                    liquidVelocityRms    = liquidVelocityRms,
                    liquidStability      = liquidStability,
                    navierValid          = navierDiagnosticsValid,
                    navierMeanVelocity   = navierMeanVelocity,
                    navierMaxVelocity    = navierMaxVelocity,
                    navierPressureDrop   = navierPressureDrop,
                    navierWallShear      = navierWallShear,
                    navierDivergenceL1   = navierDivergenceL1,
                    qualityScore         = qualityScore,
                    qualityRating        = qualityRating,
                    assessment           = assessment,
                    flowRegime           = flowRegime,
                    qualityTips          = qualityTips,
                    pipeFrictionFactor   = pipeFrictionFactor,
                    pipeHeadLoss         = pipeHeadLoss,
                    pipeFlowRate         = pipeFlowRate,
                    pipePressureGradient = pipePressureGradient,
                    pipeReynolds         = pipeReynoldsNumber,
                    machineryTorque      = machineryTorque,
                    machineryPower       = machineryPower,
                    machineryEfficiency  = machineryEfficiency,
                    machineryAngularVelocity = machineryAngularVelocity,
                    machineryMeanSwirl   = machineryMeanSwirl,
                    machineryTipSpeedRatio = machineryTipSpeedRatio,
                    machineryWakeDeficit = machineryWakeDeficit,
                    machineryEnergyDirection = machineryEnergyDirection,
                    machineryApplicationLabel = machineryApplicationLabel,
                    modelQualityScore       = 0f,
                    modelQualityGrade       = "-",
                    modelFeatureBreakdown   = "",
                    modelImprovements       = "",
                    modelPredictedCdLow     = 0f,
                    modelPredictedCdHigh    = 0f,
                    modelSeparationRisk     = 0f,
                    modelDownforcePotential = 0f,
                    modelEfficiencyScore    = 0f
                };

                if (geoAnalyzer != null && geoAnalyzer.TryGetResult(out var geoResult))
                {
                    lastMetrics.modelQualityScore       = geoResult.overallScore;
                    lastMetrics.modelQualityGrade       = geoResult.grade ?? "-";
                    lastMetrics.modelFeatureBreakdown   = geoResult.featureBreakdown ?? "";
                    lastMetrics.modelImprovements       = geoResult.improvements ?? "";
                    lastMetrics.modelPredictedCdLow     = geoResult.predictedCdLow;
                    lastMetrics.modelPredictedCdHigh    = geoResult.predictedCdHigh;
                    lastMetrics.modelSeparationRisk     = geoResult.separationRisk;
                    lastMetrics.modelDownforcePotential = geoResult.downforcePotential;
                    lastMetrics.modelEfficiencyScore    = geoResult.efficiencyScore;
                }

                bool shouldPublishMetrics = Time.unscaledTime >= nextMetricsPublishTime;
                if (shouldPublishMetrics)
                {
                    nextMetricsPublishTime = Time.unscaledTime + MetricsPublishInterval;
                    AppendMetricsHistory(lastMetrics);

                    if (uiController != null)
                        uiController.UpdateMetrics(lastMetrics);
                }
            }
        }

        private void SyncFromWindTunnel(WindTunnelSimulation3D wind)
        {
            if (wind == null) return;
            airSpeed     = wind.settings.inletVelocity;
            fluidDensity = wind.settings.airDensity;
            viscosity    = Mathf.Max(1e-6f, wind.settings.dynamicViscosity);
            turbulence   = wind.settings.turbulenceIntensity;
            angleOfAttack = wind.settings.angleOfAttack;
        }

        private void SyncVehicleEstimateInputs(WindTunnelSimulation3D wind)
        {
            if (wind == null || externalAeroEstimator == null) return;
            externalAeroEstimator.referenceAreaOverride = Mathf.Max(0f, wind.settings.vehicle != null ? wind.settings.vehicle.referenceArea : 0f);
        }

        private void SyncFromDamBreak(Simulation3D dam)
        {
            if (dam == null) return;
            fluidDensity  = Mathf.Max(1e-4f, dam.settings.density);
            viscosity     = Mathf.Max(1e-6f, dam.settings.viscosity);
            airSpeed      = Mathf.Max(0f, liquidVelocityRms);
            turbulence    = 0f;
            angleOfAttack = 0f;
        }

        private void SyncFromPipeFlow(PipeFlowSimulation3D pipe)
        {
            if (pipe == null) return;
            fluidDensity  = Mathf.Max(1e-4f, pipe.settings.fluidDensity);
            viscosity     = Mathf.Max(1e-6f, pipe.settings.dynamicViscosity);
            airSpeed      = Mathf.Max(0f, pipe.settings.inletVelocity);
            turbulence    = pipe.settings.turbulenceIntensity;
            angleOfAttack = 0f;
        }

        private void SyncFromRotatingMachinery(RotatingMachinerySimulation3D machinery)
        {
            if (machinery == null) return;
            fluidDensity  = Mathf.Max(1e-4f, machinery.settings.fluidDensity);
            viscosity     = Mathf.Max(1e-6f, machinery.settings.dynamicViscosity);
            airSpeed      = Mathf.Max(0f, machinery.settings.inletVelocity);
            turbulence    = machinery.settings.turbulenceIntensity;
            angleOfAttack = 0f;
        }

        private void ZeroAeroMetrics()
        {
            liquidKineticEnergy = liquidImpactPressure = liquidSplashHeight =
                liquidContainment = liquidVelocityRms = liquidStability = 0f;
            currentDragCoeff = currentLiftCoeff = currentSideForceCoeff = 0f;
            currentReferenceArea = currentDragForce = currentVerticalAeroForce = currentDownforce =
                currentCoPLongitudinal = currentCoPLateral = currentCoPVertical =
                currentPitchMoment = currentYawMoment = currentRollMoment =
                currentFrontAxleLoad = currentRearAxleLoad = estimatedTopSpeed = 0f;
        }

        private void EnsureFluidCouplingBindings()
        {
            if (fluidLoadIntegrator == null) return;
            if (fluidLoadIntegrator.windTunnelSimulation == null)
                fluidLoadIntegrator.windTunnelSimulation = cachedWind;
            if (fluidLoadIntegrator.partRegistry == null && modelLoader != null)
                fluidLoadIntegrator.partRegistry = modelLoader.CurrentPartRegistry;
        }

        private void RecalculateMetrics(WindTunnelSimulation3D wind, Simulation3D dam,
            PipeFlowSimulation3D pipe, RotatingMachinerySimulation3D machinery)
        {
            // Zero out mode-specific fields by default
            pipeFrictionFactor = pipeHeadLoss = pipeFlowRate = pipePressureGradient = pipeReynoldsNumber = 0f;
            machineryTorque = machineryPower = machineryEfficiency = machineryAngularVelocity = machineryMeanSwirl = machineryTipSpeedRatio = machineryWakeDeficit = 0f;
            machineryEnergyDirection = "-";
            machineryApplicationLabel = "-";

            if (currentMode == SimulationMode.WindTunnel)
            {
                liquidKineticEnergy = liquidImpactPressure = liquidSplashHeight =
                    liquidContainment = liquidVelocityRms = liquidStability = 0f;
                currentReferenceArea = currentDragForce = currentVerticalAeroForce = currentDownforce =
                    currentCoPLongitudinal = currentCoPLateral = currentCoPVertical =
                    currentPitchMoment = currentYawMoment = currentRollMoment =
                    currentFrontAxleLoad = currentRearAxleLoad = estimatedTopSpeed =
                    currentSideForceCoeff = 0f;

                dynamicPressure = 0.5f * fluidDensity * Mathf.Pow(airSpeed, 2);
                UpdateNavierDiagnostics(wind);
                TryGetCurrentModelBounds(out var modelBounds, out var modelAvailable);

                float L = 1.0f;
                if (modelAvailable)
                {
                    L = Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z));
                    L = Mathf.Max(L, 0.05f);
                }
                currentReynolds = (fluidDensity * airSpeed * L) / Mathf.Max(viscosity, 0.000001f);

                float rakeRad = wind != null && wind.settings.vehicle != null
                    ? wind.settings.vehicle.rakeAngleDegrees * Mathf.Deg2Rad
                    : 0f;
                float yawRad = angleOfAttack * Mathf.Deg2Rad;
                currentLiftCoeff = Mathf.Clamp(1.35f * Mathf.Sin(rakeRad * 0.9f), -1.2f, 1.2f);

                float baseCd = EstimateBaseDragCoeff();
                baseCd *= Mathf.Lerp(1f, 1.35f, Mathf.Abs(Mathf.Sin(yawRad)));
                currentDragCoeff = baseCd + (currentLiftCoeff * currentLiftCoeff) / Mathf.Max(Mathf.PI * 0.8f, 0.0001f);
                bool hasAeroLoadSample = false;
                ExternalAeroLoadEstimator.AeroLoadSample aeroLoadSample = default;

                if (externalAeroEstimator != null &&
                    externalAeroEstimator.TryGetCoefficients(out var sampledCd, out var sampledCl, out var sampledPressure))
                {
                    currentDragCoeff = Mathf.Lerp(currentDragCoeff, sampledCd, 0.65f);
                    currentLiftCoeff = Mathf.Lerp(currentLiftCoeff, sampledCl, 0.65f);
                    dynamicPressure  = Mathf.Max(dynamicPressure, Mathf.Abs(sampledPressure));
                    hasAeroLoadSample = externalAeroEstimator.TryGetAeroLoadSample(out aeroLoadSample);
                    if (hasAeroLoadSample)
                        currentSideForceCoeff = aeroLoadSample.sideForceCoeff;
                }

                if (navierDiagnosticsValid && dynamicPressure > 1e-3f)
                {
                    float pressurePenalty     = Mathf.Clamp01(Mathf.Abs(navierPressureDrop) / (dynamicPressure * 1.2f));
                    float recirculationPenalty = Mathf.Clamp01(1f - navierMeanVelocity / Mathf.Max(airSpeed, 0.1f));
                    currentDragCoeff *= 1f + pressurePenalty * 0.45f + recirculationPenalty * 0.35f;
                }

                currentDragCoeff = Mathf.Max(0.02f, currentDragCoeff);
                ComputeVehicleAwareEstimates(wind, modelBounds, modelAvailable, hasAeroLoadSample, aeroLoadSample);
                ComputeWindAssessment(modelBounds, modelAvailable);
            }
            else if (currentMode == SimulationMode.DamBreak)
            {
                UpdateNavierDiagnostics(null);
                UpdateLiquidDiagnostics(dam);
                dynamicPressure  = liquidImpactPressure;
                currentDragCoeff = 0f;
                currentLiftCoeff = 0f;
                currentSideForceCoeff = 0f;
                currentReferenceArea = currentDragForce = currentVerticalAeroForce = currentDownforce =
                    currentCoPLongitudinal = currentCoPLateral = currentCoPVertical =
                    currentPitchMoment = currentYawMoment = currentRollMoment =
                    currentFrontAxleLoad = currentRearAxleLoad = estimatedTopSpeed = 0f;
                float L = dam != null ? Mathf.Max(0.05f, dam.transform.localScale.y) : 1f;
                currentReynolds = (fluidDensity * liquidVelocityRms * L) / Mathf.Max(viscosity, 0.000001f);
                ComputeLiquidAssessment();
            }
            else if (currentMode == SimulationMode.PipeFlow)
            {
                UpdateNavierDiagnostics(null);
                ZeroAeroMetrics();

                if (pipe != null && pipe.TryGetDiagnostics(out var pd) && pd.valid)
                {
                    navierDiagnosticsValid = true;
                    navierMeanVelocity = pd.meanVelocity;
                    navierMaxVelocity  = pd.maxVelocity;
                    navierPressureDrop = pd.pressureDrop;
                    navierWallShear    = pd.wallShear;
                    navierDivergenceL1 = pd.divergenceL1;

                    // Derive friction factor from Colebrook approx: f ≈ 64/Re (laminar) or ~0.02 (turbulent)
                    pipeFrictionFactor   = pd.pipeReynolds > 1f ? (pd.pipeReynolds < 2300f ? 64f / pd.pipeReynolds : 0.316f / Mathf.Pow(pd.pipeReynolds, 0.25f)) : 0f;
                    // Head loss = ΔP / (ρ·g)
                    pipeHeadLoss         = pd.pressureDrop / Mathf.Max(fluidDensity * 9.81f, 0.01f);
                    pipeFlowRate         = pd.flowRate;
                    // Pressure gradient ≈ ΔP / domain length (rough estimate)
                    float domainLength   = cachedPipe != null ? cachedPipe.GetDomainBounds().size.magnitude : 1f;
                    pipePressureGradient = pd.pressureDrop / Mathf.Max(domainLength, 0.01f);
                    pipeReynoldsNumber   = pd.pipeReynolds;
                    currentReynolds      = pd.pipeReynolds;
                }

                dynamicPressure = 0.5f * fluidDensity * airSpeed * airSpeed;
                flowRegime = currentReynolds < 2300f ? "Laminar" : currentReynolds < 4000f ? "Transitional" : "Turbulent";
                qualityScore = navierDiagnosticsValid ? Mathf.Clamp01(1f - navierDivergenceL1 * 10f) : 0f;
                qualityRating = qualityScore >= 0.85f ? "Excellent" : qualityScore >= 0.70f ? "Good" : qualityScore >= 0.50f ? "Fair" : "Needs Work";
                assessment = navierDiagnosticsValid ? $"Pipe flow Re={currentReynolds:F0}, f={pipeFrictionFactor:F4}" : "Waiting for solver";
                qualityTips = currentReynolds > 4000f ? "Flow is fully turbulent. Check wall roughness model." : "Flow appears well-resolved.";
            }
            else if (currentMode == SimulationMode.RotatingMachinery)
            {
                UpdateNavierDiagnostics(null);
                ZeroAeroMetrics();

                if (machinery != null && machinery.TryGetDiagnostics(out var md) && md.valid)
                {
                    navierDiagnosticsValid = true;
                    navierMeanVelocity = md.meanVelocity;
                    navierMaxVelocity  = md.maxVelocity;
                    navierPressureDrop = md.pressureDrop;
                    navierWallShear    = md.wallShear;
                    navierDivergenceL1 = md.divergenceL1;

                    machineryTorque          = md.torque;
                    machineryPower           = md.power;
                    machineryEfficiency      = md.efficiency;
                    machineryAngularVelocity = md.angularVelocityRadS;
                    machineryMeanSwirl       = md.meanSwirl;
                    machineryTipSpeedRatio   = md.tipSpeedRatio;
                    machineryWakeDeficit     = md.wakeVelocityDeficit;
                    machineryEnergyDirection = md.energyDirection ?? "-";
                    machineryApplicationLabel = md.applicationLabel ?? "-";
                    currentReynolds          = md.machineReynolds;
                }

                dynamicPressure = 0.5f * fluidDensity * airSpeed * airSpeed;
                flowRegime = currentReynolds < 1e5f ? "Laminar Rotor" : currentReynolds < 1e6f ? "Transitional Rotor" : "Turbulent Rotor";
                qualityScore = navierDiagnosticsValid ? Mathf.Clamp01(machineryEfficiency * 1.2f) : 0f;
                qualityRating = qualityScore >= 0.85f ? "Excellent" : qualityScore >= 0.70f ? "Good" : qualityScore >= 0.50f ? "Fair" : "Needs Work";
                assessment = navierDiagnosticsValid ? $"Torque={machineryTorque:F2} Nm, P={machineryPower:F1} W, η={machineryEfficiency:P0}" : "Waiting for solver";
                qualityTips = machineryEfficiency < 0.3f ? "Low efficiency. Check blade geometry and RPM." : "Rotor performance looks reasonable.";
            }
        }

        private void UpdateNavierDiagnostics(WindTunnelSimulation3D wind)
        {
            navierDiagnosticsValid = false;
            navierMeanVelocity = navierMaxVelocity = navierPressureDrop = navierWallShear = navierDivergenceL1 = 0f;
            if (wind == null || wind.navierStokesSolver == null) return;
            if (wind.navierStokesSolver.TryGetDiagnostics(out var d) && d.valid)
            {
                navierDiagnosticsValid = true;
                navierMeanVelocity     = d.meanVelocity;
                navierMaxVelocity      = d.maxVelocity;
                navierPressureDrop     = d.pressureDrop;
                navierWallShear        = d.wallShear;
                navierDivergenceL1     = d.divergenceL1;
            }
        }

        private void AppendMetricsHistory(SimulationMetrics metrics)
        {
            metricsHistory.Add(metrics);
            const int maxRows = 20000;
            if (metricsHistory.Count > maxRows)
                metricsHistory.RemoveRange(0, metricsHistory.Count - maxRows);
        }

        public SimulationMetrics GetLatestMetrics() => lastMetrics;

        public bool IsLiveResultsComputationEnabled() => liveResultsComputationEnabled;

        public void SetLiveResultsComputationEnabled(bool enabled)
        {
            if (liveResultsComputationEnabled == enabled)
            {
                return;
            }

            liveResultsComputationEnabled = enabled;
            nextMetricsPublishTime = 0f;

            if (!enabled)
            {
                return;
            }

            if (cachedWind == null) cachedWind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (cachedDam == null) cachedDam = FindAnyObjectByType<Simulation3D>();
            if (cachedPipe == null) cachedPipe = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (cachedMachinery == null) cachedMachinery = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            currentMode = ResolveMode(cachedWind, cachedDam);

            if (currentMode == SimulationMode.WindTunnel)
            {
                RunModelQualityAnalysis();
            }
        }

        public void SetSimulationTimeScale(float timeScale)
        {
            if (cachedDam != null) cachedDam.settings.timeScale = timeScale;
            if (cachedWind != null) cachedWind.settings.timeScale = timeScale;
            if (cachedPipe != null) cachedPipe.settings.timeScale = timeScale;
            if (cachedMachinery != null) cachedMachinery.settings.timeScale = timeScale;
        }

        public void ExportResultsToCsv(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var rows = metricsHistory.Count > 0 ? metricsHistory : new List<SimulationMetrics> { lastMetrics };
            var sb = new StringBuilder(1024 + rows.Count * 96);
            sb.AppendLine("# AeroFlow CFD Results Export");
            sb.AppendLine($"# Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Data Points: {rows.Count}");
            sb.AppendLine($"# Mode: {(rows.Count > 0 ? rows[0].simulationMode : "N/A")}");
            sb.AppendLine("#");
            sb.AppendLine("time_s,mode,velocity_mps,dynamic_pressure_pa,cd,cl,cs,reynolds,reference_area_m2,drag_force_n,vertical_aero_force_n,downforce_n,cop_longitudinal_m,cop_lateral_m,cop_vertical_m,pitch_moment_nm,yaw_moment_nm,roll_moment_nm,front_axle_load_n,rear_axle_load_n,estimated_top_speed_mps,liquid_kinetic_energy_j,liquid_impact_pressure_pa,liquid_splash_height_m,liquid_containment,liquid_velocity_rms_mps,liquid_stability,flow_regime,assessment,quality_rating,quality_score,navier_valid,navier_mean_velocity_mps,navier_max_velocity_mps,navier_pressure_drop_pa,navier_wall_shear_pa,navier_divergence_l1,pipe_friction_factor,pipe_head_loss_m,pipe_flow_rate_m3s,pipe_pressure_gradient_pa_m,pipe_reynolds,machinery_torque_nm,machinery_power_w,machinery_efficiency,machinery_angular_velocity_rads,machinery_mean_swirl_mps,machinery_tip_speed_ratio,machinery_wake_deficit,machinery_energy_direction,machinery_application");
            for (int i = 0; i < rows.Count; i++)
            {
                var m = rows[i];
                sb.Append(m.timestamp.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(EscapeCsv(m.simulationMode)).Append(',');
                sb.Append(m.velocity.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pressure.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.drag.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.lift.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.sideForceCoeff.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.reynolds.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.referenceArea.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.dragForce.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.verticalAeroForce.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.downforce.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.centerOfPressureLongitudinal.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.centerOfPressureLateral.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.centerOfPressureVertical.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pitchMoment.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.yawMoment.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.rollMoment.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.frontAxleLoad.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.rearAxleLoad.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.estimatedTopSpeed.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidKineticEnergy.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidImpactPressure.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidSplashHeight.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidContainment.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidVelocityRms.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.liquidStability.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(EscapeCsv(m.flowRegime)).Append(',');
                sb.Append(EscapeCsv(m.assessment)).Append(',');
                sb.Append(EscapeCsv(m.qualityRating)).Append(',');
                sb.Append(m.qualityScore.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.navierValid ? "1" : "0").Append(',');
                sb.Append(m.navierMeanVelocity.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.navierMaxVelocity.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.navierPressureDrop.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.navierWallShear.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.navierDivergenceL1.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pipeFrictionFactor.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pipeHeadLoss.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pipeFlowRate.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pipePressureGradient.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.pipeReynolds.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryTorque.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryPower.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryEfficiency.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryAngularVelocity.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryMeanSwirl.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryTipSpeedRatio.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(m.machineryWakeDeficit.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(EscapeCsv(m.machineryEnergyDirection)).Append(',');
                sb.Append(EscapeCsv(m.machineryApplicationLabel));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[Results Export] CSV saved: {path}");
        }

        public void ExportLatestSnapshotToJson(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(lastMetrics, true));
            Debug.Log($"[Results Export] JSON saved: {path}");
        }

        public void ExportHtmlReport(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var m = lastMetrics;
            var sb = new StringBuilder(4096);
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            sb.AppendLine("<title>AeroFlow CFD Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}");
            sb.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;background:#0a141e;color:#e2eef7;padding:32px 48px}");
            sb.AppendLine("h1{font-size:28px;color:#6cf5ff;margin-bottom:4px}");
            sb.AppendLine("h2{font-size:18px;color:#6490ff;margin:24px 0 10px;border-bottom:1px solid #2a3e52;padding-bottom:4px}");
            sb.AppendLine(".subtitle{font-size:13px;color:#84a0b5;margin-bottom:24px}");
            sb.AppendLine("table{width:100%;border-collapse:collapse;margin-bottom:16px}");
            sb.AppendLine("td{padding:5px 12px;font-size:13px;border-bottom:1px solid #1a2e3e}");
            sb.AppendLine("td:first-child{color:#84a0b5;width:45%}");
            sb.AppendLine("td:last-child{color:#c8ebff;font-weight:600;text-align:right}");
            sb.AppendLine(".badge{display:inline-block;padding:3px 10px;border-radius:4px;font-size:12px;font-weight:600}");
            sb.AppendLine(".badge-good{background:rgba(80,255,160,0.15);color:#50ffa0}");
            sb.AppendLine(".badge-warn{background:rgba(255,200,80,0.15);color:#ffc850}");
            sb.AppendLine(".footer{margin-top:32px;font-size:11px;color:#556070;border-top:1px solid #1a2e3e;padding-top:12px}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>AeroFlow CFD Report</h1>");
            sb.AppendLine($"<p class=\"subtitle\">Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} | Mode: {m.simulationMode ?? "N/A"}</p>");

            sb.AppendLine("<h2>Aerodynamic Coefficients</h2><table>");
            AppendRow(sb, "Drag Coefficient (Cd)", $"{m.drag:F4}");
            AppendRow(sb, "Lift Coefficient (Cl)", $"{m.lift:F4}");
            AppendRow(sb, "Side Force Coefficient (Cs)", $"{m.sideForceCoeff:F4}");
            AppendRow(sb, "Reynolds Number", $"{m.reynolds:E2}");
            AppendRow(sb, "Dynamic Pressure", $"{m.pressure:F1} Pa");
            sb.AppendLine("</table>");

            sb.AppendLine("<h2>Forces &amp; Moments</h2><table>");
            AppendRow(sb, "Frontal Area", $"{m.referenceArea:F3} m&sup2;");
            AppendRow(sb, "Drag Force", $"{m.dragForce:F1} N");
            AppendRow(sb, "Lift / Vertical Force", $"{m.verticalAeroForce:F1} N");
            AppendRow(sb, "Downforce", $"{m.downforce:F1} N");
            AppendRow(sb, "CoP Longitudinal Offset", $"{m.centerOfPressureLongitudinal:F3} m");
            AppendRow(sb, "Pitch Moment", $"{m.pitchMoment:F1} Nm");
            AppendRow(sb, "Yaw Moment", $"{m.yawMoment:F1} Nm");
            AppendRow(sb, "Roll Moment", $"{m.rollMoment:F1} Nm");
            AppendRow(sb, "Front Axle Load", $"{m.frontAxleLoad:F1} N");
            AppendRow(sb, "Rear Axle Load", $"{m.rearAxleLoad:F1} N");
            if (m.estimatedTopSpeed > 0.01f)
                AppendRow(sb, "Estimated Top Speed", $"{m.estimatedTopSpeed * 3.6f:F1} km/h");
            sb.AppendLine("</table>");

            if (m.navierValid)
            {
                sb.AppendLine("<h2>Solver Diagnostics</h2><table>");
                AppendRow(sb, "Mean Velocity", $"{m.navierMeanVelocity:F2} m/s");
                AppendRow(sb, "Peak Velocity", $"{m.navierMaxVelocity:F2} m/s");
                AppendRow(sb, "Pressure Drop", $"{m.navierPressureDrop:F2} Pa");
                AppendRow(sb, "Wall Shear", $"{m.navierWallShear:F5} Pa");
                AppendRow(sb, "Divergence L1", $"{m.navierDivergenceL1:E2}");
                sb.AppendLine("</table>");
            }

            if (!string.IsNullOrEmpty(m.flowRegime) && m.flowRegime != "-")
            {
                sb.AppendLine("<h2>Flow Assessment</h2><table>");
                AppendRow(sb, "Flow Regime", m.flowRegime);
                AppendRow(sb, "Assessment", m.assessment ?? "-");
                AppendRow(sb, "Quality Rating", m.qualityRating ?? "-");
                AppendRow(sb, "Quality Score", $"{m.qualityScore:F1}");
                if (!string.IsNullOrEmpty(m.qualityTips) && m.qualityTips != "-")
                    AppendRow(sb, "Suggestions", m.qualityTips);
                sb.AppendLine("</table>");
            }

            if (!string.IsNullOrEmpty(m.machineryApplicationLabel) || !string.IsNullOrEmpty(m.machineryEnergyDirection))
            {
                sb.AppendLine("<h2>Rotatory Mode</h2><table>");
                AppendRow(sb, "Application", m.machineryApplicationLabel ?? "-");
                AppendRow(sb, "Energy Direction", m.machineryEnergyDirection ?? "-");
                AppendRow(sb, "Tip-Speed Ratio", $"{m.machineryTipSpeedRatio:F2}");
                AppendRow(sb, "Wake Deficit", $"{m.machineryWakeDeficit:P1}");
                AppendRow(sb, "Torque", $"{m.machineryTorque:F2} Nm");
                AppendRow(sb, "Power", $"{m.machineryPower:F1} W");
                sb.AppendLine("</table>");
            }

            sb.AppendLine($"<div class=\"footer\">AeroFlow Engineering Suite 2026 &mdash; Computational Fluid Dynamics &mdash; {metricsHistory.Count} data points recorded</div>");
            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[Results Export] HTML report saved: {path}");
        }

        private static void AppendRow(StringBuilder sb, string label, string value)
        {
            sb.Append("<tr><td>").Append(label).Append("</td><td>").Append(value).Append("</td></tr>\n");
        }

        public void CaptureViewportScreenshot(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            Camera cam = Camera.main;
            if (cam == null)
            {
                var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i].enabled && cameras[i].cameraType == CameraType.Game)
                    {
                        cam = cameras[i];
                        break;
                    }
                }
            }

            if (cam == null)
            {
                Debug.LogWarning("[Screenshot] No game camera found.");
                return;
            }

            int width = 1920;
            int height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 2;

            RenderTexture prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prevActive;

            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);

            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);

            Debug.Log($"[Screenshot] Viewport saved: {path}");
        }

        private static string EscapeCsv(string input)
        {
            if (string.IsNullOrEmpty(input)) return "\"\"";
            return $"\"{input.Replace("\"", "\"\"")}\"";
        }

        private void ComputeVehicleAwareEstimates(
            WindTunnelSimulation3D wind,
            Bounds modelBounds,
            bool modelAvailable,
            bool hasAeroLoadSample,
            ExternalAeroLoadEstimator.AeroLoadSample aeroLoadSample)
        {
            if (wind == null)
            {
                return;
            }

            WindTunnelVehicleProperties vehicle = wind.settings.vehicle ?? new WindTunnelVehicleProperties();
            bool hasVehicleFrame = wind.TryGetVehicleReferenceFrame(out WindTunnelVehicleReferenceFrame vehicleFrame) && vehicleFrame.valid;
            float referenceArea = ResolveVehicleReferenceArea(wind, modelBounds, modelAvailable);
            if (hasAeroLoadSample && aeroLoadSample.referenceArea > 1e-4f)
            {
                referenceArea = aeroLoadSample.referenceArea;
            }
            if (referenceArea <= 1e-4f)
            {
                // Fallback: use a minimum estimate from tunnel cross-section so we
                // still produce meaningful force numbers while the model bounds
                // are being resolved.
                Bounds tBounds = wind.GetTunnelBounds();
                Vector3 flowDir = ResolveCurrentFlowDirection();
                float tunnelCross = ComputeProjectedArea(tBounds.size, flowDir);
                referenceArea = Mathf.Max(tunnelCross * 0.05f, 0.01f);
            }

            currentReferenceArea = referenceArea;
            float qReference = 0.5f * fluidDensity * airSpeed * airSpeed;
            currentDragForce = qReference * currentDragCoeff * referenceArea;
            currentVerticalAeroForce = qReference * currentLiftCoeff * referenceArea;
            if (hasVehicleFrame && hasAeroLoadSample)
            {
                currentVerticalAeroForce = Vector3.Dot(aeroLoadSample.totalForce, vehicleFrame.upAxis);
                currentCoPLongitudinal = Vector3.Dot(aeroLoadSample.centerOfPressure - vehicleFrame.centerOfGravity, vehicleFrame.flowAxis);
                currentCoPLateral = Vector3.Dot(aeroLoadSample.centerOfPressure - vehicleFrame.centerOfGravity, vehicleFrame.sideAxis);
                currentCoPVertical = Vector3.Dot(aeroLoadSample.centerOfPressure - vehicleFrame.centerOfGravity, vehicleFrame.upAxis);
                currentPitchMoment = Vector3.Dot(aeroLoadSample.totalMoment, vehicleFrame.sideAxis);
                currentYawMoment = Vector3.Dot(aeroLoadSample.totalMoment, vehicleFrame.upAxis);
                currentRollMoment = Vector3.Dot(aeroLoadSample.totalMoment, vehicleFrame.flowAxis);
            }
            else if (hasVehicleFrame)
            {
                float cpFromFront = vehicleFrame.wheelbase * (1f - vehicle.frontAeroBalance);
                Vector3 fallbackCoP = vehicleFrame.frontAxleCenter + vehicleFrame.flowAxis * cpFromFront;
                currentCoPLongitudinal = Vector3.Dot(fallbackCoP - vehicleFrame.centerOfGravity, vehicleFrame.flowAxis);
                currentCoPLateral = Vector3.Dot(fallbackCoP - vehicleFrame.centerOfGravity, vehicleFrame.sideAxis);
                currentCoPVertical = Vector3.Dot(fallbackCoP - vehicleFrame.centerOfGravity, vehicleFrame.upAxis);
                currentPitchMoment = -currentVerticalAeroForce * currentCoPLongitudinal;
                currentYawMoment = 0f;
                currentRollMoment = 0f;
            }
            currentDownforce = Mathf.Max(0f, -currentVerticalAeroForce);

            float totalWeight = Mathf.Max(vehicle.massKg, 1f) * UnityEngine.Physics.gravity.magnitude;
            float aeroLoad = -currentVerticalAeroForce;
            float frontStaticLoad = totalWeight * vehicle.frontWeightDistribution;
            float rearStaticLoad = totalWeight - frontStaticLoad;
            float frontAeroLoad = aeroLoad * vehicle.frontAeroBalance;
            float rearAeroLoad = aeroLoad - frontAeroLoad;

            if (hasVehicleFrame && hasAeroLoadSample && Mathf.Abs(aeroLoad) > 1e-3f)
            {
                ComputeAxleLoadsFromCenterOfPressure(
                    vehicleFrame,
                    aeroLoadSample.centerOfPressure,
                    aeroLoad,
                    out frontAeroLoad,
                    out rearAeroLoad);
            }

            currentFrontAxleLoad = Mathf.Max(0f, frontStaticLoad + frontAeroLoad);
            currentRearAxleLoad = Mathf.Max(0f, rearStaticLoad + rearAeroLoad);

            float powerAtWheels = Mathf.Max(0f, vehicle.enginePowerKw) * 1000f * vehicle.drivetrainEfficiency;
            estimatedTopSpeed = EstimateTopSpeed(
                fluidDensity,
                currentDragCoeff,
                referenceArea,
                vehicle.massKg,
                vehicle.rollingResistanceCoeff,
                powerAtWheels);
        }

        private static void ComputeAxleLoadsFromCenterOfPressure(
            WindTunnelVehicleReferenceFrame vehicleFrame,
            Vector3 centerOfPressure,
            float verticalAeroLoad,
            out float frontAeroLoad,
            out float rearAeroLoad)
        {
            if (vehicleFrame.wheelbase <= 1e-4f)
            {
                frontAeroLoad = verticalAeroLoad * 0.5f;
                rearAeroLoad = verticalAeroLoad - frontAeroLoad;
                return;
            }

            float cpFromFront = Mathf.Clamp(
                Vector3.Dot(centerOfPressure - vehicleFrame.frontAxleCenter, vehicleFrame.flowAxis),
                0f,
                vehicleFrame.wheelbase);

            frontAeroLoad = verticalAeroLoad * ((vehicleFrame.wheelbase - cpFromFront) / vehicleFrame.wheelbase);
            rearAeroLoad = verticalAeroLoad - frontAeroLoad;
        }

        private float ResolveVehicleReferenceArea(WindTunnelSimulation3D wind, Bounds modelBounds, bool modelAvailable)
        {
            if (wind != null && wind.settings.vehicle != null && wind.settings.vehicle.referenceArea > 0f)
            {
                return wind.settings.vehicle.referenceArea;
            }

            if (!modelAvailable)
            {
                return 0f;
            }

            Vector3 flowDir = ResolveCurrentFlowDirection();
            return Mathf.Max(ComputeProjectedArea(modelBounds.size, flowDir), 1e-4f);
        }

        private static float EstimateTopSpeed(
            float rho,
            float dragCoeff,
            float referenceArea,
            float massKg,
            float rollingResistanceCoeff,
            float powerAtWheels)
        {
            if (powerAtWheels <= 1f || dragCoeff <= 0f || referenceArea <= 0f)
            {
                return 0f;
            }

            float rollingForce = Mathf.Max(0f, massKg) * UnityEngine.Physics.gravity.magnitude * Mathf.Max(0f, rollingResistanceCoeff);
            float low = 0f;
            float high = 120f;

            for (int i = 0; i < 48; i++)
            {
                float mid = 0.5f * (low + high);
                float dragForce = 0.5f * rho * dragCoeff * referenceArea * mid * mid;
                float powerRequired = (dragForce + rollingForce) * mid;
                if (powerRequired > powerAtWheels)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            return low;
        }

        private float EstimateBaseDragCoeff()
        {
            if (!TryGetCurrentModelBounds(out var b, out var hasBounds) || !hasBounds) return 0.08f;
            float length     = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float thickness  = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
            float slenderness = thickness > 0.0001f ? length / thickness : 1f;
            Vector3 flowDir = ResolveCurrentFlowDirection();
            float frontalArea = Mathf.Max(ComputeProjectedArea(b.size, flowDir), 1e-4f);
            float refArea     = Mathf.Max(b.size.x * b.size.y, b.size.x * b.size.z);
            float bluffness   = Mathf.Clamp01(frontalArea / Mathf.Max(refArea, 1e-4f));
            float baseCd      = Mathf.Lerp(0.18f, 1.0f, bluffness);
            if (slenderness >= 4.0f)      baseCd *= 0.65f;
            else if (slenderness >= 2.5f) baseCd *= 0.8f;
            else if (slenderness < 1.5f)  baseCd *= 1.15f;
            if (TryGetWindTunnelBounds(out var tunnelBounds))
            {
                float tunnelArea   = Mathf.Max(ComputeProjectedArea(tunnelBounds.size, flowDir), 1e-4f);
                float blockageRatio = Mathf.Clamp01(frontalArea / tunnelArea);
                baseCd *= Mathf.Lerp(1.0f, 1.45f, blockageRatio);
            }
            return Mathf.Clamp(baseCd, 0.08f, 1.8f);
        }

        private void ComputeWindAssessment(Bounds modelBounds, bool modelAvailable)
        {
            if (currentReynolds < 2.0e5f)      flowRegime = "Laminar";
            else if (currentReynolds < 1.0e6f) flowRegime = "Transitional";
            else                               flowRegime = "Turbulent";

            float targetCd = 0.4f, blockageRatio = 0f;
            if (modelAvailable)
            {
                float length     = Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z));
                float thickness  = Mathf.Min(modelBounds.size.x, Mathf.Min(modelBounds.size.y, modelBounds.size.z));
                float slenderness = thickness > 0.0001f ? length / thickness : 1f;
                targetCd = slenderness >= 2.5f ? 0.34f : 0.78f;
                if (TryGetWindTunnelBounds(out var tunnelBounds))
                {
                    Vector3 flowDir = ResolveCurrentFlowDirection();
                    float frontalArea = Mathf.Max(ComputeProjectedArea(modelBounds.size, flowDir), 1e-4f);
                    float tunnelArea  = Mathf.Max(ComputeProjectedArea(tunnelBounds.size, flowDir), 1e-4f);
                    blockageRatio = Mathf.Clamp01(frontalArea / tunnelArea);
                    targetCd *= Mathf.Lerp(1f, 1.2f, blockageRatio);
                }
            }

            qualityScore = Mathf.Clamp01((targetCd / Mathf.Max(currentDragCoeff, 0.01f)) * (navierDiagnosticsValid ? 1f : 0.75f));
            qualityRating = qualityScore >= 0.85f ? "Excellent" : qualityScore >= 0.70f ? "Good" : qualityScore >= 0.50f ? "Fair" : "Needs Work";

            if      (navierDiagnosticsValid && navierDivergenceL1 > 0.03f)          { assessment = "Flow field not fully stable; solver divergence is elevated."; qualityTips = "Increase iterations/frame or lower turbulence intensity."; }
            else if (navierDiagnosticsValid && navierPressureDrop > dynamicPressure * 0.45f) { assessment = "Large pressure losses detected."; qualityTips = "Reduce frontal blockage and smooth transitions."; }
            else if (blockageRatio > 0.35f)                                          { assessment = "Model blocks a high fraction of tunnel cross-section."; qualityTips = "Scale down model or enlarge tunnel bounds."; }
            else if (currentDragCoeff > targetCd * 1.2f)                            { assessment = "High drag relative to expected shape."; qualityTips = "Reduce frontal area, smooth sharp edges."; }
            else if (Mathf.Abs(angleOfAttack) > 12f)                                { assessment = "High yaw or crosswind angle increasing drag."; qualityTips = "Reduce crosswind yaw or add stabilizing aero surfaces."; }
            else                                                                     { assessment = "Balanced drag for current setup."; qualityTips = "Consider small tweaks for cleaner flow."; }
        }

        private void ComputeLiquidAssessment()
        {
            flowRegime = liquidVelocityRms < 0.4f ? "Settling" : liquidVelocityRms < 1.5f ? "Sloshing" : "Impact-Dominated";
            qualityScore = Mathf.Clamp01(0.45f * Mathf.Clamp01(liquidContainment / 0.96f)
                                        + 0.35f * Mathf.Clamp01(liquidStability)
                                        + 0.20f * (1f - Mathf.Clamp01(liquidImpactPressure / 3000f)));
            qualityRating = qualityScore >= 0.85f ? "Excellent" : qualityScore >= 0.70f ? "Good" : qualityScore >= 0.50f ? "Fair" : "Needs Work";
            if      (liquidContainment < 0.90f)    { assessment = "Fluid loss outside bounds is high."; qualityTips = "Increase boundary height or reduce timestep."; }
            else if (liquidImpactPressure > 3500f) { assessment = "High impact pressure spikes."; qualityTips = "Raise damping or reduce inlet impulse."; }
            else if (liquidStability < 0.6f)       { assessment = "Flow remains numerically noisy."; qualityTips = "Increase iterations/frame and viscosity."; }
            else                                   { assessment = "Liquid behavior is stable."; qualityTips = "Fine-tune particle count for higher detail."; }
        }

        private bool TryGetCurrentModelBounds(out Bounds bounds, out bool valid)
        {
            bounds = lastKnownModelBounds;
            valid  = hasModelBounds;
            if (!RuntimeModelLookup.TryGetRenderableBounds(out bounds)) return valid;
            lastKnownModelBounds = bounds;
            hasModelBounds = valid = true;
            return true;
        }

        private bool TryGetWindTunnelBounds(out Bounds bounds)
        {
            if (cachedWind != null) { bounds = cachedWind.GetTunnelBounds(); return true; }
            bounds = default; return false;
        }

        private Vector3 ResolveCurrentFlowDirection()
        {
            if (cachedWind == null) return Vector3.right;
            Vector3 direction = cachedWind.ResolveWindDirection();
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.right;
        }

        private static float ComputeProjectedArea(Vector3 size, Vector3 direction)
        {
            Vector3 absDir = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return size.y * size.z * absDir.x
                 + size.x * size.z * absDir.y
                 + size.x * size.y * absDir.z;
        }

        private SimulationMode ResolveMode(WindTunnelSimulation3D wind, Simulation3D dam)
        {
            if (cachedPipe != null && cachedPipe.isActiveAndEnabled) return SimulationMode.PipeFlow;
            if (cachedMachinery != null && cachedMachinery.isActiveAndEnabled) return SimulationMode.RotatingMachinery;
            if (wind != null && wind.isActiveAndEnabled) return SimulationMode.WindTunnel;
            if (dam  != null && dam.isActiveAndEnabled)  return SimulationMode.DamBreak;
            return SimulationMode.None;
        }

        private void UpdateLiquidDiagnostics(Simulation3D dam)
        {
            if (dam == null || dam.positionBuffer == null || dam.velocityBuffer == null || dam.positionBuffer.count <= 0)
            { liquidKineticEnergy = liquidImpactPressure = liquidSplashHeight = liquidContainment = liquidVelocityRms = liquidStability = 0f; return; }
            if (Time.time < nextLiquidDiagnosticsTime) return;
            nextLiquidDiagnosticsTime = Time.time + 0.25f;
            int count = dam.positionBuffer.count;
            if (liquidPositionCache == null || liquidPositionCache.Length != count)
            { liquidPositionCache = new Vector3[count]; liquidVelocityCache = new Vector3[count]; liquidSpeedSqCache = new float[count]; }
            dam.positionBuffer.GetData(liquidPositionCache);
            dam.velocityBuffer.GetData(liquidVelocityCache);
            Bounds b = new Bounds(dam.transform.position, dam.transform.localScale);
            float floorY = b.min.y, nearFloorY = floorY + b.size.y * 0.15f;
            float sumSpeedSq = 0f, maxY = float.MinValue, nearFloorSpeedSq = 0f;
            int inBoundsCount = 0, nearFloorCount = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = liquidPositionCache[i]; Vector3 v = liquidVelocityCache[i];
                float sq = v.sqrMagnitude; liquidSpeedSqCache[i] = sq; sumSpeedSq += sq;
                if (p.y > maxY) maxY = p.y;
                if (b.Contains(p)) inBoundsCount++;
                if (p.y <= nearFloorY) { nearFloorCount++; nearFloorSpeedSq += sq; }
            }
            float rms = Mathf.Sqrt(sumSpeedSq / Mathf.Max(count, 1));
            float floorRms = Mathf.Sqrt(nearFloorSpeedSq / Mathf.Max(nearFloorCount, 1));
            float massPerParticle = EstimateDamParticleMass(dam);
            liquidVelocityRms    = rms;
            liquidContainment    = inBoundsCount / Mathf.Max((float)count, 1f);
            liquidSplashHeight   = Mathf.Max(0f, maxY - floorY);
            liquidKineticEnergy  = 0.5f * massPerParticle * sumSpeedSq;
            liquidImpactPressure = 0.5f * fluidDensity * floorRms * floorRms;
            liquidStability      = 1f - Mathf.Clamp01(0.6f * (rms / Mathf.Max(dam.settings.maxVelocity, 0.1f)) + 0.4f * (1f - liquidContainment));
        }

        private float EstimateDamParticleMass(Simulation3D dam)
        {
            if (dam == null || dam.spawner == null) return 1e-4f;
            int axis = Mathf.Max(2, dam.spawner.numParticlesPerAxis);
            float spacing = dam.spawner.size / (axis - 1f);
            float volume  = Mathf.Max(spacing * spacing * spacing, 1e-8f);
            return Mathf.Max(dam.settings.density * volume, 1e-8f);
        }

        private void EnsureWindFlowOverlay(WindTunnelSimulation3D wind)
        {
            if (wind == null) return;
            if (flowVisualizer == null)
            {
                Transform existing = wind.transform.Find(WindSmokeName);
                GameObject go = existing != null ? existing.gameObject : new GameObject(WindSmokeName);
                go.transform.SetParent(wind.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                if (go.GetComponent<ParticleSystem>() == null)
                {
                    go.AddComponent<ParticleSystem>();
                }
                flowVisualizer = go.GetComponent<FlowParticleSystem>() ?? go.AddComponent<FlowParticleSystem>();
            }
            if (flowVisualizer != null)
            {
                bool effectsMode = string.Equals(
                    wind.settings.visualizationMode,
                    WindTunnelSimulation3D.VisualizationEffects,
                    System.StringComparison.OrdinalIgnoreCase);
                flowVisualizer.windTunnel = wind;
                flowVisualizer.particleSize = effectsMode ? 0.11f : 0.085f;
                flowVisualizer.emitterCoverage = effectsMode ? 0.74f : 0.86f;
                flowVisualizer.EnsureConfigured();
                var t = flowVisualizer.transform;
                if (t.parent != wind.transform) t.SetParent(wind.transform, false);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
                if (!flowVisualizer.gameObject.activeSelf) flowVisualizer.gameObject.SetActive(true);
            }
        }

        private void EnsureLaymanLiquidVisualizer(Simulation3D dam)
        {
            if (dam == null) return;
            if (laymanLiquidViz == null) laymanLiquidViz = FindAnyObjectByType<LiquidInteractionLaymanMode>();
            if (laymanLiquidViz == null)
            {
                var go = new GameObject("LaymanLiquidInteractionMode");
                laymanLiquidViz = go.AddComponent<LiquidInteractionLaymanMode>();
            }
            laymanLiquidViz.modeEnabled = laymanLiquidVisualizationMode;
            if (!laymanLiquidViz.gameObject.activeSelf) laymanLiquidViz.gameObject.SetActive(true);
        }

        private void EnsureStreamlineRenderer(WindTunnelSimulation3D wind)
        {
            if (wind == null) return;
            if (streamlineRenderer == null)
            {
                streamlineRenderer = wind.GetComponent<StreamlineFieldRenderer>();
                if (streamlineRenderer == null)
                    streamlineRenderer = wind.gameObject.AddComponent<StreamlineFieldRenderer>();
            }
            streamlineRenderer.windTunnel = wind;
            streamlineRenderer.navier     = wind.navierStokesSolver;
        }

        // Public API
        public void SetAirSpeed(float v)
        {
            airSpeed = v;
            if (cachedWind != null) cachedWind.settings.inletVelocity = v;
        }

        public void SetDensity(float d)
        {
            fluidDensity = d;
            if (cachedWind != null) cachedWind.settings.airDensity = d;
            if (cachedDam != null) cachedDam.settings.density = d;
        }

        public void SetViscosity(float v)
        {
            viscosity = v;
            if (cachedWind != null) cachedWind.settings.dynamicViscosity = v;
            if (cachedDam != null) cachedDam.settings.viscosity = v;
        }

        public void SetTurbulence(float t)
        {
            turbulence = Mathf.Clamp(t, 0f, 100f);
            if (cachedWind != null) cachedWind.settings.turbulenceIntensity = turbulence;
        }

        public void SetAngle(float a)
        {
            angleOfAttack = a;
            if (cachedWind != null) cachedWind.settings.angleOfAttack = a;
        }
        public void SetLaymanLiquidVisualizationMode(bool enabled)
        {
            laymanLiquidVisualizationMode = enabled;
            if (laymanLiquidViz != null) laymanLiquidViz.modeEnabled = enabled;
        }
    }
}
