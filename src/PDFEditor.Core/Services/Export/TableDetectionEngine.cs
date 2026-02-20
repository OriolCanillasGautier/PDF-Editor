using System;
using System.Collections.Generic;
using System.Linq;
using PDFEditor.Core.Models.Layout;

namespace PDFEditor.Core.Services.Export
{
    public class TableDetectionEngine
    {
        // Tolerance for line intersections and coordinate grouping (in points)
        private const float Tolerance = 2.0f;

        public List<TableRegion> DetectTables(List<PdfLine> lines, List<LayoutCharacter> characters)
        {
            var horizontalLines = lines.Where(l => l.IsHorizontal).ToList();
            var verticalLines = lines.Where(l => l.IsVertical).ToList();

            // Step 1: Find all intersection points and build a graph of connected lines
            var intersections = new List<(float X, float Y, PdfLine H, PdfLine V)>();
            var adjacencyList = new Dictionary<PdfLine, HashSet<PdfLine>>();

            foreach (var h in horizontalLines)
            {
                float hMinX = Math.Min(h.X1, h.X2);
                float hMaxX = Math.Max(h.X1, h.X2);
                float hY = h.Y1;

                foreach (var v in verticalLines)
                {
                    float vMinY = Math.Min(v.Y1, v.Y2);
                    float vMaxY = Math.Max(v.Y1, v.Y2);
                    float vX = v.X1;

                    if (vX >= hMinX - Tolerance && vX <= hMaxX + Tolerance &&
                        hY >= vMinY - Tolerance && hY <= vMaxY + Tolerance)
                    {
                        intersections.Add((vX, hY, h, v));

                        if (!adjacencyList.ContainsKey(h)) adjacencyList[h] = new HashSet<PdfLine>();
                        if (!adjacencyList.ContainsKey(v)) adjacencyList[v] = new HashSet<PdfLine>();
                        adjacencyList[h].Add(v);
                        adjacencyList[v].Add(h);
                    }
                }
            }

            if (!intersections.Any())
                return new List<TableRegion>();

            // Step 2: Find connected components (each component is a table)
            var visited = new HashSet<PdfLine>();
            var components = new List<List<PdfLine>>();

            foreach (var line in adjacencyList.Keys)
            {
                if (!visited.Contains(line))
                {
                    var component = new List<PdfLine>();
                    var queue = new Queue<PdfLine>();
                    queue.Enqueue(line);
                    visited.Add(line);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        component.Add(current);

                        foreach (var neighbor in adjacencyList[current])
                        {
                            if (!visited.Contains(neighbor))
                            {
                                visited.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                    components.Add(component);
                }
            }

            var tables = new List<TableRegion>();

            // Step 3: Process each component into a TableRegion
            foreach (var component in components)
            {
                var compHorizontals = component.Where(l => l.IsHorizontal).ToList();
                var compVerticals = component.Where(l => l.IsVertical).ToList();

                var compIntersections = intersections
                    .Where(i => compHorizontals.Contains(i.H) && compVerticals.Contains(i.V))
                    .ToList();

                var uniqueXs = compIntersections.Select(p => p.X)
                                            .GroupBy(x => Math.Round(x / Tolerance))
                                            .Select(g => g.Average())
                                            .OrderBy(x => x)
                                            .ToList();

                var uniqueYs = compIntersections.Select(p => p.Y)
                                            .GroupBy(y => Math.Round(y / Tolerance))
                                            .Select(g => g.Average())
                                            .OrderByDescending(y => y)
                                            .ToList();

                if (uniqueXs.Count < 2 || uniqueYs.Count < 2)
                    continue;

                var table = new TableRegion
                {
                    RowCount = uniqueYs.Count - 1,
                    ColumnCount = uniqueXs.Count - 1,
                    BBox = new PdfRect(
                        (float)uniqueXs.First(),
                        (float)uniqueYs.Last(),
                        (float)(uniqueXs.Last() - uniqueXs.First()),
                        (float)(uniqueYs.First() - uniqueYs.Last()))
                };

                bool[,] hasRightBorder = new bool[table.RowCount, table.ColumnCount];
                bool[,] hasBottomBorder = new bool[table.RowCount, table.ColumnCount];

                for (int row = 0; row < table.RowCount; row++)
                {
                    for (int col = 0; col < table.ColumnCount; col++)
                    {
                        float left = (float)uniqueXs[col];
                        float right = (float)uniqueXs[col + 1];
                        float top = (float)uniqueYs[row];
                        float bottom = (float)uniqueYs[row + 1];

                        hasRightBorder[row, col] = HasVerticalLine(compVerticals, right, top, bottom);
                        hasBottomBorder[row, col] = HasHorizontalLine(compHorizontals, bottom, left, right);
                    }
                }

                bool[,] merged = new bool[table.RowCount, table.ColumnCount];

                for (int row = 0; row < table.RowCount; row++)
                {
                    for (int col = 0; col < table.ColumnCount; col++)
                    {
                        if (merged[row, col]) continue;

                        int rowSpan = 1;
                        int colSpan = 1;

                        // Expand right
                        while (col + colSpan < table.ColumnCount && !hasRightBorder[row, col + colSpan - 1])
                        {
                            colSpan++;
                        }

                        // Expand down
                        while (row + rowSpan < table.RowCount)
                        {
                            bool canExpand = true;
                            for (int c = 0; c < colSpan; c++)
                            {
                                if (hasBottomBorder[row + rowSpan - 1, col + c])
                                {
                                    canExpand = false;
                                    break;
                                }
                            }
                            if (!canExpand) break;
                            
                            // Check if the new row has the same right border constraints
                            bool rightBordersMatch = true;
                            for (int c = 0; c < colSpan - 1; c++)
                            {
                                if (hasRightBorder[row + rowSpan, col + c])
                                {
                                    rightBordersMatch = false;
                                    break;
                                }
                            }
                            if (!rightBordersMatch) break;

                            rowSpan++;
                        }

                        for (int r = 0; r < rowSpan; r++)
                        {
                            for (int c = 0; c < colSpan; c++)
                            {
                                merged[row + r, col + c] = true;
                            }
                        }

                        float cellLeft = (float)uniqueXs[col];
                        float cellRight = (float)uniqueXs[col + colSpan];
                        float cellTop = (float)uniqueYs[row];
                        float cellBottom = (float)uniqueYs[row + rowSpan];

                        var cellRect = new PdfRect(cellLeft, cellBottom, cellRight - cellLeft, cellTop - cellBottom);
                        table.Cells.Add(new LayoutTableCell
                        {
                            Row = row,
                            Column = col,
                            RowSpan = rowSpan,
                            ColSpan = colSpan,
                            BBox = cellRect
                        });
                    }
                }

                foreach (var character in characters)
                {
                    if (!table.BBox.Intersects(character.BBox))
                        continue;

                    foreach (var cell in table.Cells)
                    {
                        if (character.BBox.CenterX >= cell.BBox.X &&
                            character.BBox.CenterX <= cell.BBox.Right &&
                            character.BBox.CenterY >= cell.BBox.Y &&
                            character.BBox.CenterY <= cell.BBox.Bottom)
                        {
                            cell.Content.Add(character);
                            break;
                        }
                    }
                }

                tables.Add(table);
            }

            return tables;
        }

        private bool HasVerticalLine(List<PdfLine> lines, float x, float top, float bottom)
        {
            foreach (var line in lines)
            {
                if (Math.Abs(line.X1 - x) <= Tolerance)
                {
                    float lineTop = Math.Max(line.Y1, line.Y2);
                    float lineBottom = Math.Min(line.Y1, line.Y2);
                    
                    if (lineTop >= top - Tolerance && lineBottom <= bottom + Tolerance)
                        return true;
                }
            }
            return false;
        }

        private bool HasHorizontalLine(List<PdfLine> lines, float y, float left, float right)
        {
            foreach (var line in lines)
            {
                if (Math.Abs(line.Y1 - y) <= Tolerance)
                {
                    float lineLeft = Math.Min(line.X1, line.X2);
                    float lineRight = Math.Max(line.X1, line.X2);
                    
                    if (lineLeft <= left + Tolerance && lineRight >= right - Tolerance)
                        return true;
                }
            }
            return false;
        }
    }
}
