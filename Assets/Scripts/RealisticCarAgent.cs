using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;
using System.IO; //logging

public class RealisticCarAgent : Agent
{
    [Header("Mode")]
    public bool trainingMode = false;
    public bool evaluationMode = false;
    int evalEpisodeCount = 0;
    public int maxEvalEpisodes = 100;

    [Header("Wheel Colliders")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;
    
    [Header("Wheel Meshes")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;
    
    [Header("Car Settings")]
    public float maxMotorTorque = 1500f;
    public float maxSteeringAngle = 30f;
    public float brakeTorque = 3000f;
    
    [Header("Sensors")]
    public Transform[] raycasts;
    public float rayDistance = 15f;
    
    [Header("Dynamic Checkpoints")]
    public bool useDynamicCheckpoints = true;
    public RuntimeRoadBuilder roadBuilder;
    
    [Header("Fallback - Manual Checkpoints")]
    public Transform[] manualCheckpoints;
    
    [Header("Wall Detection")]
    public LayerMask wallMask;

    private List<Transform> checkpoints = new List<Transform>();
    private int currentCheckpoint = 0;
    
    private bool reachedGoal = false;
    private float stopBrake = 0f;
    private bool allowDriving = false;


    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float timeSinceCheckpoint = 0f;
    
    float lastDistanceToCheckpoint;

    //logging
    static StreamWriter episodeWriter;
    static StreamWriter trajectoryWriter;

    static bool loggerInitialized = false;

    int episodeIndex = 0;
    int collisionCount = 0;
    float episodeTimer = 0f;
    bool episodeLogged = false;
    //logging ends

    protected override void Awake()
    {
        base.Awake();

        rb = GetComponent<Rigidbody>();

        //episode writer
        if (!loggerInitialized)
        {
            string path = Application.persistentDataPath + "/Logs/";
            Directory.CreateDirectory(path);

            string epPath = path + "episodes.csv";
            string trajPath = path + "trajectory.csv";

            // delete old logs
            if (File.Exists(epPath)) File.Delete(epPath);
            if (File.Exists(trajPath)) File.Delete(trajPath);

            episodeWriter = new StreamWriter(
                new FileStream(epPath, FileMode.Create, FileAccess.Write, FileShare.None)
            );

            trajectoryWriter = new StreamWriter(
                new FileStream(trajPath, FileMode.Create, FileAccess.Write, FileShare.None)
            );

            episodeWriter.WriteLine("episode,success,time,collisions");
            trajectoryWriter.WriteLine("episode,x,z");

            loggerInitialized = true;
        }
        //ends

        startPosition = transform.position;
        startRotation = transform.rotation;

        if (roadBuilder == null && useDynamicCheckpoints)
        {
            roadBuilder = FindObjectOfType<RuntimeRoadBuilder>();
        }
    }

    void Start()
    {
        
        // JITTER FIX: Set interpolation and center of mass
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.centerOfMass = new Vector3(0, -0.3f, 0); // Lower center for stability
        
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        
        // Set wheel collider centers
        SetWheelColliderCenter(frontLeftWheel);
        SetWheelColliderCenter(frontRightWheel);
        SetWheelColliderCenter(rearLeftWheel);
        SetWheelColliderCenter(rearRightWheel);
    }
    
    void SetWheelColliderCenter(WheelCollider wheel)
    {
        Vector3 center = wheel.transform.localPosition;
        wheel.center = Vector3.zero;
    }

    public override void OnEpisodeBegin()
    {   
        if (episodeIndex == 1)
            evalEpisodeCount = 0;
        
        //logging
        episodeLogged = false;

        collisionCount = 0;
        episodeTimer = 0f;
        episodeIndex++;

        allowDriving = trainingMode || evaluationMode;
        
        if(trainingMode || evaluationMode)
        {
            if (!roadBuilder.useFixedTrack)
            {
                roadBuilder.ClearAllRoads();
                roadBuilder.GenerateRandomTrack();
                roadBuilder.GenerateCheckpoints();
            }

            Vector2Int spawn = roadBuilder.GetStartCell();

            transform.position = roadBuilder.transform.position + new Vector3(
                spawn.x * roadBuilder.cellSize + roadBuilder.cellSize / 2f,
                3f,
                spawn.y * roadBuilder.cellSize + roadBuilder.cellSize / 2f
            );

            transform.rotation = Quaternion.identity;
            transform.rotation *= Quaternion.Euler(0,Random.Range(-5f, 5f),0);
        }
        else
        {
            // Reset car
            transform.position = startPosition; 
            transform.rotation = startRotation; 
        }

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        currentCheckpoint = 0;
        timeSinceCheckpoint = 0f;
        
        // Reset wheels
        ResetWheel(frontLeftWheel);
        ResetWheel(frontRightWheel);
        ResetWheel(rearLeftWheel);
        ResetWheel(rearRightWheel);
        
        // Load checkpoints dynamically
        LoadCheckpoints();

        // Orientation Alignment
        Vector3 dir = roadBuilder.GetSpawnForwardFromPath();
        
        if(dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        if (checkpoints.Count > 0)
            lastDistanceToCheckpoint =
                Vector3.Distance(transform.position,checkpoints[currentCheckpoint].position);
        
        // runtime stop reset
        reachedGoal = false;
        stopBrake = 0f;
    }

    void LoadCheckpoints()
    {
        checkpoints.Clear();

        //roadBuilder must exist
        if (useDynamicCheckpoints)
        {
            if (roadBuilder == null)
            {
                Debug.LogWarning("RoadBuilder reference is null.");
                return;
            }

            List<RoadCell> path = roadBuilder.GetCheckpointPath();

            //path must exist
            if (path == null || path.Count == 0)
            {
               Debug.LogWarning("No checkpoint path found in RoadBuilder.");
               return;
            }

            foreach (RoadCell cell in path)
            {
                if (cell == null) continue;
                if (cell.checkpoint == null) continue;

               checkpoints.Add(cell.checkpoint.transform);
            }

            Debug.Log($"Loaded {checkpoints.Count} dynamic checkpoints");
        }
        else
        {
            // Fallback to manual checkpoints
            if (manualCheckpoints != null && manualCheckpoints.Length > 0)
            {
                checkpoints.AddRange(manualCheckpoints);
                Debug.Log($"Using {checkpoints.Count} manual checkpoints");
            }
        }
    }

    // Call this when spawning car in play mode (not training)
    public void InitializeForPlayMode()
    {
        LoadCheckpoints();
    }
    
    void ResetWheel(WheelCollider wheel)
    {
        wheel.motorTorque = 0;
        wheel.brakeTorque = 0;
        wheel.steerAngle = 0;
    }
    
    // JITTER FIX: Move wheel visual updates to FixedUpdate
    void FixedUpdate()
    {
        episodeTimer += Time.fixedDeltaTime;
        // log position
        if (trajectoryWriter != null)
        {
            trajectoryWriter.WriteLine($"{episodeIndex},{transform.position.x},{transform.position.z}");
        }

        UpdateWheelMesh(frontLeftWheel, frontLeftMesh);
        UpdateWheelMesh(frontRightWheel, frontRightMesh);
        UpdateWheelMesh(rearLeftWheel, rearLeftMesh);
        UpdateWheelMesh(rearRightWheel, rearRightMesh);

        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);

        float roll = localAngVel.z;

        rb.AddRelativeTorque(new Vector3(0f, 0f, -roll * 500f));

        if(Vector3.Dot(transform.up, Vector3.up) < 0.6f)
        {
            AddReward(-1f);
            LogEpisode(false);
            EndEpisode();
        }

    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Car speed and velocity
        sensor.AddObservation(rb.velocity.magnitude / 30f);
        sensor.AddObservation(transform.InverseTransformDirection(rb.velocity));
        
        // Car rotation
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.right);
        
        // Angular velocity
        sensor.AddObservation(rb.angularVelocity.y / 10f);
        
        // Direction to next checkpoint
        if (checkpoints.Count > 0 && currentCheckpoint < checkpoints.Count)
        {
            Vector3 dirToCheckpoint = (checkpoints[currentCheckpoint].position - transform.position).normalized;
            sensor.AddObservation(transform.InverseTransformDirection(dirToCheckpoint));
            
            float distanceToCheckpoint = Vector3.Distance(transform.position, checkpoints[currentCheckpoint].position);
            sensor.AddObservation(distanceToCheckpoint / 50f);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }
        
        // Raycast distances (obstacles/walls)
        if (raycasts != null)
        {
            foreach (Transform ray in raycasts)
            {
                if (ray != null)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(ray.position, ray.forward, out hit, rayDistance, wallMask, QueryTriggerInteraction.Ignore))
                    {
                        sensor.AddObservation(hit.distance / rayDistance);
                    }
                    else
                    {
                        sensor.AddObservation(1f);
                    }
                }
                else
                {
                    sensor.AddObservation(1f);
                }
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if(!allowDriving)
        {
            rearLeftWheel.motorTorque = 0;
            rearRightWheel.motorTorque = 0;

            rearLeftWheel.brakeTorque = brakeTorque;
            rearRightWheel.brakeTorque = brakeTorque;

            return;
        }
        // Apply breaking after reaching end cell
        if(reachedGoal)
        {
            float speed = rb.velocity.magnitude;
            
            rearLeftWheel.motorTorque = 0;
            rearRightWheel.motorTorque = 0;

            stopBrake = Mathf.Lerp(stopBrake, brakeTorque, Time.fixedDeltaTime * 2f);

            rearLeftWheel.brakeTorque = stopBrake;
            rearRightWheel.brakeTorque = stopBrake;

            frontLeftWheel.steerAngle = 0;
            frontRightWheel.steerAngle = 0;
            rb.velocity *= 0.98f;

            return;
        }
        
        // Get actions
        float throttle = actions.ContinuousActions[0];  // -1 to 1
        float steering = actions.ContinuousActions[1];  // -1 to 1
        
        // Apply steering to front wheels
        float steerAngle = steering * maxSteeringAngle;
        frontLeftWheel.steerAngle = steerAngle;
        frontRightWheel.steerAngle = steerAngle;
        
        // Apply motor torque or braking
        if (throttle > 0)
        {
            // Accelerate
            float speed = rb.velocity.magnitude;
            float speedFactor = Mathf.Clamp01(1 - (speed / 20f));

            rearLeftWheel.motorTorque = throttle * maxMotorTorque * speedFactor;
            rearRightWheel.motorTorque = throttle * maxMotorTorque * speedFactor;
            rearLeftWheel.brakeTorque = 0;
            rearRightWheel.brakeTorque = 0;
        }
        else
        {
            // Brake
            rearLeftWheel.motorTorque = 0;
            rearRightWheel.motorTorque = 0;
            rearLeftWheel.brakeTorque = Mathf.Abs(throttle) * brakeTorque;
            rearRightWheel.brakeTorque = Mathf.Abs(throttle) * brakeTorque;
        }
        
        // Small time penalty
        AddReward(-0.001f);
        
        // Reward for moving towards checkpoint
        if (checkpoints.Count > 0 && currentCheckpoint < checkpoints.Count)
        {
            Vector3 toCheckpoint = (checkpoints[currentCheckpoint].position - transform.position).normalized;
            float alignment = Vector3.Dot(rb.velocity.normalized, toCheckpoint);
            AddReward(alignment * 0.01f);
        }
        
        // Rewards
        // New penalty and reward, allowing turns and alignments
        if (checkpoints.Count > 0 && currentCheckpoint < checkpoints.Count)
        {
            float currentDistance =Vector3.Distance(transform.position,checkpoints[currentCheckpoint].position);

            float progress = lastDistanceToCheckpoint - currentDistance;

            if (progress < 0.01f)
                timeSinceCheckpoint += Time.fixedDeltaTime;
            else
                timeSinceCheckpoint = 0f;

            lastDistanceToCheckpoint = currentDistance;

            if (timeSinceCheckpoint > 10f)
            {
                AddReward(-1f);
                LogEpisode(false);
                EndEpisode();
            }
        }

    }

    void LateFixedUpdate()
    {
        Collider[] hits = Physics.OverlapBox(
            transform.position,
            new Vector3(1.1f, 0.5f, 1.7f),
            transform.rotation,
            wallMask
        );

        if (hits.Length > 0)
        {
            collisionCount++;
            AddReward(-2f);
            LogEpisode(false);
            EndEpisode();
        }
    }

    
    void UpdateWheelMesh(WheelCollider collider, Transform mesh)
    {
        if (mesh == null) return;
        
        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);
        
        mesh.position = position;
        mesh.rotation = rotation;
    }

