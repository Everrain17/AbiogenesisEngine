using System;
using System.Collections.Generic;

namespace AbiogenesisEngine.Core;

public class CellSpatialGrid
{
    private Dictionary<(int, int), List<Cell>> _cells = new();
    private int _cellSize;

    public CellSpatialGrid(int cellSize = 50)
    {
        _cellSize = cellSize;
    }

    public void Clear() => _cells.Clear();

    public void Insert(Cell cell)
    {
        var key = GetKey(cell.X, cell.Y);
        if (!_cells.ContainsKey(key)) _cells[key] = new List<Cell>();
        _cells[key].Add(cell);
    }

    public Cell? FindNearestPrey(float x, float y, float radius, Cell hunter)
    {
        Cell? best = null;
        float minDist = radius * radius;
        int searchRadius = (int)(radius / _cellSize) + 1;
        int cx = (int)(x / _cellSize);
        int cy = (int)(y / _cellSize);

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                var key = (cx + dx, cy + dy);
                if (_cells.TryGetValue(key, out var cellList))
                {
                    foreach (var cell in cellList)
                    {
                        if (cell.Id == hunter.Id) continue;
                        float distSq = (cell.X - x) * (cell.X - x) + (cell.Y - y) * (cell.Y - y);
                        if (distSq < minDist && cell.Energy < hunter.Energy)
                        {
                            minDist = distSq;
                            best = cell;
                        }
                    }
                }
            }
        }
        return best;
    }

    public List<Cell> GetNearbyCells(float x, float y, float radius)
    {
        var result = new List<Cell>();
        int searchRadius = (int)(radius / _cellSize) + 1;
        int cx = (int)(x / _cellSize);
        int cy = (int)(y / _cellSize);

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                var key = (cx + dx, cy + dy);
                if (_cells.TryGetValue(key, out var cellList))
                {
                    foreach (var cell in cellList)
                    {
                        float distSq = (cell.X - x) * (cell.X - x) + (cell.Y - y) * (cell.Y - y);
                        if (distSq < radius * radius) result.Add(cell);
                    }
                }
            }
        }
        return result;
    }

    private (int, int) GetKey(float x, float y) => ((int)(x / _cellSize), (int)(y / _cellSize));
}