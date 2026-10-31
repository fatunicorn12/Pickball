using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Meshes the south island (SouthIslandTerrain) one band at a time: the walkable ground, the
    /// rock skirt round its edge, the underside cap, a low dry-stone rim wall, and the invisible
    /// fence that keeps the player from dropping onto the Delta Pylons' islets.
    ///
    /// The ground is a 1 m grid over the whole island. A cell is kept if any corner is inside the
    /// outline; its outside corners are pulled onto the outline along the distance gradient, so
    /// the edge follows the curve instead of stair-stepping. Every band uses the same grid and
    /// the same height function, so neighbouring bands share their seam vertices exactly.
    /// </summary>
    public static class IslandMeshes
    {
        private const float GridMinX = -19f, GridMaxX = 28f, GridMinZ = -105f, GridMaxZ = -50f;
        private const float WallHeight = 0.45f, WallDepth = 0.45f, FenceHeight = 14f;

        /// <summary>The five meshes of one band.</summary>
        public sealed class Band
        {
            /// <summary>Walkable ground (has a collider).</summary>
            public readonly MeshBuffer Ground = new();
            /// <summary>Rock faces round the edge (has a collider).</summary>
            public readonly MeshBuffer Skirt = new();
            /// <summary>Underside.</summary>
            public readonly MeshBuffer Cap = new();
            /// <summary>Low stone wall on the fenced edges.</summary>
            public readonly MeshBuffer RimWall = new();
            /// <summary>Invisible collider wall on every edge except the north (plateau) edge.</summary>
            public readonly MeshBuffer Fence = new();
        }

        /// <summary>Builds the band of cells whose centre z lies in [zMin, zMax).</summary>
        public static Band BuildBand(float zMin, float zMax)
        {
            int nx = Mathf.RoundToInt(GridMaxX - GridMinX), nz = Mathf.RoundToInt(GridMaxZ - GridMinZ);
            var snapped = new Vector3[nx + 1, nz + 1];
            var inside = new bool[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                float x = GridMinX + i, z = GridMinZ + j;
                float d = SouthIslandTerrain.SignedDistance(x, z);
                inside[i, j] = d <= 0f;
                Vector2 p = new(x, z);
                if (d > 0f)
                {
                    // Two projection steps: the outline is curved at the lobe and the corners.
                    for (int k = 0; k < 2; k++)
                    {
                        float dd = SouthIslandTerrain.SignedDistance(p.x, p.y);
                        p -= Gradient(p) * dd;
                    }
                }
                snapped[i, j] = new Vector3(p.x, SouthIslandTerrain.Height(p.x, p.y), p.y);
            }

            var kept = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                kept[i, j] = inside[i, j] || inside[i + 1, j] || inside[i, j + 1] || inside[i + 1, j + 1];

            var band = new Band();
            var index = new Dictionary<(int, int), int>();
            int Vertex(int i, int j)
            {
                if (index.TryGetValue((i, j), out int at)) return at;
                Vector3 v = snapped[i, j];
                at = band.Ground.Add(v, SouthIslandTerrain.Normal(v.x, v.z), SouthIslandTerrain.GroundColour(v.x, v.z),
                                     new Vector2(v.x, v.z) * 0.25f);
                index[(i, j)] = at;
                return at;
            }

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (!kept[i, j]) continue;
                float centreZ = GridMinZ + j + 0.5f;
                if (centreZ < zMin || centreZ >= zMax) continue;

                int a = Vertex(i, j), b = Vertex(i + 1, j), c = Vertex(i + 1, j + 1), d = Vertex(i, j + 1);
                band.Ground.AddFacing(a, b, c, Vector3.up);
                band.Ground.AddFacing(a, c, d, Vector3.up);
                Cap(band.Cap, snapped[i, j], snapped[i + 1, j], snapped[i + 1, j + 1], snapped[i, j + 1]);

                Vector3 centre = (snapped[i, j] + snapped[i + 1, j + 1]) * 0.5f;
                if (!Kept(kept, i, j - 1, nx, nz)) Edge(band, snapped[i, j], snapped[i + 1, j], centre);
                if (!Kept(kept, i, j + 1, nx, nz)) Edge(band, snapped[i, j + 1], snapped[i + 1, j + 1], centre);
                if (!Kept(kept, i - 1, j, nx, nz)) Edge(band, snapped[i, j], snapped[i, j + 1], centre);
                if (!Kept(kept, i + 1, j, nx, nz)) Edge(band, snapped[i + 1, j], snapped[i + 1, j + 1], centre);
            }
            return band;
        }

        private static bool Kept(bool[,] kept, int i, int j, int nx, int nz) =>
            i >= 0 && j >= 0 && i < nx && j < nz && kept[i, j];

        private static Vector2 Gradient(Vector2 p)
        {
            const float e = 0.01f;
            float dx = SouthIslandTerrain.SignedDistance(p.x + e, p.y) - SouthIslandTerrain.SignedDistance(p.x - e, p.y);
            float dz = SouthIslandTerrain.SignedDistance(p.x, p.y + e) - SouthIslandTerrain.SignedDistance(p.x, p.y - e);
            Vector2 g = new(dx, dz);
            return g.sqrMagnitude > 1e-12f ? g.normalized : Vector2.zero;
        }

        private static void Cap(MeshBuffer cap, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float y = SouthIslandTerrain.SkirtBottom;
            var dark = new Color(0.33f, 0.29f, 0.27f);
            cap.Quad(new(a.x, y, a.z), new(b.x, y, b.z), new(c.x, y, c.z), new(d.x, y, d.z), Vector3.down, dark, dark);
        }

        // One boundary edge: rock skirt, and unless it is the plateau edge, a rim wall and the fence.
        private static void Edge(Band band, Vector3 v0, Vector3 v1, Vector3 cellCentre)
        {
            Vector3 along = v1 - v0;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-6f) return;
            var outward = new Vector3(along.z, 0f, -along.x).normalized;
            Vector3 mid = (v0 + v1) * 0.5f;
            if (Vector3.Dot(outward, new Vector3(mid.x - cellCentre.x, 0f, mid.z - cellCentre.z)) < 0f) outward = -outward;

            bool plateauEdge = SouthIslandTerrain.IsNorthEdge(v0.z) && SouthIslandTerrain.IsNorthEdge(v1.z);
            float wall = plateauEdge ? 0f : WallHeight * (0.85f + 0.3f * SouthIslandTerrain.Hash((int)(mid.x * 7f), (int)(mid.z * 7f)));
            float bottom = SouthIslandTerrain.SkirtBottom;

            // Rock in soft horizontal strata (warm sandstone bands, darker toward the bottom) and a
            // grassy lip, so a 10 m cliff reads as layered rock rather than a flat wall.
            var rockLow = new Color(0.4f, 0.35f, 0.31f);
            var rockHigh = new Color(0.7f, 0.6f, 0.48f);
            var lip = new Color(0.45f, 0.6f, 0.3f);
            const float strata = 1.4f;
            float top0 = v0.y + wall, top1 = v1.y + wall;
            float highest = Mathf.Max(top0, top1);
            for (float y = bottom; y < highest - 0.01f; y += strata)
            {
                float yTop = Mathf.Min(y + strata, highest);
                float a0 = Mathf.Min(y, top0), a1 = Mathf.Min(y, top1), b0 = Mathf.Min(yTop, top0), b1 = Mathf.Min(yTop, top1);
                if (b0 - a0 < 1e-3f && b1 - a1 < 1e-3f) continue;
                int layer = Mathf.FloorToInt((y - bottom) / strata);
                float t = Mathf.Clamp01((y - bottom) / 12f);
                float tone = 0.88f + 0.22f * SouthIslandTerrain.Hash(layer, 31);
                Color c = Color.Lerp(rockLow, rockHigh, t) * tone;
                band.Skirt.Quad(new(v0.x, a0, v0.z), new(v1.x, a1, v1.z), new(v1.x, b1, v1.z), new(v0.x, b0, v0.z),
                                outward, c * 0.94f, c);
            }
            if (plateauEdge && highest > 1f)
            {
                // Grass hanging over the north cliff's edge, 2 cm proud of the rock.
                Vector3 o = outward * 0.02f;
                const float lipDepth = 0.35f;
                band.Skirt.Quad(new Vector3(v0.x, top0 - lipDepth, v0.z) + o, new Vector3(v1.x, top1 - lipDepth, v1.z) + o,
                                new Vector3(v1.x, top1 + 0.01f, v1.z) + o, new Vector3(v0.x, top0 + 0.01f, v0.z) + o,
                                outward, lip * 0.85f, lip);
            }

            if (plateauEdge) return;

            // Dry-stone wall: a top and an inner face, one block per edge.
            Vector3 in0 = v0 - outward * WallDepth, in1 = v1 - outward * WallDepth;
            in0.y = SouthIslandTerrain.Height(in0.x, in0.z);
            in1.y = SouthIslandTerrain.Height(in1.x, in1.z);
            float shade = 0.9f + 0.2f * SouthIslandTerrain.Hash((int)(mid.x * 3f) + 11, (int)(mid.z * 3f));
            var stone = new Color(0.8f * shade, 0.73f * shade, 0.6f * shade);
            var stoneTop = new Color(0.88f * shade, 0.82f * shade, 0.69f * shade);
            band.RimWall.Quad(v0 + Vector3.up * wall, v1 + Vector3.up * wall,
                              new Vector3(in1.x, v1.y + wall, in1.z), new Vector3(in0.x, v0.y + wall, in0.z),
                              Vector3.up, stoneTop, stoneTop);
            band.RimWall.Quad(in0, in1, new Vector3(in1.x, v1.y + wall, in1.z), new Vector3(in0.x, v0.y + wall, in0.z),
                              -outward, stone * 0.85f, stone);

            // Fence: double-sided so the controller is stopped from either side.
            Vector3 f0 = v0 + Vector3.down, f1 = v1 + Vector3.down;
            Vector3 f2 = v1 + Vector3.up * FenceHeight, f3 = v0 + Vector3.up * FenceHeight;
            band.Fence.Quad(f0, f1, f2, f3, outward, Color.white, Color.white);
            band.Fence.Quad(f0, f1, f2, f3, -outward, Color.white, Color.white);
        }
    }
}
