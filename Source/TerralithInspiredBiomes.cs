/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is intended for CastleForge / WorldGenPlus custom biome use.
*/

using Microsoft.Xna.Framework;
using DNA.Drawing.Noise;
using System;

namespace DNA.CastleMinerZ.Terrain.WorldBuilders
{
    /// <summary>
    /// Shared helpers for the low-base Terralith-inspired biome pack.
    /// Summary: Provides deterministic FBM / ridged-noise sampling and safe block indexing.
    /// </summary>
    internal static class TerralithLowBaseTerrainMath
    {
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        public static float Smooth01(float value)
        {
            value = Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        public static float RemapTo01(float value, float min, float max)
        {
            if (max <= min)
                return 0f;

            return Clamp01((value - min) / (max - min));
        }

        public static float Terrace(float value, float steps, float strength)
        {
            if (steps < 2f)
                return value;

            float stepped = (float)Math.Floor(Clamp01(value) * steps) / (steps - 1f);
            return MathHelper.Lerp(value, Clamp01(stepped), Clamp01(strength));
        }

        public static float Fbm2D(PerlinNoise noise, float x, float z, float scale, int octaves, float lacunarity, float gain)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            float frequency = 1f;

            for (int i = 0; i < octaves; i++)
            {
                sum += noise.ComputeNoise(x * scale * frequency, z * scale * frequency) * amplitude;
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }

            return norm > 0f ? (sum / norm) : 0f;
        }

        public static float Ridged2D(PerlinNoise noise, float x, float z, float scale, int octaves, float lacunarity, float gain)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            float frequency = 1f;

            for (int i = 0; i < octaves; i++)
            {
                float n = noise.ComputeNoise(x * scale * frequency, z * scale * frequency);
                n = 1f - Math.Abs(n);
                n *= n;

                sum += n * amplitude;
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }

            return norm > 0f ? (sum / norm) : 0f;
        }

        public static float Noise3D(PerlinNoise noise, float x, float y, float z, float scale)
        {
            return noise.ComputeNoise(new Vector3(x * scale, y * scale, z * scale));
        }

