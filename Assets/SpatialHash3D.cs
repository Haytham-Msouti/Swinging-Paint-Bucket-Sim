using System.Collections.Generic;
using UnityEngine;
namespace Assets
{
    public sealed class SpatialHash3D
    {
        private readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();

        public float CellSize { get; private set; }

        public SpatialHash3D(float cellSize)
        {
            CellSize = Mathf.Max(0.0001f, cellSize);
        }

        public void Clear()
        {
            foreach (List<int> list in cells.Values)
            {
                list.Clear();
            }
        }

        public void Insert(Vector3 position, int index)
        {
            Vector3Int cell = PositionToCell(position);
            List<int> list;
            if (!cells.TryGetValue(cell, out list))
            {
                list = new List<int>(8);
                cells.Add(cell, list);
            }

            list.Add(index);
        }

        public void Query(Vector3 position, float radius, List<int> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();

            Vector3Int centerCell = PositionToCell(position);
            int range = Mathf.Max(1, Mathf.CeilToInt(radius / CellSize));

            for (int z = -range; z <= range; z++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int x = -range; x <= range; x++)
                    {
                        Vector3Int cell = new Vector3Int(centerCell.x + x, centerCell.y + y, centerCell.z + z);
                        List<int> list;
                        if (!cells.TryGetValue(cell, out list))
                        {
                            continue;
                        }

                        results.AddRange(list);
                    }
                }
            }
        }

        private Vector3Int PositionToCell(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x / CellSize),
                Mathf.FloorToInt(position.y / CellSize),
                Mathf.FloorToInt(position.z / CellSize)
            );
        }
    }
}