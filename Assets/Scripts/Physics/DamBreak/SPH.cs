using System.Collections;
using System.Collections.Generic;
//using System.Numerics;
using UnityEngine;

namespace SPHCalculations 
{
    class Particle {
        public float pressure=0f;
        public float density=0f;
        public float mass = 4;
        public float viscosity=0f; 

        public Vector3 currentForce;

        public Vector3 velocity;
        public Vector3 position;
    }
    
     
    public class SPH : MonoBehaviour
    {

        
        List<Particle> particles = new List<Particle>();
        
        float RestingDensity = 1.293f;
        public float ScalingConstant = 0.004f;
        float Radius = 4;
        float Gas_constant = 287;
        float RefrenceTempreture = 20;
        float SutherlandConstant = 110.4f;
        float viscosity_Coff = 0.0000181f;
        public Vector3 gravity = new Vector3(0, -9.81f, 0);


        

        [Header("Enviroment Variables")]
        public int Tempreture = 20;
        
        
        void CalculateDensity()
        {
            float K_Pressure = Tempreture * Gas_constant;
            float ScalingRadius = Radius * ScalingConstant;
            
            for(int i = 0; i < particles.Count; i++) 
            {
                Particle particle = particles[i];

                for (int j = 0; j < particles.Count; j++)
                {
                    Particle neighbor = particles[j];
                    Vector3 distance = particle.position - neighbor.position;
                    float magnitude = distance.sqrMagnitude;

                    if(magnitude * ScalingConstant <= ScalingRadius){
                        float w = HelperMath.SmoothingKernelPoly6(Mathf.Sqrt(magnitude), ScalingRadius);
                        particle.density += neighbor.mass * w;
                    }                    
                }
                particle.pressure = (particle.density - RestingDensity) * K_Pressure;
            }  
        }

        void CalculateForces()
        {

            for (int i = 0; i < particles.Count; i++)
            {
                Particle particle = particles[i];

                Vector3 pressure = Vector3.zero;
                Vector3 visc = Vector3.zero;
                float SPIKY_GRAD = -10.0f / (Mathf.PI * Mathf.Pow(Radius, 5.0f));
                float VISC_LAP = 40.0f / (Mathf.PI * Mathf.Pow(Radius, 5.0f));

                float viscosity_muo = viscosity_Coff * Mathf.Pow((Tempreture / RefrenceTempreture), 1.5f) * ((RefrenceTempreture + SutherlandConstant) / (Tempreture + SutherlandConstant));

                for (int j = 0; j < particles.Count; j++)
                {
                    Particle neighbor = particles[j];

                    if (i == j) continue;
                    
                    float scaledDistance = Vector3.Distance(particle.position, neighbor.position) * ScalingConstant;
                    

                    if (scaledDistance < Radius*2)
                    {
                        float d2w_spiky = HelperMath.SpikySecondDerivative(scaledDistance, Radius);

                        Vector3 pressureGradientDirection = ((particle.position - neighbor.position).normalized);

                        pressure += pressureGradientDirection * neighbor.mass * (particle.pressure + neighbor.pressure) / (2.0f * neighbor.density) * Mathf.Pow(Radius - scaledDistance, 3.0f) * SPIKY_GRAD;

                        visc += viscosity_muo * particle.mass * (neighbor.velocity - particle.velocity) / neighbor.density * VISC_LAP ;
                    }
                }

                particle.currentForce = 100 * (gravity * particle.mass) + pressure + visc * viscosity_Coff;
            }  
        }

        private void LogReynoldsNumber()
        {
            float averageRe = 0;
            float viscosity_muo = viscosity_Coff * Mathf.Pow((Tempreture / RefrenceTempreture), 1.5f) * RefrenceTempreture + (RefrenceTempreture + SutherlandConstant)/(Tempreture + SutherlandConstant);
            for (int i = 0; i < numParticles; i++)
            {
                Particle particle = particles[i];
                float Re = (1.293f * particle.velocity.magnitude * Radius)/viscosity_muo ;
                averageRe += Re;
            }
            averageRe /= numParticles;

            Debug.Log($"Average Reynolds Number: {averageRe}");
        }

        private void UpdatePosition (float timestep) {
        
        for (int i = 0; i < particles.Count; i++)
        {
            Particle particle = particles[i];

            particle.velocity += timestep * particle.currentForce / particle.density;
            particle.position += timestep * particle.velocity;

            //Debug.Log("Particle " + i + " position: " + particle.position);
        }
    }

    [Header("Time")]
    public float timeStep;

    void Start()
    {
        Spawner();
    }
    //[SerializeField] private GameObject sphere ;
    private void Update() {
        CalculateDensity();
        CalculateForces();
        UpdatePosition(timeStep);
        //Instantiate(sphere ,(particles[0].position ) * 5 * Time.deltaTime  ,  Quaternion.identity ) ; 

        if (Input.GetKeyDown(KeyCode.Y))  
        {
            LogReynoldsNumber();
        }

    }

    [Header("Particle Settings")]
    public int numParticles = 100; 
    public int numlines = 5; 
    public Vector3 spawnAreaCenter = Vector3.zero;
    public Vector3 spawnAreaSize = new Vector3(13f, 13f, 13f);

/*
    private Vector3 GetRandomSpawnPosition()
    {
        float x = Random.Range(spawnAreaCenter.x - spawnAreaSize.x / 2f, spawnAreaCenter.x + spawnAreaSize.x / 2f);
        float y = Random.Range(spawnAreaCenter.y - spawnAreaSize.y / 2f, spawnAreaCenter.y + spawnAreaSize.y / 2f);
        float z = Random.Range(spawnAreaCenter.z - spawnAreaSize.z / 2f, spawnAreaCenter.z + spawnAreaSize.z / 2f);
        return new Vector3(x, y, z);
    }
*/
    private void Spawner(){

        for(int j = 0; j < numlines; j++){
            for(int i = 0; i < numParticles/numlines; i++){

            Particle particle = new Particle{

            position = new Vector3 (i,j,0),
            currentForce = Vector3.zero,
            velocity=Vector3.zero,
            };
            Debug.Log("Particle " + i + " position: " + particle.position);
            particles.Add(particle);
            }
        }

    }

    }

}