        public static bool TryGetIndex(BlockTerrain terrain, int worldX, int worldY, int worldZ, out int index)
        {
            index = terrain.MakeIndexFromWorldIndexVector(new IntVector3(worldX, worldY, worldZ));
            return (uint)index < (uint)terrain._blocks.Length;
        }
    }

    /// <summary>
    /// Forested low-base alpine foothills.
    /// Summary: Keeps basin floors relatively low so neighboring cliffs and peaks feel much taller.
    /// </summary>
    public sealed class TerralithLowBaseFoothillsBiome : Biome
    {
        private readonly PerlinNoise _noise;

        public TerralithLowBaseFoothillsBiome(WorldInfo worldInfo)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
            WaterDepth = 12f;
        }

        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            if (terrain._resetRequested)
                return;

            int groundLimit = SampleHeight(worldX, worldZ, blender);
            if (groundLimit < 18) groundLimit = 18;
            if (groundLimit > 114) groundLimit = 114;

            float steepness = SampleSteepness(worldX, worldZ, blender);
            int snowLine = 84 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX + 1800f, worldZ - 1800f, 0.0045f, 2, 2f, 0.5f) * 5f);
            int dirtDepth = steepness >= 8f ? 2 : 4;

            bool snowyTop = groundLimit >= snowLine;
            bool grassyTop = !snowyTop && steepness < 10f;
            bool rockyTop = !snowyTop && !grassyTop;

            for (int y = 0; y <= groundLimit; y++)
            {
                if (terrain._resetRequested)
                    return;

                int worldY = minY + y;
                if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                    continue;

                int block = rockblock;

                if (y == groundLimit)
                {
                    if (snowyTop)
                        block = snowBlock;
                    else if (grassyTop)
                        block = grassblock;
                    else
                        block = rockblock;
                }
                else if (y >= groundLimit - dirtDepth)
                {
                    if (snowyTop)
                        block = (y >= groundLimit - 2) ? snowBlock : rockblock;
                    else if (grassyTop)
                        block = dirtblock;
                    else
                        block = rockblock;
                }
                else if (rockyTop && y >= groundLimit - 1)
                {
                    block = dirtblock;
                }

                terrain._blocks[index] = block;
            }

            float alcoveMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 900f, worldZ - 1200f, 0.020f, 4, 2.05f, 0.55f);
            if (steepness >= 9f || alcoveMask >= 0.56f)
            {
                int carveStart = Math.Max(22, groundLimit - 24);
                int carveEnd = groundLimit - 3;
                for (int y = carveStart; y <= carveEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float cave = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 500f, y - 40f, worldZ - 500f, 0.055f)
                               + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 300f, y + 150f, worldZ + 300f, 0.110f) * 0.5f;

                    float carveHeightFactor = TerralithLowBaseTerrainMath.RemapTo01(y, carveStart, carveEnd);
                    float threshold = 0.77f - (alcoveMask * 0.20f) - (steepness * 0.012f) + (carveHeightFactor * 0.04f);
                    if (cave > threshold)
                        terrain._blocks[index] = emptyblock;
                }
            }
        }

        private int SampleHeight(int worldX, int worldZ, float blender)
        {
            float macro = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX, worldZ, 0.0018f, 3, 2f, 0.5f);
            float macro01 = (macro + 1f) * 0.5f;
            float hillMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(macro01, 0.34f, 0.84f));

            float rolling = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX, worldZ, 0.0068f, 5, 2f, 0.5f);
            float ridges = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 1400f, worldZ - 700f, 0.011f, 4, 2.1f, 0.55f);
            float shelves = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 2100f, worldZ + 900f, 0.026f, 3, 2.05f, 0.5f);
            float buttresses = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 3000f, worldZ - 1700f, 0.016f, 4, 2.1f, 0.55f);

            float valleyNoise = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 2600f, worldZ + 2600f, 0.0036f, 3, 2f, 0.5f);
            float valley01 = (valleyNoise + 1f) * 0.5f;
            float valleyMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(0.72f - valley01, 0.16f, 0.62f));

            float dramaticShelf = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 3200f, worldZ - 1800f, 0.018f, 4, 2.1f, 0.55f);
            float terraceBoost = TerralithLowBaseTerrainMath.Terrace((shelves * 0.55f) + (dramaticShelf * 0.45f), 4f, 0.80f);

            float height = 24f;
            height += rolling * 12f;
            height += hillMask * 15f;
            height += ridges * 14f * blender;
            height += shelves * 12f * hillMask * blender;
            height += dramaticShelf * 14f * hillMask * blender;
            height += buttresses * 8f * hillMask * blender;
            height += terraceBoost * 6f * hillMask * blender;
            height -= valleyMask * 18f;

            height = MathHelper.Lerp(34f, height, TerralithLowBaseTerrainMath.Clamp01(blender));
            return (int)height;
        }

        private float SampleSteepness(int worldX, int worldZ, float blender)
        {
            int h = SampleHeight(worldX, worldZ, blender);
            int hx = SampleHeight(worldX + 1, worldZ, blender);
            int hz = SampleHeight(worldX, worldZ + 1, blender);
            return Math.Abs(h - hx) + Math.Abs(h - hz);
        }
    }

    /// <summary>
    /// Main showcase biome for the pack.
    /// Summary: Low valley floors, pale cliff walls, high ledges, and aggressive escarpment carving.
    /// </summary>
    public sealed class TerralithLowBaseCliffsBiome : Biome
    {
        private readonly PerlinNoise _noise;

        public TerralithLowBaseCliffsBiome(WorldInfo worldInfo)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
            WaterDepth = 14f;
        }

        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            if (terrain._resetRequested)
                return;

            int groundLimit = SampleHeight(worldX, worldZ, blender);
            if (groundLimit < 18) groundLimit = 18;
            if (groundLimit > 126) groundLimit = 126;

            float steepness = SampleSteepness(worldX, worldZ, blender);
            float cliffMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 1800f, worldZ - 1300f, 0.014f, 5, 2.1f, 0.55f);
            float gullyMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 2500f, worldZ + 900f, 0.024f, 4, 2.1f, 0.55f);
            float wallMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 4600f, worldZ - 600f, 0.0105f, 5, 2.15f, 0.56f);
            float palisadeMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 6200f, worldZ + 2100f, 0.016f, 5, 2.16f, 0.56f);

            int snowLine = 90 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 1200f, worldZ + 1200f, 0.004f, 2, 2f, 0.5f) * 6f);
            int treeLine = 72 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX + 1600f, worldZ - 1600f, 0.0035f, 2, 2f, 0.5f) * 4f);
            int soilDepth = steepness >= 12f ? 1 : 2;

            bool snowyTop = groundLimit >= snowLine;
            bool grassyTop = !snowyTop && groundLimit <= treeLine && steepness < 9f && cliffMask < 0.62f && wallMask < 0.64f && palisadeMask < 0.64f;

            for (int y = 0; y <= groundLimit; y++)
            {
                if (terrain._resetRequested)
                    return;

                int worldY = minY + y;
                if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                    continue;

                int block = rockblock;

                if (y == groundLimit)
                {
                    if (snowyTop)
                        block = snowBlock;
                    else if (grassyTop)
                        block = grassblock;
                    else if (groundLimit > treeLine)
                        block = rockblock;
                    else
                        block = dirtblock;
                }
                else if (y >= groundLimit - soilDepth)
                {
                    if (snowyTop)
                        block = (y >= groundLimit - 2) ? snowBlock : rockblock;
                    else if (grassyTop)
                        block = dirtblock;
                    else if (cliffMask < 0.44f && wallMask < 0.46f)
                        block = dirtblock;
                    else
                        block = rockblock;
                }
                else if (groundLimit >= snowLine && y >= groundLimit - 5)
                {
                    block = snowBlock;
                }

                terrain._blocks[index] = block;
            }

            int carveStart = Math.Max(16, groundLimit - 64);
            int carveEnd = groundLimit - 4;
            if (carveEnd > carveStart)
            {
                for (int y = carveStart; y <= carveEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float cave = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 700f, y - 100f, worldZ - 700f, 0.040f)
                               + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 200f, y + 250f, worldZ + 200f, 0.085f) * 0.45f;

                    float heightFactor = TerralithLowBaseTerrainMath.RemapTo01(y, carveStart, carveEnd);
                    float threshold = 0.77f - (cliffMask * 0.24f) - (wallMask * 0.18f) - (palisadeMask * 0.12f) - (steepness * 0.012f) - (gullyMask * 0.12f) + (heightFactor * 0.06f);
                    if (cave > threshold)
                        terrain._blocks[index] = emptyblock;
                }
            }

            if ((gullyMask > 0.52f || wallMask > 0.58f || cliffMask > 0.70f) && steepness > 9f)
            {
                int gullyStart = Math.Max(16, groundLimit - 56);
                int gullyEnd = groundLimit - 2;
                for (int y = gullyStart; y <= gullyEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float slit = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 3000f, y, worldZ + 3000f, 0.070f)
                                 + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 5200f, y - 60f, worldZ - 5200f, 0.032f) * 0.35f;
                    if (slit > 0.46f)
                        terrain._blocks[index] = emptyblock;
                }
            }

            if (wallMask > 0.66f || palisadeMask > 0.64f)
            {
                int headwallStart = Math.Max(18, groundLimit - 30);
                int headwallEnd = groundLimit - 6;
                for (int y = headwallStart; y <= headwallEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float notch = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 8200f, y - 40f, worldZ - 8200f, 0.060f)
                                + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 7300f, y + 80f, worldZ + 7300f, 0.028f) * 0.30f;
                    if (notch > 0.56f)
                        terrain._blocks[index] = emptyblock;
                }
            }
        }

        private int SampleHeight(int worldX, int worldZ, float blender)
        {
            float massif = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX, worldZ, 0.00145f, 3, 2f, 0.5f);
            float massif01 = (massif + 1f) * 0.5f;
            float massifMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(massif01, 0.40f, 0.92f));

            float lowerRidges = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 900f, worldZ - 900f, 0.0062f, 4, 2f, 0.52f);
            float cliffRidges = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 1600f, worldZ + 1600f, 0.0135f, 5, 2.1f, 0.56f);
            float escarpment = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 2400f, worldZ - 2400f, 0.022f, 4, 2.15f, 0.55f);
            float terraces = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 3400f, worldZ - 1000f, 0.048f, 3, 2f, 0.5f);
            float walls = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 5200f, worldZ + 1600f, 0.009f, 5, 2.12f, 0.56f);
            float palisades = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 6200f, worldZ + 2100f, 0.016f, 5, 2.16f, 0.56f);
            float monoliths = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 7600f, worldZ - 1900f, 0.031f, 4, 2.1f, 0.54f);

            float basin = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 500f, worldZ + 500f, 0.0026f, 3, 2f, 0.5f);
            float basinMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(0.70f - ((basin + 1f) * 0.5f), 0.14f, 0.60f));
            float gorge = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 4100f, worldZ + 900f, 0.010f, 4, 2.05f, 0.54f);
            float abyss = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 6200f, worldZ + 2400f, 0.0065f, 4, 2.0f, 0.52f);

            float wallLiftMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01((escarpment * 0.45f) + (walls * 0.35f) + (palisades * 0.20f), 0.58f, 0.92f));
            float terraceBoost = TerralithLowBaseTerrainMath.Terrace(terraces, 5f, 0.85f);

            float height = 22f;
            height += massifMask * 12f;
            height += lowerRidges * 18f;
            height += cliffRidges * 44f * blender;
            height += escarpment * 60f * massifMask * blender;
            height += terraces * 18f * massifMask * blender;
            height += walls * 30f * blender;
            height += palisades * 24f * massifMask * blender;
            height += monoliths * 12f * massifMask * blender;
            height += wallLiftMask * (20f + (massifMask * 18f)) * blender;
            height += terraceBoost * 8f * massifMask * blender;
            height -= basinMask * 22f;
            height -= gorge * 10f;
            height -= abyss * 10f;

            height = MathHelper.Lerp(34f, height, TerralithLowBaseTerrainMath.Clamp01(blender));
            return (int)height;
        }

        private float SampleSteepness(int worldX, int worldZ, float blender)
        {
            int h = SampleHeight(worldX, worldZ, blender);
            int hx = SampleHeight(worldX + 1, worldZ, blender);
            int hz = SampleHeight(worldX, worldZ + 1, blender);
            return Math.Abs(h - hx) + Math.Abs(h - hz);
        }
    }

    /// <summary>
    /// Jagged high peaks and snowy spires.
    /// Summary: Uses the extra headroom from a ~-30 world-floor target to spend more of the vertical budget on sharp summits.
    /// </summary>
    public sealed class TerralithLowBasePeaksBiome : Biome
    {
        private readonly PerlinNoise _noise;

        public TerralithLowBasePeaksBiome(WorldInfo worldInfo)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
            WaterDepth = 16f;
        }

        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            if (terrain._resetRequested)
                return;

            int groundLimit = SampleHeight(worldX, worldZ, blender);
            if (groundLimit < 28) groundLimit = 28;
            if (groundLimit > 127) groundLimit = 127;

            float steepness = SampleSteepness(worldX, worldZ, blender);
            float carveBias = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 4200f, worldZ - 1100f, 0.020f, 5, 2.15f, 0.56f);
            float towerMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 5200f, worldZ + 2600f, 0.012f, 5, 2.18f, 0.56f);
            float finMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 7100f, worldZ - 3200f, 0.034f, 4, 2.18f, 0.55f);
            int snowLine = 86 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 800f, worldZ + 800f, 0.003f, 2, 2f, 0.5f) * 5f);

            for (int y = 0; y <= groundLimit; y++)
            {
                if (terrain._resetRequested)
                    return;

                int worldY = minY + y;
                if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                    continue;

                int block = rockblock;

                if (y == groundLimit)
                {
                    block = (groundLimit >= snowLine) ? snowBlock : rockblock;
                }
                else if (y >= groundLimit - 3)
                {
                    block = (groundLimit >= snowLine) ? snowBlock : rockblock;
                }
                else if (y >= groundLimit - 5 && steepness < 6f)
                {
                    block = dirtblock;
                }

                terrain._blocks[index] = block;
            }

            int carveStart = Math.Max(20, groundLimit - 70);
            int carveEnd = groundLimit - 4;
            if (carveEnd > carveStart)
            {
                for (int y = carveStart; y <= carveEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float cave = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 1200f, y, worldZ - 1200f, 0.045f)
                               + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 500f, y + 200f, worldZ + 500f, 0.095f) * 0.5f;

                    float heightFactor = TerralithLowBaseTerrainMath.RemapTo01(y, carveStart, carveEnd);
                    float threshold = 0.76f - (carveBias * 0.25f) - (towerMask * 0.18f) - (finMask * 0.10f) - (steepness * 0.014f) + (heightFactor * 0.08f);
                    if (cave > threshold)
                        terrain._blocks[index] = emptyblock;
                }
            }

            float summitSlit = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 5000f, worldZ + 5000f, 0.040f, 4, 2.2f, 0.55f);
            if (summitSlit > 0.48f || towerMask > 0.56f || finMask > 0.58f)
            {
                int start = Math.Max(groundLimit - 24, 34);
                for (int y = start; y <= groundLimit - 2; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float slit = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 7100f, y + 90f, worldZ + 7100f, 0.075f)
                                 + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 8100f, y - 40f, worldZ - 8100f, 0.040f) * 0.30f;
                    if (slit > 0.50f)
                        terrain._blocks[index] = emptyblock;
                }
            }
        }

        private int SampleHeight(int worldX, int worldZ, float blender)
        {
            float macro = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX, worldZ, 0.0012f, 3, 2f, 0.5f);
            float macro01 = (macro + 1f) * 0.5f;
            float massifMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(macro01, 0.44f, 0.94f));

            float ridgeBase = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 700f, worldZ - 700f, 0.007f, 4, 2.1f, 0.55f);
            float jagged = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 2400f, worldZ + 2400f, 0.018f, 5, 2.15f, 0.55f);
            float needles = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 3300f, worldZ - 3300f, 0.038f, 4, 2.2f, 0.55f);
            float towers = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 5400f, worldZ + 1800f, 0.012f, 5, 2.18f, 0.56f);
            float micro = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 4200f, worldZ - 1000f, 0.070f, 3, 2.05f, 0.5f);
            float fins = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 7100f, worldZ - 3200f, 0.034f, 4, 2.18f, 0.55f);

            float basin = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 900f, worldZ + 900f, 0.0022f, 3, 2f, 0.5f);
            float basinMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(0.66f - ((basin + 1f) * 0.5f), 0.12f, 0.52f));

            float crown = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01((jagged * 0.35f) + (needles * 0.40f) + (towers * 0.25f), 0.58f, 0.92f));

            float height = 34f;
            height += massifMask * 18f;
            height += ridgeBase * 20f;
            height += jagged * 42f * blender;
            height += needles * 54f * massifMask * blender;
            height += towers * 34f * massifMask * blender;
            height += fins * 16f * massifMask * blender;
            height += micro * 14f * blender;
            height += crown * (22f + (massifMask * 16f)) * blender;
            height -= basinMask * 18f;

            height = MathHelper.Lerp(42f, height, TerralithLowBaseTerrainMath.Clamp01(blender));
            return (int)height;
        }

        private float SampleSteepness(int worldX, int worldZ, float blender)
        {
            int h = SampleHeight(worldX, worldZ, blender);
            int hx = SampleHeight(worldX + 1, worldZ, blender);
            int hz = SampleHeight(worldX, worldZ + 1, blender);
            return Math.Abs(h - hx) + Math.Abs(h - hz);
        }
    }

    /// <summary>
    /// Ultra-dramatic alpine biome for the most extreme preset in the pack.
    /// Summary: Combines the low basin target with wall lifts, spire masks, and deeper vertical carving.
    /// </summary>
    public sealed class TerralithLowBaseSkybreakersBiome : Biome
    {
        private readonly PerlinNoise _noise;

        public TerralithLowBaseSkybreakersBiome(WorldInfo worldInfo)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
            WaterDepth = 18f;
        }

        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            if (terrain._resetRequested)
                return;

            int groundLimit = SampleHeight(worldX, worldZ, blender);
            if (groundLimit < 22) groundLimit = 22;
            if (groundLimit > 127) groundLimit = 127;

            float steepness = SampleSteepness(worldX, worldZ, blender);
            float wallMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 6100f, worldZ - 900f, 0.010f, 5, 2.15f, 0.56f);
            float spireMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 7400f, worldZ + 1700f, 0.031f, 4, 2.18f, 0.55f);
            float gashMask = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 4500f, worldZ + 2600f, 0.020f, 4, 2.1f, 0.55f);

            int snowLine = 82 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 800f, worldZ + 800f, 0.003f, 2, 2f, 0.5f) * 5f);
            int treeLine = 68 + (int)(TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX + 1200f, worldZ - 1200f, 0.0035f, 2, 2f, 0.5f) * 4f);
            int soilDepth = steepness >= 10f ? 1 : 2;

            bool snowyTop = groundLimit >= snowLine;
            bool grassyTop = !snowyTop && groundLimit <= treeLine && steepness < 8f && wallMask < 0.58f && spireMask < 0.56f;

            for (int y = 0; y <= groundLimit; y++)
            {
                if (terrain._resetRequested)
                    return;

                int worldY = minY + y;
                if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                    continue;

                int block = rockblock;
                if (y == groundLimit)
                {
                    if (snowyTop)
                        block = snowBlock;
                    else if (grassyTop)
                        block = grassblock;
                    else
                        block = rockblock;
                }
                else if (y >= groundLimit - soilDepth)
                {
                    if (snowyTop)
                        block = (y >= groundLimit - 2) ? snowBlock : rockblock;
                    else if (grassyTop)
                        block = dirtblock;
                    else
                        block = rockblock;
                }
                else if (snowyTop && y >= groundLimit - 5)
                {
                    block = snowBlock;
                }

                terrain._blocks[index] = block;
            }

            int carveStart = Math.Max(18, groundLimit - 82);
            int carveEnd = groundLimit - 4;
            if (carveEnd > carveStart)
            {
                for (int y = carveStart; y <= carveEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float cave = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 1800f, y - 80f, worldZ - 1800f, 0.042f)
                               + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 2600f, y + 220f, worldZ + 2600f, 0.088f) * 0.48f;
                    float heightFactor = TerralithLowBaseTerrainMath.RemapTo01(y, carveStart, carveEnd);
                    float threshold = 0.74f - (wallMask * 0.20f) - (spireMask * 0.18f) - (gashMask * 0.14f) - (steepness * 0.013f) + (heightFactor * 0.08f);
                    if (cave > threshold)
                        terrain._blocks[index] = emptyblock;
                }
            }

            if (gashMask > 0.50f || wallMask > 0.60f)
            {
                int gashStart = Math.Max(18, groundLimit - 64);
                int gashEnd = groundLimit - 2;
                for (int y = gashStart; y <= gashEnd; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float slit = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 9100f, y - 60f, worldZ - 9100f, 0.062f)
                                 + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 8400f, y + 40f, worldZ + 8400f, 0.034f) * 0.32f;
                    if (slit > 0.46f)
                        terrain._blocks[index] = emptyblock;
                }
            }

            if (spireMask > 0.54f)
            {
                int spireStart = Math.Max(groundLimit - 28, 34);
                for (int y = spireStart; y <= groundLimit - 2; y++)
                {
                    int worldY = minY + y;
                    if (!TerralithLowBaseTerrainMath.TryGetIndex(terrain, worldX, worldY, worldZ, out int index))
                        continue;

                    float slit = TerralithLowBaseTerrainMath.Noise3D(_noise, worldX - 10300f, y + 70f, worldZ + 10300f, 0.072f)
                                 + TerralithLowBaseTerrainMath.Noise3D(_noise, worldX + 9800f, y - 20f, worldZ - 9800f, 0.038f) * 0.28f;
                    if (slit > 0.51f)
                        terrain._blocks[index] = emptyblock;
                }
            }
        }

        private int SampleHeight(int worldX, int worldZ, float blender)
        {
            float macro = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX, worldZ, 0.0011f, 3, 2f, 0.5f);
            float macro01 = (macro + 1f) * 0.5f;
            float massifMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(macro01, 0.42f, 0.95f));

            float lowerRidges = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 1200f, worldZ - 1200f, 0.0068f, 4, 2.1f, 0.55f);
            float cliffBands = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 2100f, worldZ + 2100f, 0.014f, 5, 2.14f, 0.56f);
            float wallBands = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 4400f, worldZ - 4400f, 0.021f, 4, 2.16f, 0.55f);
            float needles = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 7100f, worldZ + 1700f, 0.031f, 4, 2.18f, 0.55f);
            float towers = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 5600f, worldZ - 1400f, 0.012f, 5, 2.18f, 0.56f);
            float terraces = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX - 5300f, worldZ - 2100f, 0.046f, 3, 2.05f, 0.50f);
            float monoliths = TerralithLowBaseTerrainMath.Ridged2D(_noise, worldX + 8100f, worldZ + 900f, 0.028f, 4, 2.12f, 0.54f);

            float basin = TerralithLowBaseTerrainMath.Fbm2D(_noise, worldX - 600f, worldZ + 600f, 0.0024f, 3, 2f, 0.5f);
            float basinMask = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01(0.68f - ((basin + 1f) * 0.5f), 0.12f, 0.56f));

            float crown = TerralithLowBaseTerrainMath.Smooth01(TerralithLowBaseTerrainMath.RemapTo01((cliffBands * 0.25f) + (wallBands * 0.30f) + (needles * 0.25f) + (towers * 0.20f), 0.56f, 0.92f));
            float terraceBoost = TerralithLowBaseTerrainMath.Terrace(terraces, 5f, 0.85f);

            float height = 22f;
            height += massifMask * 12f;
            height += lowerRidges * 18f;
            height += cliffBands * 36f * blender;
            height += wallBands * 48f * massifMask * blender;
            height += needles * 44f * massifMask * blender;
            height += towers * 26f * massifMask * blender;
            height += terraces * 16f * massifMask * blender;
            height += monoliths * 16f * massifMask * blender;
            height += terraceBoost * 8f * massifMask * blender;
            height += crown * (26f + (massifMask * 18f)) * blender;
            height -= basinMask * 26f;

            height = MathHelper.Lerp(34f, height, TerralithLowBaseTerrainMath.Clamp01(blender));
            return (int)height;
        }

        private float SampleSteepness(int worldX, int worldZ, float blender)
        {
            int h = SampleHeight(worldX, worldZ, blender);
            int hx = SampleHeight(worldX + 1, worldZ, blender);
            int hz = SampleHeight(worldX, worldZ + 1, blender);
            return Math.Abs(h - hx) + Math.Abs(h - hz);
        }
    }
}
