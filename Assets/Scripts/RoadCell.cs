using UnityEngine;
using System.Collections.Generic;

public class RoadCell : MonoBehaviour
{
    // Which directions this cell connects to (North, East, South, West)
    public bool north = false;
    public bool east = false;
    public bool south = false;
    public bool west = false;
    
    public int gridX;
    public int gridY;
    
    // Walls for each direction (managed by RoadBuilder)
    public GameObject northWall;
    public GameObject eastWall;
    public GameObject southWall;
    public GameObject westWall;
    
    // Checkpoint
    public GameObject checkpoint;
    public int checkpointOrder = -1;
    
    public void SetConnections(bool n, bool e, bool s, bool w)
    {
        north = n;
        east = e;
        south = s;
        west = w;
        
        UpdateWalls();
    }
    
    void UpdateWalls()
    {
        // Show/hide walls based on connections
        if (northWall != null) northWall.SetActive(!north);
        if (southWall != null) southWall.SetActive(!south);
        if (eastWall != null) eastWall.SetActive(!east);
        if (westWall != null) westWall.SetActive(!west);
    }
    
    public int GetConnectionCount()
    {
        int count = 0;
        if (north) count++;
        if (east) count++;
        if (south) count++;
        if (west) count++;
        return count;
    }

    public void CreateCheckpoint(int order, GameObject checkpointPrefab)
{
    checkpointOrder = order;

    checkpoint = Instantiate(
        checkpointPrefab,
        transform.position + Vector3.up * 2.0f,
        Quaternion.identity,
        transform
    );

    checkpoint.tag = "Checkpoint";
    checkpoint.name = "Checkpoint_" + order;
}

    
    public void RemoveCheckpoint()
    {
        if (checkpoint != null)
        {
            Destroy(checkpoint);
            checkpoint = null;
        }
        checkpointOrder = -1;
    }
    
    // For A* pathfinding
    public List<RoadCell> GetNeighbors(RoadCell[,] grid, int gridWidth, int gridHeight)
    {
        List<RoadCell> neighbors = new List<RoadCell>();
        
        if (north && gridY + 1 < gridHeight && grid[gridX, gridY + 1] != null)
            neighbors.Add(grid[gridX, gridY + 1]);
            
        if (south && gridY - 1 >= 0 && grid[gridX, gridY - 1] != null)
            neighbors.Add(grid[gridX, gridY - 1]);
            
        if (east && gridX + 1 < gridWidth && grid[gridX + 1, gridY] != null)
            neighbors.Add(grid[gridX + 1, gridY]);
            
        if (west && gridX - 1 >= 0 && grid[gridX - 1, gridY] != null)
            neighbors.Add(grid[gridX - 1, gridY]);
        
        return neighbors;
    }
}