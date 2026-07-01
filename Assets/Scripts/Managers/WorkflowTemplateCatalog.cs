using System.Collections.Generic;

namespace AeroFlow.Managers
{
    public sealed class WorkflowTemplateDefinition
    {
        public string id;
        public string title;
        public string summary;
        public string outputs;
        public string recommendedScene;
    }

    public static class WorkflowTemplateCatalog
    {
        public const string ExternalAero = "external_aero";
        public const string InternalFlow = "internal_flow";
        public const string RotatoryMode = "rotatory_mode";
        public const string RotatingMachinery = RotatoryMode;
        public const string FreeSurface = "free_surface";
        public const string FsiLite = "fsi_lite";
        public const string PlaybackValidation = "playback_validation";

        private static readonly Dictionary<string, WorkflowTemplateDefinition> templates = new Dictionary<string, WorkflowTemplateDefinition>
        {
            {
                ExternalAero,
                new WorkflowTemplateDefinition
                {
                    id = ExternalAero,
                    title = "External Aero",
                    summary = "Car/bike/airfoil in a wind tunnel domain (Navier-Stokes grid mode).",
                    outputs = "Cd, Cl, pressure map, streamlines",
                    recommendedScene = "WindTunnelSample"
                }
            },
            {
                InternalFlow,
                new WorkflowTemplateDefinition
                {
                    id = InternalFlow,
                    title = "Internal Flow",
                    summary = "Pipe/duct/manifold with inlet-outlet BCs.",
                    outputs = "Pressure drop, velocity profile, recirculation",
                    recommendedScene = "WindTunnelSample"
                }
            },
            {
                RotatingMachinery,
                new WorkflowTemplateDefinition
                {
                    id = RotatingMachinery,
                    title = "Rotatory Mode",
                    summary = "Windmill, fan, propeller, and turbine simulations with rotating geometry and energy exchange.",
                    outputs = "Torque, power, wake spiral, pressure gradients, tip-speed ratio",
                    recommendedScene = "WindTunnelSample"
                }
            },
            {
                FreeSurface,
                new WorkflowTemplateDefinition
                {
                    id = FreeSurface,
                    title = "Free-Surface Flow",
                    summary = "Dam-break, water impact, sloshing (SPH particle mode).",
                    outputs = "Splash height, containment, impact pressure",
                    recommendedScene = "Test C (3D)"
                }
            },
            {
                FsiLite,
                new WorkflowTemplateDefinition
                {
                    id = FsiLite,
                    title = "Conjugate / FSI Lite",
                    summary = "Fluid force drives movable parts (flap/valve/blade) via joints.",
                    outputs = "Part forces, torques, motion response",
                    recommendedScene = "WindTunnelSample"
                }
            },
            {
                PlaybackValidation,
                new WorkflowTemplateDefinition
                {
                    id = PlaybackValidation,
                    title = "Playback / Validation",
                    summary = "Import offline CFD fields and compare with realtime solver.",
                    outputs = "Trend agreement, model validation score",
                    recommendedScene = "WindTunnelSample"
                }
            }
        };

        public static IReadOnlyDictionary<string, WorkflowTemplateDefinition> Templates => templates;

        public static WorkflowTemplateDefinition GetOrDefault(string templateId)
        {
            if (!string.IsNullOrEmpty(templateId) && templates.TryGetValue(templateId, out var def))
            {
                return def;
            }
            return templates[ExternalAero];
        }
    }
}
