using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class RuntimeRoadBuilder : MonoBehaviour
{
    [Header("Training Modes")]
    public bool useFixedTrack = false;

    [Header("Grid Settings")]
    public int gridWidth = 15;
    public int gridHeight = 15;
    public float cellSize = 10f;
    
    [Header("Road Prefabs")]
    public GameObject roadCellPrefab;
    public GameObject wallPrefab;
    public GameObject checkpointPrefab;
    
    [Header("Special Markers")]
    public GameObject startMarkerPrefab;
    public GameObject endMarkerPrefab;
    public GameObject carPrefab;
    
    [Header("UI References")]
    public UnityEngine.UI.Button clearButton;
    public UnityEngine.UI.Button setStartButton;
    public UnityEngine.UI.Button setEndButton;
    public UnityEngine.UI.Button generateCheckpointsButton;
    public UnityEngine.UI.Button goButton;
    public TMPro.TextMeshProUGUI modeText;
    
    private RoadCell[,] grid;
    private Camera mainCamera;
    
    [Header("Camera Manager")]
    public CameraManager cameraManager;
    
    private GameObject startMarker;
    private GameObject endMarker;
    private GameObject spawnedCar;
    
    private Vector2Int? startCell;
    private Vector2Int? endCell;
    
    private List<RoadCell> checkpointPath = new List<RoadCell>();
    private List<RoadCell> actualCheckpointPath = new List<RoadCell>();

    private enum EditorMode { Draw, SetStart, SetEnd }
    private EditorMode currentMode = EditorMode.Draw;
    
    void Start()
    {
        grid = new RoadCell[gridWidth, gridHeight];

        if (cameraManager != null)
        {
            mainCamera = cameraManager.editorCamera;
        }

        if (useFixedTrack)
        {
            GenerateRandomTrack();
            GenerateCheckpoints();
        }
        
        // Setup UI buttons
        if (clearButton) clearButton.onClick.AddListener(ClearAllRoads);
        if (setStartButton) setStartButton.onClick.AddListener(() => SetMode(EditorMode.SetStart));
        if (setEndButton) setEndButton.onClick.AddListener(() => SetMode(EditorMode.SetEnd));
        if (generateCheckpointsButton) generateCheckpointsButton.onClick.AddListener(GenerateAndSpawn);
        if (goButton) goButton.onClick.AddListener(StartCar);
        
        UpdateModeText();
    }
    
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }
    }
    
    void HandleClick()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit))
        {
            Vector3 localPos = hit.point;
            int x = Mathf.FloorToInt(localPos.x / cellSize);
            int y = Mathf.FloorToInt(localPos.z / cellSize);
            
            if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
            {
                HandleGridClick(x, y);
            }
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            float enter;
            
            if (groundPlane.Raycast(ray, out enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                int x = Mathf.FloorToInt(hitPoint.x / cellSize);
                int y = Mathf.FloorToInt(hitPoint.z / cellSize);
                
                if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                {
                    HandleGridClick(x, y);
                }
            }
        }
    }
    
    void HandleGridClick(int x, int y)
    {
        switch (currentMode)
        {
            case EditorMode.Draw:
                PlaceOrRemoveRoad(x, y);
                break;
            case EditorMode.SetStart:
                SetStartLocation(x, y);
                break;
            case EditorMode.SetEnd:
                SetEndLocation(x, y);
                break;
        }
    }
    
    void PlaceOrRemoveRoad(int x, int y)
    {
        if (grid[x, y] != null)
        {   
            Vector2Int clicked = new Vector2Int(x, y);

            if (startCell.HasValue && startCell.Value == clicked)
            {
                if (startMarker != null) Destroy(startMarker);
                startCell = null;
            }

            if (endCell.HasValue && endCell.Value == clicked)
            {
                if (endMarker != null) Destroy(endMarker);
                endCell = null;
            }
            // Remove existing road (walls are children so they'll be destroyed too)
            Destroy(grid[x, y].gameObject);
            grid[x, y] = null;
            
            UpdateNeighborConnections(x, y);
        }
        else
        {
            // Place new road at Y = 0.5
            // Vector3 worldPos = new Vector3(x * cellSize + cellSize / 2f, 0.5f, y * cellSize + cellSize / 2f);
            Vector3 worldPos = transform.position + new Vector3(x * cellSize + cellSize / 2f,0.5f,y * cellSize + cellSize / 2f);
            GameObject roadObj = Instantiate(roadCellPrefab, worldPos, Quaternion.identity, transform);
            
            RoadCell cell = roadObj.GetComponent<RoadCell>();
            if (cell == null)
            {
                cell = roadObj.AddComponent<RoadCell>();
            }
            
            cell.gridX = x;
            cell.gridY = y;
            grid[x, y] = cell;
            
            // Create walls for this cell
            CreateWallsForCell(cell);
            
            // Determine connections based on neighbors
            UpdateCellConnections(x, y);
            UpdateNeighborConnections(x, y);
        }
    }
    
    void UpdateCellConnections(int x, int y)
    {
        RoadCell cell = grid[x, y];
        if (cell == null) return;
        
        bool north = (y + 1 < gridHeight && grid[x, y + 1] != null);
        bool south = (y - 1 >= 0 && grid[x, y - 1] != null);
        bool east = (x + 1 < gridWidth && grid[x + 1, y] != null);
        bool west = (x - 1 >= 0 && grid[x - 1, y] != null);
        
        cell.SetConnections(north, east, south, west);
    }
    
    void UpdateNeighborConnections(int x, int y)
    {
        if (y + 1 < gridHeight) UpdateCellConnections(x, y + 1);
        if (y - 1 >= 0) UpdateCellConnections(x, y - 1);
        if (x + 1 < gridWidth) UpdateCellConnections(x + 1, y);
        if (x - 1 >= 0) UpdateCellConnections(x - 1, y);
    }
    
    void CreateWallsForCell(RoadCell cell)
    {
        // North wall (at +Z edge)
        cell.northWall = CreateWall(cell.transform, new Vector3(0, 1f, 0.6f), Vector3.zero);
        cell.northWall.name = "NorthWall";
        
        // South wall (at -Z edge)
        cell.southWall = CreateWall(cell.transform, new Vector3(0, 1f, -0.6f), Vector3.zero);
        cell.southWall.name = "SouthWall";
        
        // East wall (at +X edge, rotated 90°)
        cell.eastWall = CreateWall(cell.transform, new Vector3(0.6f, 1f, 0), new Vector3(0, 90, 0));
        cell.eastWall.name = "EastWall";
        
        // West wall (at -X edge, rotated 90°)
        cell.westWall = CreateWall(cell.transform, new Vector3(-0.6f, 1f, 0), new Vector3(0, 90, 0));
        cell.westWall.name = "WestWall";
    }

    
    GameObject CreateWall(Transform parent, Vector3 localPosition, Vector3 rotation)
    {
        GameObject wall;
        
        if (wallPrefab != null)
        {
            // wall = Instantiate(wallPrefab, parent);
            wall = Instantiate(wallPrefab);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localRotation = Quaternion.Euler(rotation);
        }
        else
        {
            // Create default wall
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(parent);
            wall.transform.localPosition = localPosition;
            wall.transform.localRotation = Quaternion.Euler(rotation);
            wall.transform.localScale = new Vector3(cellSize, 2f, 0.5f);
            wall.GetComponent<MeshRenderer>().material.color = Color.gray;
        }
        
        wall.tag = "Wall";
        return wall;
    }
    
    void SetStartLocation(int x, int y)
    {
        if (grid[x, y] == null)
        {
            Debug.Log("Place a road here first!");
            SetMode(EditorMode.Draw);
            return;
        }
        if (endCell.HasValue && endCell.Value == new Vector2Int(x,y))
        {
            Debug.LogWarning("Start cannot be same as End!");
            SetMode(EditorMode.Draw);
            return;
        }
        
        startCell = new Vector2Int(x, y);
        
        if (startMarker != null) Destroy(startMarker);
        
        Vector3 pos = transform.position + new Vector3(x * cellSize + cellSize / 2f,1f,y * cellSize + cellSize / 2f);
        
        if (startMarkerPrefab != null)
        {
            startMarker = Instantiate(startMarkerPrefab, pos, Quaternion.identity, transform);
        }
        else
        {
            startMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            startMarker.transform.position = pos;
            startMarker.transform.localScale = new Vector3(2f, 0.5f, 2f);
            startMarker.GetComponent<MeshRenderer>().material.color = Color.green;
            startMarker.transform.SetParent(transform);
        }
        
        SetMode(EditorMode.Draw);
        Debug.Log("Start location set at: " + x + ", " + y);
    }
    
    void SetEndLocation(int x, int y)
    {
        if (grid[x, y] == null)
        {
            Debug.Log("Place a road here first!");
            SetMode(EditorMode.Draw);
            return;
        }
        if (startCell.HasValue && startCell.Value == new Vector2Int(x,y))
        {
            Debug.LogWarning("End cannot be same as Start!");
            SetMode(EditorMode.Draw);
            return;
        }
        
        endCell = new Vector2Int(x, y);
        
        if (endMarker != null) Destroy(endMarker);
        
        Vector3 pos = transform.position + new Vector3(x * cellSize + cellSize / 2f,1f,y * cellSize + cellSize / 2f);
        
        if (endMarkerPrefab != null)
        {
            endMarker = Instantiate(endMarkerPrefab, pos, Quaternion.identity, transform);
        }
        else
        {
            endMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            endMarker.transform.position = pos;
            endMarker.transform.localScale = new Vector3(2f, 0.5f, 2f);
            endMarker.GetComponent<MeshRenderer>().material.color = Color.red;
            endMarker.transform.SetParent(transform);
        }
        
        SetMode(EditorMode.Draw);
        Debug.Log("End location set at: " + x + ", " + y);
    }
    
    public void GenerateCheckpoints()
    {
        if (startCell == null || endCell == null)
        {
            Debug.LogWarning("Set both start and end locations first!");
            return;
        }
        
        // Clear existing checkpoints
        ClearCheckpoints();
        
        // Find path using A* from start to end
        var path = FindPath(startCell.Value, endCell.Value);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("No path found between start and end!");
            checkpointPath = new List<RoadCell>(); // IMPORTANT
            return;
        }

        checkpointPath = path;

        
        // Create checkpoints along the path, skip turns
        actualCheckpointPath.Clear();
        int order = 0;

        for (int i = 0; i < checkpointPath.Count; i++)
        {
            if(i > 0 && i < checkpointPath.Count - 1)
            {
                Vector2Int prev = new Vector2Int(
                    checkpointPath[i-1].gridX,
                    checkpointPath[i-1].gridY);

                Vector2Int curr = new Vector2Int(
                    checkpointPath[i].gridX,
                    checkpointPath[i].gridY);

                Vector2Int next = new Vector2Int(
                    checkpointPath[i+1].gridX,
                    checkpointPath[i+1].gridY);

                Vector2Int d1 = curr - prev;
                Vector2Int d2 = next - curr;

                // if direction changes → corner cell
                if(d1 != d2)
                    continue;
            }       

            checkpointPath[i].CreateCheckpoint(order++, checkpointPrefab);
            actualCheckpointPath.Add(checkpointPath[i]);
        }

        Debug.Log($"Generated {checkpointPath.Count} checkpoints along optimal path");
    }
    
    List<RoadCell> FindPath(Vector2Int start, Vector2Int end)
    {
        RoadCell startNode = grid[start.x, start.y];
        RoadCell endNode = grid[end.x, end.y];
        
        if (startNode == null || endNode == null) return null;
        
        List<RoadCell> openSet = new List<RoadCell>();
        HashSet<RoadCell> closedSet = new HashSet<RoadCell>();
        Dictionary<RoadCell, RoadCell> cameFrom = new Dictionary<RoadCell, RoadCell>();
        Dictionary<RoadCell, float> gScore = new Dictionary<RoadCell, float>();
        Dictionary<RoadCell, float> fScore = new Dictionary<RoadCell, float>();
        
        openSet.Add(startNode);
        gScore[startNode] = 0;
        fScore[startNode] = Heuristic(startNode, endNode);
        
        while (openSet.Count > 0)
        {
            // Get node with lowest fScore
            RoadCell current = openSet.OrderBy(n => fScore.ContainsKey(n) ? fScore[n] : float.MaxValue).First();
            
            if (current == endNode)
            {
                return ReconstructPath(cameFrom, current);
            }
            
            openSet.Remove(current);
            closedSet.Add(current);
            
            foreach (RoadCell neighbor in current.GetNeighbors(grid, gridWidth, gridHeight))
            {
                if (closedSet.Contains(neighbor)) continue;
                
                float tentativeGScore = gScore[current] + 1;
                
                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (gScore.ContainsKey(neighbor) && tentativeGScore >= gScore[neighbor])
                {
                    continue;
                }
                
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeGScore;
                fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, endNode);
            }
        }
        
        return null; // No path found
    }
    
    float Heuristic(RoadCell a, RoadCell b)
    {
        return Mathf.Abs(a.gridX - b.gridX) + Mathf.Abs(a.gridY - b.gridY);
    }
    
    List<RoadCell> ReconstructPath(Dictionary<RoadCell, RoadCell> cameFrom, RoadCell current)
    {
        List<RoadCell> path = new List<RoadCell>();
        path.Add(current);
        
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        
        path.Reverse();
        return path;
    }
    
    void ClearCheckpoints()
    {
        foreach (RoadCell cell in checkpointPath)
        {
            if (cell != null)
            {
                cell.RemoveCheckpoint();
            }
        }
        checkpointPath.Clear();
    }
    
    void SpawnCar()
    {
        if (startCell == null)
        {
            Debug.LogWarning("Set a start location first!");
            return;
        }

        //if ML car, require checkpoints
        bool isMLCar = carPrefab != null && carPrefab.GetComponent<RealisticCarAgent>() != null;

        if (isMLCar)
        {
            if (checkpointPath == null || checkpointPath.Count == 0)
            {
                Debug.LogWarning("Generate checkpoints before spawning ML car.");
                return;
            }
        }
        
        if (spawnedCar != null) Destroy(spawnedCar);
        
        Vector3 spawnPos = new Vector3(
            startCell.Value.x * cellSize + cellSize / 2f, 
            3f, 
            startCell.Value.y * cellSize + cellSize / 2f
        );
        
        if (carPrefab != null)
        {
            spawnedCar = Instantiate(carPrefab, spawnPos, Quaternion.identity);
            RealisticCarAgent mlAgent = spawnedCar.GetComponent<RealisticCarAgent>();

            if(mlAgent != null)
                mlAgent.InitializeForPlayMode();
        }
        else
        {
            spawnedCar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawnedCar.transform.position = spawnPos;
            spawnedCar.transform.localScale = new Vector3(2f, 1f, 3f);
            spawnedCar.GetComponent<MeshRenderer>().material.color = Color.blue;
            spawnedCar.AddComponent<Rigidbody>();
        }
        
        Debug.Log("Car spawned at start location");
    }
    
    void StartCar()
    {
        if (spawnedCar == null)
        {
            Debug.LogWarning("Spawn a car first!");
            return;
        }
        
        if (endCell == null)
        {
            Debug.LogWarning("Set an end location first!");
            return;
        }
        
        if (checkpointPath == null || checkpointPath.Count == 0)
        {
            Debug.LogWarning("Generate checkpoints first!");
            return;
        }
        
        // Check if it's an ML Agent car
        RealisticCarAgent mlAgent = spawnedCar.GetComponent<RealisticCarAgent>();
        if (mlAgent != null)
        {
            // ML Agent car - initialize with checkpoints
            // mlAgent.InitializeForPlayMode();
            // Debug.Log("ML Agent car initialized with dynamic checkpoints");

            mlAgent.StartDrivingRuntime();

            if(cameraManager != null)
                cameraManager.SwitchToDriving(spawnedCar);

            Debug.Log("ML Agent started!");
        }
    }
    
    public void GenerateAndSpawn()
    {
        if (startCell == null || endCell == null)
        {
            Debug.LogWarning("Set both start and end first!");
            return;
        }
        GenerateCheckpoints();
        StartCoroutine(SpawnAndInit());
    }

    // Public method for agents to get checkpoint path
    public List<RoadCell> GetCheckpointPath()
    {
        return actualCheckpointPath;
    }
    
    public Vector3 GetSpawnForwardFromPath()
    {
        if(checkpointPath == null || checkpointPath.Count < 2)
            return Vector3.forward;

        Vector3 dir = checkpointPath[1].transform.position - checkpointPath[0].transform.position;
        dir.y = 0f;
        return dir.normalized;
    }


    void SetMode(EditorMode mode)
    {
        currentMode = mode;
        UpdateModeText();
    }
    
    void UpdateModeText()
    {
        if (modeText != null)
        {
            modeText.text = "Mode: " + currentMode.ToString();
        }
    }
    
    public void ClearAllRoads()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (grid[x, y] != null)
                {
                    Destroy(grid[x, y].gameObject);
                    grid[x, y] = null;
                }
            }
        }
        
        if (startMarker != null) Destroy(startMarker);
        if (endMarker != null) Destroy(endMarker);
        if (spawnedCar != null) Destroy(spawnedCar);
        
        startCell = null;
        endCell = null;
        checkpointPath.Clear();

        if(cameraManager != null)
            cameraManager.SwitchToEditor();
        
        Debug.Log("All roads cleared");
    }

    public Vector2Int GetStartCell()
    {
        return startCell.Value;
    }

    public void GenerateRandomTrack()
    {
        ClearAllRoads();

        int x = Random.Range(2, gridWidth - 2);
        int y = Random.Range(2, gridHeight - 2);

        startCell = new Vector2Int(x, y);

        PlaceOrRemoveRoad(x, y);

        int trackLength = Random.Range(15, 30);

        Vector2Int current = startCell.Value;

        for(int i = 0; i < trackLength; i++)
        {
            List<Vector2Int> dirs = new List<Vector2Int>()
            {
                Vector2Int.up,
                Vector2Int.down,
                Vector2Int.left,
                Vector2Int.right
            };

            dirs.Shuffle();   // we add this below

            foreach(var dir in dirs)
            {
                Vector2Int next = current + dir;

                if(next.x > 1 && next.x < gridWidth - 2 &&
                    next.y > 1 && next.y < gridHeight - 2 &&
                    grid[next.x, next.y] == null)
                {
                    PlaceOrRemoveRoad(next.x, next.y);
                    current = next;
                    break;
                }
            }
        }

        endCell = current;
    }

    IEnumerator SpawnAndInit()
    {
        SpawnCar();

        yield return null;

        if (spawnedCar != null)
        {
            RealisticCarAgent agent = spawnedCar.GetComponent<RealisticCarAgent>();
            if (agent != null)
                agent.InitializeForPlayMode();
        }
    }

}