    public void StartDrivingRuntime()
    {
        allowDriving = true;
    }


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Vertical");   // Throttle/Brake
        continuousActionsOut[1] = Input.GetAxis("Horizontal"); // Steering
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Checkpoint"))
        {
            // Check if it's the correct checkpoint
            if (checkpoints.Count > 0 && currentCheckpoint < checkpoints.Count)
            {
                if (other.transform == checkpoints[currentCheckpoint])
                {
                    AddReward(1.0f);
                    currentCheckpoint++;
                    timeSinceCheckpoint = 0f;
                    
                    Debug.Log($"Checkpoint {currentCheckpoint}/{checkpoints.Count} reached!");
                    
                    if (currentCheckpoint >= checkpoints.Count)
                    {
                        AddReward(10.0f);
                        Debug.Log("All checkpoints completed!");
                        
                        if(trainingMode || evaluationMode){
                            LogEpisode(true);
                            EndEpisode();
                        }
                        else
                            reachedGoal = true;
                    }
                }
            }
        }
        
        if ((trainingMode || evaluationMode) && other.CompareTag("Wall"))
        {   
            collisionCount++;
            AddReward(-2.0f);
            Debug.Log("Hit wall! Episode ended.");
            LogEpisode(false);
            EndEpisode();
        }
    }

    void OnCollisionEnter(Collision other)
    {
        if ((trainingMode || evaluationMode) && other.collider.CompareTag("Wall"))
        {   
            collisionCount++;
            Debug.Log("Wall Hit!");
            LogEpisode(false);
            AddReward(-2.0f);
            EndEpisode();
        }
    }

    void OnDrawGizmos()
    {
        if (raycasts != null)
        {
            foreach (Transform ray in raycasts)
            {
                if (ray != null)
                {
                    Gizmos.color = Color.red;
                    RaycastHit hit;
                    if (Physics.Raycast(ray.position, ray.forward, out hit, rayDistance, wallMask, QueryTriggerInteraction.Ignore))
                    {
                        Gizmos.DrawLine(ray.position, hit.point);
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(hit.point, 0.2f);
                    }
                    else
                    {
                        Gizmos.DrawRay(ray.position, ray.forward * rayDistance);
                    }
                }
            }
        }
        
        // Draw checkpoint path
        if (Application.isPlaying && checkpoints.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = currentCheckpoint; i < checkpoints.Count - 1; i++)
            {
                if (checkpoints[i] != null && checkpoints[i + 1] != null)
                {
                    Gizmos.DrawLine(checkpoints[i].position, checkpoints[i + 1].position);
                }
            }
        }
    }


    void LogEpisode(bool success)
    {
        if (episodeLogged || episodeWriter == null) return;
        episodeLogged = true;
        

        if (evaluationMode)
        {
            evalEpisodeCount++;

            if (evalEpisodeCount >= maxEvalEpisodes)
            {
                Debug.Log("Evaluation complete.");

                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #endif
            }
        }
        episodeWriter.WriteLine($"{episodeIndex},{(success ? 1 : 0)},{episodeTimer},{collisionCount}");
        episodeWriter.Flush();
    }

    void OnApplicationQuit()
    {
        episodeWriter?.Close();
        trajectoryWriter?.Close();
    }
    protected override void OnDisable()
    {
        base.OnDisable();

        episodeWriter?.Close();
        trajectoryWriter?.Close();
    }
    
}